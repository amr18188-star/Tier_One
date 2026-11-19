# Tier1 — Business-logic & requirements review

Scope: docs/requirements.md (v0.2), docs/kpi-framework.md, docs/calculation-review.md traced through
Domain → Application (FormsService, ApprovalRules, ValueRules, ResultsRules/ResultsService, TargetsService/TargetLookup,
DistributionRules, ReviewService, ScorecardRules/Service, CyclesService, ConflictsService, IndicatorRules, HomeService, FormNotifier)
→ Api (CollectionEndpoints) and the permission catalog/seeder, including commits 6fae618 (direct approval paths) and 224ed7e (home for all roles).
Read-only review: nothing was built, run or executed.

Confidence legend: **verified** = deterministic from the code path read end to end (not executed); **likely** = depends on a role/data
configuration that the code permits but that I did not observe in a live DB. A test that settles each item is suggested in the fix.

Reference IDs used below: REQ-x.y = section of requirements.md; DEC-n = decision row n in section 2.

---

## Findings (most severe first)

### BL-1 · Critical · likely — Approval specials are not bound to the scope of the role that grants them (cross-college approval)
- **Where:** `src/Tier1.Application/Access/EffectiveAccess.cs:46` (`HasSpecial` is scope-less), `:91-98` (whole-university widening of View to All),
  `src/Tier1.Application/Access/AccessService.cs:78-86`, `src/Tier1.Application/Collection/FormsService.cs:209-212, 224-225, 310-316`
  (approve/return load the form with `Cap.View` scope), `ApprovalRules.cs:63-64`.
- **Evidence:** `ApproveAsync` → `LoadAsync(id, Cap.View, …)` → `UnitScope.For(me, Feature, cap).Require(unit)`; then `MayReview` only asks
  `access.HasSpecial(Features.Forms, SpecialFor(step))`. Specials are a flat union across all role assignments; View scope is the union of all
  assignment scopes and is widened to **All** when any scope value is a `ViewsWholeUniversity` unit (SPU and ICT in the seed).
