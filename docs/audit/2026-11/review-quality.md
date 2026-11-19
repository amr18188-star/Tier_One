# Tier1: tests, code review and backend architecture

Scope: this was a read-only review. I ran no build or tests. I worked from `git log -15`, `git show` on the backend commits (224ed7e, cc5780e, 6fae618), and direct reads of the Application, Api, Infrastructure, tests and web test files. Each finding is graded as **verified** (traced in the code) or **likely** (traced, but depends on configuration or data I could not check).
Most of the recent 15 commits change the frontend. The backend changes are the direct-approval paths (6fae618), the "home page for every role" grant (224ed7e), and E2E updates (cc5780e).

---

## Findings (most severe first)

### CR-1: SYS now holds `dash.home`, which breaks SOD-11 and blocks editing the SYS role
- **Severity:** High. **Confidence:** verified.
- **Location:** `src/Tier1.Application/Access/permission-catalog.json:144-167` (`"SYS": "V:A"` under `dash.home`, module `"dash"`); `src/Tier1.Infrastructure/Persistence/Seeder.cs:222-241` (`GrantHomeToAllRolesOnceAsync`); `src/Tier1.Application/Access/RoleRules.cs:36,70`; `src/Tier1.Application/Access/RbacAdminService.cs:152`; `web/src/features/matrix/MatrixPage.tsx:80,158`.
- **Evidence:**
  - `RoleRules.BusinessModules = {"dash","kpi","dc"}`, and `Validate("SYS", …)` throws SOD-11 for any grant whose module is in that set.
  - Commit 224ed7e gives SYS the `dash.home` grant in two ways: through the catalog defaults (new databases, via `SeedRolesAsync`) and through a direct `RoleGrants.Add` that skips `RoleRules.Validate` (existing databases).
  - The matrix page starts its draft from the role's full grant set (`setDraft(detail.data.grants)`). It then PUTs `clean(draft)`, and `ProposeAsync` runs `RoleRules.Validate(role.Code, …)` on that whole set.
- **What breaks:**
  - Any proposal to change the SYS role, for example adding `adm.settings:E`, is rejected with `sod_sod_11`: "مسؤول النظام لا يرى محتوى المعاملات: الصفحة الرئيسية…". The only way through is to untick Home, which brings back the "no permission" landing page this commit set out to fix.
  - `UsersAdminService.EnsureCompatibleRoleAsync` (lines 500-506) validates other roles against the SYS rules. Assigning T1O to a user who holds SYS now fails too, because T1O also gained `dash.home`.
  - The new test `ReferenceDataTests.Every_role_can_open_the_home_page…` asserts the contradiction instead of catching it.
- **Fix:**
  - Treat the home page as role-neutral. Either move `dash.home` into its own module (for example `"home"`), or add an explicit `NeutralFeatures = {"dash.home"}` exemption in `RoleRules.Validate`. `HomeService` already hides counts the user cannot see, so the page itself is safe.
  - Add a regression test: `Catalog_defaults_of_every_role_pass_RoleRules_Validate`. It runs `RoleRules.Validate(code, false, defaults)` for every non-SA catalog role.
  - Make the seeder's "once" fixes call `RoleRules.Validate` before they write grants (see ARCH-3).

### CR-2: Approval authority uses unscoped specials plus the Forms:View scope
- **Severity:** High. **Confidence:** likely (requires a user with more than one role, which the IAM model allows).
- **Location:** `src/Tier1.Application/Access/EffectiveAccess.cs:46` (`HasSpecial` ignores scope); `src/Tier1.Application/Collection/ApprovalRules.cs:55-64` (`MayReview` and `DirectApprovalTo` use only `HasSpecial`); `src/Tier1.Application/Collection/FormsService.cs:310-316` (`LoadAsync` checks only `UnitScope.For(me, Forms, View)`) and `:367-375`; `src/Tier1.Application/Collection/FormNotifier.cs:71`.
- **Evidence:** Specials are merged into a flat set (`fa.Specials.Add(s)`, EffectiveAccess.cs:135). Review rights are therefore "has the special anywhere" plus "can view this unit". Capabilities are scoped per capability (see the test `An_action_is_scoped_by_its_own_capability_not_by_the_wider_view`); specials are not.
- **What breaks:** A user holds DEAN scoped to F06 and VP scoped to F06 and F08 (VP grants Forms:V on its units). They open an F08 department form and see `directDeanApproval=true`. `POST /approve` moves the form from DepartmentReview straight to Quality, skipping the F08 department representative, committee and dean. The direct-approval paths added in 6fae618 make this worse: one out-of-scope click now skips three steps instead of one.
- **Fix:**
  - Carry a scope on each special in `EffectiveAccess`, taken from the grant that supplied it, for example `Specials: Dictionary<string, (AccessScope, values)>`.
  - Add `UnitScope.ForSpecial(me, feature, special)`, and make `MayReview` and `DirectApprovalTo` take the unit and require `ForSpecial(...).Covers(unit)`.
  - Apply the same rule in `FormNotifier.ReviewersAsync`.
  - Tests: `Dean_of_one_college_with_wider_view_cannot_approve_other_college_form`, and the same case for direct approval.

