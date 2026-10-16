# Private messaging

## Architecture and policy

Chat uses the existing SQL Server context, Identity cookie session, antiforgery header,
profile/avatar components, and post-access repository. No extra service/database is required.
`IChatService` is the application boundary, implemented by the EF persistence service; controllers
and the hub use the same operations. The app has no blocking/moderation subsystem, so any
registered user can contact another user (not themselves). `MembersAsync` and `OpenAsync` are
policy enforcement points for a future blocking/reporting policy.

A conversation has an immutable normalized pair key (two sorted lowercase 32-character GUIDs,
separated by `:`), uniquely indexed in SQL. Opening the same pair in either direction returns
HTTP 200 and the same conversation. A race losing the unique-key insert reloads the winner.
Participants have a composite primary key and an Identity FK. Messages' composite sender FK
requires the sender to be a participant. There is no endpoint to change/add participants.

A per-conversation `LastSequence` concurrency token serializes sends. Each successful save
atomically advances the sequence and inserts the message in EF's transaction. A loser reloads
and retries. This avoids timestamp ties, late commits below a read cursor, and lost latest-message
updates. UTC timestamps remain display data; ordering/read progress use sequence numbers.
Read cursors are concurrency-checked and advance monotonically; unread counts exclude self-sends.
Retries are bounded and exhausted contention returns 409.

The client chooses a UUID `id` before sending and keeps it and the original payload for retries.
The primary key enforces durable idempotency. Reusing an ID for a different sender/conversation
or payload returns 409. A retry following deletion of its shared post can also return 409 because
the persisted post reference was cleared; clients should reconcile history rather than create a
new ID blindly. Pending sends live only in the page, not across browser reloads.

### Status semantics

* **Sent**: the database committed the message. A network timeout does not imply failure; retry
  with the same ID or retrieve history.
* **Read**: the recipient explicitly advanced their persistent read cursor through this message.
  The UI does this only in an open, focused, visible chat window.
* **Delivered**: not implemented. A SignalR send is not a delivery acknowledgement.

### Sharing image posts

Send `sharedPostId` on the ordinary message endpoint (optional text). Both participants must
currently have access under the existing author/follower rule. No image bytes are copied.
Post deletion sets the FK to null but retains the message's `post` type. Reads recheck the
viewer's access; deleted/inaccessible posts render “Post deleted or no longer available”. The
existing post detail/content endpoints independently enforce current visibility. An already-open
link may stop working after an unfollow. Chat grants no additional post access.

## REST contracts

All routes require the existing cookie. Unsafe requests require `X-XSRF-TOKEN` from the existing
CSRF bootstrap. Known errors use the existing Problem Details handler (400 invalid, 404 missing
or not a participant, 409 conflicting retry/contended write, 429 rate limited).

| Method / path | Request / response |
|---|---|
| `GET /api/conversations?page=1&pageSize=30` | Existing paged-result shape, maximum 100; participant summaries, latest message, unread count |
| `POST /api/conversations` | `{ "otherUserId": "uuid" }` → conversation, including existing pair |
| `GET /api/conversations/{id}` | Conversation summary, participant profile fields/read cursors |
| `GET /api/conversations/{id}/messages?before=51&limit=50` | Descending sequence array, strictly before cursor, max 100; omit cursor for latest |
| `POST /api/conversations/{id}/messages` | `{ "id": "client-uuid", "content": "Hello", "sharedPostId": null }` → persisted message |
| `PUT /api/conversations/{id}/read` | `{ "sequence": 51 }` → `{ conversationId, userId, sequence }` |

Text is trimmed, max 4,000 characters, and cannot be empty unless sharing a post. GUID.Empty
retry IDs are rejected. The fixed-window abuse limit is 60 operations/user/minute shared across
REST mutations and hub invocations (including typing/join), with no queue. This is single-instance
protection; reverse-proxy connection/request limits are recommended for public deployments.

## SignalR and recovery

`/api/chat` uses Identity cookies, never query-string bearer tokens. POST transport/negotiation
requests validate antiforgery. Explicit same-origin validation protects WebSocket handshakes;
CORS alone does not. Keep Host/scheme correct through the trusted proxy. Cross-origin chat is
intentionally unsupported. The hub closes connections on authentication expiration.

