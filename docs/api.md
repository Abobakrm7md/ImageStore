# API reference

Base path: `/api`. Interactive documentation: **`/swagger`** (enabled in Development, or with
`Swagger__Enabled=true`). Swagger UI sends the anti-forgery header automatically after you call
`GET /api/auth/csrf-token`.

## Conventions

- **Authentication:** an HttpOnly session cookie set by `register`/`login`. Send requests from the
  same origin (or allow your origin via `Cors:AllowedOrigins` and send credentials).
- **CSRF:** every `POST`, `PUT` and `DELETE` must include the header `X-XSRF-TOKEN` with the value
  of the `XSRF-TOKEN` cookie. Obtain it with `GET /api/auth/csrf-token`; it is refreshed
  automatically after login, registration and logout.
- **Errors:** RFC 7807 Problem Details (`application/problem+json`), always with a `traceId`.
  Validation errors list field errors under `errors` with camelCase keys:

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    "title": "One or more validation errors occurred.",
    "status": 400,
    "errors": { "files[1]": ["'evil.png': The file content is not a supported image format (JPEG, PNG or WebP)."] },
    "traceId": "00-…"
  }
  ```

- **Timestamps** are ISO-8601 UTC (`2026-10-15T10:43:10.1107Z`). **Enums** are camelCase strings.

| Status | Meaning                                                                                 |
| ------ | --------------------------------------------------------------------------------------- |
| 400    | Validation error, malformed request, or invalid/missing anti-forgery token (`code: "antiforgery_validation_failed"`) |
| 401    | Not authenticated (or invalid credentials on login)                                     |
| 404    | Not found – also returned for resources owned by/hidden from another user (images, posts, comments) |
| 409    | Concurrent conflicting change; retry                                                    |
| 413    | Upload request larger than the configured limits                                        |
| 415    | Wrong content type (e.g. JSON sent to the upload endpoint)                              |
| 429    | Too many authentication attempts (`Retry-After` header)                                 |

## Authentication

### `GET /api/auth/csrf-token` → `204`
Sets the HttpOnly anti-forgery cookie and the readable `XSRF-TOKEN` cookie.

### `POST /api/auth/register` → `201`
```json
{ "email": "alice@example.com", "password": "Passw0rd123" }
```
Creates the account and signs it in. Response: `{ "id": "…", "email": "alice@example.com" }`.
`400` for invalid email, weak password (≥ 8 characters, upper- and lower-case letter and digit)
or an email already in use.

### `POST /api/auth/login` → `200`
```json
{ "email": "alice@example.com", "password": "Passw0rd123", "rememberMe": false }
```
`rememberMe: true` makes the session cookie persistent. Wrong credentials, unknown accounts and
locked accounts (5 failures → 15 minutes) all receive the same generic `401`.

### `POST /api/auth/logout` → `204`
Clears the session. Succeeds even if the session has already expired.

### `GET /api/auth/me` → `200`
`{ "id": "…", "email": "alice@example.com" }` or `401`.

## Images

All image endpoints require authentication and only ever see the caller's images.

### Image resource
```json
{
  "id": "01a13f28-a2bf-7ff6-9409-e3a28b48d8cb",
  "title": "Beach sunset",
  "description": "Summer trip",
  "originalFileName": "sunset.jpg",
  "contentType": "image/jpeg",
  "fileSizeBytes": 32264,
  "width": 1200,
  "height": 800,
  "tags": ["beach", "holiday"],
  "createdAtUtc": "2026-10-15T10:43:10.1107Z",
  "updatedAtUtc": null
}
```
No file-system paths are ever returned.

### `GET /api/images` → `200`

| Query parameter | Default                         | Notes                                     |
| --------------- | ------------------------------- | ----------------------------------------- |
| `page`          | `1`                             | ≥ 1                                       |
| `pageSize`      | `12`                            | 1–100                                     |
| `search`        | –                               | Case-insensitive match in title or description (max 200 chars; `%`, `_`, `[` are literal) |
| `tag`           | –                               | Exact tag (case-insensitive)              |
| `sortBy`        | `createdAt`                     | `createdAt`, `title`, `fileSize`          |
| `sortDirection` | `desc` for dates, else `asc`    | `asc`, `desc`                             |

```json
{ "items": [ { "id": "…", "title": "…" } ], "page": 1, "pageSize": 12, "totalCount": 37, "totalPages": 4 }
```

### `GET /api/images/{id}` → `200` / `404`

### `POST /api/images` → `201`
`multipart/form-data`:

| Field         | Required | Notes                                                                         |
| ------------- | -------- | ----------------------------------------------------------------------------- |
| `files`       | yes      | One or more files (repeat the field); max `ImageUpload:MaxFilesPerRequest`    |
| `title`       | no       | Applied to every file; defaults to each file name without extension (≤ 200)   |
| `description` | no       | ≤ 2000 characters                                                             |
| `tags`        | no       | Repeat the field for several tags (≤ 20 tags, ≤ 50 characters each)           |

Each file must have an allowed extension, be at most `MaxFileSizeBytes`, and its **content** must
be a valid JPEG, PNG or WebP of the same type as its extension. If any file is invalid, nothing is
stored and the response is `400` with one error per file (`files[0]`, `files[1]`, …).

Response (`Location` header set for single-file uploads):
```json
{ "items": [ { "id": "…", "title": "sunset", "contentType": "image/jpeg", "…": "…" } ] }
```

### `PUT /api/images/{id}` → `200`
```json
{ "title": "Beach sunset", "description": "Summer trip", "tags": ["beach", "holiday"] }
```
`title` is required. `tags` replaces the complete tag list (omit or `[]` to remove all tags);
an empty `description` clears it. File data and ownership cannot be changed. Returns the updated
image.

### `DELETE /api/images/{id}` → `204`
Deletes the metadata, then the stored file.

### `GET /api/images/{id}/content` → `200`
The image bytes for display: `Content-Type` of the verified format, `Content-Disposition: inline`,
`Cache-Control: private, no-cache`, `ETag` (send `If-None-Match` for `304`), `Accept-Ranges`.

### `GET /api/images/{id}/download` → `200`
Same bytes with `Content-Disposition: attachment; filename*=UTF-8''<original name>`.

### `GET /api/images/upload-policy` → `200`
```json
{
  "maxFileSizeBytes": 10485760, "maxFilesPerRequest": 10,
  "allowedExtensions": [".jpg", ".jpeg", ".png", ".webp"],
  "allowedContentTypes": ["image/jpeg", "image/png", "image/webp"],
  "titleMaxLength": 200, "descriptionMaxLength": 2000, "maxTagsPerImage": 20, "tagMaxLength": 50
}
```

## Users & following

All endpoints require authentication. Every `isFollowing` flag in a response is **viewer-relative**:
it describes whether the *calling* user follows the given user.

### User summary (list item)
```json
{
  "id": "01a13f28-a2bf-7ff6-9409-e3a28b48d8cb",
  "userName": "alice",
  "displayName": "Alice Example",
  "hasProfilePicture": true,
  "isFollowing": false
}
```

### `GET /api/users` → `200`
Searches profiles. Query: `page`, `pageSize` (default 12, max 100), `search` (optional,
case-insensitive substring of user name or display name, max 100 chars; `%`, `_`, `[` are
literal). Ordered by user name. Response: `PagedResult<UserSummary>`.

### `GET /api/users/me` → `200` / `PUT /api/users/me` → `200`
The caller's own profile. `PUT` body: `{ "displayName": "Alice", "bio": "Photographer" }`
(display name required, ≤ 100; bio optional ≤ 500, empty clears). The user name cannot change.
Returns the updated profile.

### Profile resource
```json
{
  "id": "…", "userName": "alice", "displayName": "Alice Example", "bio": "Photographer",
  "hasProfilePicture": true, "followerCount": 12, "followingCount": 7, "postCount": 3,
  "isFollowing": false, "isCurrentUser": false, "createdAtUtc": "2026-10-15T10:43:10.1107Z"
}
```
`postCount` is only meaningful when the caller may see the posts (the user themself or a
follower); for everyone else it is `0`.

### `GET /api/users/{id}` → `200` / `404`

### `PUT /api/users/me/profile-picture` → `200`
`multipart/form-data` with a single `file` field: JPEG, PNG, WebP or GIF, ≤ 2 MiB, verified by
content (not just extension). Replaces any existing picture. Returns the updated profile.

### `DELETE /api/users/me/profile-picture` → `204`
Removes the picture; the profile falls back to an initials avatar.

### `GET /api/users/{id}/profile-picture` → `200` / `404`
Streams the picture bytes with `Cache-Control: private, no-cache` and an `ETag` that changes when
the picture is replaced (send `If-None-Match` for `304`). `404` when the user has no picture.
Pictures are never exposed as public static files.

### `POST /api/users/{id}/follow` → `204`
Follows a user. Idempotent: following again is not an error. `400` when `{id}` is the caller
(self-follow), `404` when the user does not exist.

### `DELETE /api/users/{id}/follow` → `204`
Unfollows. Idempotent: unfollowing a user you do not follow also returns `204`. Unfollowing
immediately revokes feed access, post details, image bytes, comments and reactions of that
user's posts (they answer `404` afterwards).

### `GET /api/users/{id}/followers` / `GET /api/users/{id}/following` → `200` / `404`
Paginated `PagedResult<UserSummary>`, newest follow first.

### `GET /api/users/{id}/posts` → `200` / `404`
The user's published posts, newest first, as `PagedResult<ImagePost>`. **`404` unless the caller
is the user or follows them** – the published gallery is private to followers.

## Feed

### `GET /api/feed` → `200`
The personalized feed: posts of everyone the caller follows, **strictly newest first**
(`createdAtUtc DESC, id DESC` – deterministic, no ranking algorithm), server-side paginated with
`page`/`pageSize` (default 10, max 100). Every item includes author info, comment count, total
and per-type reaction counts and the caller's own reaction. Following/unfollowing changes the
feed immediately; no duplicates or gaps across pages for stable data.

## Posts

A post publishes one image from the caller's library to their followers. Visibility everywhere
(details, image bytes, download, comments, reactions): **the author or a follower** – everyone
else gets `404` (not `403`, to avoid leaking existence).

### Post resource
```json
{
  "id": "…", "title": "Beach sunset", "description": "Summer trip",
  "createdAtUtc": "2026-10-15T10:43:10.1107Z", "updatedAtUtc": null,
  "author": { "id": "…", "userName": "alice", "displayName": "Alice Example", "hasProfilePicture": true },
  "image": { "imageId": "…", "contentType": "image/jpeg", "width": 1200, "height": 800 },
  "commentCount": 2, "reactionCount": 3,
  "reactionCounts": [ { "reaction": "like", "count": 2 }, { "reaction": "love", "count": 1 },
                      { "reaction": "laugh", "count": 0 }, { "reaction": "wow", "count": 0 },
                      { "reaction": "sad", "count": 0 }, { "reaction": "dislike", "count": 0 } ],
  "myReaction": "love", "isMine": false
}
```

### `POST /api/posts` → `201`
```json
{ "imageId": "…", "title": "Beach sunset", "description": "Summer trip" }
```
`imageId` must be an image **owned by the caller** (anything else is `404`). `title` defaults to
the image title (≤ 200); `description` optional (≤ 2000). Each image can only be published once:
`409` if it already has a post. `Location` header points at the new post.

### `GET /api/posts/{id}` → `200` / `404`

### `PUT /api/posts/{id}` → `200` / `404`
Edits title/description of **your own** post (the image cannot be replaced). Anyone else gets
`404`.

### `DELETE /api/posts/{id}` → `204` / `404`
Deletes your own post together with all its comments and reactions (database cascade). The
underlying image **stays in your library** (with its file); it simply becomes unpublished and can
be posted again.

### `GET /api/posts/{id}/content` → `200` / `404`
The post image bytes for display: verified `Content-Type`, `Content-Disposition: inline`,
`Cache-Control: private, no-cache`, `ETag`, `Accept-Ranges`. Same visibility rules as the post –
unfollowing revokes access. There are no public/static image URLs.

### `GET /api/posts/{id}/download` → `200` / `404`
Same bytes with `Content-Disposition: attachment`.

## Comments

### Comment resource
```json
{
  "id": "…", "postId": "…",
  "author": { "id": "…", "userName": "bob", "displayName": "Bob", "hasProfilePicture": false },
  "content": "Great shot!", "createdAtUtc": "…", "updatedAtUtc": null,
  "isMine": true, "canDelete": true
}
```
`isMine`: written by the caller (edit + delete). `canDelete`: the caller is the comment author
**or the post author** (moderation – post owners may delete any comment on their posts, but never
edit someone else's).

### `GET /api/posts/{postId}/comments` → `200` / `404`
Oldest first, paginated (default 20, max 100). Requires post access.

### `POST /api/posts/{postId}/comments` → `201`
`{ "content": "Great shot!" }` – required, non-whitespace, ≤ 2000 characters. Requires post
access (commenting on posts you cannot see is `404`).

### `PUT /api/posts/{postId}/comments/{commentId}` → `200` / `404`
Replaces the content; **author only** (post owners cannot edit foreign comments).

### `DELETE /api/posts/{postId}/comments/{commentId}` → `204` / `404`
By the comment author or the post author (moderation).

## Reactions

Six stable reaction types (stored as an enum, serialized camelCase): `like`, `love`, `laugh`,
`wow`, `sad`, `dislike`. Each user has **at most one active reaction per post**, enforced by a
unique index on `(postId, userId)`.

### `GET /api/posts/{postId}/reactions` → `200` / `404`
```json
{ "totalCount": 3, "counts": [ { "reaction": "like", "count": 2 }, … ], "myReaction": "love" }
```
`counts` always lists all six types (zeros included). Requires post access.

### `PUT /api/posts/{postId}/my-reaction` → `200` / `400` / `404`
`{ "reaction": "wow" }` – adds the reaction, or **changes** an existing one (still exactly one
row). Unknown values, numbers or `null` are `400`. Safe under duplicate/concurrent requests: a
uniqueness violation is retried once and answered idempotently. Returns the fresh summary.

### `DELETE /api/posts/{postId}/my-reaction` → `204` / `404`
Removes the caller's reaction; idempotent (`204` even without a reaction).

## Health


### `GET /health` → `200 Healthy` / `503 Unhealthy`
Checks database connectivity and that the storage root is writable. No details are exposed.

## Example session with curl

```bash
BASE=http://localhost:5080
JAR=cookies.txt
xsrf() { awk '$6 == "XSRF-TOKEN" { print $7 }' "$JAR" | tail -1; }

curl -s -c "$JAR" -b "$JAR" "$BASE/api/auth/csrf-token"
curl -s -c "$JAR" -b "$JAR" -H "X-XSRF-TOKEN: $(xsrf)" -H 'Content-Type: application/json' \
     -d '{"email":"alice@example.com","password":"Passw0rd123"}' "$BASE/api/auth/register"

curl -s -c "$JAR" -b "$JAR" -H "X-XSRF-TOKEN: $(xsrf)" \
     -F "files=@holiday.jpg" -F "tags=travel" "$BASE/api/images"

curl -s -b "$JAR" "$BASE/api/images?search=holiday&page=1&pageSize=12"

# publish an image as a post, follow someone, read the feed
curl -s -c "$JAR" -b "$JAR" -H "X-XSRF-TOKEN: $(xsrf)" -H 'Content-Type: application/json' \
     -d '{"imageId":"<image-id>","title":"Beach sunset"}' "$BASE/api/posts"
curl -s -c "$JAR" -b "$JAR" -H "X-XSRF-TOKEN: $(xsrf)" -X POST "$BASE/api/users/<user-id>/follow"
curl -s -b "$JAR" "$BASE/api/feed?page=1&pageSize=10"
```