### CR-3: Direct approval takes over the normal step and the UI hides Return
- **Severity:** Medium. **Confidence:** verified.
- **Location:** `src/Tier1.Application/Collection/ApprovalRules.cs:55-60`; `FormsService.cs:213-214`; `web/src/features/forms/FormPage.tsx:164`.
- **Evidence:**
  - `DirectApprovalTo` returns `Quality` whenever the user has `approve_as_dean_directly`, even when they also hold the normal special for the current step (`canReviewNormally`). Only the super admin is exempt.
  - `ApproveAsync` always takes the direct target when one exists; the request has no way to choose.
  - The UI sets `canReturn = reviewing && !directApproval`, but the server still allows `/return` for a holder of the normal special (`EnsureReviewerAsync(allowDirect:false)`).
- **What breaks:** A dean who also sits on the college committee (roles CTC and DEAN) can never approve only the committee step. Every approval jumps to central quality, and they see no Return button on their own committee step. SOD-C2 ("one person approves one step") is also weakened by design. That needs an explicit product decision (DEC).
- **Fix:**
  - Add `Mode` (`Normal|Direct`) to `ApproveFormRequest`, defaulting to `Normal` when `canReviewNormally`.
  - Return both options in `FormActions` and show Return whenever `MayReview(..., allowDirect:false)` holds.
  - Write down in a DEC whether direct approval satisfies SOD-C2.

### CR-4: A reviewer who approved in an earlier round loses the form from "awaiting me"
- **Severity:** Medium. **Confidence:** verified.
- **Location:** `src/Tier1.Application/Collection/FormsService.cs:81-82,95-96` compared with `:491-494` and `:373`.
- **Evidence:**
  - `ListAsync` builds `approvedByMe` from every Approved event on the submission, across all rounds.
  - `BuildAsync` (`sinceSubmit`) and `EnsureReviewerAsync` (`e.At > SubmittedAt-1s`) count only events since the latest submission.
- **What breaks:** The committee approves, the dean returns the form, and data entry resubmits. The form is back at CollegeCommittee, and the same committee member may approve it (the server allows it and `canReview` is true). But it disappears from their `awaitingMe` list and from the Home task "نماذج تنتظر مراجعتك", so it stalls. Mail is still sent, which makes the mismatch more confusing.
- **Fix:** Filter `approvedByMe` to events with `At >= SubmittedAt - 1s`, joined on the submission. Better: extract one `ReviewEligibility.Can(me, unit, submission, editors, eventsSinceSubmit)` used in all three places (see ARCH-6).

### CR-5: Notifications are enqueued after the state change commits, in a separate save
- **Severity:** Medium. **Confidence:** verified.
- **Location:** `FormsService.cs:202-203, 216-217, 231-232, 247-248`; `FormNotifier.cs:14` (the doc comment says "الرسالة تضاف في معاملة العملية نفسها"), `:36-37`.
- **Evidence:** `await db.SaveChangesAsync(ct); await notifier.MovedAsync(...)` runs a second SaveChanges with no surrounding transaction.
- **What breaks:** The approval is committed, then `ReviewersAsync` fails (a DB timeout, or an `access.GetAsync` exception for one user). The client gets a 500, retries, and gets `not_in_review`. The reviewer is never notified, while the audit log shows the move.
- **Fix:** Enqueue mail before the single `SaveChangesAsync` (the outbox is a DB table), or wrap the move and the enqueue in one `BeginTransactionAsync`.
- **Test:** `Approve_commits_state_and_outbox_atomically` (integration with a fault-injecting `IMailOutbox`).

