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
| **Domain**         | `Image`/`Tag` entities (required title, length limits, UTC timestamps, tags owned by the image owner, max 20 unique tags) plus the social model: `UserProfile`, `UserFollow`, `ImagePost`, `Comment`, `PostReaction` and the `ReactionType` enum, each with its invariants (see below). No framework references. |
| **Application**    | Use cases (`ImageService`, `AuthService`, `SocialService`, `PostService`, `CommentService`, `ReactionService`), DTOs, request validation, file-signature inspection (`ImageFormatInspector`), file-name sanitizing, storage-path rules (incl. profile pictures), and the ports it needs: `IImageRepository`, `ISocialRepository`, `IPostRepository`, `ICommentRepository`, `IPostReactionRepository`, `IImageFileStorage`, `IIdentityService`, `ICurrentUser`. |
| **Infrastructure** | EF Core `ImageStoreDbContext` (Identity + images/tags + social tables), entity configurations, migrations (`InitialCreate`, `AddSocialSharing`), repositories (`ImageRepository`, `SocialRepository`, `PostRepository`, `CommentRepository`, `PostReactionRepository`), ASP.NET Core Identity adapter (creates a `UserProfile` with a generated user name on registration), `LocalImageFileStorage`, `StorageMaintenanceService`, health checks. |
| **Api**            | Thin controllers (`Auth`, `Images`, `Users`, `Feed`, `Posts`), cookie authentication, anti-forgery, authorization policies, CORS, rate limiting, exception → Problem Details mapping, upload request limits, security headers, Swagger, static SPA hosting. |

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
- **Post visibility:** an `ImagePost` and everything attached to it (image bytes via
  `GET /api/posts/{id}/content` and `/download`, comments, reactions) is visible to **the author
  and the author's followers only**. The check runs in the service layer on *every* request
  (`author OR follower`), inside the database query where possible. Unfollowing therefore
  revokes access immediately – feed, details, bytes, comments and reactions all answer `404`
  afterwards. As with images, "not accessible" and "does not exist" produce the same `404`, so
  existence is not leaked.
- Profile *metadata* (name, picture, counts) is visible to every signed-in user; the published
  post gallery (`GET /api/users/{id}/posts`) follows the stricter follower rule.
- Comments may be edited by their author only, and deleted by their author **or the post author**
  (moderation). Reactions are per-user rows; nobody can change another user's reaction.
- Protected images are never served as static files – there is no public URL for post content,
  image content or profile pictures.
- Angular guards only improve UX; the API is the security boundary.

## File storage

