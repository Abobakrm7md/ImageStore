# Architecture and design decisions

## Layers

The backend follows Clean Architecture with dependencies pointing inwards:

```
ImageStore.Api ──► ImageStore.Application ──► ImageStore.Domain
      │                    ▲
      └──► ImageStore.Infrastructure ─┘
```

| Project            | Responsibility                                                                                                                                                                                                                                                         |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Domain**         | `Image` and `Tag` entities with their invariants (required title, length limits, UTC timestamps, tags owned by the image owner, max 20 unique tags). No framework references.                                                                                         |
| **Application**    | Use cases (`ImageService`, `AuthService`), DTOs, request validation, file-signature inspection (`ImageFormatInspector`), file-name sanitizing, storage-path rules, and the ports it needs: `IImageRepository`, `IImageFileStorage`, `IIdentityService`, `ICurrentUser`. |
| **Infrastructure** | EF Core `ImageStoreDbContext` (Identity + images/tags), entity configurations, migrations, `ImageRepository`, ASP.NET Core Identity adapter, `LocalImageFileStorage`, `StorageMaintenanceService`, health checks.                                                     |
| **Api**            | Thin controllers, cookie authentication, anti-forgery, authorization policies, CORS, rate limiting, exception → Problem Details mapping, upload request limits, security headers, Swagger, static SPA hosting.                                                         |

Deliberate non-choices: no MediatR/CQRS, no generic repository, no AutoMapper, no validation
library. The single repository exists because it adds value: it keeps EF Core out of the
application layer and makes the owner filter part of every query method's signature.

## Authentication and CSRF

**Choice: ASP.NET Core Identity cookies, not bearer tokens.** The browser stores the session in
an `HttpOnly`, `Secure`, `SameSite=Strict` cookie (named `__Host-ImageStore.Auth` when always
Secure), so JavaScript – including injected script – can never read it, and nothing is kept in
`localStorage`. Image `<img src>` and download links work with no token plumbing.

Cookies need CSRF protection for state-changing requests. The API uses ASP.NET Core
anti-forgery in the "double submit" pattern that Angular's `HttpClient` supports natively:

1. `GET /api/auth/csrf-token` stores the anti-forgery *cookie token* (HttpOnly) and returns the
   *request token* in a readable `XSRF-TOKEN` cookie.
2. Angular's built-in XSRF interceptor copies that cookie into the `X-XSRF-TOKEN` header on every
   unsafe same-origin request.
3. A global MVC filter (`AntiforgeryValidationFilter`) validates every POST/PUT/PATCH/DELETE –
   including login and registration (prevents login CSRF).

Tokens are bound to the user, so they are re-issued in the same response after login,
registration and logout. If a token is stale, the API answers `400` with
`code: "antiforgery_validation_failed"`; the SPA's interceptor fetches a fresh token and retries
once.

Other measures: Identity password policy (≥ 8 chars, upper/lower/digit), lockout after 5 failed
attempts (15 min), generic login failure messages, constant-time-ish handling of unknown accounts
(a dummy hash is verified), per-IP rate limiting of login/registration, and a **fallback
authorization policy** – every endpoint requires an authenticated user unless it explicitly opts
out (`[AllowAnonymous]`).

## Ownership and authorization

- The user id is taken only from the authenticated principal (`ICurrentUser`); request bodies
  never carry an owner, and unknown JSON properties such as `ownerUserId` are ignored.
- Every repository read takes `ownerUserId` and applies it **inside the database query**
  (`WHERE OwnerUserId = @owner AND Id = @id`), so another user's row is never materialized.
- A foreign image id produces exactly the same `404` Problem Details as a non-existent id – no
  metadata, file name or path is revealed.
- Tags are private per user (unique `(OwnerUserId, NormalizedName)`).
- Angular guards only improve UX; the API is the security boundary.

## File storage

```
{Storage:RootPath}/
├─ .staging/                       incoming uploads (never readable through the API)
└─ {ownerUserId}/
   └─ {imageId}.{jpg|png|webp}     final files (server-generated names)
```

- The root is configurable and must be outside `wwwroot` (start-up check); no static-file
  middleware serves it. Files are only reachable through the authorized `content`/`download`
  endpoints.
- Directories are created `0750`, files `0640` on Unix.
- Storage paths are composed solely from two server-generated GUIDs and the canonical extension
  of the *detected* format. Before any file-system access the relative path must match a strict
  regular expression **and** its resolved absolute path must stay inside the root (defence in
  depth against a tampered database row – covered by integration tests).
- The original file name is sanitized (directory parts, control and reserved characters removed,
  length-limited) and kept only as metadata, e.g. for `Content-Disposition` on download.

### Upload pipeline

1. **Request limits:** a resource filter raises the body-size limit only for the upload endpoint
   (`MaxFileSizeBytes × MaxFilesPerRequest + 1 MiB`) and reads the form up front, turning
   oversized or malformed bodies into `413`/`400`.
2. **Cheap checks:** file count, metadata lengths, extension allow-list, declared size.
3. **Staging:** each file is streamed in 80 KB chunks into `.staging/{random}.tmp`; the byte
   counter aborts as soon as the limit is exceeded (a lying `Content-Length` does not help).