### CR-6: The reviewer lookup scans every user on each transition, and direct approval widens the recipients
- **Severity:** Medium (performance and noise). **Confidence:** verified.
- **Location:** `FormNotifier.cs:83-90`; `ApprovalRules.MayReview` (allowDirect defaults to true) at `FormNotifier.cs:71`.
- **Evidence:** `UsersWhereAsync` loads all active users and computes `access.GetAsync(u.Id)` for each one, on every submit, approve, return and reopen, and per form in `RemindAsync`.
- **What breaks:** Every department submission now also emails every dean and committee member whose view covers the unit, because they can "direct approve". The daily reminder job costs forms × users effective-access computations.
- **Fix:** Pre-filter candidates in SQL (users with an active assignment to a role whose grant holds the step special, plus overrides), then evaluate only those. Use `allowDirect:false` for notifications unless the product wants the fan-out.

### CR-7: `Seed:Structure=true` moved into the base appsettings.json, so it applies in production
- **Severity:** Medium. **Confidence:** likely (it may be intended).
- **Location:** `src/Tier1.Api/appsettings.json:40-42`; `Seeder.cs:100-110`; `DatabaseMigrator.cs:21`.
- **Evidence:** Before 6fae618 the flag came only from the dev compose file and appsettings.Development.json. It now applies in every environment. `--migrate` on an empty production database therefore loads `ReferenceSeed` and the official structure (23 colleges, 192 departments) without anyone asking. `StartupGuards` does not cover this flag.
- **Fix:** If this is intended, document it in deploy/README and add an explicit `Seed__Structure` to `deploy/docker-compose.yml`. If not, revert to Development-only.

### CR-8: Excel import silently ignores reason-only changes and misreads "1,5"
- **Severity:** Low-Medium. **Confidence:** verified.
- **Location:** `src/Tier1.Application/Collection/FormsService.Import.cs:121-122` (the `same` check leaves out `Reason`) and `:160` (`','` is removed).
- **What breaks:**
  - A row with "غير متاح" and a corrected reason is reported as "same" and not applied.
  - A cell `1,5` (a common decimal-comma habit) is imported as `15` with no warning.
  - The local `current` also shadows the `current` (ICurrentUser) primary-constructor parameter at line 120.
- **Fix:**
  - Include `Reason` in the `same` check.
  - Treat a comma as a thousands separator only when followed by exactly 3 digits; otherwise return a `not_a_number` / "ambiguous decimal" issue.
  - Rename the local.
  - Tests: `Import_reason_only_change_is_applied`, `Import_decimal_comma_is_rejected_not_scaled`.

### CR-9: The default Results view can open on a rehearsal (trial) cycle
- **Severity:** Low. **Confidence:** likely (it is a product decision).
- **Location:** `src/Tier1.Application/Collection/ResultsService.cs:385-394`.
- **Evidence:** `PickCycleAsync` takes the latest non-draft cycle that has an approval, with no `Trial` filter. Every E2E run and every rehearsal creates and approves trial cycles that "open today", so `/results` with no cycleId shows a rehearsal.
- **Fix:** Prefer `!c.Trial` and fall back to trials only when no real cycle exists.

### TEST-1: The guard "every endpoint declares its access" skips every collection endpoint
- **Severity:** High. **Confidence:** verified.
- **Location:** `tests/Tier1.Tests/EndpointPolicyTests.cs:37-42` maps Auth, Iam, Logs, Settings and Tier1, but not `api.MapCollection()`; `src/Tier1.Api/Program.cs` maps it.
- **What breaks:** About 35 routes are outside the guard: forms, approve/return, documents, results/export, scorecard, review/transfer, progress. A new collection route without `RequirePermission` would pass CI. The policy-resolves-against-catalog test also never sees the new `dc.forms:approve_department_directly` / `approve_as_dean_directly` policies.
- **Fix:**
  - Add `ApiEndpoints.MapApi(this RouteGroupBuilder api)` in Api, used by both `Program.cs` and the test.
  - Add `Endpoint_count_matches_program_mapping` so a forgotten group fails.
  - Add `[InlineData("POST /api/forms/{id:guid}/approve", …)]` cases.

### TEST-2: The approval state machine has no unit tests for the new rules
- **Severity:** High. **Confidence:** verified.
- **Location:** `tests/Tier1.Tests/CollectionRulesTests.cs:113-129` covers only `Next`, `SpecialFor` and `EnsureSeparation`. `DirectApprovalTo`, `MayReview`, `SkippedBy`, the status mapping in `FormsService.Move`, and the audit-verb choice are untested. They are pure functions apart from `EffectiveAccess`, which `EffectiveAccessTests` already constructs.
- **What breaks:** Any change to the direct-approval precedence (CR-3) or to the skipped-path rendering can only be caught by the E2E test, which skips without a server (TEST-3).
- **Fix:** Add a decision-table `[Theory]`. Names are in the backlog (B1–B3).

