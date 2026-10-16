# Persistent notifications

## What ships

The main layout's **Notifications** bell shows the authoritative unread count. The notification
center provides All/Unread/type filters, server-side pagination, mark-read/all, delete/clear-read,
relative timestamps and preferences. It uses the same Identity session, antiforgery, profile/post
access rules, external SQL Server, API and **single existing `/api/chat` SignalR connection**.
The existing transient `NotificationService` remains the toast renderer; the new `InboxService`
manages persistent activity. No new broker, database, SignalR connection, or remote audio asset.

Supported types (stable enum IDs 1–8; JSON camel-case strings):

| Type | Recipient / event policy |
|---|---|
| `chatMessage` | Other participant; one event per persisted message UUID |
| `sharedPost` | Other participant; replaces, not duplicates, the chat-message notice |
| `newFollower` | Followed user; one notification for the lifetime follower/recipient pair |
| `postComment` | Post author, unless actor is the author |
| `postReaction` | Post author; one lifetime notice per actor/post, not one per reaction change/retry |
| `commentReply` | Parent comment author, if still able to view the post |
| `commentMention` | Up to 20 distinct valid `@username` mentions with current post access |
| `systemAnnouncement` | Explicit recipients selected by a trusted Administrator |

Self-notifications are suppressed centrally. For a single comment and recipient, **mention > reply
> post comment**: only one item is created. Comment edits do not resend mentions or replies. Existing
comments are not retroactively scanned. Reactions can be changed or removed/re-added without new
notices. Follow/unfollow cannot be used to repeatedly notify someone. Tombstones preserve these rules
even after the user dismisses an item.

### Replies and mentions

The repository previously had neither replies nor mention notifications. Comments now accept optional
`parentCommentId`, verified against the **same accessible post** before creation. Replies remain in the
existing paged chronological comment list; a Reply action and a parent link are included. The parent
reference is logical rather than a cascading self-FK: deleting a parent keeps replies, and opening the
old reference returns 404. Text remains plain interpolated text, never HTML.

Mentions recognize existing ASCII username rules, case-insensitively; terminal sentence dots/hyphens
are trimmed. Unknown/inaccessible names are silently ignored (no account/access oracle). Mentioning
someone does not change followers or grant content access. The page has a hint and no new editor or
rich-text dependency. `GET /api/posts/{postId}/comments/{commentId}` supports authorized notification
navigation to a focused comment independently of the currently loaded comments page.

## Architecture and reliable creation

* Domain: `Notification`, `NotificationType`, `NotificationPreferences`.
* Application: `NotificationCreationService`, immutable `NotificationEvent`, inbox/transport contracts.
* Infrastructure: transactional event bridge, recipient-scoped inbox repository/service, dispatcher.
* API: notification controller and the existing hub's `Clients.User(...)` transport adapter.

There was no domain-event bus/unit-of-work event collection in the existing app. Instead of adding a
broker or scattering after-save calls through controllers, `NotificationEventBridge` translates newly
Added message/follow/comment/reaction entities at the existing DbContext save boundary and requests
central application-level creation. Event types, audience priority, mention parsing and self-suppression
are implemented in that application service; an `INotificationAudience` adapter supplies data access.
The bridge stages the result **before** EF's normal SaveChanges transaction.
The business action and its inbox/outbox rows commit together, or both fail. Both synchronous and
asynchronous saves use the bridge; application code continues using async saves. Failed concurrent
follow/reaction inserts clear the rejected unit of work before the existing retry logic reloads it.

A unique `(RecipientUserId, EventKey)` index and deterministic notification UUID enforce event
idempotency. Checks before staging are an optimization, not the uniqueness guarantee. Concurrent
conflicts are handled by existing source-operation retries; the real-SQL chat test verifies concurrent
message retries also create exactly one notification. Two **separate new comment submissions** are
separate business events (comment POST itself has no client retry ID). Clients should not blindly
resubmit comments on an ambiguous timeout.

### Durable outbox, without extra infrastructure

The notification record doubles as a small SQL outbox: `DispatchedAtUtc`, `NextAttemptAtUtc`, and
`DeliveryAttempts` are indexed delivery state. A scoped background dispatcher checks every 2 seconds,
processes at most 50 records, and invokes `NotificationChanged(notificationId)` only on the intended
recipient's authenticated user channel. It sends **no message/comment text or source identifiers**.
Clients retrieve their own authorized representation and unread count via REST.

Failed sends are logged with the notification ID (not private content), retained, and retried with
exponential backoff (2 seconds up to 256 seconds). A failure after persistence does not roll back a
successful business action or discard the notification. A crash after sending but before recording
dispatch can resend the same ID: delivery is **at least once**, not exactly once. A successful SignalR
send does **not** mean the user was online, delivered an acknowledgment, or read it. Offline users
retrieve the inbox when they return. Reconnect refreshes without replaying old sounds. Clients also
reconcile every 30 seconds, covering read/delete/preference invalidation failures.

