# Tier1 security review

Mode: review only. No project files changed. Branch `phase-1-foundation` @ e9b1a7e.
Method: the `security-review` skill (threat model, then authz/isolation, auth/sessions, secrets, injection, files, config, deps).
Evidence comes from reading the code. No exploit was run against a live instance. "verified" means I traced the path in the code; "likely" means the code path exists but depends on library behaviour or deployment that I did not execute.

## Executive summary

The base is strong. Every `/api` route declares a policy, and there is a fallback policy too. Sessions are opaque server-side tokens stored as hashes, in `__Host-` HttpOnly cookies. Antiforgery is checked on every unsafe method. Passwords use PBKDF2-SHA512 with 210k iterations. TOTP has replay protection (`LastTotpStep`) and atomic attempt reservation. CSP, HSTS, nosniff and frame-deny are set. Uploads are checked by magic bytes and stored under server-generated keys behind a strict regex. Mongo audit chains use HMAC with an external key and an insert+find-only DB role. StartupGuards refuse dev settings outside Development. `npm audit --omit=dev` reports 0 vulnerabilities across 262 prod packages.

The remaining risks sit in **service-level scope checks that are missing on some mutations and reads**, in **availability** (Excel zip bomb, no upload quota or rate limit, account-lockout DoS) and in **deployment and repo hygiene** (app uses the Postgres superuser, DB dumps and log exports committed to git). There is also a design gap: **MFA is tied to role codes, not to capabilities**.

**Commit 224ed7e (home page for all roles): no data leak found.** The commit adds `dash.home V:A` for T1O and SYS (permission-catalog.json:166-167, Seeder `GrantHomeToAllRolesOnceAsync`). It also *narrows* HomeService: definition-status and conflict counts are now gated by dictionary/conflicts rights (HomeService.cs:111-130). Every task card is still gated by its own capability, and structure completeness stays unit-scoped (StructureService.cs:84-88). SYS gets an empty home. One residual leak predates the commit, see SEC-9: `Standards[].OpenConflicts` (HomeService.cs:108,131) is still shown to dictionary viewers who have no `kpi.conflicts` right.

## Findings (most severe first)

### SEC-1: Excel import can be zip-bombed (memory/CPU DoS) by any data-entry user
- Severity: **Medium** · Confidence: likely (library behaviour not executed)
- Location: `src/Tier1.Api/Endpoints/CollectionEndpoints.cs:77-85`, `src/Tier1.Application/Collection/FormsService.Import.cs:52`, `src/Tier1.Infrastructure/Files/ClosedXmlSpreadsheets.cs:41,50-54`
- Evidence: `const long max = 5 * 1024 * 1024;` checks only the *compressed* size, then `book = new XLWorkbook(content);` loads the whole workbook. The row limit `if (lastRow - 1 > maxRows)` runs only *after* the full load, and `lastCol` has no limit at all. The endpoint has no `RequireRateLimiting`.
- Scenario: a user with `dc.forms:E` (DE, DREP and CTC roles, so many accounts) uploads a 5 MB .xlsx whose `sheet1.xml` inflates to more than 1 GB of repeated `<c>` cells, or a single row spread over 16,384 columns. ClosedXML/OpenXML SDK (no `MaxCharactersInPart`) builds millions of cell objects. That can push the process into OOM or long GC stalls, and it takes the whole API down during a collection deadline. Repeated requests make it worse.
- Fix: before opening, inspect the zip with `System.IO.Compression.ZipArchive`. Reject when total uncompressed size is over about 20 MB, when any entry's compression ratio is over 100, or when there are more than N entries. Read with `SpreadsheetDocument.Open(stream, false, new OpenSettings { MaxCharactersInPart = ... })` or a streaming reader. Cap columns at 12. Add `.RequireRateLimiting(ApiSetup.SensitiveLimit)` to `/forms/{id}/import`.

