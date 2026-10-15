# ImageStore client (Angular)

The Angular front end of ImageStore. See the [repository README](../README.md) for the full
architecture, configuration and run instructions.

```bash
npm ci                 # install exact dependency versions
npm start              # dev server on http://localhost:4200 (proxies /api to http://localhost:5080)
npm run test:ci        # unit tests (Vitest)
npm run build          # production build in dist/image-store-client/browser
npm run build:api      # production build copied to ../src/ImageStore.Api/wwwroot (single-server deployment)
```
