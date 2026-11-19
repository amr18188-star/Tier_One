# Tier1 backend review: architecture, transactions, contracts, integrations

Scope: src/Tier1.Domain, src/Tier1.Application, src/Tier1.Infrastructure, src/Tier1.Api, web/src/api, docs/requirements.md, docs/deployment.md, deploy/.
Method: read-only static review using the skills system-design, backend-engineering, api-contract-design and integration-engineering. No build, test or run.
"verified" means I confirmed it by reading the code path end to end. "likely" means the code supports it but it still needs a test to confirm.

## What is already solid (keep it)

- **Audit log.** Writes go to the Postgres outbox (`logs.outbox`) in the same transaction as the business write. A single-leader dispatcher then ships them to MongoDB. It uses a hash chain, `_id` dedup and a check for colliding chain sequence numbers. Partial failure between Postgres and Mongo is handled correctly, with no loss and no duplicates (OutboxDispatcher.cs:15-24, 142-181).
- **Mail outbox.** Mail is claimed with `FOR UPDATE SKIP LOCKED` plus a lease (`locked_until`). Retries use exponential backoff for up to 8 attempts. The body is encrypted at rest and erased after sending.
- **Optimistic concurrency.** `xmin` row versions (`IsRowVersion`) are on almost every mutable aggregate. A DbUpdateConcurrencyException, or SQLSTATE 23505, becomes a 409.
- **Error codes and auth.** Errors carry a stable `code` and a bilingual message chosen by Accept-Language. Endpoints use declarative permission policies, with a test asserting that every `/api` route declares one. CSRF is enforced. Rate limits apply per user and per IP.
- **Startup.** Startup guards reject demo/dev settings outside Development (StartupGuards.cs).

---

## Findings (most severe first)

### BE-1: Concurrent autosaves on one form fail with a false 409 ("someone else changed it")
- **Severity:** High. **Confidence:** likely (needs an integration test).
- **Where:** src/Tier1.Application/Collection/FormsService.cs:116, 159-161; src/Tier1.Infrastructure/Persistence/Tier1DbContext.Collection.cs:50; web/src/features/forms/useValueAutosave.ts:104-110, 159-161.
- **Evidence:**
  - `SaveValueAsync` loads the `Submission` with tracking. `Upsert` always writes `ctx.Submission.LastEditedBy/LastEditedAt` (and Status the first time).
  - `Submission.Version` is `IsRowVersion()` (xmin), so every value save also issues `UPDATE submissions … WHERE xmin = @loaded`.
  - The web app runs one save queue per item, so several items can save in parallel.
- **Failure scenario:**
  1. A data-entry user changes indicator A, tabs to indicator B and types.
  2. Both PUTs to `/api/forms/{id}/values` overlap: both load submission xmin V, request A commits (V becomes V+1), and request B's submission UPDATE matches 0 rows.
  3. Request B gets DbUpdateConcurrencyException and returns 409 `conflict`.
  4. The UI shows "someone else changed this value — Save mine over it", although nobody touched B. Two editors in the same department hit this constantly.
- **Fix:**
  - Stop dirtying the Submission row on each value save. Either:
    - write `last_edited_at/by` with a separate `ExecuteUpdateAsync` in the same transaction, which is not a concurrency-checked tracked entity; or
    - derive it from `max(submitted_values.edited_at)`.
  - Keep transition safety another way: inside the save transaction, run `SELECT … FROM dc.submissions WHERE id=@id FOR SHARE`. Submit/Approve then need `FOR UPDATE` and will serialize against it. Alternatively, guard the value write with `WHERE EXISTS (submission editable)`.
  - Add a regression test with two parallel saves on different indicators.

### BE-2: A null version silently overwrites concurrent edits (lost update)
- **Severity:** High. **Confidence:** verified.
- **Where:**
  - src/Tier1.Application/Common/Shared.cs:58-68: `Concurrency.Expect` skips the check when `version is null`. The comment says "version optional to keep old integration working".
  - FormsService.cs:137-147 (value upsert).
  - The same pattern appears in SaveCycleRequest, SaveTargetRequest, TransferRequest, SaveMember/Partner/Initiative requests and DefinitionDrawer.tsx:46 (`version: createsVersion ? undefined : v.version`).
  - Submit, Return and Reopen accept no version at all (FormsService.cs:190-250; FormPage.tsx:62, 357, 370).
- **Failure scenario:**
  1. Users A and B open the same form. Indicator X is empty for both, so the client holds `version: null`.
  2. A saves 10, which inserts the row.
  3. B saves 20 with `version: null`. The row now exists, `Expect` is skipped, and B overwrites A's 10 without any conflict. Neither the audit log nor the UI flags it.
  4. Likewise, a reviewer can Return a form based on a stale screen after the editor changed values, because ReturnRequest carries no version.
- **Fix:**
  - Make the version mandatory for updates. A missing version on an existing row should return 428 (or 409 `version_required`).
  - Treat `version: null` as "expect the row not to exist": if the row exists, return 409.
  - Add a `Version` field to Return, Reopen and Submit requests, compared against `Submission.Version` as Approve already does (FormsService.cs:210).
  - Remove the "optional for old integration" escape hatch: there is no external integration yet.

