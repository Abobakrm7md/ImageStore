# Dark editorial UI

## Design and implementation

The existing Angular application now uses a shared charcoal/violet system in
`client/src/styles.css`, with tonal surfaces, consistent focus rings, spacing,
container widths, corners and motion. The shell (`app.*`) retains the existing
routes and cookie-authenticated session, with desktop navigation, a compact mobile
menu, notification badge and account disclosure.

- **Feed / PostCard / ReactionBar:** ordered responsive image-first grid; a wider
  leading image on large screens, stable cover thumbnails, secondary identity and
  actions, local SVG reaction controls, existing paging/empty/retry states.
- **Gallery / ImageLightbox / image and post details:** native full-screen dialog,
  uncropped fit and actual-size modes, collection navigation, arrows/Z/Escape,
  browser focus trapping and restoration, scroll locking, loading and retry.
- **People / profiles / account / authentication / upload / new post:** shared
  dark controls and spacing, responsive identity/forms and private-library picker.
  Existing drag/drop, validation, progress and submission contracts remain intact.
- **Notifications:** activity/type filters, distinct unread rows, category icons,
  bulk-action feedback and a collapsible preferences panel. Inbox persistence,
  category policy, sound deduplication and realtime services are unchanged.
- **Chat:** responsive inbox/conversation panes, mobile back navigation, readable
  bubbles, a compact mobile active header, scroll-to-latest and older-history
  position preservation. Empty keyboard submissions are ignored; retry payloads
  and IDs, membership and shared connection remain unchanged.
- **Shared controls:** local SVG icons, native dialogs, accessible pagination names,
  toasts, avatars, empty states, skeletons and reduced-motion support.

No new production dependencies, remote fonts, image assets, API contracts or
backend changes are introduced by the redesign. The existing Puppeteer e2e
package is reused for the UI fixture harness. Additional temporary Chromium
launch tooling was installed outside the repository for sandbox validation only.

## Validation (2026-10-16)

- `npm run build --prefix client`: successful production compilation.
- `npm run test:ci --prefix client`: **82 tests / 7 files passing**, including new
  viewer scroll-lock, close, collection-keyboard, retry, zoom-reset and chat checks.
- `git diff --check`: clean. There is no lint script in the client package.
- Chromium layout matrix: **16 routes × 6 widths = 96 checks** at 375, 430, 768,
  1024, 1366 and 1440px. Includes feed, gallery, image detail, upload, new post,
  post detail/comments, people, profile, followers, following, account,
  notifications, selected chat, 404, login and registration. No horizontal page
  overflow or uncaught JavaScript exceptions in this matrix. Desktop/mobile
  screenshots of the main compositions were inspected.
- Browser interactions: gallery open/arrow navigation/fit/actual-size/Escape,
  scroll restoration and thumbnail focus return; mobile menu/Escape; reaction
  picker; unread filter; chat initial latest scroll, composer visibility and
  back/select; unsupported upload feedback. Additional checks exercise feed
  loading/error/retry/empty, pagination and broken-image viewer retry.

**These browser checks use test-only intercepted JSON responses and SVG images.**
They validate client rendering and interactions, not real authentication,
persistence, network delivery, SQL, upload storage, sound audibility or SignalR.
The simulated hub negotiation deliberately fails; the disconnected/retrying UI
is expected. No fixtures or request interception are shipped in the client.

.NET and Docker are unavailable in this sandbox. Full-stack API/SignalR and
real-image/upload tests still need to run in an environment with the existing
backend and external SQL Server. Device keyboard behavior, screen-reader output
and non-Chromium browsers also need manual testing.

## Repeat the UI checks

```sh
npm ci --prefix client
npm start --prefix client -- --host 0.0.0.0 --allowed-hosts
# In another terminal, with a usable Chromium installation:
npm ci --prefix e2e
npm run test:ui --prefix e2e
```

Optional environment variables: `UI_BASE_URL` (default `http://localhost:4200`),
`CHROME_BIN` (custom Chromium executable), `UI_SCREENSHOTS=1` (desktop/mobile
screenshots). Results are written to git-ignored `artifacts/ui/`. Browser runtime
libraries must be present. `npm test --prefix e2e` remains the separate **real
full-stack** smoke test; it was not run against a live backend for this redesign.
