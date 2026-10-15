# ImageStore

A small social image-sharing platform: users register, sign in, and upload, browse, search, edit,
download and delete **their own** images — then publish selected images as **posts**, follow other
people, and see a chronological **feed** of posts from the people they follow, with **comments**
and **reactions**. Image binaries live on the local file system; metadata lives in SQL Server.
Every request is authorized on the server: posts (including their image bytes, comments and
reactions) are visible only to the author and the author's followers, and unfollowing revokes
access immediately.

| Layer    | Technology                                                                                       |
| -------- | ------------------------------------------------------------------------------------------------ |
| Backend  | .NET 10 (LTS), ASP.NET Core Web API (controllers), C# with nullable reference types              |
| Data     | EF Core 10 (Code First migrations) on SQL Server, ASP.NET Core Identity                          |
| Auth     | Identity cookie authentication (HttpOnly, Secure, SameSite) + anti-forgery tokens (CSRF)          |
| API docs | Swagger / OpenAPI (Swashbuckle)                                                                   |
| Frontend | Angular 22 (standalone components, zoneless, strict TypeScript, signals, Reactive Forms, Router) |
| Tests    | xUnit (unit + integration with `WebApplicationFactory`), Vitest (Angular), Puppeteer smoke test   |

More detail: [docs/architecture.md](docs/architecture.md) (design and security decisions) ·
[docs/api.md](docs/api.md) (endpoint reference) · [docs/deployment.md](docs/deployment.md)
(production guidance).

---

## Features

- **Accounts:** register, sign in ("keep me signed in" option), sign out; account lockout after
  repeated failures; rate-limited auth endpoints.
- **Private gallery:** responsive card grid with thumbnails, title, date, size and tags;
  server-side pagination; search by title/description; sort (date, title, size); filter by tag.
- **Upload:** one or many files, drag & drop or file picker, previews, client-side pre-checks,
  per-file progress, success/failure per file, cancel and retry.
- **Details:** large view (full-screen lightbox), upload date, size, content type, pixel
  dimensions, original file name; edit title, description and tags; download; delete with
  confirmation.