### SEC-2: Org-unit mutations and unit indicator assignment ignore the caller's unit scope (vertical/horizontal escalation if the matrix grants scoped edit)
- Severity: **Medium** · Confidence: verified (latent with the current default grants)
- Location: `src/Tier1.Application/Organization/OrgUnitsService.cs:84-88` (Create: parent not scope-checked), `:121-124` (Update), `:167-171` (Close/Merge); `src/Tier1.Application/Organization/UnitIndicatorsService.cs:62-65` (SaveAsync)
- Evidence: `await access.RequireAsync(Feature, Cap.Edit, ct); var unit = await db.OrgUnits.FirstOrDefaultAsync(u => u.Id == id, ct)`. There is no `UnitScope.For(...).Require(unit)`, although List/History/GetAsync in the same services do apply scope. The UI computes `canEdit` from the Edit scope (UnitIndicatorsService.cs:59), so administrators will believe the U-scope is enforced.
- Scenario: the matrix (or an individual override) gives a role `org.units:E/D` with scope "its units", for example letting deans maintain their own college. A dean of college A can then rename or close any unit in the university, merge it into another, create units under any parent, or change which indicators any college must report. That rewrites another college's data-collection obligations. Today only PM and SYS hold these caps, at scope A.
- Fix: in every mutating method, load the target (and the parent or merge target) and call `UnitScope.For(me, Feature, cap).Require(...)`. Add an EndpointPolicy-style test that runs each mutating service method as a U-scoped user against an out-of-scope unit and expects 404.

### SEC-3: Target distribution endpoint leaks every college's targets to unit-scoped users
- Severity: **Medium** · Confidence: verified
- Location: `src/Tier1.Application/Kpi/TargetsService.cs:280-301`, route `GET /api/kpi/indicators/{code}/distribution` (`Tier1Endpoints.cs:99-100`, policy `kpi.targets:V`)
- Evidence: `await access.RequireAsync(Feature, Cap.View, ct);` loads all active colleges and `targets.Where(t => t.IndicatorId == indicator.Id)`, then returns `CollegeShare(c.Id, c.NameAr, own.Value, own.Numerator, own.Denominator, own.Text, ...)` for **every** college. `GetAsync` (line 57-70) does filter by `viewScope`.
- Scenario: a data-entry user, department rep, viewer or VP (all `kpi.targets V:U`) calls `/distribution?year=2027` for each indicator code. They receive the targets, numerators and denominators of every other college. That breaks the documented rule that "out-of-scope units are treated as non-existent".
- Fix: apply `UnitScope.For(me, Feature, Cap.View)` to `colleges`. If the university comparison is needed, return the aggregated result only, without per-college rows, when scope is not All. Add a regression test with a DE user.

### SEC-4: The application connects to PostgreSQL as the bootstrap superuser
- Severity: **Medium** · Confidence: verified
- Location: `deploy/docker-compose.yml:11` (`Username=tier1`), `:38` (`POSTGRES_USER: tier1`)
- Evidence: the official postgres image makes `POSTGRES_USER` a SUPERUSER, and the app's runtime connection string uses that same account. Migrations (`--migrate`) use it too.
- Scenario: any future SQL injection, a malicious migration, or a compromised app container gets full cluster control. That includes `COPY ... TO PROGRAM` (OS command execution inside the DB container), reading or altering the audit outbox before dispatch, and dropping roles. It also makes the "audit logs cannot be tampered with" claim depend entirely on the app process.
- Fix: keep `tier1` (superuser) for `--migrate` only. Create a `tier1_app` role with `CONNECT`, `USAGE` on the schema and `SELECT/INSERT/UPDATE/DELETE` on tables, and use it for the running app. Revoke `UPDATE/DELETE` on the outbox and audit tables from `tier1_app` where the design allows it. Document this in docs/deployment.md.