### BE-3: Form transitions and their notifications run in two transactions; post-commit failures return 500 for a committed approval
- **Severity:** High. **Confidence:** verified.
- **Where:** FormsService.cs:202-203, 216-217, 231-232, 247-248; FormNotifier.cs:12-15, 21-41.
- **Evidence:**
  - Every transition calls `await db.SaveChangesAsync(ct)`, which commits the state change plus the audit outbox row.
  - It then calls `await notifier.MovedAsync(...)`. That method resolves recipients with many queries (N+1, see BE-16), enqueues mail and calls `SaveChangesAsync` a second time.
  - The class comment claims "الرسالة تضاف في معاملة العملية نفسها" (the message is added in the same transaction), which is not true.
  - The request `CancellationToken` is passed through.
- **Failure scenario:**
  1. The dean approves a form.
  2. The approval commits.
  3. Then the browser navigates away (ct is cancelled), or a DB blip or exception hits the recipient lookup.
  4. The approval stands, but no reviewer or editor email is ever enqueued. The API returns 500, or nothing.
  5. The user retries and gets 409 `not_in_review` or an SoD (separation-of-duties) 403. They believe the approval failed, while the next reviewer was never notified. Nothing records that the notification was lost.
- **Fix:**
  - Enqueue a domain event `form.moved {submissionId, fromStep, toStep, actorId}` into an outbox row **before** the single SaveChanges, so it commits atomically.
  - Resolve recipients and render messages in a background handler that is retryable and idempotent (key = event id plus recipient).
  - Never pass the request token to post-commit side effects.

### BE-4: Multi-statement writes outside a transaction leave data inconsistent and unaudited
- **Severity:** High. **Confidence:** verified.
- **Where:**
  - src/Tier1.Application/Collection/CyclesService.cs:78, 88, 95.
  - src/Tier1.Application/Organization/UnitIndicatorsService.cs:101, 114.
- **Evidence:**
  - `CyclesService.UpdateAsync` sets the expected version, then runs `ExecuteUpdateAsync` on `dc.submissions` (DueOn). That commits immediately in autocommit mode, before `SaveChangesAsync` writes the cycle and its audit row.
  - `UnitIndicatorsService.SaveAsync` runs `ExecuteDeleteAsync` on removed assignments, which also commits immediately, before `SaveChangesAsync` adds new rows, updates `unit.CustomIndicators` and writes the audit row.
- **Failure scenario:**
  - An admin extends an open cycle's deadline from a stale screen. The `Version` mismatch makes SaveChanges throw 409, but every form's DueOn has already moved. The cycle still shows the old date, and there is no audit event for a deadline change in a regulated process.
  - In UnitIndicators, a unique or FK failure on the add step leaves the removals applied without audit.
- **Fix:**
  - Wrap both in `await using var tx = await db.Database.BeginTransactionAsync(ct)` and commit after SaveChanges, as RbacAdminService.cs:121 already does.
  - Better still, add an `IUnitOfWork.ExecuteInTransactionAsync` helper. Also add an analyzer or architecture test flagging `ExecuteUpdate`/`ExecuteDelete` in Application services outside a transaction.

### BE-5: The "one unit per university indicator" rule is check-then-act without a DB constraint
- **Severity:** Medium. **Confidence:** verified.
- **Where:** UnitIndicatorsService.cs:83-96; Tier1DbContext.Tier1.cs:74-82 (PK only on UnitId and IndicatorId).
- **Failure scenario:** Two admins assign university-level indicator U to two different central units at the same moment. Both pre-checks see nothing, and both inserts succeed. When the cycle opens, `map.ResponsibleFor` picks one unit arbitrarily, and the other unit's data never counts.
- **Fix:** Serialize with `pg_advisory_xact_lock(hash('unit_indicators:university'))` inside the transaction from BE-4. Alternatively, add a denormalized `level` column with a partial unique index `(indicator_id) WHERE level='University'`.

### BE-6: The error contract is inconsistent, and production binding errors return bodiless 400s
- **Severity:** Medium. **Confidence:** likely for the production 400 behaviour; verified for the rest.
- **Where:**
  - ApiSetup.cs:78-79 (`AddProblemDetails()` registered but unused), 118-156 (custom `{status, code, title, traceId}`).
  - SessionAuthentication.cs:54-68 and Middleware.cs:24-25: 401, 403 and csrf responses have no traceId.
  - Program.cs:99: `/api/{**rest}` returns an empty 404.
  - FormsService.cs:125: the Arabic text is also passed as the English message.
  - Shared.cs:12-21 returns 400 while DomainException defaults to 422.
- **Evidence and scenarios:**
  - `RouteHandlerOptions.ThrowOnBadRequest` defaults to true only in Development. In production, malformed JSON, an unparsable Guid or enum, or a missing body gets a 400 with no JSON body. The handler's `BadHttpRequestException`/`JsonException` branches (ApiSetup.cs:133-134) only run in dev and tests. web/src/api/client.ts:92 then shows the raw text "HTTP 400" to Arabic users.
  - Validation is a single code with no per-field `errors[]`, so forms cannot highlight fields.
  - English UI users get Arabic text for value-rule errors.
  - Clients cannot rely on one status for validation, since both 400 and 422 are used.
- **Fix:**
  - Set `services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true)` in every environment.
  - Emit RFC 9457 `application/problem+json` with extensions `code`, `traceId` and `errors[{field, code, params}]` from one writer used by the exception handler, the auth handler, CSRF, the rate limiter and the 404 fallback.
  - Document a single error-code table. Pick 400 for structural errors and 422 for rule violations, and apply it consistently.
  - Return `traceId` in a response header (`traceparent` or `X-Trace-Id`) on every response.