- **Validation you can trust:** the server verifies JPEG/PNG/WebP **file signatures and header
  structure** (never the client's `Content-Type` or extension alone), enforces size and pixel
  limits, and rejects mismatches such as PNG content named `.jpg`.
- **Profiles & following:** generated user name, editable display name/bio, profile picture
  (uploaded, content-verified, served only through an authorized endpoint), follower/following
  counts and lists, people search, one-click follow/unfollow (idempotent, no self-follow).
- **Posts & feed:** publish any of your images as a post (title/description, edit/delete your own
  posts only); the feed is a strictly chronological, server-side paginated stream of posts from
  the people you follow — no ranking, no duplicates across pages. Unfollowing removes their posts
  from the feed *and* revokes access to post details, image bytes, comments and reactions (404).
- **Comments:** paginated, oldest first; authors edit/delete their own comments; post owners may
  additionally delete (moderate) any comment on their posts; empty/whitespace comments and
  oversized content are rejected.
- **Reactions:** Like · Love · Laugh · Wow · Sad · Dislike as a stable enum; exactly one active
  reaction per user and post (unique index), add/change/remove is idempotent and safe under
  concurrency; totals and per-type counts are computed, never denormalized.

## Repository layout

```
ImageStore/
├─ ImageStore.slnx                 Solution (src + tests)
├─ Directory.Build.props           Shared compiler settings (nullable, warnings as errors, net10.0)
├─ Directory.Packages.props        Central NuGet package versions
├─ global.json                     .NET SDK 10.0.x (roll forward to the latest feature band)
├─ .config/dotnet-tools.json       Local tool manifest (dotnet-ef)
├─ src/
│  ├─ ImageStore.Domain/           Entities and invariants (Image, Tag, UserProfile, UserFollow,
│  │                               ImagePost, Comment, PostReaction, ReactionType) – no dependencies
│  ├─ ImageStore.Application/      Use cases, DTOs, validation, file-signature inspection,
│  │                               repository/storage interfaces, ownership-scoped services
│  ├─ ImageStore.Infrastructure/   EF Core DbContext + configurations + migrations (incl. social
│  │                               tables), Identity (profile creation on registration), repositories,
│  │                               local file storage (images + profile pictures), maintenance, health
│  └─ ImageStore.Api/              Controllers, auth/CSRF/CORS/rate limiting, Problem Details,
│                                  upload limits, security headers, Swagger, composition root
├─ tests/
│  ├─ ImageStore.UnitTests/        Domain, validation, file inspection, storage, ImageService
│  ├─ ImageStore.IntegrationTests/ Full HTTP pipeline (auth, CSRF, ownership, uploads, storage
│  │                               security, persistence/SQL translation, maintenance, following,
│  │                               feed, posts, comments, reactions, access revocation)
│  └─ TestData/                    Real sample JPEG/PNG/WebP files used by the tests
├─ client/                         Angular application
├─ e2e/                            Optional browser smoke test of the whole stack (Puppeteer)
├─ docs/                           Architecture, API and deployment documentation
└─ storage/                        Default development upload root (git-ignored, created at run time)
```

## Prerequisites

| Tool       | Version                                                       |
| ---------- | ------------------------------------------------------------- |
| .NET SDK   | 10.0.100 or later 10.0.x (`global.json` rolls forward)        |
| Node.js    | `^22.22.3`, `^24.15.0` or `>= 26` (Angular 22 requirement)    |
| npm        | 10+                                                           |
| SQL Server | 2016+ (LocalDB, Developer/Express edition, Azure SQL or Docker) |

## Quick start (local development)

### 1. Database connection string

Development defaults to **LocalDB** (Windows) – see `appsettings.Development.json`. On
macOS/Linux, or to use another server, store the connection string as a user secret (never in
`appsettings*.json`):

```bash
dotnet user-secrets set "ConnectionStrings:ImageStore" \
  "Server=localhost,1433;Database=ImageStore;User Id=sa;Password=<your-password>;TrustServerCertificate=True" \
  --project src/ImageStore.Api
```

Need a SQL Server quickly? `docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=<your-password> -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest`

### 2. Run the API (http://localhost:5080)

```bash
dotnet run --project src/ImageStore.Api --launch-profile http
```

In Development, pending migrations are applied automatically at start-up
(`Database:ApplyMigrationsOnStartup=true`), uploads are stored in `./storage/uploads`, and
Swagger UI is available at **http://localhost:5080/swagger**.

### 3. Run the Angular app (http://localhost:4200)

```bash
cd client
npm ci
npm start
```

The dev server proxies `/api` and `/swagger` to the API (`client/proxy.conf.json`), so the
browser sees a single origin: cookies and CSRF protection work without CORS.

### Try it without SQL Server

For a quick look, the API can use EF Core's non-persistent in-memory store. This is refused
outside the Development/Testing environments, and data disappears when the API stops:

```bash
Database__Provider=InMemory dotnet run --project src/ImageStore.Api --launch-profile http
# PowerShell: $env:Database__Provider="InMemory"; dotnet run --project src/ImageStore.Api --launch-profile http
```

## Database and migrations

Schema (created by the `InitialCreate` and `AddSocialSharing` migrations in
`src/ImageStore.Infrastructure/Persistence/Migrations`):

| Table                                       | Purpose / notable columns                                                                                                                                                                                                                                                                     |
| ------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Images`                                    | `Id` (uniqueidentifier, PK) · `OwnerUserId` (FK → AspNetUsers, *no action*) · `Title` nvarchar(200) · `Description` nvarchar(2000) null · `OriginalFileName` nvarchar(255) · `StoredFileName` varchar(64) · `RelativeStoragePath` varchar(128) **unique** · `ContentType` varchar(100) · `FileSizeBytes` bigint · `Width`/`Height` int null · `CreatedAtUtc` datetime2 · `UpdatedAtUtc` datetime2 null |
| `Tags`                                      | `Id` · `OwnerUserId` (FK, *no action*) · `Name` nvarchar(50) · `NormalizedName` nvarchar(50) — **unique (OwnerUserId, NormalizedName)**: tags are private per user                                                                                                                              |
| `ImageTags`                                 | `ImageId` + `TagId` (composite PK, both cascade), index on `TagId`                                                                                                                                                                                                                              |
| `AspNetUsers`, `AspNetRoles`, …             | ASP.NET Core Identity (GUID keys); `AspNetUsers.CreatedAtUtc` added                                                                                                                                                                                                                              |
| `UserProfiles`                              | `UserId` (PK = `AspNetUsers.Id`, cascade) · `UserName` nvarchar(30) · `NormalizedUserName` varchar(30) **unique** · `DisplayName` nvarchar(100) · `Bio` nvarchar(500) null · `ProfilePicturePath`/`ProfilePictureContentType` null · `CreatedAtUtc`/`UpdatedAtUtc`                                |
| `UserFollows`                               | Composite PK `(FollowerUserId, FollowedUserId)` (both FK → AspNetUsers, cascade) · `CreatedAtUtc` — duplicates impossible; index on `FollowedUserId` for follower lists                                                                                                                           |
| `ImagePosts`                                | `Id` (uniqueidentifier PK, GUIDv7) · `AuthorUserId` (FK → UserProfiles, *restrict*) · `ImageId` (FK → Images, cascade) **unique** — one post per image · `Title` nvarchar(200) · `Description` nvarchar(2000) null · `CreatedAtUtc`/`UpdatedAtUtc`; indexes `(AuthorUserId, CreatedAtUtc)` and `CreatedAtUtc` |
| `Comments`                                  | `Id` PK · `PostId` (FK → ImagePosts, cascade) · `AuthorUserId` (FK → UserProfiles, *restrict*) · `Content` nvarchar(2000) · `CreatedAtUtc`/`UpdatedAtUtc`; index `(PostId, CreatedAtUtc)`                                                                                                         |
| `PostReactions`                             | `Id` PK · `PostId` (FK → ImagePosts, cascade) · `UserId` (FK → AspNetUsers, *restrict*) · `Reaction` int (enum: 1=like … 6=dislike) · `CreatedAtUtc`/`UpdatedAtUtc` — **unique `(PostId, UserId)`**: one active reaction per user and post                                                        |

Indexes for the owner-scoped queries: `IX_Images_OwnerUserId_CreatedAtUtc` (default gallery
order), `IX_Images_OwnerUserId_Title` (title sort). Counts (followers, comments, reactions) are
**not** denormalized – they are computed with indexed queries. All timestamps are UTC and are read
back with `DateTimeKind.Utc`. The database stores only relative paths (`{userId}/{imageId}.{ext}`,
`profiles/{userId}.{ext}`), never image bytes or absolute paths. `AddSocialSharing` also backfills
a `UserProfiles` row for every pre-existing account.

```bash
dotnet tool restore                      # installs dotnet-ef from .config/dotnet-tools.json

# Apply migrations (uses the API's configuration: user secrets / environment variables)
dotnet ef database update --project src/ImageStore.Infrastructure --startup-project src/ImageStore.Api

# Add a migration after changing the model
dotnet ef migrations add <Name> --project src/ImageStore.Infrastructure \
  --startup-project src/ImageStore.Api --output-dir Persistence/Migrations

# Idempotent SQL script for DBAs / production pipelines
dotnet ef migrations script --idempotent -o artifacts/migrate.sql \
  --project src/ImageStore.Infrastructure --startup-project src/ImageStore.Api
```

An integration test (`Migrations_Are_In_Sync_With_The_Model`) fails if the model changes
without a new migration.

## Configuration

All settings live in `src/ImageStore.Api/appsettings.json` (safe defaults, no secrets) and can be
overridden per environment, with user secrets, or with environment variables (`:` becomes `__`).

| Setting                                          | Default                       | Description                                                                                         |
| ------------------------------------------------ | ----------------------------- | --------------------------------------------------------------------------------------------------- |
| `ConnectionStrings__ImageStore`                  | *(empty)*                     | SQL Server connection string – **set via env var / secret store**                                    |
| `Database__Provider`                             | `SqlServer`                   | `SqlServer`, or `InMemory` (Development/Testing only)                                                |
| `Database__ApplyMigrationsOnStartup`             | `false` (`true` in Dev)       | Apply pending migrations at start-up                                                                 |
| `Storage__RootPath`                              | `storage/uploads`             | Upload root; relative to the content root, or absolute. Must not be inside `wwwroot` (checked at start-up) |
| `ImageUpload__MaxFileSizeBytes`                  | `10485760` (10 MiB)           | Per-file limit (enforced on the bytes actually received)                                             |
| `ImageUpload__MaxFilesPerRequest`                | `10`                          | Files per upload request                                                                             |
| `ImageUpload__MaxImagePixels`                    | `100000000`                   | Maximum width × height                                                                               |
| `ImageUpload__AllowedExtensions__0..n`           | `.jpg .jpeg .png .webp`       | Subset of the formats the server can verify                                                          |
| `ImageUpload__AllowedContentTypes__0..n`         | `image/jpeg image/png image/webp` | Checked against the type detected from the file signature                                        |
| `AuthCookies__SameSite`                          | `Strict`                      | `Strict`/`Lax`/`None` (`None` requires `SecurePolicy=Always`)                                        |
| `AuthCookies__SecurePolicy`                      | `Always` (`SameAsRequest` in Dev) | Cookies are `__Host-` prefixed when always Secure                                               |
| `AuthCookies__ExpireHours`                       | `8`                           | Sliding session lifetime                                                                             |
| `Cors__AllowedOrigins__0..n`                     | *(none)*                      | Cross-origin SPA hosts (same-origin deployment needs none)                                           |
| `RateLimiting__Authentication__PermitLimit` / `WindowSeconds` | `10` / `60`      | Login/registration attempts per client IP and window                                                 |
| `ForwardedHeaders__Enabled`, `KnownProxies`, `KnownNetworks` | `false`             | Trust `X-Forwarded-For/Proto` from your reverse proxy (see deployment guide)                          |
| `StorageMaintenance__*`                          | enabled, hourly               | Cleans stale staging files; reports (optionally deletes) orphaned files                              |
| `DataProtection__KeysPath`                       | *(empty)*                     | Persist cookie-encryption keys (required in containers / multiple instances)                         |
| `Swagger__Enabled`                               | `false` (`true` in Dev)       | Swagger UI and OpenAPI document                                                                      |

## API overview

All endpoints return JSON; errors use RFC 7807 **Problem Details** (with a `traceId`). Unsafe
requests (POST/PUT/DELETE) require the `X-XSRF-TOKEN` header – see [docs/api.md](docs/api.md).

| Method | Path                         | Auth | Description                                                             |
| ------ | ---------------------------- | ---- | ----------------------------------------------------------------------- |
| GET    | `/api/auth/csrf-token`       | –    | Issues the anti-forgery token (`XSRF-TOKEN` cookie)                     |
| POST   | `/api/auth/register`         | –    | Create an account and sign in (201)                                     |
| POST   | `/api/auth/login`            | –    | Sign in (200 / generic 401)                                             |
| POST   | `/api/auth/logout`           | –    | Sign out (204, idempotent)                                              |
| GET    | `/api/auth/me`               | ✔    | Current user                                                            |
| GET    | `/api/images`                | ✔    | `page`, `pageSize` (≤100), `search`, `tag`, `sortBy`, `sortDirection`   |
| GET    | `/api/images/{id}`           | ✔    | Image metadata                                                          |
| POST   | `/api/images`                | ✔    | Multipart upload (`files`, optional `title`, `description`, `tags`)     |
| PUT    | `/api/images/{id}`           | ✔    | Update title, description, tags                                         |
| DELETE | `/api/images/{id}`           | ✔    | Delete metadata and file (204)                                          |
| GET    | `/api/images/{id}/content`   | ✔    | Image bytes, `inline`, ETag/304, range requests                         |
| GET    | `/api/images/{id}/download`  | ✔    | Image bytes as an attachment with the original file name                |
| GET    | `/api/images/upload-policy`  | ✔    | Upload limits for client-side pre-checks                                |
| GET    | `/api/users`                 | ✔    | Search profiles (`search`, `page`, `pageSize`)                          |
| GET    | `/api/users/me`              | ✔    | Own profile (counts, flags)                                             |
| PUT    | `/api/users/me`              | ✔    | Update display name / bio                                               |
| PUT    | `/api/users/me/profile-picture` | ✔ | Upload/replace profile picture (multipart `file`, ≤ 2 MiB)             |
| DELETE | `/api/users/me/profile-picture` | ✔ | Remove profile picture (204)                                          |
| GET    | `/api/users/{id}`            | ✔    | Public profile of any user                                              |
| GET    | `/api/users/{id}/profile-picture` | ✔ | Picture bytes (ETag/304, 404 when none)                             |
| GET    | `/api/users/{id}/posts`      | ✔    | Published posts – **only for the user and their followers** (else 404)  |
| POST   | `/api/users/{id}/follow`     | ✔    | Follow (204, idempotent; 400 self-follow; 404 unknown user)             |
| DELETE | `/api/users/{id}/follow`     | ✔    | Unfollow (204, idempotent) – revokes access to their posts              |
| GET    | `/api/users/{id}/followers`  | ✔    | Paginated follower list                                                 |
| GET    | `/api/users/{id}/following`  | ✔    | Paginated following list                                                |
| GET    | `/api/feed`                  | ✔    | Chronological feed of followed users' posts (`page`, `pageSize` ≤ 100)  |
| POST   | `/api/posts`                 | ✔    | Publish one of your images (201; 409 if already published)              |
| GET    | `/api/posts/{id}`            | ✔    | Post details (author + followers, else 404)                             |
| PUT    | `/api/posts/{id}`            | ✔    | Edit title/description (own posts only)                                 |
| DELETE | `/api/posts/{id}`            | ✔    | Delete own post (204; comments/reactions cascade, image stays)          |
| GET    | `/api/posts/{id}/content`    | ✔    | Post image bytes, `inline`, ETag/304, ranges (author + followers)       |
| GET    | `/api/posts/{id}/download`   | ✔    | Post image bytes as an attachment (author + followers)                  |
| GET    | `/api/posts/{id}/comments`   | ✔    | Paginated comments, oldest first                                        |
| POST   | `/api/posts/{id}/comments`   | ✔    | Add a comment (201)                                                     |
| PUT    | `/api/posts/{id}/comments/{commentId}` | ✔ | Edit own comment                                            |
| DELETE | `/api/posts/{id}/comments/{commentId}` | ✔ | Delete own comment – or any comment on your own post (moderation) |
| GET    | `/api/posts/{id}/reactions`  | ✔    | Reaction summary (total, per type, own reaction)                        |
| PUT    | `/api/posts/{id}/my-reaction`| ✔    | Set/change your reaction (one per user + post)                          |
| DELETE | `/api/posts/{id}/my-reaction`| ✔    | Remove your reaction (204, idempotent)                                  |
| GET    | `/health`                    | –    | Database and storage health (`Healthy`/`Unhealthy`)                      |

Another user's image always yields the same `404` as a non-existent one – and so does any post
you may not see (not authored, not following).

## Tests

```bash
dotnet test ImageStore.slnx            # unit + integration tests (no SQL Server required)
cd client && npm run test:ci           # Angular unit tests (Vitest + jsdom)
```

Results of the last executed runs:

| Suite                               | Result                                                                  |
| ----------------------------------- | ------------------------------------------------------------------------ |
| Angular unit tests (Vitest)         | **58 passed** (incl. social/post API clients, FollowButton, ReactionBar)  |
| Angular production build            | **passes** with zero errors/warnings                                      |
| `ImageStore.UnitTests`              | 109 passed *before the social extension*; extended by profile, follow, post, comment, reaction and profile-picture unit tests |
| `ImageStore.IntegrationTests`       | 91 passed *before the social extension*; extended by following, feed, post, comment, reaction, social-persistence and storage-persistence suites |
| Browser smoke test (`e2e/`)         | 20/20 checks *before the social extension* (landing is now the feed)      |

> **Note:** the .NET suites for the social features could not be *executed* in the environment
> where they were written (no .NET SDK, no NuGet access). They follow the established, passing
> patterns; run `dotnet test ImageStore.slnx` on a machine with the .NET 10 SDK to verify.

Integration tests host the real API in memory with `WebApplicationFactory`, replace SQL Server
with the EF Core in-memory provider, and give each test class its own temporary storage directory
(deleted afterwards). SQL Server specifics are covered without a server: migrations-vs-model
check, index metadata, and SQL translation of the owner-scoped search query (`ToQueryString`).

Optional browser smoke test against a running app (downloads a test Chrome on `npm ci`):

```bash
cd e2e && npm ci && BASE_URL=http://localhost:4200 npm test
```

## Build environment and package versions

This solution was built and tested offline (no access to nuget.org) with the .NET SDK
**10.0.400** (runtime 10.0.11). NuGet packages were sourced from signature-verified copies, so
`Directory.Packages.props` pins the newest versions available there: EF Core / Identity
**10.0.9**, `Microsoft.AspNetCore.Mvc.Testing` 10.0.7, Swashbuckle 10.2.3, xUnit 2.9.3. All are
stable .NET 10 releases; to adopt the latest servicing patch, bump the versions in
`Directory.Packages.props` (keep all EF Core packages on the same patch) and in
`.config/dotnet-tools.json`.

## Known limitations

- **Thumbnails** are the original images scaled by the browser (`loading="lazy"`); there is no
  server-side resizing, so galleries of large photos transfer full-size files.
- **Search** uses `LIKE '%term%'` (case-insensitive under the default SQL Server collation),
  scoped to the owner first; for very large libraries consider SQL Server full-text search.
- **Single server:** files are on local disk. Multiple instances need shared storage (and shared
  Data Protection keys).
- **No optimistic concurrency** on metadata edits (last write wins).
- **No account management** beyond register/login/logout and profile editing (no email
  confirmation, password reset, 2FA or self-service account deletion). User deletion is blocked
  by foreign keys until the user's images, posts, comments and reactions are removed.
- **Feed is chronological only** by design – no ranking/recommendation algorithms; there are no
  notifications, hashtags or share/repost mechanics.
- **EXIF orientation** is not applied when reporting dimensions (stored pixel dimensions are shown).
- File/metadata consistency is best-effort across two systems; see
  [docs/architecture.md](docs/architecture.md#file-and-database-consistency) for the strategy and
  the remaining edge cases.