Read/delete/preference mutations persist before sending a best-effort invalidation (`null` ID).
Chat's persistent read cursor marks matching message notifications read in the same save, and the
existing receipt broadcast invalidates the reader's notification count across tabs. Reading a
notification alone does not claim the underlying message was read.

`Notifications:DispatchEnabled` defaults to `true`; tests disable the worker and drive it explicitly.
Do not disable it in production unless intentionally pausing real-time notifications. API retrieval
still works. Monitor failed-delivery logs and the age/count of pending outbox rows. No notification
is discarded after a maximum number of attempts.

## API

All endpoints require the authenticated recipient; unsafe requests use the existing `X-XSRF-TOKEN`.
Responses are non-cacheable. Mutations share the existing 60/user/minute chat/activity limiter.
New follows, comments and reaction updates have an additional 120/user/minute social activity policy.

| Method / route | Contract |
|---|---|
| `GET /api/notifications?page=1&pageSize=20&unread=true&type=commentMention` | Existing `PagedResult`; max 100; omit filters for all visible types; `unread=false` means read only |
| `GET /api/notifications/{id}` | Recipient-owned safe DTO; 404 for missing, dismissed, or someone else's item |
| `GET /api/notifications/unread-count` | `{ "count": 3 }`, SQL count respecting presentation preferences |
| `PATCH /api/notifications/{id}/read` | Idempotently sets read timestamp; 204 |
| `PATCH /api/notifications/read-all` | Marks visible items existing at request start read; 204 |
| `DELETE /api/notifications/{id}` | Dismisses an owned item, retaining a dedup tombstone; 204 |
| `DELETE /api/notifications/read` | Dismisses the caller's read items, including hidden categories; 204 |
| `GET /api/notifications/preferences` | Persisted preferences or documented defaults |
| `PUT /api/notifications/preferences` | Full replacement; last successful write wins; volume 0–100 |
| `POST /api/notifications/announcements` | **Administrator role only**, accepted for durable dispatch; max 100 explicit recipients |

The DTO includes ID, type, safe generic text, authorized actor display name, UTC creation/read times,
critical/available flags, and authorized destination IDs. Message bodies and comment bodies are never
stored in the notification. Projection batches actor, source, and access lookups for the page rather
than issuing one query per item. Post checks reuse `PostRepository.BuildAccessibleQuery`.

Deleted/inaccessible content produces `available:false`, generic “activity no longer available” text,
and **null actor/destination IDs**. The client re-fetches on selection and destinations enforce their
own access rules. Shared-post notifications lead to the conversation: the chat's existing shared-post
access policy then controls the link. The source IDs are deliberate logical references, not cascading
FKs, so source deletion cannot erase the inbox or create SQL Server cascade-path conflicts.
Recipient identity is a cascading Identity FK; actor identity is a non-cascading FK. An eventual account
removal workflow must null actor references before deleting that account (no account-deletion feature
exists today), and can then cascade its own inbox/preferences. Do not bypass this with raw deletes.

### Announcements

Example authenticated **Administrator** request:

```json
{
  "eventId": "a8f0ab6e-c06a-43a0-9017-3960562f1aba",
  "recipientUserIds": ["42cfc121-ccbb-43fb-8e41-37a5f1dd995a"],
  "text": "Scheduled maintenance begins at 22:00 UTC.",
  "critical": false
}
```

Use the same eventId when retrying/batching recipients; the first committed text for each event/user
is authoritative. Retry a concurrent 409 with that same ID. All-system broadcasts should be sent by
an administrative job in bounded batches of 100 recipients, not by querying every account in one
web request. Provision the `Administrator` Identity role through your trusted operational process;
this feature introduces **no** self-service role assignment or public notification-creation endpoint.
Critical security/service notices bypass in-app/category opt-outs, but never force audio or browser
permission. Users can still acknowledge/dismiss them.

## Preferences, sounds and multiple tabs

Preferences control **presentation, not creation**. All qualifying events remain persistent even when
in-app notifications or a category are disabled. API list/count filter hidden categories in SQL;
reenabling a category reveals its retained history. Critical notices always appear. Preferences:

* In-app, chat/shared posts, follows, comments/replies, reactions, mentions, announcements: on by default.
* Sound: **off** by default; volume 35%; quiet while viewing the active conversation: on by default.
* `browserPushEnabled`: always false for now; attempts to enable it are rejected, not falsely accepted.

Disabling sound never suppresses an inbox record. **Test sound** or an actual pointer/key interaction
unlocks Web Audio; automatic events cannot initialize audio. Browser autoplay restrictions are respected
and failures only affect sound. A tiny locally synthesized oscillator tone requires no hosted asset,
third-party URL, or CSP relaxation.

A root service owns the notification state and reuses ChatService's connection. The bell works across
routes, not only on the notification page. Only fresh, unread, authorized notifications generate a
visual toast and (if enabled) sound. Events older than 60 seconds, reconnect refreshes, self-events,
and duplicates do not sound. Persistent storage and Web Locks coordinate event claims across browser
tabs; in storage-restricted browsers the fallback guarantees per-tab deduplication only. A focused,
visible conversation has a short-lived (15-second) cross-tab presence marker, refreshed every 5
seconds, so another tab generally does not sound while the recipient reads that chat. Presence is
cleared on logout/blur/navigation and is advisory, not an authorization or message-read signal.