### TEST-3: No CI, and E2E tests pass when skipped
- **Severity:** High. **Confidence:** verified.
- **Location:** There is no `.github/workflows` or other pipeline in the repo. `tests/Tier1.E2E/E2E.cs:68-73` (`E2EFact` skips when `/health/live` does not answer); `ScorecardAndLoadTests.cs:26-27,202-203` (opt-in environment flags).
- **What breaks:** `dotnet test` on a clean machine reports green with every E2E test skipped. Nothing runs the 14 unit-test classes or `vitest` automatically, and nothing would have caught TEST-1 or CR-1 before merge.
- **Fix:**
  - Add a pipeline with these stages:
    1. `dotnet build -warnaserror` plus unit tests.
    2. `npm ci && npm test && npm run build`.
    3. Integration tests (Testcontainers Postgres and Mongo, see TEST-4).
    4. Optionally the E2E tests against `docker compose -f docker-compose.flow.yml`.
  - Add `T1_E2E_REQUIRED=1`, which turns a skip into a failure in CI.

### TEST-4: No integration layer between the pure rules and full E2E
- **Severity:** High. **Confidence:** verified.
- **Location:** The test projects reference only xunit. There is no `Microsoft.AspNetCore.Mvc.Testing` or Testcontainers.
- **Evidence:**
  - `public partial class Program;` already exists, so `WebApplicationFactory<Program>` is feasible.
  - Services rely on Postgres-specific behaviour: `xmin` row versions (`IsRowVersion`), `pg_advisory_xact_lock` (UsersAdminService.cs:553), array columns (`Specials.Contains`), and `ExecuteUpdateAsync`. EF InMemory or SQLite cannot prove any of these.
- **Untested critical paths:**
  - `FormsService` transitions and concurrency.
  - All of `UsersAdminService`: SOD-9/10/11 and the minimum of 2 super admins.
  - `RbacAdminService` propose/approve.
  - `ResultsService.Actual` orchestration over real data.
  - Import all-or-nothing.
  - The idempotency of the Seeder's "once" fixes.
  - `CyclesService.OpenAsync` form creation.
- **Fix:** Create a `tests/Tier1.IntegrationTests` project:
  - Testcontainers.PostgreSql and Testcontainers.MongoDb with one shared container per collection fixture.
  - Respawn, or a transaction rollback per test.
  - `WebApplicationFactory<Program>` with a fake `IClock` and a test `ICurrentUser` or cookie login.
  - The first tests are listed in the backlog.

### TEST-5: The E2E suite is fragile and has vacuous checks
- **Severity:** Medium. **Confidence:** verified.
- **Location and evidence:**
  - `CollectionTests.cs:30-314` is one 280-line test covering 8 scenarios. The first exception aborts the rest, and the failure names no step.
  - Checks that pass with nothing tested: `CollectionTests.cs:176` (`t.Check(true, "No department-level indicators…")`), and the `if (pct is not null)` (`:88`), `if (ratio …)` (`:94`), `if (targetRow is not null)` (`:254`) and `if (units.TryGetValue("ENG-ELE"…))` (`:200`) guards. If the seed data changes, these checks disappear silently.
  - `CollectionTests.cs:196-197`: the stale-version check uses `deptrep`, who is already blocked by SOD-C1. The expected 409 holds only because the version check runs before `EnsureReviewerAsync`. Use an otherwise-eligible reviewer (the dean).
  - `CollectionTests.cs:22-27,311`: mail is checked by counting `.eml` files in the repo's `data/mail`, which works only for a local server or a bind mount. It polls with `Task.Delay` for up to 10 s.
  - `E2E.cs:130`: `Calls.Today()` uses the machine's local `DateTime.Today`, while the server validates in Cairo time. A run near midnight in a CI timezone can fail the `opensOn` and `dueOn` checks.
  - Shared mutable demo database with no cleanup. `ScenarioTests` (cc5780e) opens two trial cycles and closes them only on the success path. A failure leaves them open, which changes later runs' `awaitingMe` and Home counts, and feeds CR-9.
  - `E2E.cs:31` caches clients in a static, non-thread-safe `Dictionary`. This is safe only because parallelization is disabled (`AssemblyInfo.cs`).