### SEC-5: MFA enforcement is keyed to role *codes*, not to the capabilities a user holds
- Severity: **Medium** · Confidence: verified
- Location: `src/Tier1.Application/Accounts/Sessions.cs:34` (`MfaRoles = ["SA","SYS","AUD","PRES","HC","PM"]`), `SignInService.cs:98,267-274`, `src/Tier1.Api/Security/PermissionAuthorization.cs:32`, `RbacAdminService.cs:76-81` (role copy)
- Evidence: `if (!context.User.HasClaim(ClaimNames.MfaVerified, "true") && mine.RoleCodes.Any(auth.Value.MfaEnforcedFor)) return;` checks role codes only. Custom roles (`/iam/roles` with `CopyFromRoleId` from PM, QA, DEAN and so on) and Allow overrides are never MFA-enforced. QA (`approve_final`), DEAN (`approve_college`), CTC and T1O (`dc.university:E`, which transfers official university values) are not in the list either.
- Scenario: (a) an SA, with a second SA's approval, creates `PM2` as a copy of PM for a deputy. The deputy then holds dictionary, targets and units edit plus `reopen` with password-only sessions. (b) A phished QA or T1O password alone lets the attacker give final approval to forms, or change the university's official KPI values.
- Fix: derive "MFA required" from effective access. Require it when the user holds any decision special (`PermissionCatalog.DecisionSpecials`), any `adm.*` or `log.*` capability, `dc.university:E`, or E/A/D at scope All. Apply the same check in `RoleRequiresMfaAsync` and in `PermissionHandler`. Revoke sessions when such a grant appears (as `AfterRoleChangeAsync` already does for codes).

### SEC-6: Database dumps and audit/HTTP log exports are committed to git
- Severity: **Medium** · Confidence: verified (content appears to be demo data)
- Location: `نقل-المنظومة/backups/tier1.dump`, `tier1_demo.dump` (pg_dump custom format, about 1.6 MB), `نقل-المنظومة/backups/mongo/*/{audit_events,security_events,http_requests,...}.jsonl` (about 15 MB), added in commit 2143403. The accompanying README says the transfer package also carries `.env`, data-protection keys and "both databases with their accounts". Those files are not in git, but this README sits in the repo.
- Evidence: the security_events refs are all `*@tier1.local`. app_errors contain full stack traces with local paths (`D:\AMR_Private\Tier1\...`). The dumps contain the users table (PBKDF2 hashes, `MfaSecretProtected`) and the whole dataset.
- Scenario: anyone with repo access (forks, CI, contractors) gets a full DB copy and the audit trail. The workflow normalises moving DB dumps through git. The first time it is done with the real university DB, every account hash, staff member and evaluation result leaks, and stays in history permanently.
- Fix: delete the folder, add `*.dump`, `backups/` and `نقل-المنظومة/backups/` to `.gitignore`, and purge it from history (`git filter-repo`). Treat any account in those dumps as compromised if it was ever reused outside the demo. Move the migration tooling to a separate private channel, and add gitleaks plus a size/extension pre-commit check in CI.

### SEC-7: Role assignment bypasses the "you cannot grant what you don't hold" and decision-special rules
- Severity: **Medium** · Confidence: verified
- Location: `src/Tier1.Application/Access/UsersAdminService.cs:222-256` (AssignRoleAsync) compared with `:473-480` (EnsureGranterCoversAsync, used only for overrides)
- Evidence: AssignRoleAsync blocks only reserved roles (`ReservedRoles.Contains(role.Code) && !mine.IsSuperAdmin`). It does not check that the granter's own access covers the role's grants, and it does not forbid roles that carry `DecisionSpecials`. `AddOverrideAsync` enforces both.
- Scenario: a holder of `adm.grants:A` (PRES by default, or any role an SA later gives grants:A) assigns QA (`approve_final`), PM (`reopen`, full dictionary, targets and units) or DEAN with arbitrary scope values to a colleague. The colleague becomes effective immediately, with no second approval. Through roles that person can hand out authority the granter is forbidden to hand out individually.
- Fix: in AssignRoleAsync, require `mine.Covers(...)` for every grant of the role at the requested scope, unless the caller is SA. Route roles containing decision specials through the same SA approval (`needsApproval`) used for SA assignments. Audit-log the computed effective delta.

### SEC-8: Uploads have no rate limit or per-user quota, and stored files are never deleted (disk exhaustion)
- Severity: **Low-Medium** · Confidence: verified
- Location: `src/Tier1.Api/Endpoints/CollectionEndpoints.cs:96-123` (evidence, structure and plan uploads have no `RequireRateLimiting`), `DocumentsService.cs:213-236` (no quota; Void keeps the file), `LocalFileStore.cs:15` (20 MB each)
- Evidence: the ApiSetup comment says the sensitive limit covers "الرفع" (uploads), but none of the upload routes apply it. `VoidAsync` only sets `VoidedAt`.
- Scenario: a DE user scripts 20 MB PDF uploads, uploading then voiding then uploading again. Each one persists on `/data/files`. The appdata volume (shared with DP keys and checkpoints) fills, and writes, key-ring rotation and checkpoints start to fail. Each upload is also buffered fully in memory (`MemoryStream` up to 60 MB for plan documents).
- Fix: add `.RequireRateLimiting(ApiSetup.SensitiveLimit)` to all upload and import routes. Add a per-submission and per-user byte quota, reject duplicates by `Sha256`, stream to disk instead of buffering, and alert on volume usage.