Hub methods: `Join(conversationId)`, `Leave(conversationId)`,
`SendMessage(conversationId, request)`, `MarkRead(conversationId, sequence)`,
`Typing(conversationId, bool)`. Every data operation and group join verifies membership.
Typing is ephemeral, throttled by the client and server, and expires in the UI after 4 seconds.

Events: `ConversationChanged(conversationId)` is a private notification to both authenticated
user IDs (all tabs); the receiving client retrieves its own authorized DTO/history.
`ReadUpdated(receipt)` is likewise private. `Typing(id, userId, bool)` goes only to other
connections in the verified conversation group. Invalidations deliberately avoid broadcasting
sender-relative shared-post data to another viewer.

One root Angular service owns the connection and stops it on logout. Reconnect rejoins the active
chat and reloads summaries/history; recovery walks backward through **all** pages until reaching
previously loaded messages. Message UUIDs deduplicate REST/realtime reconciliation. An open chat
also reconciles every 30 seconds, covering a server crash after commit but before broadcasting.
There is no durable event outbox or delivered guarantee. REST always remains available when
SignalR is interrupted. Initial connection failures retry, and the UI exposes connection state.

## Migration and deployment

Back up SQL Server, install the .NET 10 SDK and repository EF tools, then apply:

```sh
dotnet tool restore
dotnet ef database update --project src/ImageStore.Infrastructure --startup-project src/ImageStore.Api
```

Set `ConnectionStrings__ImageStore` for the migration process. `20261016090000_AddChat` is
additive (three tables and indexes). The EF model snapshot and migration target model are included.
Production startup migration remains disabled; run migrations once during deployment. Rolling
back this migration destroys all chat history. Never use the Development InMemory provider for
persistent conversations or to validate relational concurrency.

This checkout previously had deployment guidance but no Docker/Compose/Nginx files. The new
root Dockerfile builds the **existing** Angular app into the existing API. `compose.yaml` keeps
SQL Server external and persists uploads and Data Protection keys. No Redis is added.

Provide `IMAGESTORE_SQL_CONNECTION` and `IMAGESTORE_TLS_DIR` (containing `fullchain.pem` and
`privkey.pem`), then `docker compose up --build -d`. TLS terminates at Nginx; API port 8080 is not
published. The example reserves `172.29.73.0/24` for trusted proxies; change both the Compose
subnet and `ForwardedHeaders__KnownNetworks__0` if it conflicts with your network. Keep database
credentials out of Git. Protect/backup key volumes as secrets. Persistent keys preserve cookie
sessions across restarts. Nginx config includes WebSocket upgrade and unbuffered long-lived
requests; reuse its `/api/chat` block if you have an existing reverse proxy.

For multiple API instances: use Azure SignalR or a supported Redis backplane for private events,
sticky routing when required by the transport/backplane, shared Data Protection keys/application
name, shared image storage, SQL Server, and distributed abuse controls. Database uniqueness and
read/message concurrency are already database-enforced; group routing and limits are not shared
in this single-instance release. An outbox is needed if guaranteed notification delivery is required.

## Validation

```sh
npm ci --prefix client
npm run build --prefix client
npm run test:ci --prefix client
dotnet test ImageStore.slnx
# Optional real SQL concurrency/constraint test; creates and drops an isolated temporary database:
IMAGESTORE_TEST_SQL='Server=...;Database=master;...' dotnet test --filter ChatSqlServerTests
```

The SQL test login needs CREATE/DROP DATABASE permissions; do not use production credentials.
Without that variable the SQL behavior test is explicitly skipped. Normal API tests use the
existing InMemory test host and cover identity, unauthorized access, durable/idempotent history,
unread/read state, cursor paging, invalid text, sharing visibility, CSRF/origin and unauthorized
hub joins. Client tests cover multi-page recovery, deduplication, stable retries, and switching chats.

In the implementation sandbox: Angular build/tests were executable. Backend build/tests and SQL
migration execution were **not** executable because .NET/SQL Server were absent; Docker was also
absent. Run those checks before deploying. In particular the hand-authored additive migration
and model snapshot must pass `HasPendingModelChanges` and the real-SQL test in CI.