- **Fix:**
  - Split into one `[E2EFact]` per step, using fixture-created data via API (create a dedicated trial cycle per class).
  - Replace each conditional `if` with `Assert.Skip`/fail plus the reason.
  - Close cycles in `IAsyncLifetime.DisposeAsync`.
  - Use a Cairo-time `Today`.
  - Read mail from an API or a configured path.

### TEST-6: User and role administration (the highest-risk security code) has no tests
- **Severity:** Medium-High. **Confidence:** verified.
- **Location:** `src/Tier1.Application/Access/UsersAdminService.cs` (599 lines) and `RbacAdminService.cs` (376 lines). Only `RoleRules` (pure) is unit-tested.
- **Evidence:** These rules are enforced only in service code: SOD-9 (self-management, reserved roles), SOD-10 (minimum of 2 permanent super admins, independent approver, `DependsOnAsync`), SOD-11 (combining roles), sessions revoked on an MFA role, and the advisory-lock serialisation.
- **Fix:** Integration tests (TEST-4), named in backlog B6.

### TEST-7: Frontend tests cover models only; no interaction or guard tests
- **Severity:** Medium. **Confidence:** verified.
- **Location:** `web/vite.config.ts:41` (`environment: "node"`); there is no `@testing-library/*` or jsdom. There are 11 test files, and 931 of their 1,574 lines test design tokens and appearance. `FormPage.tsx` (445 lines: review, return, direct and reopen logic), `UserDrawer.tsx` (374), route permission guards, and the save queue integrated into the page have no tests.
- **Fix:**
  - Add `jsdom`, `@testing-library/react` and `msw` for component tests.
  - First, extract `formActions(f)` from `FormPage` into `forms/model.ts` and unit-test it (`canReturn`, direct labels, `skippedByDean`).
  - Then add a Playwright smoke test on the flow profile (login → submit → approve), RTL included.

### TEST-8: A new test fixes the wrong expectation
- **Severity:** Low. **Confidence:** verified.
- **Location:** `tests/Tier1.Tests/ReferenceDataTests.cs:54-64`.
- **Evidence:** It asserts that every role, SYS included, has `dash.home`. No test cross-checks catalog defaults against `RoleRules`, so two sources of truth can disagree (CR-1).
- **Fix:** Keep the home-page intent, and add the cross-check from CR-1.

### ARCH-1: `FormsService` (515 + 167 lines) mixes six responsibilities, and other services reach into it
- **Severity:** Medium. **Confidence:** verified.
- **Evidence:** One class holds:
  - list and query, with in-memory filtering of every submission and unit;
  - context loading;
  - value entry;
  - workflow transitions and audit verbs;
  - DTO assembly, with targets, documents and issues;
  - Excel template and import.

  The pure rule `FormsService.Applies` is called from `CyclesService.cs:195` and `ResultsService.cs:198,329`. `FormsService.ToDoc` is called six times from `DocumentsService`.
- **Effect:** Every change to approvals touches the 515-line file. The review-eligibility logic is duplicated three times with different behaviour (CR-4).
- **Proposed split** (behaviour-preserving, one PR each, see the refactoring plan):
  - `FormAssignmentRules` (pure): `Applies`, `EntryNote`.
  - `FormContextLoader` (internal): `LoadAsync`, `TargetsAsync`, `DocsAsync`, `EvidenceCountsAsync`.
  - `FormWorkflowService`: Submit, Approve, Return, Reopen, Extend, `Move`, plus a pure `ReviewEligibility`.
  - `FormEntryService`: SaveValue, ClearValue, Import, Template.
  - `FormQueryService`: List, Get, Build.

  Keep `FormsService` as a thin facade until the endpoints are moved.

### ARCH-2: `UsersAdminService` (599 lines) mixes account lifecycle, assignments, overrides and policy, and has Postgres SQL in Application
- **Severity:** Medium. **Confidence:** verified.
- **Evidence:** Seventeen public operations and nine private policy helpers. `BeginLockedAsync` runs `SELECT pg_advisory_xact_lock(...)` (line 553) inside the Application layer.
- **Proposed split:**
  - `UserAccountsService`: create, update, deactivate, reactivate, unlock, MFA reset, temporary password.
  - `RoleAssignmentService`: assign, approve, update, revoke.
  - `UserOverridesService`.
  - `IamPolicy`: a pure class for the SOD-9/10/11 decisions on loaded data (unit-testable).
  - `IIamLock` port in Abstractions, implemented in Infrastructure with the advisory lock.