- **Failure scenario:** (a) User holds `DE` scoped to `SPU` (whole-university view) and `CTC` scoped to college F05 → Forms:View = All, has
  `review_college` + `approve_department_directly` → can approve the college-committee step (or direct-approve) of every college in the
  university. (b) User holds `DEAN` for F05 and `DE` for F07 → can perform the dean approval for F07 forms. Requirement REQ-3.2 ("الصلاحية
  مرتبطة بالإجراء والجهة") and acceptance 7.4 are violated; `FormNotifier.ReviewersAsync` (line 71) uses the same View scope, so these users
  are also emailed.
- **Fix:** carry scope with specials (e.g. `FeatureAccess.SpecialScopes[special] = scope + values` built from the grant that carried it, never
  widened by `ViewsWholeUniversity`); in `EnsureReviewerAsync`/`DirectApprovalTo`/`ListAsync`/`FormNotifier` require
  `ScopeForSpecial(Forms, special).Covers(unit)`. Add tests: multi-role user and SPU user get 404/403 on another college's approve/return.

### BL-2 · High · verified — Unapproved (Proposed) targets drive performance status; a dean/committee can set its own yardstick
- **Where:** `src/Tier1.Application/Kpi/TargetLookup.cs:42-69` (no `Status` filter), `ResultsService.cs:280`, `FormsService.cs:434`,
  `Scorecard/ScorecardService.cs:100`, `Scorecard/ProgressService.cs:57`; permission `kpi.targets` DEAN/CTC = `VPE:U`.
- **Evidence:** `Latest(targets.Where(t => t.UnitId == unit.Id && t.Kind == TargetKind.Target), year)` — Proposed rows are used exactly like
  Approved ones. `TargetsService.SaveAsync` lets DEAN/CTC create/edit a Proposed target for their college.
- **Failure scenario:** College F05 has an approved-by-nobody target of 80% pass rate; dean edits it to 40% (stays Proposed) → Results,
  heat map, college progress and the form's "target" column immediately show "محقق". REQ-4.3 / DEC-8 say targets and their distribution are
  approved by the higher committee; 4-10-1 #3 requires thresholds "وفق حدود معتمدة".
- **Fix:** judge only against `Status == Approved` targets (show Proposed separately labelled "مقترح"), or freeze the target set into the cycle at
  open time (like `CycleIndicator.VersionId`). Test: proposed-only target → `Performance.NoTarget`.

### BL-3 · High · verified — Direct approval paths (commit 6fae618) collapse up to three review steps into one person, are forced, and remove the ability to return
- **Where:** `src/Tier1.Application/Collection/ApprovalRules.cs:48-61`, `FormsService.cs:212-215` (approve uses `allowDirect: true`),
  `FormsService.cs:225` + `ApprovalRules.cs:63` (return uses `allowDirect: false`), `web/src/features/forms/FormPage.tsx:164`
  (`canReturn = reviewing && !directApproval`), catalog `dc.forms` specials, `Seeder.cs:174-175`.
- **Evidence:** `if (access.HasSpecial(Features.Forms, ApproveAsDeanDirectly)) return ApprovalStep.Quality;` is evaluated before and
  independently of `canReviewNormally`; the CTC rule is suppressed when the user can review normally, the dean rule is not.
- **Failure scenarios:**
  1. Data entry submits a department form → dean (default `DEAN` role) approves at `DepartmentReview` → form jumps to `Quality`; the
     department representative and the college committee never see it. The default path in REQ-4.6 is not mandatory any more; no requirement
     or decision documents this (requirements.md, user-guide.md contain no "direct approval").
  2. The dean disagrees with the data at the same step → there is no return: backend `ReturnAsync` → 403 (dean lacks `review_department`), UI
     hides the button. The only actions offered are "approve" or "wait" — an approval-biased one-way door.
  3. A person who is both CTC member and dean (small institutes) can never do a normal committee approval: every approval at
     `CollegeCommittee` is forced to `Quality`, so the dean step is silently consumed. Same for a DREP who is also dean at `DepartmentReview`.
  4. `FormsService.ListAsync:95` / `FormNotifier.cs:71` treat DREP, CTC and DEAN as simultaneous reviewers of one step → three "مطلوب منك"
     tasks and three emails for one action.
- **Fix:** obtain and record a DEC for direct approval; make it an explicit request flag (`ApproveFormRequest.Direct`) with a mandatory reason,
  never the implicit default; let anyone who may approve a step also return it; apply the same "only when not a normal reviewer" rule to both
  directs; consider allowing direct approval only after the skipped step is overdue. Tests: dean returns at DepartmentReview; dual-role
  CTC+DEAN gets normal approval unless `direct=true`.

### BL-4 · High · verified — Reopening locked values is decided by the plan manager alone; closed cycles can never be corrected
- **Where:** `FormsService.cs:237-250`, `CollectionEndpoints.cs` `/reopen` → `RequireSpecial(dc.cycles, reopen)`; catalog `dc.cycles.reopen` roles `["PM"]`.
- **Evidence:** a single call with a 5-char reason moves an approved form back to Entry. Requirement DEC-8 and REQ-3.2 (HC row):
  "تعتمد [اللجنة العليا] طلبات إعادة فتح القيم المقفلة"; REQ-4.6 "إعادة فتحها بطلب مسبب وموافقة محددة". `if (ctx.Cycle.Status != CycleStatus.Open)` blocks
  any reopen after closure, and `CyclesService` has no cycle reopen.
- **Failure scenario:** PM reopens an approved college form without committee approval → values change and results/official values shift
  (ReviewService state "Changed") with no second person involved. Conversely, an error found in Q1 after the Q1 cycle is closed cannot be
  corrected through any audited path (stuck state).
- **Fix:** introduce `ReopenRequest` (requested by PM with reason → approved by HC with minutes ref, requester ≠ approver); allow reopening a
  single form in a closed cycle through the same request (or a controlled "reopen cycle for unit X"). Test the SoD and the closed-cycle path.

### BL-5 · High · verified — Value validation forbids legitimate negative and >100 values for every indicator
- **Where:** `src/Tier1.Application/Collection/ValueRules.cs:60, 62-63, 74-75`; `TargetsService.cs:340-352` for targets.
- **Evidence:** `if (n < 0) … "البسط لا يكون سالبًا"`, `if (v < 0) … "القيمة لا تكون سالبة"`, `IsPercent && v > 100` — unconditional.
  REQ-4-5-1 #5 makes bounds configurable per indicator ("منع القيم السالبة للأعداد التي لا تقبلها", "نسبة من 0 إلى 100 فقط حين يكون ذلك نطاق المؤشر").
- **Failure scenario:** S10-02 "هامش الفائض / العجز التشغيلي" (% , numerator/denominator per calculation-review) — a college with a deficit
  (numerator −2,000,000) gets error `negative`; it can only answer "غير متاح", so the university ratio silently excludes deficits and overstates
  the margin. S02-02 enrollment growth cannot be negative either; a growth % above 100 is rejected.
- **Fix:** add `MinValue/MaxValue/AllowNegative` (or a "signed" flag) to `IndicatorVersion` and to `ValueSpec`/`CycleSpec`; apply in ValueRules,
  TargetRules and import. Unit test with S10-02 deficit.

### BL-6 · High · verified — Partial ratio/average aggregates are judged as final; "not available" counts as reported
- **Where:** `src/Tier1.Application/Collection/ResultsRules.cs:41-44, 50, 115`; consumers `ResultsService.cs:83-99, 129, 371`; `ReviewService.cs:157-160`.
- **Evidence:** `Status` returns `Incomplete` only for `Aggregation.Sum`; `reported = values.Count` includes `NotAvailable`/`NotApplicable`.
- **Failure scenario:** pass-rate indicator (RatioOfSums) after 2 of 24 colleges are approved → university value is the ratio of those 2 and is
  judged "محقق/متأخر"; standards summary and heat map count it. For a Sum indicator where one college answers "غير متاح", `Reported == Expected`
  → status is judged against the full university target → "متأخر". REQ-4-10-1 #3: "نقص البيانات أو عدم اعتمادها حالة مستقلة، ولا يُحسب ضعفًا".
  ReviewService treats the same row as `Ready` and `TransferAll` copies it to the official value.
- **Fix:** count only `Number` values as reported; exclude `NotApplicable` from `expected`; return `Incomplete` for every aggregation when
  reported numbers < expected. Tests per aggregation with 1-of-N and NotAvailable cases.

### BL-7 · Medium · verified — Results and Scorecard compute "achievement" with different formulas (and the docs contradict each other)
- **Where:** `ResultsRules.cs:83-93` vs `Scorecard/ScorecardRules.cs:64-71`; `ScorecardService.cs:112` (exact-year target) vs `TargetLookup.cs:68-69`
  (carry-forward); docs: calculation-review.md (S07-03 = "الوصول إلى قيمة"/Range) vs kpi-framework.md (7.03 = تنازلي).
- **Failure scenarios:** Range indicator, target 12 h, actual 13 h → Results 92% "قريب"; Scorecard treats Range as higher-is-better → 100%
  "في المستوى". Lower-is-better count with target 0 and actual 3 → Results 0% "متأخر"; Scorecard `100 − v` = 97%. A 2029 year with no explicit
  target → Results uses the 2028 target, Scorecard shows no target. Acceptance 7.6 / 4-10-1 ("تتطابق كل أرقام الرسومات مع الجداول والتقارير") fails.
- **Fix:** one Domain-level `Achievement(direction, actual, target)` and one target-resolution function used by Results, Review, Progress and
  Scorecard; get a DEC on S07-03 direction. Golden tests covering all directions and target = 0.

### BL-8 · Medium · verified — The official university value can be overridden by one person with a 5-character reason
- **Where:** `src/Tier1.Application/Scorecard/ReviewService.cs:100-126` (`TransferAsync` with `req.Value`).
- **Evidence:** `var final = value ?? row.Computed`; `adjusted` only requires a ≥5-char note; no approval step; the scorecard, tier and council
  KPI.xlsx read only `UniversityValues` (ScorecardService.cs:101-104).
- **Failure scenario:** T1O enters 95 for an indicator computed at 62 with note "تصحيح" → university tier changes and the council file is exported
  with 95; no second person sees it. REQ-4-5-1 #6 ("تُمنع التعديلات اليدوية على النتائج المحسوبة"), acceptance 7.9 (HC approves the result).
- **Fix:** forbid manual override, or make an adjusted/partial transfer a Proposed value that HC/QA approves (SoD transferrer ≠ approver), keeping
  `ComputedValue` and the adjusted value side by side in reports.

### BL-9 · Medium · likely — Separation-of-duties checks miss the submitter and earlier editors
- **Where:** `ApprovalRules.cs:87-93`, `FormsService.cs:373-374, 493-494`; same single-field pattern in `Kpi/IndicatorRules.cs:103` (SOD-K1),
  `Kpi/TargetsService.cs:262` (SOD-K2, `UpdatedBy`).
- **Evidence:** `editors` = `ctx.Values.Values.Select(v => v.EditedBy)` — only the *last* editor per value; the `Submitted` event's `By` is never checked.
- **Failure scenarios:** DREP (Forms `E:U` + `review_department`) presses Submit on values typed by data entry and then approves the
  DepartmentReview step of his own submission. CTC (Forms `E:U` over the whole college) submits a department form and direct-approves it to
  the dean (BL-3). User A enters a value, user B overwrites it, A can review. Target: PM proposes, HC member X tweaks, HC member Y… fine, but
  if X proposed originally and Y made the last edit, X can approve his own proposal.
- **Fix:** include the submitter and all editors since the last approval (keep a value-edit history table rather than only `EditedBy`) in the
  separation set; same for definitions/targets (track `CreatedBy` + all editors of the working version).

### BL-10 · Medium · verified — Amending an approved target is self-approved; deleting a target destroys its revision history; approval has no version check
- **Where:** `Kpi/TargetsService.cs:94-107, 181, 245-254` (amend), `:231` (`db.TargetRevisions.RemoveRange(...)`), `:257-264` (approve).
- **Failure scenarios:** a user holding Edit + approve (SA, or a PM given HC) changes an approved 2027 target from 80 to 60 — it stays
  `Approved`, nobody else approves the amendment. Deleting an approved target removes all `TargetRevision` rows, so the "previous version"
  required by REQ-4.3 and acceptance 7.8 survives only in the Mongo audit log. HC approves a target while PM edits it concurrently → HC approves a
  value it never saw (no `Version` in `CommitteeDecisionRequest`).
- **Fix:** amendments create a Proposed revision that a different HC member approves; soft-delete targets and keep revisions; require the row
  version on approve.

### BL-11 · Medium · verified — Reopen overwrites the approved values in place; results drop the unit immediately
- **Where:** `FormsService.cs:237-250`, `Upsert` `:148-158`; `ResultsService.cs:277-279` (only `Status == Approved` submissions).
- **Failure scenario:** an approved college form is reopened for one indicator → all of that college's values vanish from Results, heat map and
  the T1O review (state "Changed") until re-approval, and the old approved numbers exist only in audit JSON. REQ-4.6 "الاحتفاظ بالقيمة السابقة
  وكل مراحل اعتمادها"; REQ-4.2 rule 4.
- **Fix:** persist an approved snapshot per approval round (`ApprovedValue` rows with round number) and let results read the last approved
  snapshot until the next final approval (or show both with a label) — decide via Q-003.

### BL-12 · Medium · verified — Startup seeder grants new decision powers to existing roles without matrix approval or audit
- **Where:** `src/Tier1.Infrastructure/Persistence/Seeder.cs:173-192` (`SeedFormApprovalSpecialAsync`, `GrantHomeToAllRolesOnceAsync`).
- **Evidence:** adds `approve_department_directly` / `approve_as_dean_directly` to every system CTC/DEAN grant in an existing DB on next start,
  bumps the access version, writes only a `Settings` marker — no audit event, no second-SA approval (catalog `adm.roles.approve`).
- **Failure scenario:** deploying 6fae618 to production silently gives every dean the power to skip two review steps; the matrix history and
  the hash-chained audit show no change.
- **Fix:** deliver catalog changes as pending matrix changes that a second SA approves, or at minimum write a sealed audit event per grant change;
  never auto-grant `DecisionSpecials` outside Development.

### BL-13 · Medium · verified — Cycles open with indicators that still have open conflicts
- **Where:** `Collection/CyclesService.cs:155-193` (`PlanAsync` checks only `i.Effective` and `Suspended`).
- **Failure scenario:** an indicator whose university vs. college targets conflict (status Open/Ready/Deferred) enters the first official cycle;
  colleges are judged against disputed targets. REQ-8-1 #2: "المؤشرات ذات التعارضات المفتوحة تُؤجل إلى دورة لاحقة".
- **Fix:** in non-trial cycles skip indicators referenced by unresolved conflicts and list them in `OpenCyclePreview.Skipped`.

### BL-14 · Medium · verified — "Awaiting me" and review emails fan out to every possible approver, including all super admins
- **Where:** `FormNotifier.cs:68-72` (`ReviewersAsync` has no SA exclusion, unlike `EditorsAsync:80`), `FormsService.cs:93-96`.
- **Failure scenario:** every submission and every daily reminder emails both SA accounts (Forms `V:A` + all specials) for every form in the
  university, and with BL-3 also every CTC member and the dean for each department form. Reminders become noise; whoever clicks first wins.
- **Fix:** notify the primary reviewer(s) of the step only; exclude SA; show direct reviewers the form in a separate "optional" list.

### BL-15 · Low · verified — Notification is not in the same transaction as the transition
- **Where:** `FormsService.cs:202-203, 216-217, 231-232, 247-248`; `FormNotifier.cs:36-37` (second `SaveChangesAsync`). Class comment claims "في معاملة العملية نفسها".
- **Failure scenario:** approval commits, outbox save fails → client gets 500; retry returns "not_in_review"; the next reviewer is never emailed.
- **Fix:** enqueue mail before the single `SaveChangesAsync` (outbox in the same unit of work).

### BL-16 · Low · verified — Last-writer-wins on values: version is optional and the Excel import never sends one
- **Where:** `Common/Shared.cs:65-68` (`Expect` is a no-op when version is null), `FormsService.Import.cs:126` (`Version: null`), `SaveValueRequest.Version` optional.
- **Failure scenario:** user A previews an import; user B saves a corrected value; A applies → B's value is silently overwritten. REQ-4-5-1 #12.
- **Fix:** make version mandatory when the row exists; carry per-row versions from preview to apply and reject changed rows.

### BL-17 · Low · verified — Big-change warning never fires when the previous approved value is 0
- **Where:** `ValueRules.cs:85` (`p != 0`).
- **Failure scenario:** previous patents 0 → new 4,000 passes with no explanation required (REQ-4-5 "طلب تفسير للانحرافات الكبيرة").
- **Fix:** use an absolute threshold (or always warn) when the previous value is 0.

### BL-18 · Low · verified — Results default to a rehearsal cycle
- **Where:** `ResultsService.cs:385-394` (`PickCycleAsync` has no `Trial` filter).
- **Failure scenario:** the newest cycle with approvals is an internal rehearsal → leadership's results page opens on rehearsal data as if official.
- **Fix:** prefer `!Trial`; label trial cycles clearly.

### BL-19 · Low · verified — Units counted in totals differ from units shown; historical cycles follow today's structure
- **Where:** `ResultsService.cs:326-330` (contributors: no `Active` filter) vs `:188` and `:380` (breakdown/visible units: `u.Active`).
- **Failure scenario:** a college closed after Q1 still contributes to the Q1 university total but disappears from the Q1 breakdown and heat map
  → the listed colleges do not add up to the total; REQ-3-1 "ربط البيانات التاريخية بالهيكل الذي سُجلت فيه".
- **Fix:** freeze contributing units per cycle (as `CycleIndicator.UnitIds` already does for tasks) and use the frozen list for totals and lists.

### BL-20 · Low · verified — Department forms show the "each college" or university target as the department's own target
- **Where:** `TargetLookup.cs:47-61` (departments fall through to EachCollege/University); used by `FormsService.cs:438`.
- **Failure scenario:** "مشروعان لكل كلية" (Sum) is displayed as the target of every department; data entry compares its count to a college target.
- **Fix:** return `TargetInfo.None` for departments unless they have their own target (or a defined department split).

### Checked with no defect found
- **Commit 224ed7e (home for all roles):** `HomeService.cs:110-133` gates dictionary/conflict counts and standards by the matching feature, tasks
  are gated by Forms/Dictionary/Targets/Conflicts/Structure permissions, so SYS (now `dash.home V:A`) sees only an empty home. No data leak found.
  The seeder part shares the governance issue in BL-12.
- **Editing after approval / double submit:** `EnsureEditable` guards save, clear, import, evidence upload/void; `Submission.Version` is a row
  version, so concurrent approve/submit fails one request; repeated approve after commit is blocked by step/state + SOD-C2.
- **Ratio aggregation and zero denominators:** `RatioOfSums` sums numerators/denominators (no average of ratios); denominator ≤ 0 is rejected at
  entry and a zero sum yields `null` with a note.

---

## Requirement gaps & open questions

Gaps (requirement present, no implementation found):
- REQ-3.2 / DEC-8: higher-committee decisions recorded "بأعضائها الحاضرين" — `ConflictsService.DecideAsync` stores only minutes ref and date;
  a decision can also be recorded on an Open (unprepared) conflict, skipping the PM preparation step of REQ-4.4.
- REQ-4.4 automated data-quality checks (duplicates across colleges, total ≠ sum of categories, unusual jump vs. previous period) do not create
  conflict records; conflicts come only from the seed.
- REQ-4.6 partial approval per indicator, comments/replies per value, temporary delegation as an explicit object (only time-bounded role
  assignments exist).
- REQ-4.2 rule 2 (no circular formulas/dependencies): no dependency model exists, so the rule cannot be enforced.
- REQ-4-17 #7: role suggested by structure membership is "سُحب تلقائيًا عند انتهاء العضوية" — no link between membership end and role revocation found.
- REQ-3.2 "مدير النظام التقني لا يحصل تلقائيًا على صلاحية اعتماد البيانات": the SA role holds every decision special by design (FullGrants);
  whether SA counts as the "technical administrator" is undecided.

Contradictions inside the documents:
- requirements 4-10-1 #3 (missing data is not poor performance) and §4-10 ("درجة إجمالية تحتاج منهجية معتمدة", open item #2) vs.
  kpi-framework.md step 4 ("المؤشر بلا قيمة يحسب صفرًا") — the scorecard already scores missing as 0 and derives a tier from it.
- calculation-review.md: S07-03 is "الوصول إلى قيمة" (Range); kpi-framework.md: 7.03 is "تنازلي". The code honours both in different screens (BL-7).
- requirements §11 lists 4-6 as the default path only; commit 6fae618 introduced skip paths with no recorded decision (BL-3).

Open questions (with recommendation):
- **Q-001:** Are direct approvals (CTC skips the department rep; dean skips rep + committee) an approved policy?
  Options: a) remove; b) keep as explicit, reasoned opt-in; c) keep only after the skipped step is overdue.
  Recommendation: c (or b), because the default path in REQ-4.6 exists to get two independent reviews. Affects: BL-3, BL-9, BL-14.
