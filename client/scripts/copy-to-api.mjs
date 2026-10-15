// Copies the production build into the API's wwwroot for single-server deployments
// (the API then serves the SPA and the REST endpoints from the same origin).
import { cpSync, existsSync, rmSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = resolve(clientRoot, 'dist/image-store-client/browser');
const target = resolve(clientRoot, '../src/ImageStore.Api/wwwroot');

if (!existsSync(source)) {
  console.error(`Build output not found at ${source}. Run "npm run build" first.`);
  process.exit(1);
}

rmSync(target, { recursive: true, force: true });
cpSync(source, target, { recursive: true });
console.log(`Copied ${source} -> ${target}`);