### SEC-9: Cross-feature information leaks via the dictionary and home endpoints
- Severity: **Low** · Confidence: verified
- Location: `src/Tier1.Application/Kpi/DictionaryService.cs:64-72,82-98`, `HomeService.cs:108,131`
- Evidence: `ListAsync` returns the university baseline, the 2031 target and the "each college" target (from `db.Targets`) and `OpenConflicts` counts to anyone with `kpi.dictionary:V`. `StandardsAsync` and Home return `OpenConflicts` per standard. DE, DREP, INIT, VIEW and T1O hold dictionary V:A but have no `kpi.conflicts` right, and DE, DREP and VIEW have targets only at V:U.
- Scenario: low-privilege viewers learn university targets and pending-conflict counts that the matrix withholds from them.
- Fix: null out target columns unless `kpi.targets:V` covers the university, and conflict counts unless `kpi.conflicts:V`. Do this in the DTO builders, not in the UI.

### SEC-10: Account-lockout DoS and per-IP throttles keyed on exact IP
- Severity: **Low** · Confidence: verified
- Location: `src/Tier1.Application/Accounts/SignInService.cs:59-74`, `Sessions.cs:22` (`MaxFailedLoginsPerAccount = 20`), `Security/LoginThrottle.cs:18`, `Startup/ApiSetup.cs:64`
- Evidence: 20 failures from any set of IPs lock the account for 15 minutes. The per-IP block (5) and the rate limiter (30/min) are keyed by the full IP string, so an attacker rotating IPv6 /64 addresses or a few hosts is not slowed.
- Scenario: an attacker who knows the email convention re-locks the president's, a dean's or QA's account every 15 minutes during an approval deadline. The audit log records it, but the user cannot sign in.
- Fix: partition limiters by /64 for IPv6. Use progressive delays rather than hard lock, or a lock that the owner's known device or session can bypass. Alert on `auth.locked` bursts. Optionally check new passwords against breached-password lists (`PasswordPolicy`, SignInService.cs:279).

### SEC-11: Approval authority inherits the (widenable) *view* scope
- Severity: **Low** · Confidence: verified
- Location: `src/Tier1.Application/Collection/FormsService.cs:209,224` (`LoadAsync(id, Cap.View, ...)` before `EnsureReviewerAsync`), `EffectiveAccess.cs:91-98` (`ViewsWholeUniversity` turns View/Print U into All)
- Evidence: a review special is unscoped, and its reach equals the user's `dc.forms:V` scope. View is widened to All whenever any assignment scope contains a "views whole university" unit.
- Scenario: a CTC or DEAN assignment whose scope list includes a strategic-planning unit flagged `ViewsWholeUniversity` can review or approve every college's forms. Separation of duties still blocks self-approval.
- Fix: scope approvals by an explicit capability (for example `dc.forms:E` scope, or a dedicated review scope), or exclude the wide-view expansion when evaluating approvals.

### SEC-12: System administrator (SYS) can change result-affecting business thresholds
- Severity: **Low** · Confidence: verified
- Location: `permission-catalog.json:616-630` (`adm.settings` → `"SYS": "VE:A"`), `src/Tier1.Domain/Common/SystemSetting.cs:23-38` (`tier1_min`, `near_target_percent`, `plan_q1_start_year`...), `SettingsService.cs:30`
- Evidence: SOD-11 says SYS "does not see transaction content or approve academic decisions", yet SYS can change the tier thresholds and the plan calendar that decide the university's official tier classification.
- Scenario: a SYS account (or a compromised one) changes `tier1_min` and silently changes the scorecard tier shown to the president and council. It is audited, but it is not dual-controlled.
- Fix: split the settings into technical ones (SYS) and evaluation ones (PM or HC with approval), or require a second approver for evaluation keys.