### BE-7: Enum inputs accept undefined integers, which are stored
- **Severity:** Medium. **Confidence:** verified.
- **Where:** ApiSetup.cs:30 (`new JsonStringEnumConverter()`, where `allowIntegerValues` defaults to true). Only 3 call sites use `Enum.IsDefined` (RoleRules.cs:59, UsersAdminService.cs:380, StrategyService.cs:268).
- **Failure scenario:** `PUT /api/forms/{id}/values {"kind": 7, "reason": "....."}`. `ValueRules.Check` treats any non-`Number` kind as a reason-only value, so `SubmittedValue.Kind = 7` is persisted. Results and scorecard code that switches on `ValueKind` then misclassifies the value. The same applies to `TargetKind`, `ConflictSeverity` and others.
- **Fix:** Use `new JsonStringEnumConverter(allowIntegerValues: false)`, and add an endpoint filter or validator that rejects undefined enum values with 400 `invalid_value`.

### BE-8: Create, upload and decision endpoints are not idempotent; double-submit creates duplicates
- **Severity:** Medium. **Confidence:** verified.
- **Where:**
  - Tier1Endpoints.cs:58, 65, 128, 138, 148 (POST members, partners, pillars, standards, initiatives).
  - CollectionEndpoints.cs:31 (POST cycles) and 96-123 (evidence and document uploads).
  - No `Idempotency-Key` support anywhere.
- **Failure scenario:** A slow network and a double click on "add member" or "upload evidence" produce two committee members or two identical evidence files. Uploads are only distinguishable by hash, and a duplicate blocks "clear value" until both are voided. Two trial cycles with the same name can also be created.
- **Fix:**
  - Add an `Idempotency-Key` header middleware for POSTs backed by a table `(key, user_id, route, request_hash, status, response, created_at)` with a unique `(user_id, key)` and 24-hour retention:
    - same key and same hash: replay the stored response;
    - different hash: 422;
    - still in flight: 409.
  - Add natural unique constraints where business allows it, for example `(owner_type, owner_id, sha256) WHERE voided_at IS NULL` on documents.

### BE-9: Mail configuration can silently go wrong in production
- **Severity:** Medium. **Confidence:** verified.
- **Where:**
  - FormNotifier.cs:18: `App:PublicUrl` falls back to `http://localhost:5150`.
  - SmtpEmailSender.cs:14-17, 43-48: `Email:PickupDirectory` writes `.eml` files and returns success. `From` defaults to `tier1@localhost`.
  - StartupGuards.cs:301 checks only `Email:Host`.
  - FormNotifier reads `IConfiguration` directly from the Application layer.
- **Failure scenario:**
  - An IIS deployment (docs/deployment.md option 2) forgets `App:PublicUrl`, and every approval or reminder email links to localhost.
  - An operator copies the dev `Email:PickupDirectory` into production settings. Every mail is marked `sent_at` and erased, nobody receives anything, and nothing flags it.
- **Fix:** Introduce typed `AppOptions {PublicUrl}` and `EmailOptions` with `ValidateOnStart`. Outside Development, require an absolute https `PublicUrl` and a real `From`, and reject `PickupDirectory`. Inject `IOptions<AppOptions>` instead of `IConfiguration`.

### BE-10: The notification abstraction is email-only, untemplated and channel-specific
- **Severity:** Medium. **Confidence:** verified.
- **Where:**
  - Ports.cs:151-155: `IMailOutbox.Enqueue(kind, to /*email*/, subject, body)`.
  - MailOutbox.cs:15-31 hardcodes `Audience=Staff` and `Module="adm"`.
  - Domain/Notifications/Mail.cs:12, 38-73: Beneficiary, exm/trn/req modules, BatchId, Cancel*, ResentFromId and portal.verify are leftover schema from another product.
  - `MessageTemplate`/`MessageTemplates` (Mail.cs:87-120, Ports.cs:47) is never read.
  - FormNotifier.cs:25-36 contains hardcoded Arabic text, so `AppUser.PreferredLanguage` is ignored.
  - AppUser (Identity.cs) has no phone or opt-in fields.
  - CheckpointPublisher (Checkpoints.cs:34, 81) calls `IEmailSender` directly and bypasses the outbox.
- **Impact:** Adding WhatsApp or SMS means a schema change on the users table, a new outbox, and rewriting every notifier call. Template-only channels such as WhatsApp Business need template ids and variables, which free-text `subject/body` cannot carry. English-preferring users get Arabic mail.
- **Fix:** See the target design below. In short:
  - `INotificationPublisher.Publish(NotificationRequest{EventKey, RecipientUserId, Variables, DedupKey})`;
  - per-channel delivery rows;
  - a template store keyed by `(event, channel, lang)`;
  - provider adapters behind `IChannelSender`.
  - Drop or migrate the dead columns.

### BE-11: Operators cannot see or resend failed notifications
- **Severity:** Medium. **Confidence:** verified.
- **Where:** No endpoint reads `adm.mail_outbox`; the only readers are FormNotifier.cs:52 (dedup) and MailDispatcher. docs/requirements.md:399 requires "تسجيل حالة إرسال الإشعارات وإعادة محاولة الفاشل منها" (record notification send status and retry failures).
- **Failure scenario:** SMTP credentials expire. Eight attempts later the rows get `failed_at`, and staff bodies are erased (MailDispatcher.cs:120-125). The only trace is integration-log events. There is no list, no count on `/api/logs/status`, no alert and no "resend" action, so reminders are lost for the whole cycle.
- **Fix:**
  - Add `GET /api/admin/notifications?status=failed&page=…` and `POST /api/admin/notifications/{id}/resend`, with a permission and an audit entry. The resend action needs a body source: keep a template reference plus variables instead of erasing them.
  - Add a pending/failed mail backlog to readiness/metrics and to `LogsStatusDto`.

