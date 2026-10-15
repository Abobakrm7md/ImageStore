// Browser smoke test of the complete ImageStore stack: auth, uploads, gallery, editing, deletion,
// responsive layout and sign-out, driven through a real (headless) Chrome.
//
//   BASE_URL                   App URL (default http://127.0.0.1:4200, i.e. "npm start" in client/)
//   PUPPETEER_EXECUTABLE_PATH  Use an existing Chrome/Chromium instead of the downloaded one
//   E2E_NO_SANDBOX=1           Disable the Chrome sandbox (needed in some containers/CI runners)
//   E2E_SCREENSHOTS            Directory for screenshots (default ./screenshots)
import puppeteer from 'puppeteer';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const FIXTURES = path.join(HERE, 'fixtures');
const BASE = (process.env.BASE_URL ?? 'http://127.0.0.1:4200').replace(/\/$/, '');
const SHOTS = process.env.E2E_SCREENSHOTS ?? path.join(HERE, 'screenshots');
fs.mkdirSync(SHOTS, { recursive: true });

const consoleErrors = [];
const failedRequests = [];
const results = [];
const check = (name, ok, detail = '') => {
  results.push({ name, ok, detail });
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ' — ' + detail : ''}`);
};

const browser = await puppeteer.launch({
  headless: true,
  args: ['--lang=en-US', ...(process.env.E2E_NO_SANDBOX === '1' ? ['--no-sandbox', '--disable-dev-shm-usage'] : [])],
  defaultViewport: { width: 1280, height: 860, deviceScaleFactor: 1 },
});
const page = await browser.newPage();
page.on('console', (msg) => { if (msg.type() === 'error') consoleErrors.push(msg.text()); });
page.on('pageerror', (err) => consoleErrors.push('pageerror: ' + err.message));
page.on('response', (res) => {
  if (res.status() >= 500) failedRequests.push(`${res.status()} ${res.request().method()} ${res.url()}`);
});

const waitText = (text, timeout = 10000) =>
  page.waitForFunction((t) => document.body.innerText.includes(t), { timeout }, text);
// Clears a field the way a user would (Angular's value accessor listens to "input" events).
const clearField = (selector) =>
  page.$eval(selector, (el) => {
    el.value = '';
    el.dispatchEvent(new Event('input', { bubbles: true }));
  });
const shot = (name) => page.screenshot({ path: `${SHOTS}/${name}.png`, fullPage: true });

try {
  // 1. Guard redirects anonymous users to the login page.
  await page.goto(`${BASE}/images`, { waitUntil: 'networkidle0' });
  await page.waitForSelector('form input[type=email]');
  check('anonymous user is redirected to login', page.url().includes('/login?returnUrl=%2Fimages'), page.url());
  await shot('01-login');

  // 2. Register (client-side validation first).
  await page.goto(`${BASE}/register`, { waitUntil: 'networkidle0' });
  await page.click('button[type=submit]');
  await waitText('Email is required.');
  check('register form shows client-side validation', true);
  const email = `e2e-${Date.now()}@example.com`;
  await page.type('#register-email', email);
  await page.type('#register-password', 'weakpass');
  await page.type('#register-confirm', 'different');
  await page.click('#register-email');
  await waitText('Use upper- and lower-case letters and at least one digit.');
  await waitText('The passwords do not match.');
  check('password policy and confirmation are validated', true);
  await shot('02-register-validation');
  await clearField('#register-password');
  await page.type('#register-password', 'Gallery2026');
  await clearField('#register-confirm');
  await page.type('#register-confirm', 'Gallery2026');
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.click('button[type=submit]')]);
  await waitText('No images yet');
  check('registration signs in and shows the empty gallery', page.url().endsWith('/images'), page.url());
  await shot('03-empty-gallery');

  // 3. Upload: three valid images + one fake image.
  await page.goto(`${BASE}/images/upload`, { waitUntil: 'networkidle0' });
  await page.waitForSelector('input[type=file]');
  const input = await page.$('input[type=file]');
  await input.uploadFile(
    ...['sunset-over-hills.jpg', 'green-valley.png', 'moonlit-ridge.webp', 'not-really-an-image.png'].map((name) => path.join(FIXTURES, name)),
  );
  await page.waitForFunction(() => document.querySelectorAll('.upload-item').length === 4);
  await page.type('#upload-description', 'Landscapes from the E2E run');
  await page.type('#upload-tags', 'landscape, e2e');
  await shot('04-upload-queue');
  await page.click('.upload-footer .btn-primary');
  await page.waitForFunction(() => document.querySelectorAll('.upload-item.status-failed, .upload-item.status-rejected, .upload-item.status-done').length === 4, { timeout: 20000 });
  const statuses = await page.$$eval('.upload-item', (items) => items.map((i) => i.className.match(/status-(\w+)/)?.[1]));
  const failedMessage = await page.$eval('.upload-item.status-rejected .field-error', (el) => el.textContent.trim());
  check('valid files upload, the fake image is rejected by the server', JSON.stringify(statuses) === JSON.stringify(['done', 'done', 'done', 'rejected']), `${statuses} / "${failedMessage}"`);
  await shot('05-upload-results');

  // 4. Gallery shows the images, loaded through the authorized content endpoint.
  await page.goto(`${BASE}/images`, { waitUntil: 'networkidle0' });
  await page.waitForFunction(() => {
    const imgs = [...document.querySelectorAll('.image-card img')];
    return imgs.length === 3 && imgs.every((img) => img.complete && img.naturalWidth > 0);
  }, { timeout: 15000 });
  const titles = await page.$$eval('.card-title', (els) => els.map((e) => e.textContent.trim()));
  check('gallery renders 3 cards with loaded thumbnails', titles.length === 3, titles.join(', '));
  await shot('06-gallery');

  // 5. Search (debounced, reflected in the URL) and tag filter.
  await page.type('input[type=search]', 'valley');
  await page.waitForFunction(() => location.search.includes('q=valley') && document.querySelectorAll('.image-card').length === 1, { timeout: 8000 });
  check('search filters the gallery and updates the URL', true, await page.evaluate(() => location.search));
  await clearField('input[type=search]');
  await page.waitForFunction(() => document.querySelectorAll('.image-card').length === 3, { timeout: 8000 });
  await page.click('.image-card .tag');
  await page.waitForFunction(() => location.search.includes('tag=') && document.querySelector('.tag-active'), { timeout: 8000 });
  check('clicking a tag filters by tag', true, await page.evaluate(() => location.search));
  await page.goto(`${BASE}/images`, { waitUntil: 'networkidle0' });

  // 6. Details page: edit metadata.
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.click('.image-card .thumb')]);
  await page.waitForSelector('#edit-title');
  await clearField('#edit-title');
  await page.type('#edit-title', 'Edited in the browser');
  await page.click('button[type=submit]');
  await waitText('Changes saved.');
  const heading = await page.$eval('.detail-title', (el) => el.textContent.trim());
  check('editing metadata saves and refreshes the details', heading === 'Edited in the browser', heading);
  const facts = await page.$eval('.facts', (el) => el.innerText.replace(/\s+/g, ' '));
  check('details show size, type and dimensions', /Dimensions \d+ × \d+ px/.test(facts) && facts.includes('image/'), facts);
  await shot('07-details');

  // 7. Lightbox opens and closes.
  await page.click('.preview-button');
  await page.waitForSelector('dialog.lightbox[open] img');
  check('large view opens in a lightbox', true);
  await shot('08-lightbox');
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('dialog.lightbox[open]'));

  // 8. Delete with confirmation.
  await page.click('.side-actions .btn-danger');
  await page.waitForSelector('dialog.dialog[open]');
  await shot('09-confirm-delete');
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.click('dialog.dialog[open] .btn-danger')]);
  await page.waitForFunction(() => document.querySelectorAll('.image-card').length === 2, { timeout: 10000 });
  check('delete removes the image after confirmation', true);

  // 9. Mobile layout.
  await page.setViewport({ width: 390, height: 844, deviceScaleFactor: 2, isMobile: true });
  await page.reload({ waitUntil: 'networkidle0' });
  await page.waitForSelector('.image-card img');
  await shot('10-mobile-gallery');
  const actionsFit = await page.$$eval('.image-card', (cards) =>
    cards.every((card) => {
      const box = card.getBoundingClientRect();
      return [...card.querySelectorAll('.card-actions .btn')].every((button) => {
        const rect = button.getBoundingClientRect();
        return rect.width > 0 && rect.left >= box.left - 0.5 && rect.right <= box.right + 0.5;
      });
    }),
  );
  check('all card actions (incl. delete) fit inside the cards on mobile', actionsFit);
  await page.click('.menu-toggle');
  await page.waitForSelector('.main-nav.open');
  await shot('11-mobile-menu');
  check('mobile layout renders with a collapsible menu', true);
  await page.setViewport({ width: 1280, height: 860 });

  // 10. Logout, then protected pages redirect to login again.
  await page.goto(`${BASE}/images`, { waitUntil: 'networkidle0' });
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.click('.main-nav .btn-ghost')]);
  check('sign out returns to the login page', page.url().includes('/login'), page.url());
  await page.goto(`${BASE}/images/upload`, { waitUntil: 'networkidle0' });
  check('after sign out, protected routes redirect to login', page.url().includes('/login'), page.url());

  // 11. Login again with the same account.
  await page.type('#login-email', email);
  await page.type('#login-password', 'Gallery2026');
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.click('button[type=submit]')]);
  check('login returns to the requested page', page.url().endsWith('/images/upload'), page.url());

  // 12. Unknown route.
  await page.goto(`${BASE}/definitely/not/here`, { waitUntil: 'networkidle0' });
  await waitText('Page not found');
  check('unknown routes show the not-found page', true);
} catch (error) {
  check('unexpected failure', false, error.message);
  await shot('99-failure').catch(() => {});
} finally {
  await browser.close();
}

const relevantErrors = consoleErrors.filter((e) => !/status of 40[01]/.test(e));
check('no browser console errors (besides expected 400/401 responses)', relevantErrors.length === 0, relevantErrors.join(' | '));
check('no server errors (5xx)', failedRequests.length === 0, failedRequests.join(' | '));
const failed = results.filter((r) => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} checks passed`);
process.exit(failed ? 1 : 0);
