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
| 404    | Not found – also returned for images owned by another user                              |
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
```