### BE-12: Checkpoint publication can append duplicate lines and lose the email
- **Severity:** Medium. **Confidence:** verified.
- **Where:** src/Tier1.Infrastructure/Logging/Checkpoints.cs:64-98.
- **Evidence:**
  1. Lines are appended to the external file (lines 66-71) **before** `ChainCheckpoints` rows are saved (line 98).
  2. The email path catches only IOException, InvalidOperationException, SmtpCommandException, ServiceNotConnectedException and SocketException. MailKit `AuthenticationException`, `SslHandshakeException` and the `OperationCanceledException` from the sender's 45-second timeout (SmtpEmailSender.cs:52-53) all propagate.
- **Failure scenario:** The SMTP password changes. The nightly job appends checkpoint lines to the file, then the auth exception aborts the job before SaveChanges. The next night, `AnyAsync(chain, seq)` is still false, so the same seq is appended again with a new timestamp and signature. The auditors' file now holds conflicting checkpoints for one seq, and no email was sent or retried.
- **Fix:**
  - Persist `ChainCheckpoints` first with a `published_to` status.
  - Make the file append idempotent: skip a seq already present in the file.
  - Send the email through the notification outbox, and catch all non-cancellation exceptions.

### BE-13: Health checks and observability are minimal
- **Severity:** Medium. **Confidence:** verified.
- **Where:** Program.cs:95-97; DatabaseMigrator.cs:38-50; deploy/Dockerfile (no HEALTHCHECK); deploy/docker-compose.yml (`app` has no healthcheck, and caddy depends on it unconditionally); no OpenTelemetry or metrics packages in any csproj.
- **Failure scenarios:**
  - Mongo is down: ready is still 200 while audit events pile up in `logs.outbox`.
  - Production defaults `Database:MigrateOnStartup` to false, so a new image starts against an un-migrated schema and reports ready, and requests then fail with 500.
  - The mail backlog grows invisibly (BE-11).
  - There are no request-rate, latency or error metrics. Request logs go only to a Mongo `http_requests` collection through a channel with `DropOldest` (MongoLogStore.cs:155).
- **Fix:**
  - Use `AddHealthChecks()` with these checks: Postgres; pending EF migrations (`GetPendingMigrationsAsync().Any()` fails readiness); Mongo ping (degraded, not unhealthy); outbox oldest-pending age; mail failed count; `Files:Root` writable; Data Protection key ring loadable. Keep `/health/live` dependency-free.
  - Add OpenTelemetry (ASP.NET Core, HttpClient, Npgsql, runtime metrics) with an OTLP exporter switch.
  - Add a container HEALTHCHECK.

### BE-14: Module boundaries are not enforced in the data-access port
- **Severity:** Medium. **Confidence:** verified.
- **Where:**
  - Ports.cs:15-102: one `IAppDb` exposes all ~40 DbSets plus `DatabaseFacade` and `ChangeTracker` to every service.
  - Tier1.Application.csproj references EF Core Relational.
  - UsersAdminService.cs:552-553 runs Postgres-specific `pg_advisory_xact_lock` SQL in Application.
  - FormNotifier.cs:16 takes `IConfiguration`.
  - Tier1.Api/Startup/ApiSetup.cs:7, 13 and Security/Middleware.cs:5, 8 depend on MongoDB.Bson and Infrastructure.Logging.
- **Impact:** Any service in any module (Collection, Kpi, Access, Scorecard) can read or write any table. FormNotifier reads IAM users, and HomeService reads everything. Ownership of `org.unit_indicators`, `dc.*` and `kpi.*` is convention only. New modules will multiply this coupling, and module-level tests or extraction become impractical. The dependency direction is otherwise correct: Domain is pure, Application depends only on Domain, Infrastructure on Application, and Api on Infrastructure.
- **Fix:**
  - Keep the monolith.
  - Introduce per-module read/write ports, such as `ICollectionStore`, or at least module-scoped interfaces that expose only owned sets plus read-only query services for foreign data.
  - Add NetArchTest rules: Application must not reference Npgsql or MongoDB; module A must not use module B's write ports.
  - Hide raw SQL behind `IDistributedLock` and `IUnitOfWork` ports.

### BE-15: Scheduled jobs are not leader-elected; reminder dedup depends on unordered results
- **Severity:** Low-Medium. **Confidence:** verified.
- **Where:** FormReminderJob.cs:16-48; ChainVerifier.cs:145-155 (NightlyChainVerification); RetentionJob.cs. Only OutboxDispatcher takes an advisory lock (OutboxDispatcher.cs:183-211). FormNotifier.cs:47-63: `forms` has no `OrderBy`, and the dedup key is `rem:{form}:{date}:{globalCounter}`.
- **Failure scenario:** With two app instances (AccessVersionPoller shows multi-instance is anticipated), both run reminders around the same time. Different row orders produce different counter suffixes, so the unique index does not catch duplicates and users get duplicate reminders. Checkpoints get appended twice (BE-12).
- **Fix:** Wrap each scheduled job in `pg_try_advisory_lock(jobKey)`. Make the dedup key `rem:{form}:{recipientUserId}:{date}` and drop the counter.