### ARCH-3: The Seeder has become an unversioned data-migration engine that bypasses domain validation
- **Severity:** Medium. **Confidence:** verified.
- **Location:** `src/Tier1.Infrastructure/Persistence/Seeder.cs:139-277`.
- **Evidence:**
  - Four "run once" fixes, keyed by ad-hoc strings in `Settings` (`catalog:`, `catalog-fix:`, `catalog:dc.forms:<special>`), run on every startup, production included.
  - Each repeats the `AccessStates.Version + 1` bump. The same bump also appears in `AccessService`, five copies in total.
  - The fixes write `RoleGrant` rows directly without `RoleRules.Validate`, which is the root cause of CR-1.
  - The Flow and Demo branches add more conditional paths (`flow ? [] : DemoCentralUnits`).
- **Proposed fix:**
  - An `IDataFix { string Id; Task ApplyAsync(); }` list run in order by a `DataFixRunner`, with one `data_fixes` table, one access-version bump, and a post-condition `RoleRules.Validate` for every system role.
  - Move the demo and flow seeding into a separate `DemoSeeder`.

### ARCH-4: Demo, maintenance and import tooling ship inside the production API, and E2E depends on the whole Api project
- **Severity:** Low-Medium. **Confidence:** verified.
- **Evidence:**
  - `src/Tier1.Api/Startup/DemoData/*` (~440 lines; an HTTP client that drives the API) and `Startup/Maintenance.cs` (backup and restore).
  - `Infrastructure/Files/SeedImporters.cs` (436 lines) is reached through `Program.cs` CLI flags.
  - `tests/Tier1.E2E.csproj` references `Tier1.Api` only to reuse `DemoClient`.
  - These are guarded by `StartupGuards`, but they enlarge the attack surface and the build graph.
- **Fix:** Create `tools/Tier1.Tools` (CLI: import, backup, reset, demo-data) and `tests/Tier1.TestSupport` (DemoClient, JSON helpers). The API keeps only `--migrate` and `--create-superadmin`.

### ARCH-5: Endpoint registration is split and inconsistent
- **Severity:** Low. **Confidence:** verified.
- **Evidence:**
  - Route groups are listed separately in `Program.cs` and in `EndpointPolicyTests` (the cause of TEST-1).
  - `CollectionEndpoints` also hosts strategy documents, structure documents, scorecard, review/transfer and progress.
  - Policy level is inconsistent: `/scorecard/kpi.xlsx` declares only `Results:V` (line 192), while the service requires the special `export` (`ScorecardService.cs:69`); `/results/{id}/export.xlsx` declares the special at the endpoint.
- **Fix:** One `MapApi()`, one endpoints file per module (`ScorecardEndpoints`, `DocumentEndpoints`), and declare the strictest policy at the endpoint so the policy test sees it.

### ARCH-6: Business rules are duplicated with subtle drift
- **Severity:** Low-Medium. **Confidence:** verified.
- **Evidence:**
  - Review eligibility is computed in `ListAsync:93-96`, `BuildAsync:492-494` and `EnsureReviewerAsync:367-375` with different "since submit" windows (CR-4).
  - The university root is looked up as `ParentId == null` ordered by SortOrder in `ResultsService.cs:293`, and as `Kind == University` in `FormsService.cs:436`.
  - The access-version bump appears five times (ARCH-3).
  - The approval-step audit verb is chosen by pattern logic inside `Move` (line 381-403) instead of next to `ApprovalRules`.
- **Fix:** `ReviewEligibility` (pure), `OrgUnits.UniversityAsync()` as a single helper, `IAccessService.BumpVersionAsync` used everywhere, and `ApprovalRules.AuditVerb(from, to, action)` (unit-testable).

Layering otherwise looks sound:
- Domain has no package references.
- Application references only Domain and EF Core abstractions (`IAppDb` exposes `DbSet`). That is a pragmatic, accepted coupling, not a violation.
- Application has no Infrastructure reference.
- Api endpoints are thin lambdas, apart from the file-upload plumbing.

---

## Test backlog (prioritized)

