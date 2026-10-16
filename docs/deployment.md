# Deployment guide

## Recommended topology: one origin

Serve the Angular app and the API from the **same origin**. Cookies then stay `SameSite=Strict`,
CSRF protection works without CORS, and the strict Content-Security-Policy applies. The simplest
way is to let the API host the built SPA:

```bash
# 1. Build the SPA into src/ImageStore.Api/wwwroot
cd client && npm ci && npm run build:api && cd ..

# 2. Publish the API (includes wwwroot)
dotnet publish src/ImageStore.Api -c Release -o publish
```

Run `publish/ImageStore.Api` (or `dotnet ImageStore.Api.dll`) behind a TLS-terminating reverse
proxy (nginx, IIS, Caddy, YARP, a cloud load balancer). Unknown client routes fall back to
`index.html`; `/api/*`, `/health` and static assets are served as usual. Uploaded images are never
served as static files.

Alternative: host the SPA elsewhere and have the proxy route `/api/*` to the API on the same
host name. Only if the SPA must live on a different origin, add it to `Cors:AllowedOrigins` and
make sure both sites share the same registrable domain (cookies are `SameSite=Strict`).

## Production configuration

Provide settings through environment variables or a secret store – never commit secrets.

| Variable                                     | Example / guidance                                                                           |
| -------------------------------------------- | -------------------------------------------------------------------------------------------- |
| `ASPNETCORE_ENVIRONMENT`                     | `Production` (enables HSTS + HTTPS redirection; Swagger off; generic error responses)         |
| `ConnectionStrings__ImageStore`              | `Server=sql.internal;Database=ImageStore;User Id=imagestore_app;Password=…;Encrypt=True`    |
| `Storage__RootPath`                          | Absolute path on persistent storage, e.g. `/var/lib/imagestore/uploads` or `/data/uploads`  |
| `DataProtection__KeysPath`                   | Persistent directory, e.g. `/var/lib/imagestore/keys` (otherwise every restart signs all users out) |
| `ForwardedHeaders__Enabled`                  | `true` behind a reverse proxy                                                                |
| `ForwardedHeaders__KnownProxies__0`          | IP of the proxy if it is not on the loopback interface (or `KnownNetworks__0=10.0.0.0/8`)    |
| `ImageUpload__MaxFileSizeBytes` etc.         | Optional limit tuning                                                                        |

`AuthCookies` defaults (`SameSite=Strict`, `SecurePolicy=Always`, 8 h sliding expiration) are
already production-grade. The API refuses to start when the connection string is missing, when
the in-memory provider is configured outside Development, when upload settings are inconsistent,
or when the storage root overlaps `wwwroot`.

### Behind a reverse proxy

TLS usually terminates at the proxy, so without forwarded headers the API would believe requests
are plain HTTP: `Secure` cookies (and the anti-forgery system) would refuse to work, HTTPS
redirection would loop, and rate limiting would see only the proxy's IP. Enable
`ForwardedHeaders__Enabled=true` and list your proxy in `KnownProxies`/`KnownNetworks`
(loopback is trusted by default). Only these proxies' `X-Forwarded-*` headers are honoured.

Example nginx site:

```nginx
server {
    listen 443 ssl http2;
    server_name images.example.com;
    # ssl_certificate / ssl_certificate_key ...

    # Must allow MaxFileSizeBytes x MaxFilesPerRequest + 1 MiB (defaults: 10 MiB x 10 + 1 MiB).
    client_max_body_size 110m;

    location / {
        proxy_pass         http://127.0.0.1:5000;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_request_buffering off;   # stream uploads to the API
    }
}
```

On IIS, raise `maxAllowedContentLength` (in bytes) in `web.config` accordingly.

## Database migrations in production

Keep `Database__ApplyMigrationsOnStartup=false` in production and apply migrations as a
deployment step, with an account that has DDL rights (the application account only needs
read/write on the tables):

```bash
dotnet tool restore

# Option A – idempotent SQL script, reviewed and executed by your pipeline or DBA
dotnet ef migrations script --idempotent -o artifacts/migrate.sql \
  --project src/ImageStore.Infrastructure --startup-project src/ImageStore.Api

# Option B – self-contained migration bundle
dotnet ef migrations bundle --self-contained -r linux-x64 -o artifacts/efbundle \
  --project src/ImageStore.Infrastructure --startup-project src/ImageStore.Api
./artifacts/efbundle --connection "<connection string>"
```

## Persistent storage and backups