## Retention and operating limits

Hourly cleanup redacts/dismisses up to 200 items created more than 90 days ago **only if read or already
explicitly dismissed**. Unread, undismissed items do not expire automatically. Minimal event-key/recipient
identity tombstones remain indefinitely to prevent old business events from resurrecting after cleanup.
These are still personal metadata; define administrative export/erasure policies as part of account
removal. This policy deliberately trades small tombstones for reliable deduplication.

This is sized for the existing single-instance deployment. There is no distributed outbox lease;
multiple dispatchers may deliver the same ID. For scaling, configure shared Data Protection keys,
shared storage, the existing chat's supported SignalR backplane/service, distributed rate limits,
and an outbox claim/lease (or designate a single dispatcher). The SQL uniqueness/read model remains
valid. Never claim cross-instance live delivery works without the SignalR scale-out configuration.

## Deployment

Apply migration `20261016180000_AddNotifications` using the existing workflow, after `AddChat`:

```sh
dotnet tool restore
dotnet ef database update --project src/ImageStore.Infrastructure --startup-project src/ImageStore.Api
```

The migration adds Notifications, NotificationPreferences, indexes and optional Comments.ParentCommentId.
Back up first; rollback removes all notification/preference data and reply references. The checked-in
snapshot and target model must pass `HasPendingModelChanges` before deployment.

No Docker/Nginx changes are necessary: `/api/notifications` already uses the same-origin API proxy,
and events use `/api/chat` with its WebSocket configuration. Existing external SQL Server and volumes
continue persisting records and authentication. No new secret is required for in-app notifications.
There is no service worker in this release, hence no worker cache changes or offline notification claim.

## Optional Web Push — intentionally not implemented in this release

The inbox, SignalR, preferences and sound work without browser push or permission. To add Web Push:

1. Add a push-subscription table (authenticated UserId, unique endpoint fingerprint, endpoint + p256dh/auth
   keys, timestamps/expiry/device label) with bounded per-user subscriptions and explicit user ownership.
   Encrypt subscription secrets at rest; validate endpoint HTTPS and block private/internal destinations
   to avoid SSRF. Never accept a client-provided user ID as the owner.
2. Introduce a maintained, audited Web Push implementation for VAPID signing and standards-compliant
   payload encryption. Configure per-environment `WebPush:Subject`, `PublicKey`, and **server-only**
   `PrivateKey` via a secret manager/runtime environment. Expose only the public application-server key.
3. Add CSRF-protected subscribe/unsubscribe endpoints, per-device ownership checks, rate limits and a
   server capability endpoint. Update the persisted preferences only once an explicit subscription
   succeeds. Support multiple devices; account logout on shared devices needs a clear unsubscribe policy.
4. Register a same-origin service worker over HTTPS. Serve the worker script with `Cache-Control: no-cache`
   and correct scope/MIME; never cache authenticated inbox/API responses in the worker. Show an explanation
   and request Notifications permission **only after an explicit Enable push click**, checking browser
   support and denial states first. iOS/iPadOS installation/platform constraints must be documented/tested.
5. Extend the durable dispatcher with separate push attempt state per subscription. Use a generic
   “New activity in ImageStore” preview by default, stable tags, safe same-origin notificationclick URLs,
   and fresh authorization after navigation. Do not include message bodies on lock screens.
6. Remove subscriptions on 404/410 from the push service; retry 429/5xx with bounded exponential backoff
   and monitor invalid credentials. Disabling push must not disable the inbox or sounds.
7. Add permission/subscribe/unsubscribe, cross-user ownership, encrypted delivery, endpoint-validation,
   invalid/expired-subscription, worker update/click, secret-leak, and platform-specific end-to-end tests.

No fake push-success response, permission prompt, VAPID key or placeholder remote endpoint ships here.

## Validation

```sh
npm run build --prefix client
npm run test:ci --prefix client
dotnet test ImageStore.slnx
# Requires an isolated SQL Server login with create/drop database permission:
IMAGESTORE_TEST_SQL='Server=...;Database=master;...' dotnet test --filter ChatSqlServerTests
```

Tests include persistence, filters/pagination, unread counts, read/all/delete ownership, lifetime dedup,
chat retries/read synchronization, follows/comments/reactions/replies/mentions, self suppression,
preferences/critical notices/admin-only creation, source redaction, failure before commit, delivery
failure/retry, exact SignalR user targeting, retention and migration metadata. Client tests cover fresh
versus replayed events, sound preferences/autoplay failure, cross-tab claims/presence and server filters.
The opt-in SQL chat race test also asserts one notification per message retry and read synchronization.

In this sandbox the Angular build and tests run, but **dotnet, SQL Server and Docker are unavailable**.
Backend compilation/tests and migration execution have not been verified here; run them (especially
model-snapshot and real-SQL transaction/concurrency tests) before deploying. No Web Push tests are
claimed because that capability is explicitly deferred.