### BE-16: Notification recipient resolution is N+1 and runs inline in the request
- **Severity:** Low-Medium. **Confidence:** verified.
- **Where:** FormNotifier.cs:84-91: `UsersWhereAsync` loads every active user and calls `access.GetAsync(u.Id)` per user, for every transition and for every reminder form.
- **Impact:** Submit and approve latency grows with the user count, and each step involves several hundred access computations. Combined with BE-3, a slow resolution raises the chance of post-commit failure.
- **Fix:** Move resolution into the background handler (BE-3). Query candidates by role grants that include the needed special action and scope, instead of evaluating every user.

### BE-17: List endpoints are unpaginated and filtered in memory
- **Severity:** Low. **Confidence:** verified.
- **Where:**
  - FormsService.cs:65-106: loads every submission of every open or closed cycle and every org unit when `cycleId` is null, then applies scope, search and status filters in memory.
  - Also `/iam/users`, `/kpi/indicators`, `/kpi/conflicts` and `/cycles`. Only `/logs/{collection}` paginates, with a cap of 200 (LogsService.cs:171).
- **Impact:** Acceptable at the current ~150 units, but the forms list grows by about 600 rows a year. Search is `Contains` with ordinal case.
- **Fix:** Default `cycleId` to the latest open cycle. Push scope, status and search filters into SQL. Add `page`/`pageSize` (max 200) with `totalCount`, using the existing LogPage shape.

### BE-18: Endpoint conventions are inconsistent and the API has no versioning
- **Severity:** Low. **Confidence:** verified.
- **Where:**
  - Deletes are `POST …/{id}/delete` (Tier1Endpoints.cs:103, 115, 132, 142, 152; IamEndpoints `/roles/{id}/delete`), but `DELETE /forms/{id}/values/{indicatorId}` (CollectionEndpoints.cs:52).
  - Creates return 200, not 201 with Location.
  - `GET /results` returns 204 when no cycle exists (CollectionEndpoints.cs:151).
  - `year ?? 2027` is hardcoded (Tier1Endpoints.cs:99).
  - Import preview and apply share one POST with an `apply` query flag (CollectionEndpoints.cs:77).
  - Routes have no `/v1` prefix and no deprecation policy.
- **Fix:**
  - Document the convention: POST action routes for business verbs (`/approve`, `/void`, `/delete`-with-reason) are fine, but apply them everywhere.
  - Return 201 for creates.
  - Return 200 with `{cycle: null}` instead of 204.
  - Derive the default year from the plan calendar.
  - Add a `/api/v1` group alias now, while the web app is the only consumer.

### BE-19: Upload failures leave orphaned files
- **Severity:** Low. **Confidence:** verified.
- **Where:** DocumentsService.cs:228 (`files.SaveAsync`) runs before the caller's SaveChanges (lines 76, 100, 130). LocalFileStore has no sweep.
- **Scenario:** An evidence upload is followed by a concurrency or FK failure on save. The file stays under `Files:Root` forever and is included in backups.
- **Fix:** Add a nightly sweep that deletes storage keys older than 1 hour that have no `documents.storage_key` row, or write a `pending_files` row first.

### BE-20: Outbox tables grow without bound
- **Severity:** Low. **Confidence:** verified.
- **Where:** RetentionJob.cs:39-45 purges only sessions. Sent `logs.outbox` rows keep their full audit payloads (IP, user agent, changes), and sent `adm.mail_outbox` rows are never deleted.
- **Impact:** The Postgres copy duplicates the Mongo audit trail indefinitely, against the stated "minimal retention" intent and the size of the backups.
- **Fix:** Purge `logs.outbox` rows with `sent_at` older than N days whose chain day anchor is verified intact, and purge sent or failed mail older than the retention setting.

### BE-21: MailDispatcher misclassifies a post-send persistence failure
- **Severity:** Low. **Confidence:** verified.
- **Where:** MailDispatcher.cs:96-133.
- **Evidence:**
  - If `sender.SendAsync` succeeds and `SaveChangesAsync` (line 105) throws, the generic catch records a failed send attempt and schedules a retry, so the email is sent again (at-least-once).
  - `audit.RecordNowAsync` in the failure path (line 131) is not guarded. If it throws, the loop aborts, and the remaining claimed rows stay leased for 10 minutes without their error state saved.
- **Fix:** Use separate try blocks for send and persist. Set a deterministic `Message-ID` (`<outbox-{id}@host>`) so relays and clients can dedup. Guard the failure-path audit call as the success path already does.