- The process must be able to create directories and files under `Storage__RootPath`; nothing
  else should be able to write there. The application creates directories `0750` and files
  `0640` on Unix. The root holds uploaded images (`{ownerUserId}/{imageId}.ext`), profile
  pictures (`profiles/{userId}.ext`) and the temporary `.staging/` directory.
- Only **relative** paths are stored in the database, so the root can live anywhere the process
  can reach – nothing is tied to a machine-specific absolute path.
- **Back up the database and the storage directory together** (same point in time, or the
  database first followed by the files). The storage directory deserves its *own* backup job
  (e.g. nightly `rsync`/snapshot of `Storage__RootPath` to a different disk or object storage)
  because database backups alone never contain the image bytes. After restoring an older
  database, files uploaded later become "orphans": they are reported by the storage maintenance
  job and are only deleted automatically if you enable `StorageMaintenance__DeleteOrphanedFiles=true`.
- Durability follows the platform: redeployments that replace the app directory do not touch the
  storage root, but ephemeral disks or container filesystems *do* lose files – mount a volume
  (see Docker below) and verify a restore before claiming durability.
- Monitor the logs for `orphaned` and `missing from storage` messages and use `/health` for
  liveness/readiness probes.

### Moving files between storage roots

Because the database stores only relative paths, moving to a new root (bigger disk, new machine,
volume) is a copy operation, not a data migration:

1. Stop the API (or accept a short read-only window); note that in-flight uploads land in
   `.staging/` and are cleaned up automatically.
2. Copy the **entire** tree, preserving structure:
   `rsync -a /old/root/ /new/root/` (trailing slashes matter).
3. Point `Storage__RootPath` at the new root and start the API; verify `/health`, a gallery page
   and one profile picture.
4. Keep the old root until the next successful backup of the new one, then remove it.

No database change is needed at any point – rows never reference the absolute root.

### Deletions and what remains on disk

- **Deleting a post** removes the post row and cascades its comments and reactions, but the
  image (row *and* file) stays in the owner's library; it can be published again.
- **Deleting an image** removes its post (with comments/reactions) via cascade, deletes the
  database row first and then the file; a failed file delete leaves a reported orphan.
- **Removing a profile picture** deletes the file under `profiles/` after the database commit.
- **Deleting an account** is blocked (foreign keys are `Restrict`) while the user still owns
  posts, comments or reactions, or while follow relationships reference them – remove that
  content first; the profile and its picture then cascade with the account.

### Docker (optional)

No container image is included, but the application is container-friendly. Mount a volume for
uploads and Data Protection keys so they survive container replacement:

```bash
docker volume create imagestore-data

docker run -d --name imagestore -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ASPNETCORE_URLS=http://+:8080 \
  -e ConnectionStrings__ImageStore="Server=sql;Database=ImageStore;User Id=imagestore_app;Password=…;Encrypt=True" \
  -e Storage__RootPath=/data/uploads \
  -e DataProtection__KeysPath=/data/keys \
  -e ForwardedHeaders__Enabled=true \
  -e ForwardedHeaders__KnownNetworks__0=172.16.0.0/12 \
  -v imagestore-data:/data \
  imagestore-api:latest
```

A minimal image can be built from the published output:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY publish/ .
USER $APP_UID
ENTRYPOINT ["dotnet", "ImageStore.Api.dll"]
```

(The volume must be writable by the container user.)

## Scaling out

The application targets a single server. Running several instances additionally requires:
shared storage with atomic rename semantics for `Storage__RootPath`, a shared Data Protection key
ring (e.g. a shared directory or a database/Redis key store), a distributed rate limiter if
per-instance limits are not acceptable, and running the storage maintenance job on one instance
only (`StorageMaintenance__Enabled=false` elsewhere).

## Security checklist

- [ ] HTTPS only; HSTS is enabled automatically outside Development.
- [ ] `ForwardedHeaders` configured with the exact proxy addresses.
- [ ] Connection string and other secrets in environment variables / a secret store.
- [ ] Least-privilege SQL login for the application; migrations run with a separate account.
- [ ] `Storage__RootPath` on persistent storage, outside the web root, backed up with the database.
- [ ] `DataProtection__KeysPath` persistent and protected (on Linux consider `ProtectKeysWithCertificate`).
- [ ] Swagger disabled (default outside Development) and `Cors:AllowedOrigins` empty for same-origin hosting.
- [ ] Reverse-proxy body-size limit aligned with the upload limits.
- [ ] Optional: antivirus / content scanning of uploads (not included).