### SEC-13: Development guard bypassable, and production does not check for demo data or dev keys
- Severity: **Low** · Confidence: verified
- Location: `src/Tier1.Api/Startup/StartupGuards.cs:47-60`, `docker-compose.dev.yml:24-26`
- Evidence: `CheckLoopbackOnly` inspects `urls`, `ASPNETCORE_URLS` and Kestrel endpoints only. `ASPNETCORE_HTTP_PORTS`/`HTTPS_PORTS` bind all interfaces and pass the check (the dev compose relies on this). Outside Development nothing rejects `*@tier1.local` accounts already in the DB, the committed dev chain key `ybMmZEAJ...`, or the documented demo password `Tier1-Demo-Pass1` (docs/test-guide.md:72).
- Scenario: (a) Development is run on a lab server with `ASPNETCORE_HTTP_PORTS=80` (copied from the compose file). Demo SA accounts with a public password and MFA off are reachable on the LAN. (b) A dev DB or dump (see SEC-6) is restored into production, and the demo SA accounts work there.
- Fix: also parse `ASPNETCORE_HTTP_PORTS`/`HTTPS_PORTS` and the effective `IServerAddressesFeature` after start. In non-Development, refuse to start if any active user ends with `@tier1.local` or if the chain key equals the committed dev key.

### SEC-14: Session and access caches allow up to 30-60 s of use after revocation on other instances; unlimited concurrent sessions
- Severity: **Low** · Confidence: verified
- Location: `src/Tier1.Application/Accounts/Sessions.cs:69,94` (30 s principal cache; idle and expiry are not rechecked on a cache hit), `AccessService.cs:86`
- Evidence: `if (cache.TryGetValue<SessionPrincipal>(Key(hash), out var cached)) return cached;`. Revocation clears the cache only on the local instance.
- Scenario: in a multi-instance deployment, a deactivated user or revoked session keeps working for up to 30 s on the other node. There is no cap on concurrent sessions per user (the skill policy asks for an explicit decision).
- Fix: acceptable for the single-instance deploy. Document it and decide a concurrency policy. For scale-out, use a distributed cache or a revocation version that is checked on every hit.

### SEC-15: Docker and deploy hardening gaps
- Severity: **Low** · Confidence: verified
- Location: `deploy/docker-compose.yml:18,34,49,86`, `deploy/Dockerfile:5,11,17`, `Tier1.Api/Startup/Maintenance.cs:23,45-60`
- Evidence: the DP certificate password is passed as a plain environment variable (`DataProtection__CertificatePassword`, visible in `docker inspect`). Images use floating tags (`postgres:18`, `mongo:8`, `caddy:2`, `node:24-bookworm-slim`, `dotnet/sdk:10.0`) with no digests. Containers have no `read_only`, `cap_drop: [ALL]` or `no-new-privileges`. `--backup` writes the DB dump and the DP key ring zip unencrypted. The dev `web` service runs `npm install` instead of `npm ci`.
- Fix: mount the password as a Docker secret file, pin images by digest and update them with Renovate, add `read_only: true`, `tmpfs: /tmp`, `cap_drop: [ALL]` and `security_opt: [no-new-privileges:true]` to app and caddy, encrypt backups (age/GPG) and store them off-host, and use `npm ci` in dev.

### SEC-16: Dependency scanning is not part of the pipeline (.NET not scanned)
- Severity: **Low** (process) · Confidence: verified
- Evidence: `npm audit --omit=dev` gives 0 issues. I did not run `dotnet list package --vulnerable` (build-restricted). The relevant packages are ClosedXML 0.104.2, MailKit 4.18.1, MongoDB.Driver 3.12.0, QRCoder 1.6.0, Npgsql EF 10.0.3. The repo has no CI workflow for audits or secret scanning.
- Fix: add a CI job that runs `dotnet list package --vulnerable --include-transitive`, `npm audit --omit=dev` and gitleaks, and fails on High.