```
{Storage:RootPath}/
├─ .staging/                       incoming uploads and profile pictures (never readable through the API)
├─ profiles/
│  └─ {userId:D}.{jpg|png|webp|gif} profile pictures (server-generated names)
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
- **Profile pictures** use the same staging → inspection → atomic-commit pipeline (JPEG, PNG,
  WebP or GIF, ≤ 2 MiB, content verified). Replacing a picture deletes the old file only *after*
  the database commit; `profiles/` paths are exempt from the orphan-file scan because their rows
  live on `UserProfile`, not `Image`.

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

## Social domain model

Migration `AddSocialSharing` adds five tables; all timestamps are UTC and all foreign keys are
explicit:

| Table           | Key & constraints                                                                                                    | FK behaviour                                                                                     |
| --------------- | -------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| `UserProfiles`  | PK `UserId` (= `AspNetUsers.Id`), unique index on `NormalizedUserName`                                                | `Cascade` from `AspNetUsers` – deleting the account removes the profile (and its picture).        |
| `UserFollows`   | Composite PK `(FollowerUserId, FollowedUserId)` – duplicates impossible; self-follows are rejected by the domain entity and the service (`400`) | `AspNetUsers` **Restrict** on both sides – SQL Server forbids two cascade paths into one table, and account deletion is already guarded by the authorship FKs; follow rows are removed explicitly first. |
| `ImagePosts`    | PK `Id` (GUIDv7 – time-ordered), **unique** `ImageId` (one post per image), index `(AuthorUserId, CreatedAtUtc DESC)` | Author → `UserProfiles` **Restrict**; Image → `Images` **Cascade**.                                |
| `Comments`      | PK `Id`, index `(PostId, CreatedAtUtc)`                                                                              | Post **Cascade**; author → `UserProfiles` **Restrict**.                                            |
| `PostReactions` | PK `Id`; **unique index `(PostId, UserId)`** – DB-level one-reaction-per-user; `Reaction` stored as `int`              | Post **Cascade**; user → `AspNetUsers` **Restrict**.                                               |

Design decisions:

- **No redundant counters.** Follower/following/post/comment/reaction counts are computed with
  indexed `COUNT` queries (or aggregated in the feed query). At this scale that is cheaper to
  keep correct than denormalized columns; revisit only with measurements.
- **Restrict on authorship** means an account cannot be deleted while it still owns posts,
  comments or reactions – deliberate: content removal must be an explicit step (delete posts →
  delete comments/reactions → delete account), so nobody's feed silently loses context.
  The profile (with its picture) still cascades with the user, while follow rows are `Restrict`
  (SQL Server allows only one cascade path per table pair) and are removed explicitly before an
  account deletion.
- **Deleting a post** cascades its comments and reactions but **keeps the image** (row and file)
  in the author's library – it just becomes unpublished and can be posted again. **Deleting an
  image** cascades its post, comments and reactions, then removes the file (the existing
  delete-image flow). **Unfollowing** keeps the other side's content untouched; reactions and
  comments an unfollowed user made on *your* posts are retained.
- Reactions are a closed enum (`Like=1 … Dislike=6`) with stable wire names; adding a reaction
  type is a code + migration change, never a client-side string.
- `UserProfile.UserName` is generated at registration from the email local part (sanitized, with
  `-2…-5` suffixes and a random fallback on collision) and is immutable afterwards; the display
  name is free text.

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
- The gallery's, feed's and people-search state (page, search, sort, tag) lives in the URL, so
  reloads, deep links and the back button work.
- Routes: `/feed`, `/posts/new`, `/posts/:id`, `/users` (search), `/users/:id` (profile),
  `/users/:id/followers`, `/users/:id/following`, `/account`, plus the existing `/images*`
  gallery routes; all behind `authGuard`, with `/` redirecting to the feed.
- Reusable social components in `shared/components`: `Avatar` (picture with initials fallback),
  `FollowButton` (two-way `following` model + `changed` output, calls the API itself),
  `ReactionBar` (quick toggle + six-reaction picker, emits the fresh summary), `PostCard` and
  `UserRow`. Typed clients `SocialApiService`/`PostApiService` mirror the API contracts.
- Mutations refresh data from the server response (or a targeted reload) instead of optimistic
  guessing; the one exception is appending your own comment, which is reconciled by the resource
  refresh afterwards.
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
| Unit (xUnit)              | Domain invariants (images **and** profiles/follows/posts/comments/reactions); signature inspection with real and crafted malicious/truncated files; file-name sanitizing; storage-path rules incl. profile pictures; local storage (staging, atomic commit, no overwrite, traversal attempts); `ImageService` incl. DB-failure compensation. |
| Integration (xUnit)       | The real HTTP pipeline via `WebApplicationFactory`: registration/login/logout, cookie flags, CSRF enforcement and user binding, 401 for every protected endpoint, uploads of all formats, invalid/oversized/malicious uploads, 413, cross-user access, pagination/search/sort/tag filter, edits, deletion, tampered storage paths, Problem Details, security headers, Swagger, health, migrations-vs-model, SQL translation, maintenance, concurrent tag creation, **plus the social surface:** follow/unfollow (idempotency, self-follow, duplicates), feed filtering & deterministic pagination, access revocation after unfollowing (details/content/download/comments/reactions → 404), cross-user post edit/delete, comment authorization & moderation, reaction lifecycle & uniqueness (incl. repeated requests), profile pictures, storage persistence across host restarts. |
| Frontend (Vitest)         | Utilities and validators (incl. reactions/initials), Problem Details parsing, auth service, guards, interceptor (401 redirect, CSRF refresh-and-retry), gallery URL state, pagination, upload queue (concurrency, progress, rejected vs failed), social/post API clients, `FollowButton` and `ReactionBar` behaviour (toggle, change, remove, error paths). |
| Browser smoke test        | `e2e/smoke.mjs` drives Chrome through the complete user journey including mobile layout, and fails on console errors or 5xx responses.                                                                                                            |