| # | Level | File (new or existing) | Test names | Covers |
|---|---|---|---|---|
| B1 | Unit | `CollectionRulesTests` → new `ApprovalRulesTests.cs` | `Direct_approval_decision_table` (Theory: unit kind × step × {DREP, CTC, DEAN, CTC+DEAN, SA, QA} → expected target or null), `Super_admin_never_takes_a_direct_path`, `Dean_with_normal_special_is_offered_both_paths` (after the CR-3 fix), `May_review_without_direct_requires_the_step_special` | CR-3, TEST-2 |
| B2 | Unit | `ApprovalRulesTests.cs` | `Skipped_by_marks_department_review_for_committee_direct`, `Skipped_by_marks_department_and_committee_for_dean_direct_on_department`, `Skipped_by_marks_committee_for_dean_direct_on_college`, `Normal_approvals_skip_nothing` | path rendering |
| B3 | Unit | `ApprovalRulesTests.cs` | `Move_sets_status_InReview_Returned_InProgress_Approved_by_target`, `Audit_verb_for_each_transition_including_direct` (after extracting `AuditVerb`) | state machine |
| B4 | Unit | `ReferenceDataTests` / `RoleRulesTests` | `Catalog_defaults_of_every_role_pass_RoleRules_Validate`, `System_admin_may_hold_the_home_page`, `Every_decision_special_in_catalog_is_in_DecisionSpecials` | CR-1 |
| B5 | Unit | `EndpointPolicyTests` | `Every_api_endpoint_declares_its_access` over `MapApi()` (all groups), `Form_review_routes_require_review_specials`, `Approve_route_accepts_direct_specials_return_does_not`, `Scorecard_export_requires_results_export_special` | TEST-1 |
| B6 | Integration (Testcontainers PG) | `UsersAdminServiceTests` | `Cannot_manage_own_account_SOD9`, `Non_SA_cannot_assign_reserved_role`, `SA_assignment_needs_independent_approver_SOD10`, `Revoking_leaves_at_least_two_permanent_super_admins`, `SYS_cannot_be_combined_with_content_role_SOD11`, `Assigning_MFA_role_revokes_sessions`, `Concurrent_revokes_of_two_SAs_cannot_both_succeed` (two tasks plus advisory lock) | TEST-6 |
| B7 | Integration | `FormWorkflowTests` | `Submit_requires_complete_values_and_evidence`, `Return_requires_note_and_flags_items`, `Editor_cannot_approve_SOD_C1`, `Same_person_cannot_approve_two_steps_SOD_C2`, `Concurrent_approvals_one_wins_other_gets_409` (xmin), `Reopen_requires_special_and_open_cycle`, `Closed_cycle_blocks_entry_and_review`, `Out_of_scope_dean_with_wider_view_cannot_approve` (CR-2), `Reviewer_from_previous_round_sees_form_in_awaiting_me` (CR-4), `Approve_and_outbox_mail_commit_atomically` (CR-5) | approval and scope |
| B8 | Integration | `RbacAdminServiceTests` | `Propose_then_approve_by_other_SA_applies_grants`, `Proposer_cannot_approve_own_change`, `Stale_base_version_conflicts`, `System_admin_role_change_is_accepted_after_home_fix` | CR-1 |
| B9 | Integration | `ResultsServiceTests` | `University_ratio_is_sum_of_numerators_over_denominators_from_approved_forms_only`, `Unapproved_forms_do_not_count`, `Distinct_count_uses_central_entry`, `Default_cycle_prefers_real_over_trial` (CR-9), `Dean_sees_only_own_college_units` | calculations and scope |
| B10 | Integration | `FormImportTests` | `Import_with_any_error_applies_nothing`, `Import_reason_only_change_is_applied`, `Import_decimal_comma_is_rejected`, `Import_arabic_indic_digits`, `Import_after_concurrent_edit_conflicts` | imports |
| B11 | Integration | `SeederTests` | `Seed_on_empty_db_is_valid_against_RoleRules`, `Seed_twice_is_idempotent`, `Data_fixes_do_not_override_admin_changes`, `Flow_profile_refuses_non_empty_db` | ARCH-3 |
| B12 | API (WebApplicationFactory) | `ApiAuthTests` | `Unauthenticated_api_returns_401_not_spa`, `Csrf_required_on_mutations`, `Forms_get_out_of_scope_returns_404` | access |
| B13 | Frontend | `forms/model.test.ts` | `formActions_hides_return_only_for_direct_only_reviewers`, `direct_label_matches_target`, `skipped_states_render` | CR-3 UI |
| B14 | Frontend (jsdom + RTL + msw) | `FormPage.test.tsx`, `RequirePermission.test.tsx` | `Approve_dialog_sends_version`, `409_shows_refresh_message`, `Route_without_permission_redirects_home` | UI |
| B15 | E2E hardening | `CollectionTests` | Split into per-step facts, remove the vacuous `if` branches, Cairo-time `Today`, dispose trial cycles | TEST-5 |