- **Q-002:** Who authorises reopening an approved form, and can a closed cycle's form be reopened?
  Options: a) PM alone (current); b) PM requests, HC approves. Recommendation: b, per DEC-8. Affects: BL-4, BL-11.
- **Q-003:** While a reopened form is being corrected, should results show the last approved values or nothing?
  Options: a) nothing (current); b) last approved snapshot, labelled "قيد التصحيح". Recommendation: b, so dashboards do not swing. Affects: BL-11, BL-6.
- **Q-004:** Are performance judgements made against Proposed targets? Recommendation: no — approved only, proposed shown as information. Affects: BL-2.
- **Q-005:** Scorecard methodology for missing data, weights and the S07-03 direction (open item #2). Recommendation: HC decision before the
  tier is shown to leadership; until then show the tier as "تجريبي". Affects: BL-7, BL-8.

---

## Extension readiness (what must be in place before adding capabilities in this area)

1. **Scoped decisions:** a single authorisation primitive `CanDecide(user, feature, special, unit)` that carries the grant's own scope; every
   new approval type (initiatives, accreditation actions, report publishing) should use it instead of `HasSpecial` + View scope (BL-1).
2. **One workflow engine, data-driven:** the approval path is hard-coded per `OrgUnitKind` in `ApprovalRules.PathFor`, with special cases in
   `DirectApprovalTo`, `SkippedBy`, `Move` audit verbs and the UI. REQ-4.6 asks for different paths for central indicators and partial approval;
   model "path template → steps → required special → allowed skips" as data before adding more branches.
3. **Value history:** an append-only value/approval-round table (who entered, who submitted, who approved each round) is a prerequisite for
   correct SoD (BL-9), reopen semantics (BL-11), Excel re-imports and integrations (REQ-4.16 "منع التكرار عند إعادة الاستيراد").
4. **Per-indicator validation spec:** min/max/sign/integer/evidence rules stored on the indicator version and snapshotted in the cycle (BL-5);
   integrations and import must call the same `ValueRules`.
5. **Single calculation library:** one achievement/target-resolution/aggregation module shared by Results, Review, Progress, Scorecard and
   exports, with golden tests from calculation-review.md (BL-6, BL-7).
6. **Governed catalog changes:** permission-catalog changes delivered as reviewable matrix changes (second-SA approval + sealed audit), not seeder
   side effects (BL-12).
7. **Transactional outbox** for all side effects of a transition (mail, future in-app notifications, escalations) (BL-15).
8. **Approved-only inputs:** cycles, targets and official values should reference approved, versioned records (definitions already do this via
   `CycleIndicator.VersionId`; targets and the contributing unit list do not — BL-2, BL-19).