4. **Inspection:** `ImageFormatInspector` reads only headers: JPEG markers up to the SOF segment,
   PNG signature + IHDR (with CRC and color-type validation), WebP RIFF size + VP8/VP8L/VP8X
   headers. It determines the real content type and pixel dimensions; the client's
   `Content-Type` is ignored. Content/extension mismatches and excessive pixel counts are
   rejected.
5. **All or nothing:** if any file in the request is invalid, the request fails with per-file
   errors (`files[i]`) and every staged file is deleted.
6. **Commit:** staged files are moved into place with an atomic same-volume rename that never
   overwrites (`File.Move(..., overwrite: false)`); readers can never observe partial files.
7. **Persist:** all metadata rows are saved in one `SaveChanges` (one transaction). This step
   ignores client cancellation so that a disconnect cannot leave the outcome undefined.

### File and database consistency

The file system and the database cannot share a transaction, so each operation is ordered so
that the worst case is a harmless **orphaned file**, never a record pointing to a missing file:

| Situation                                                     | Behaviour                                                                                                                                                                       |
| ------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Upload: validation fails                                      | Staged files deleted, nothing stored.                                                                                                                                           |
| Upload: DB write fails                                        | The service asks the database whether the rows exist. If they do not, the committed files are deleted (compensation). If they *do* (commit succeeded but the acknowledgement was lost) or the check itself fails, files are kept and the event is logged. |
| Upload: process crash                                         | Partial data can only exist in `.staging`; the maintenance service deletes staging files older than 60 min.                                                                     |
| Delete                                                        | Ownership verified, then the **database row is deleted first**; the file is removed only after the commit. If file deletion fails, the file is orphaned and logged.            |
| Delete: file already missing                                  | The delete still succeeds (logged as a warning).                                                                                                                                |
| Read: file missing or path invalid                            | `404` to the client, error/critical log on the server.                                                                                                                          |

`StorageMaintenanceService` (hourly by default) deletes stale staging files and **reports**
stored files that have no database row (older than 24 h). Automatic deletion of such orphans is
opt-in (`StorageMaintenance:DeleteOrphanedFiles`) because, after restoring an older database
backup, an "orphan" may be the only copy of an image.

Remaining edge cases: a record whose file was deleted or lost outside the application (returns
404 and is logged); orphaned tag rows if an upload fails after its new tags were created (they
are invisible and reused when the tag name is used again).

### Concurrent tag creation

The SPA uploads several files in parallel, often with the same new tags. `GetOrCreateTagsAsync`
commits new tags in their own short transaction; if a concurrent request wins the race (unique
index violation), the losing request discards its copies and reuses the existing rows. Reads
tolerate duplicates as well. This was found and fixed through browser end-to-end testing and is
covered by regression tests.

## HTTP behaviour

- **Problem Details** everywhere (validation errors use camelCase keys such as `title` or
  `files[0]`; every response carries a `traceId`). Unexpected exceptions return a generic `500`
  without exception messages or paths.
- **Image responses:** `Content-Type` from the verified format, `X-Content-Type-Options: nosniff`,
  `Content-Disposition` (`inline` for content, `attachment` with RFC 5987 file name for
  downloads), `Cache-Control: private, no-cache` with a strong `ETag` (304 on revalidation, so
  authorization runs on every use), range requests.
- **Security headers** on all responses: CSP (`default-src 'none'` for the API, a strict
  self-only policy for the SPA), `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
  `Cross-Origin-Opener-Policy`, `Cross-Origin-Resource-Policy`, `Permissions-Policy`; HSTS and
  HTTPS redirection outside Development.

## Frontend

- Standalone components, zoneless change detection, signals for state, `rxResource` for data
  loading, Reactive Forms (typed, non-nullable), functional guards and interceptors, lazy-loaded
  routes, `withComponentInputBinding()` for route parameters.
- The gallery's state (page, search, sort, tag) lives in the URL, so reloads, deep links and the
  back button work.
- Images are displayed with plain `<img src="/api/images/{id}/content">`; the session cookie
  authorizes the request. Upload previews use object URLs that are revoked when removed or when
  the page is destroyed.
- `HttpClient` uses the XHR backend because Angular's default fetch backend cannot report upload
  progress.
- Subscriptions are bounded by `takeUntilDestroyed`; in-flight uploads are aborted when the user
  cancels or leaves the page.

## Testing strategy

| Level                     | What is covered                                                                                                                                                                                                                                   |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Unit (xUnit)              | Domain invariants; signature inspection with real and crafted malicious/truncated files; file-name sanitizing; storage-path rules; local storage (staging, atomic commit, no overwrite, traversal attempts); `ImageService` incl. DB-failure compensation. |
| Integration (xUnit)       | The real HTTP pipeline via `WebApplicationFactory`: registration/login/logout, cookie flags, CSRF enforcement and user binding, 401 for every protected endpoint, uploads of all formats, invalid/oversized/malicious uploads, 413, cross-user access, pagination/search/sort/tag filter, edits, deletion, tampered storage paths, Problem Details, security headers, Swagger, health, migrations-vs-model, SQL translation, maintenance, concurrent tag creation. |
| Frontend (Vitest)         | Utilities and validators, Problem Details parsing, auth service, guards, interceptor (401 redirect, CSRF refresh-and-retry), gallery URL state, pagination, upload queue (concurrency, progress, rejected vs failed).                              |
| Browser smoke test        | `e2e/smoke.mjs` drives Chrome through the complete user journey including mobile layout, and fails on console errors or 5xx responses.                                                                                                            |