---

## Refactoring plan (incremental, behaviour-preserving)

Each step is one PR. Characterization tests go in first, and the whole suite runs after each step.

1. **Safety net.** Add CI (TEST-3). Add `MapApi()` and fix `EndpointPolicyTests` (TEST-1). Add tests B1–B5. No production behaviour change, except the CR-1 fix, which is a separate bug PR.
2. **Pure extractions.** Move `FormsService.Applies`/`EntryNote` to `FormAssignmentRules` and update the three callers. Move `ToDoc` to a `DocMapping` static. Add `ReviewEligibility` and `ApprovalRules.AuditVerb`, and have `ListAsync`, `BuildAsync` and `EnsureReviewerAsync` delegate to them. The CR-4 fix stays a separate commit with its test.
3. **Integration test project** (TEST-4) with B7 tests against the current `FormsService`.
4. **Split `FormsService`** behind a facade: first `FormContextLoader`, then `FormWorkflowService`, `FormEntryService` and `FormQueryService`. Endpoints switch to the new services, then the facade is deleted.
5. **IAM.** Introduce the `IIamLock` port and the pure `IamPolicy`, with B6 tests. Then split `UsersAdminService` into three services. Keep the endpoint contracts unchanged.
6. **Seeder.** Introduce `DataFixRunner` and move the four fixes into it (IDs preserved, mapped from the existing `Settings` keys so they do not re-run). Add the post-seed `RoleRules` validation and B11 tests.
7. **Tooling out of the Api.** Move DemoData, SeedImporters and Maintenance to `tools/Tier1.Tools`, and DemoClient to `tests/Tier1.TestSupport`. Update docker-compose commands and docs.
8. **Scoped specials** (CR-2) once the integration tests exist. This is a behaviour change: write a DEC, then change `EffectiveAccess` and `UnitScope.ForSpecial`.

---

## Extension readiness: how to add a module (for example "Initiative activities") with tests

1. **Catalog.** Add the feature to `src/Tier1.Application/Access/permission-catalog.json` (`key`, `module`, caps, specials with `defaultRoles`, defaults per role). Decide whether SYS may hold it: if the module is business content, SYS must have no default (CR-1). `SeedNewFeatureGrantsAsync` grants defaults to existing roles once.
2. **Domain and data.** Add the entity in `Tier1.Domain/<Module>`, a `Tier1DbContext.<Module>.cs` partial with a row-version `Version` for concurrency, `IAppDb` DbSets, and an EF migration (`dotnet ef migrations add`).
3. **Rules first.** Put validation, state transitions and calculations in a static `<Module>Rules` class in Application, unit-tested in `tests/Tier1.Tests/<Module>RulesTests.cs` with decision tables.
4. **Service.** Write `<Module>Service(IAppDb, IClock, ICurrentUser, IAccessService, IAuditLog)`. Every public method starts with `access.RequireAsync(feature, cap)` and `UnitScope.For(...).Require(unit)`, takes `Version` for concurrency, records audit, and saves once (outbox included). Register it in `Application/DependencyInjection.cs`.
5. **Endpoints.** Create `<Module>Endpoints.Map<Module>()` with `RequirePermission`/`RequireSpecial` on every route, and register it in `MapApi()`. `EndpointPolicyTests` then guards it automatically, which works only after the TEST-1 fix.
6. **Integration tests.** In `tests/Tier1.IntegrationTests/<Module>ServiceTests.cs` (Testcontainers), cover: allowed in scope, denied out of scope (404), denied without capability (403), concurrency 409, audit row written, and transaction rollback on failure.
7. **Frontend.** Add `web/src/features/<module>/` with `api.ts`, `model.ts` (pure, with `model.test.ts`) and pages. Gate the route with the catalog permission and add a component test for the permission-hidden actions.
8. **E2E (optional).** Add one happy-path `[E2EFact]` on the flow profile that creates its own data and cleans up.

Current readiness: the rules layer, catalog and endpoint-policy guard are good extension points. What is missing is CI, an integration test layer, and a single endpoint registration. Until those exist, a new module's authorization and persistence behaviour can only be verified by hand or by the skipped E2E suite.