### BE-22: No machine-checked contract between backend DTOs and web/src/api types
- **Severity:** Low. **Confidence:** verified.
- **Where:** web/src/api/*.ts are hand-written mirrors. The solution has no OpenAPI package or generated client.
- **Result of the manual check:** I compared ~35 DTOs and all ~100 frontend `/api/...` paths against the backend records and routes:
  - Names, nullability and enum literals match, including CycleDto, FormDto, ValueDto, ResultsDto, IndicatorResult, Review*, Progress*, ScorecardDto, LogItemDto, User/Assignment/Override, ConflictDto and TargetDto.
  - Minor looseness only: `UnitIndicatorItem.aggregation` is `string` in TS but the `Aggregation` enum in C#, and decimals are serialized as JSON numbers.
- **Risk:** Any rename drifts silently, with no CI signal.
- **Fix:** Add `Microsoft.AspNetCore.OpenApi`, emit `openapi.json` at build time, generate TS types with `openapi-typescript`, and fail CI on diff. Optionally send money or precise decimals as strings later.

---

## Current architecture (as-is)

```
Browser (React/antd, RTL)  ── same-origin cookie session + X-XSRF-TOKEN ──►  Caddy (TLS, 443)
                                                                               │
Tier1.Api (ASP.NET Core minimal APIs, single process, Kestrel :8080)          ▼
 ├─ Program.cs: one-shot CLI modes (--migrate, --mongo-setup, --reset-kpi, --load-structure,
 │   --backup, --restore-test, --import-*, --create-superadmin), StartupGuards, /health/live|ready, SPA fallback
 ├─ Startup/ApiSetup: JSON (string enums), SessionAuthenticationHandler, PermissionPolicyProvider
 │   ("perm:feature:cap|special"), fallback policy = complete session, Antiforgery, RateLimiter (login, sensitive),
 │   ForwardedHeaders (TrustedProxies), ApiExceptionHandler (DomainException/Npgsql → {status,code,title,traceId})
 ├─ Security/: Csrf, RequestLanguage (ar default), RequestLogging → Mongo, SecurityHeaders (CSP, no-store on /api)
 ├─ Endpoints/: Auth, Iam (/iam/*), Logs (/logs/*), Settings (/admin/settings), Tier1 (/home, /org/units,
 │   /structure, /kpi, /strategy, /kpi/conflicts), Collection (/cycles, /forms, /documents, /results,
 │   /scorecard, /review, /progress, /plan/calendar)
 └─ Startup/DemoData: dev-only hosted seeder
            │ (DI only; endpoints call Application services directly)
Tier1.Application (EF Core-aware services + pure rules)
 ├─ Abstractions/Ports: IAppDb (all DbSets), IClock, ICurrentUser, IMailOutbox, IFileStore, ISecretProtector, ISpreadsheets…
 ├─ Access (RBAC: catalog JSON, EffectiveAccess, UnitScope, AccessVersion cache), Accounts (sign-in, MFA, sessions)
 ├─ Auditing (AuditLog → logs.outbox in-tx or direct insert), Logs (Mongo reader, chain verify)
 ├─ Organization, Structure, Kpi (Dictionary, Targets, Strategy, Conflicts), Collection (Cycles, Forms+Import,
 │   Documents, Results, FormNotifier), Scorecard (Scorecard, Review, Progress), Home, Settings
 └─ *Rules.cs: pure, unit-tested logic (ApprovalRules, ValueRules, ResultsRules, ScorecardRules, DistributionRules…)
            │
Tier1.Domain: POCO entities + DomainException + CairoTime/Lang (no dependencies)
            ▲
Tier1.Infrastructure (implements ports)
 ├─ Persistence: Tier1DbContext (Npgsql, snake_case, schemas iam/org/kpi/dc/adm/logs, xmin row versions),
 │   migrations, Seeder/ReferenceSeed/KpiFramework/UniversityStructure, RetentionJob
 ├─ Logging: OutboxDispatcher (leader advisory lock, hash chain → Mongo), TechLogWriter/Pump (bounded channel),
 │   ChainVerifier + NightlyChainVerification + CheckpointPublisher (file + direct SMTP)
 ├─ Email: SmtpEmailSender (MailKit; PickupDirectory → .eml), MailDispatcher (SKIP LOCKED, 8 retries), FormReminderJob
 ├─ Files: LocalFileStore (Files:Root), ClosedXML spreadsheets, KpiWorkbook, SeedImporters
 └─ Security: DataProtection secret protector, password hasher, QR, HttpCurrentUser; AccessVersionPoller (5 s)

Stores: PostgreSQL 18 (system of record, both outboxes) · MongoDB 8 (chained audit/security/access/integration logs,
        http_requests, app_errors with TTL) · local disk (evidence, DP keys, checkpoints)
Background: OutboxDispatcher, TechLogPump, NightlyChainVerification, AccessVersionPoller, MailDispatcher,
            RetentionJob, FormReminderJob (all in-process IHostedService; only OutboxDispatcher is leader-elected)
External integrations today: SMTP only. No inbound webhooks, no HTTP clients, no SSO, no SMS/WhatsApp/payments.
```

**Dual-write status:**
- Postgres to Mongo audit: correct transactional outbox.
- Postgres to SMTP: an outbox exists, but form notifications are enqueued in a separate second transaction (BE-3), and checkpoint mail bypasses the outbox (BE-12).
- Disk to Postgres for files: the file is written first, so orphans can remain (BE-19).

---

## Extension readiness and target design

### Readiness scorecard

| Capability | Today | Gap |
|---|---|---|
| Transactional outbox | Yes for audit; partial for mail | Generalize to domain events; enqueue notifications inside the business transaction |
| Retry/backoff, leasing | Yes (mail: SKIP LOCKED, 8 attempts) | No dead-letter UI or resend, no Retry-After handling, no per-provider rate limit |
| Provider abstraction | `IEmailSender` (Infrastructure) | Channel-agnostic sender, template ids, delivery status callbacks |
| Recipient model | Email string only | User id, phone (E.164), WhatsApp opt-in, language, channel preferences |
| Inbound webhooks | None | Signature verification, raw-body capture, `inbound_events` dedup table, replay protection |
| Secrets/config | env/appsettings + StartupGuards | Typed validated options per provider, secret rotation (two active keys), no secrets in logs |
| Outbound HTTP | None | `IHttpClientFactory` + resilience (timeouts, retry on idempotent calls, circuit breaker) |
| SSO | Local password + TOTP | External-login mapping table, OIDC handler, JIT/link policy |
| Payments | None | Out of scope today; design below if needed |

### (a) Integrations layer: outbox, delivery and provider adapters

**Event outbox (generalizes `logs.outbox`; keep the audit chain as its own consumer).**
- Table `int.outbox_events(id uuid v7 PK, type text, version int, aggregate_type, aggregate_id, payload jsonb, occurred_at, available_at, attempts, locked_until, processed_at, last_error)`.
- Application calls `IEventOutbox.Add(new FormMoved(...))` before the single `SaveChanges`. A `DomainEventDispatcher` hosted service claims rows with SKIP LOCKED and a lease, and routes each row to `IEventHandler<T>` implementations.
- Handlers must be idempotent. Store `(handler, event_id)` in `int.processed_events` with a unique constraint.

**Notification pipeline** (the `NotificationRequested` handler turns business events into deliveries).
- `INotificationPublisher.Publish(new NotificationRequest(EventKey: "form.awaiting_review", RecipientUserId, Variables, DedupKey))`.
- `notify.templates(key, channel, lang, subject, body, provider_template_id, provider_template_lang, version, active)` replaces the hardcoded Arabic strings and the dead `MessageTemplate` entity.
- `notify.deliveries(id, request_id, user_id, channel{email,whatsapp,sms,inapp}, address_masked, address_protected, template_key, template_version, variables_protected jsonb, status{PENDING, IN_PROGRESS, ACCEPTED, DELIVERED, FAILED_RETRYABLE, FAILED_FINAL, UNKNOWN, CANCELLED}, provider, provider_message_id, attempts, next_attempt_at, locked_until, last_error, created_at, sent_at, delivered_at)`.
  - Unique `(request_id, channel)`; unique `(provider, provider_message_id)`.
- Channel routing per user preference and event policy. Example: SoD-critical items go by email plus in-app; reminders use the user's preferred channel; WhatsApp only if `whatsapp_opt_in_at` is set and an approved template exists, since WhatsApp Cloud API free text is allowed only inside the 24-hour customer-service window.
- An in-app inbox (`notify.inbox`) is required by requirements 4-15 and is the zero-cost channel.
- `DeliveryDispatcher` (generic version of MailDispatcher):
  - claims due deliveries;
  - resolves `IChannelSender` by channel and provider;
  - classifies results as `Accepted(providerId)`, `Retryable(retryAfter)`, `Permanent(code)` or `Unknown`;
  - uses exponential backoff with jitter and honours Retry-After;
  - caps concurrency per provider;
  - after max attempts sets `FAILED_FINAL`, which is visible in the admin UI and can be resent via a new delivery with `resent_from_id`.

**Adapters** (Infrastructure/Integrations/<Provider>/), each with:
- typed options with `ValidateOnStart`;
- a named HttpClient with an explicit timeout;
- an `IChannelSender` implementation;
- an optional `IDeliveryStatusWebhook` implementation.

| Channel | Adapter | Notes |
|---|---|---|
| Email | `SmtpChannelSender` (MailKit, current code); optional `ApiMailSender` for an HTTP provider | Deterministic Message-ID; bounce/complaint webhook → DELIVERED/FAILED_FINAL |
| WhatsApp | `WhatsAppCloudSender` | Template messages with locale; store wamid; status webhook (sent/delivered/read/failed) |
| SMS | `SmsSender` (local Egyptian gateway) | Arabic is UCS-2 (70 chars per segment), so compute segments and cost; E.164 validation |
| Payment (future) | `IPaymentGateway` + `payments(id, purpose, amount_minor int, currency, status, provider_ref, idempotency_key unique)` | Source of truth is the server callback plus status query, never the browser redirect; reconciliation job |

**Inbound webhooks:**
- Route group `/api/integrations/{provider}/webhook`: anonymous, no CSRF, its own rate limit.
- Buffer the raw body (`EnableBuffering`) and verify the signature per the provider spec, using a constant-time compare and a timestamp window. Paymob, for example, computes its HMAC over ordered fields, not the raw body.
- Insert into `int.inbound_events(provider, event_id, received_at, payload, signature_ok, processed_at)` with a unique `(provider, event_id)` and return 200 fast. Processing happens in a background handler.
- Process status changes monotonically: never move from DELIVERED back to SENT.

**Secrets:**
- Keep env and appsettings for on-prem, but bind each provider to `XxxOptions` with `[Required]`, and extend StartupGuards for enabled providers.
- Support `CurrentSecret` and `PreviousSecret` for webhook verification during rotation.
- Never log tokens. Mask phone numbers as is already done for emails.

**SSO (when needed):**
- Table `iam.external_logins(provider, subject, user_id, linked_at)` with unique `(provider, subject)`.
- An OIDC handler issues the **same server-side session** (`UserSession`) after mapping, so revocation, MFA policy and RBAC stay unchanged.
- Policy: link only pre-provisioned users by verified email, with no JIT creation of privileged roles.

**Failure-point table (target):**

| Failure point | Done | Saved | Status | Completion | Dedup |
|---|---|---|---|---|---|
| Crash before commit | nothing | nothing | – | user retries (Idempotency-Key) | idem key |
| After commit, before dispatch | state + event | outbox row | PENDING | dispatcher picks up | event id |
| Provider timeout | maybe sent | attempt++ | UNKNOWN → query provider or retry with provider idempotency key | status webhook/poll | provider_message_id unique |
| Sent, local save fails | sent | lease expires | retried | duplicate possible (email): Message-ID | `(request_id, channel)` |
| Final failure | – | FAILED_FINAL | admin resend | new delivery, `resent_from_id` unique | |

### (b) How to add a new module (for example "Initiative follow-up / التوجيهات")

1. **Domain:** `src/Tier1.Domain/<Module>/<Module>.cs`. Entities are POCOs with `uint Version`. Status enums use stable English names.
2. **Persistence:** `Tier1DbContext.<Module>.cs` (partial) holds the mapping in its own Postgres schema, plus `IsRowVersion()`, unique and check constraints, and FKs. Add a migration with `dotnet ef migrations add <Module>`. Reference data goes in a numbered idempotent seed, not in the migration.
3. **Application:** `src/Tier1.Application/<Module>/`
   - `<Module>Rules.cs`: pure, with no EF code.
   - `<Module>Service.cs`: orchestration. Shape every write as: load, check scope with `access.RequireAsync` and `UnitScope`, run the rules, apply `Concurrency.Expect` with a **required** version, `audit.Record`, `events.Add`, then **one** SaveChanges (or an explicit transaction).
   - `Dtos.cs`: request and response records. Never return entities.
   - Register the services in `Application/DependencyInjection.cs` under a module section. Better still, give each module its own `Add<Module>()` extension.
   - Expose a module-scoped port (`I<Module>Store`) rather than growing `IAppDb`.
4. **Permissions:** add the feature, its capabilities and specials to `Access/permission-catalog.json`. Add default-role grants in `RoleRules` and the seed. Feature keys look like `<mod>.<feature>`.
5. **Endpoints:** `src/Tier1.Api/Endpoints/<Module>Endpoints.cs` with `Map<Module>(this RouteGroupBuilder api)`, called from Program.cs.
   - Every route declares `.RequirePermission`, `.RequireSpecial` or `.RequireAnyPermission`; EndpointPolicyTests enforces this.
   - Use business verbs as `POST /{id}/<verb>`.
   - Paginated lists return `{items, page, pageSize, totalCount}`.
   - Mark sensitive routes with `.RequireRateLimiting(SensitiveLimit)`.
6. **Events and notifications:** publish domain events through the outbox, then add a template row per channel and language. Do not call notifiers inline.
7. **Background work:** use an `IHostedService` with `pg_try_advisory_lock(<jobKey>)` and an idempotent run.
8. **Contract:** update OpenAPI, regenerate the TS types in `web/src/api/<module>.ts`, and add error codes to the error table.
9. **Tests:**
   - Unit tests for the rules.
   - API integration tests: the authorization matrix, out-of-scope returns 404, invalid transition returns 409, stale version returns 409, double POST with an Idempotency-Key replays.
   - A concurrency test with two parallel requests.
   - An audit-event assertion.
   - An EndpointPolicy test entry.
10. **Docs:** a requirements reference (REQ/BR) in the service summary, deployment keys, and an ADR if a default policy changes.

### (c) Recommended ADRs

| ADR | Decision to record | Status |
|---|---|---|
| ADR-001 | Modular monolith on Postgres; module-owned schemas and ports; architecture tests enforce boundaries (BE-14) | Proposed |
| ADR-002 | Error contract: RFC 9457 problem+json with `code`, `traceId`, `errors[]`; 400 vs 422 rule; error-code registry (BE-6) | Proposed |
| ADR-003 | Concurrency: mandatory version tokens on all updates and transitions (409 on mismatch, 428 when missing); no aggregate-row touch on child edits (BE-1, BE-2) | Proposed |
| ADR-004 | Idempotency-Key for POST creates, uploads and decisions; storage, TTL, replay semantics (BE-8) | Proposed |
| ADR-005 | Generic domain-event outbox and handler idempotency; notifications are always post-commit and asynchronous (BE-3) | Proposed |
| ADR-006 | Notification model: channels, templates per (event, channel, lang), user contact and consent fields, in-app inbox, delivery statuses (BE-10, BE-11) | Proposed |
| ADR-007 | Integration adapter standard: typed validated options, HttpClient resilience, webhook verification and inbound dedup, secret rotation | Proposed |
| ADR-008 | Scheduled jobs: Postgres advisory-lock leader per job; behaviour with multiple instances (BE-15) | Proposed |
| ADR-009 | API versioning (`/api/v1`) and OpenAPI-generated TS types as the contract source with a CI diff gate (BE-18, BE-22) | Proposed |
| ADR-010 | Health and observability: liveness/readiness composition, OpenTelemetry metrics and traces, alert thresholds (outbox age, failed deliveries) (BE-13) | Proposed |
| ADR-011 | SSO via OIDC mapped onto server-side sessions (keeps immediate revocation); provisioning and linking policy | Needs decision |
| ADR-012 | Data retention for outbox, mail and orphan files (BE-19, BE-20) | Proposed |

**Open questions that need decisions:**
- Q-01: Will the API run as more than one instance? This decides how urgent BE-15 is.
- Q-02: Which SMS/WhatsApp provider will be used, and is WhatsApp Business onboarding approved?
- Q-03: Is the university's SSO (Entra ID or other) in scope for phase 2?
- Q-04: What is the retention period for delivered notification content?