### Checked and found sound (no finding)
- Every `/api` endpoint has an explicit policy plus a Staff fallback (`ApiSetup.cs:38-44`, test `EndpointPolicyTests.cs:50`). Services re-check caps and unit scope for forms, results, progress, structure, documents and targets (except SEC-2 and SEC-3). Document download authorises via the owning form, college or plan.
- Login uses a uniform error and a dummy hash, atomic failure reservation, and session rotation after MFA or a password change. A change-password request from an MFA-pending session is refused. TOTP uses ±1 window, a replay step and a 5-try lock. Temporary passwords come from CSPRNG (16 chars). SA assignment needs an independent second SA, and at least 2 SAs are enforced.
- CSRF: header-token antiforgery on all unsafe `/api` methods, `SameSite=Strict` AF cookie, `__Host-` cookies enforced in production.
- No raw SQL with user input. The Mongo log search regex is escaped. CSV export neutralises formulas. ClosedXML writes strings as text.
- Uploads: allow-list with magic bytes, server-generated storage keys, regex plus root containment (`LocalFileStore.cs:36-45`), nosniff, inline only for pdf/png/jpeg.
- Frontend: no `dangerouslySetInnerHTML` with server data (the only one is the static shadcn chart style, which is unused). Shiki tokens are rendered as React spans. No tokens in local storage, no open redirects. Dev role tabs are gated by `import.meta.env.DEV` plus localhost.
- Headers: strict CSP (`script-src 'self'`), HSTS on HTTPS, `no-store` on `/api`. Forwarded headers are trusted only from the Caddy IP. Mongo app role is insert+find only.
- Git history: no production secrets found (only dev values, by design).

## Extension readiness (before payments, WhatsApp, email campaigns or new modules)

1. **Central resource-scope enforcement.** SEC-2 and SEC-3 show that a scope check per method gets forgotten. Add a helper such as `RequireScopedAsync(feature, cap, unit)` used by every new service, plus a reflection or integration test that runs each mutating and read endpoint as a U-scoped user against an out-of-scope unit.
2. **Capability-based MFA and step-up** (SEC-5). Payment refunds, message broadcasts and integration-key management must require fresh MFA, for example a TOTP re-prompt within the last 5 minutes, not just a role code.
3. **Secrets management.** Move integration secrets (payment keys, WhatsApp tokens, SMTP) out of env vars into Docker secrets or a vault. Keep separate keys per environment, a rotation runbook, and gitleaks in CI (SEC-6, SEC-15).
4. **Inbound webhooks.** Verify signatures (HMAC or provider SDK) over the raw body, enforce a timestamp window, store the event ID with a unique constraint for idempotency, process through the existing outbox, and never trust client "payment succeeded" redirects; confirm server-to-server.
5. **Outbound calls.** Use a typed `HttpClient` per provider with a host allow-list (SSRF guard), timeouts, Polly retries with idempotency keys, and logging to `integration_events` without bodies or PII.
6. **Abuse and cost controls.** Add per-user and per-tenant rate limits and quotas for messaging, OTP, export and uploads, partitioned by user and by IPv6 /64 (SEC-8, SEC-10). Add opt-in and consent records for phone and WhatsApp numbers.
7. **Least-privilege identities.** Use a non-superuser DB role for the app (SEC-4), and separate DB/Mongo identities for background jobs and integrations so one compromised connector cannot touch IAM tables.
8. **Payments.** Use hosted checkout or tokenisation to stay out of PCI scope. Keep a server-side amount and price source of truth, an append-only ledger table with DB constraints, and four-eyes approval for refunds via the existing SOD framework.
9. **Pipeline.** Add dependency audits (`dotnet list package --vulnerable`, npm audit), a container image scan and SAST (semgrep) as blocking CI gates. Pin image digests.
10. **Data lifecycle.** Write a retention and deletion policy for evidence files, exports and backups. Encrypt backups. Never move DB dumps through git.

## Coverage
Checked: authN, password storage, MFA/OTP, sessions and cookies, CSRF, authorisation on all endpoints (6 endpoint files, about 95 routes) and scope in services, mass assignment (DTOs map explicit fields), input validation, file upload, download and import, frontend XSS, secrets in git and history, headers and CORS (no CORS configured, same-origin only), logging of sensitive data (masked email on failures, no bodies or queries logged), audit-chain design, npm deps, deploy config.
Not done: runtime exploitation, `dotnet list package --vulnerable` (build restrictions), an exhaustive read of the E2E tests, the internal pg_dump contents (compressed binary, inferred from the README and the logs).
