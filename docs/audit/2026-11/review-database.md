# Tier1: database and backend performance review

Scope: PostgreSQL schema (EF Core / Npgsql), migrations, seeding, MongoDB audit chain, and the backend query and performance paths.
Method: the `database-engineering` and `performance-review` skills, run as a read-only diagnosis. Nothing was built, run or measured. Every finding comes from reading the code (code-level evidence, no runtime measurement).
Confidence: **verified** means the code path was traced end to end. **likely** means it depends on runtime timing or data distribution.

## Volume assumptions used in this review

| Item | Today (seed files) | Growth case (task brief) |
|---|---|---|
| Indicators | 92 (60 college, 30 university, 2 department) | 100–300 |
| Org units | 246 (191 departments, 25 colleges or institutes, about 30 central) | 500 or more |
| Forms (submissions) per cycle | about 250 | about 500 |
| Approved values per cycle | about 2k | 10k–50k |
| Quarterly cycles | 4 per year over the 2027–2031 plan, so 20 or more, plus trial cycles | same |
| Audit events | every value save, transition and export is one outbox row and one Mongo document | about 0.5–1 M per year |
| Staff accounts | about 1–2k (DREP, DE, CTC and DEAN for every department and college) | same |

## What is already done well (keep it)

- Business uniqueness is mostly enforced in the database:
  - one form per unit per cycle: `ux (cycle_id, unit_id)`
  - one value per form per indicator: `(submission_id, indicator_id)`
  - one official value per indicator per quarter: `(indicator_id, year, quarter)`
  - one target per indicator, unit, year and kind
  - one working definition version per indicator, and one pending RBAC change per role (both partial unique indexes)
- CHECK constraints exist on value kind and value, ratio denominator, cycle dates, unit code, member periods and initiative years.
- `xmin` row versions (`uint Version` with `IsRowVersion`) are used on every mutable aggregate.
- Timestamps use `DateTimeOffset` (stored as timestamptz). Dates use `DateOnly` (stored as date).
- Value columns use `numeric(20,4)`.
- `AsNoTracking` is used consistently on reads. The DbContext is scoped, and background jobs create their own scopes.
- No synchronous I/O on request paths.
- Migrations only add things. Production migrates through an explicit `--migrate` step; `MigrateOnStartup` defaults to development only.
- The chain hash is computed off the request path by a single leader (advisory lock), reserved in PostgreSQL, then sent idempotently to Mongo with `w:majority` and journaling. There is no chain write contention on user requests.
- The mail queue uses `FOR UPDATE SKIP LOCKED`.

---

## Findings (most severe first)

### PERF-B1: Each submit, approve, return or reopen computes effective access for every active user, one by one, inside the HTTP request
- **Severity:** High. **Confidence:** verified.
- **Where:**
  - `src/Tier1.Application/Collection/FormNotifier.cs:83-90` (`UsersWhereAsync`)
  - called from `FormNotifier.cs:25,28,31,54`
  - called by `FormsService.cs:203,217,232,248`
  - cache-miss cost is in `Access/AccessService.cs:57-91`
- **Evidence:**
  ```csharp
  var users = await db.Users.AsNoTracking().Where(u => u.Active)...ToListAsync(ct);
  foreach (var u in users) if (predicate(await access.GetAsync(u.Id, ct))) result.Add(u.Email);
  ```
  - On a cache miss, `GetAsync` runs 4–5 queries: the users check, user_roles, roles with grants, user_overrides, and wide units.
  - The cache lasts 60 seconds, and its key includes the global access version. Any RBAC change invalidates every entry.
- **Failure scenario:**
  - A dean approves a college form. The reviewers of the next step are computed over about 1,500 active accounts, which costs 6,000–7,500 sequential queries.
  - At about 1 ms each, that is 6–8 seconds of latency. The request holds one pooled connection the whole time.
  - At the deadline peak (dozens of approvals per minute) this pins connections and can exhaust the default 100-connection pool.
  - `RemindAsync` repeats the scan for every overdue form (about 250) every hour.
  - The E2E load test does not catch this because it only issues GETs.
- **Fix:**
  1. Resolve recipients in SQL from the role assignments. Join `iam.user_roles` (active, approved, in the validity window) with `iam.role_grants` filtered to the needed feature and special (for example `forms` with special `review_college`). Filter by scope in SQL: `scope_values && @ancestorCodes`, where `@ancestorCodes` are the codes taken from `unit.Path`. Then apply overrides only to the few candidates (tens of users).
  2. Move recipient resolution out of the request. Enqueue a single `dc.form_moved` outbox or mail job row in the same transaction (see DB-2), and expand recipients in the background `MailDispatcher`.
  3. Add a GIN index for the array-overlap filter: `CREATE INDEX ix_user_roles_scope_values ON iam.user_roles USING gin (scope_values);`

### DB-1: Two value saves on the same form conflict with each other
- **Severity:** High. **Confidence:** verified on the server side; likely to trigger in practice.
- **What happens:** every value save also updates the form row (`dc.submissions`), which carries a row version. So two concurrent saves on the same form produce false 409 errors and autosaves that never land.
- **Where:**
  - `FormsService.cs:116` (`LoadAsync(..., tracking: true)` tracks the `Submission`)
  - `FormsService.cs:159-161` (`ctx.Submission.LastEditedBy/LastEditedAt/Status` are modified on every save)
  - `Tier1DbContext.Collection.cs:50` (`Submission.Version` with `IsRowVersion`, which is `xmin`)
  - client side: `web/src/features/forms/useValueAutosave.ts:82-84` (`flushAll` = `Promise.all` over every row) and line 160 (one PUT per row, rows in parallel)
- **Evidence:**
  - Each `PUT /api/forms/{id}/values` issues `UPDATE dc.submissions ... WHERE id=@id AND xmin=@orig`, and `LastEditedAt` changes on every save.
  - Two in-flight saves for different indicators of the same form both read the same `xmin`. The second one gets a `DbUpdateConcurrencyException`, which `ApiSetup.cs:125` turns into a 409.
  - The UI then shows "This value changed elsewhere…" and does not save.
- **Failure scenario:**
  - A department data-entry user types three values quickly, or presses "next pillar" or "Submit", which calls `flushAll`.
  - 2–20 parallel PUTs race, and all but one fail with a misleading conflict.
  - With two editors on a college form (DE and CTC) during the deadline week, conflicts happen continuously.
- **Fix:** keep the protection between a value save and a submit, but stop value saves from conflicting with each other.
  - Do not track `Submission` in `SaveValueAsync`, `ClearValueAsync` or `ImportAsync`. Inside the same transaction, run one conditional statement that serializes on the row lock and re-checks editability atomically:
    ```sql
    UPDATE dc.submissions
       SET last_edited_by=@u, last_edited_at=@now, status = CASE WHEN status=1 THEN 2 ELSE status END
     WHERE id=@id AND step=1 AND status IN (1,2,4)
    ```
    (EF `ExecuteUpdateAsync`.) If it affects 0 rows, roll back with `form_locked`.
  - Keep the `xmin` check only on the transition endpoints (submit, approve, return, reopen).
  - The per-value `xmin` stays as the real lost-update guard.
  - Add a test that fires 10 parallel saves on different indicators of one form and expects ten 200 responses.

### DB-2: Workflow transitions and their notifications are committed separately, which contradicts the code comment
- **Severity:** Medium-High. **Confidence:** verified.
- **Where:**
  - `FormsService.cs:202-203`, `216-217`, `231-232`, `247-248`: `await db.SaveChangesAsync(ct); await notifier.MovedAsync(...)`
  - `FormNotifier.cs:37` calls `SaveChangesAsync` again
  - the class comment (`FormNotifier.cs:14`) says the message is added in the same transaction
- **Failure scenario:**
  - The transition commits. Then `MovedAsync`, which is slow per PERF-B1, times out or the client disconnects (`ct` is cancelled).
  - The user gets a 500 or a cancellation, the reviewer is never emailed, and a retry of the action fails with `not_in_review`.
  - The result is silent loss of notification email.
- **Fix:**
  - Enqueue the notification before the single `SaveChangesAsync`, so the transition and the mail rows commit atomically.
  - Better, enqueue one lightweight "form moved" job row (outbox pattern) and resolve recipients in the background (see PERF-B1).
  - Do not pass the request `ct` into post-commit work.

### PERF-B2: The forms list and the home page load every open and closed cycle's forms and do O(forms × cycle indicators) work in memory, with no pagination
- **Severity:** High as data grows. **Confidence:** verified.
- **Where:**
  - `FormsService.cs:65-106`, called by `HomeService.cs:36` (`forms.ListAsync(null, true, ...)`) on every home page load for anyone with `forms:V`
- **Evidence:**
  - Cycles are filtered to `Open || Closed` with no year bound.
  - Line 73 loads every submission of those cycles. Line 74 loads all org units. Line 75 loads every `CycleIndicator` of every cycle.
  - Line 92 runs `cis.Count(ci => ci.CycleId == s.CycleId && Applies(ci, unit))` for each form, a nested scan.
  - Unit-scope filtering happens in C# (line 87) after loading everything.
  - Closed cycles can never be "awaiting me" (line 93 requires `Open`), yet `awaitingMe=true` from home still loads them.
- **Failure scenario:**
  - In year 3: 12 cycles × 250 forms = 3,000 forms, and 12 × 92 = 1,100 cycle indicators, so about 3.3 M `Applies` calls per home load.
  - In the growth case: 20 × 500 forms and 20 × 300 indicators, so 10k × 6k = 60 M calls, which is seconds of CPU per request.
  - The response is also unbounded: 10k `FormRow` objects for university-wide roles.
  - The home page is the landing page for every user.
- **Fix:**
  1. When `awaitingMe` is set, or no `cycleId` is given, restrict to `Status == Open`. Default the list to the open cycles plus the most recent closed one.
  2. Compute `required` once per (cycle, unit kind, unit) with a dictionary built per cycle: group `cis` by `CycleId` first, so the cost becomes O(forms × indicators per cycle).
  3. Push the scope filter into SQL:
     ```sql
     JOIN org.units u ON u.id = s.unit_id
     WHERE u.path LIKE ANY(@prefixes)
     ```
     This needs `CREATE INDEX ix_units_path_pattern ON org.units (path text_pattern_ops);` (see DB-10).
  4. Add `page` and `pageSize` (maximum 200) with stable ordering, `ORDER BY ... , s.id`.
  5. Give the home page a dedicated `COUNT(*) ... GROUP BY status` query instead of materializing the list.

### PERF-B3: Results, trend, college progress and export rebuild a full snapshot per cycle, repeat full-table loads, and never cache closed cycles
- **Severity:** High as data grows. **Confidence:** verified.
- **Where:**
  - `ResultsService.cs:267-295` (`SnapshotAsync`): 9 queries per snapshot, including all `org.units`, all `kpi.standards`, wide `IndicatorVersion` rows, and all approved values of the cycle
  - `ResultsService.cs:136-147`: trend loop with up to 8 snapshots
  - `ResultsService.cs:215-229` (`UnitQuartersAsync`): one snapshot per quarterly cycle, unbounded, about 20 by 2031
  - `ProgressService.cs:52-60`: plus all `UniversityValues`, all `Targets`, and a full `ScorecardService.ComputeAsync`
  - `ResultsService.cs:152-157` (`ExportAsync`): runs `OverviewAsync` (2 snapshots), then a 3rd identical snapshot
- **Failure scenario:**
  - The college progress page in 2031 runs 20 snapshots, about 180 queries.
  - It materializes 20 × (2k–10k) values, 20 × 246+ units and 20 × the indicator versions, which is 40k–200k rows per request.
  - The indicator trend runs about 72 queries.
  - These are the dashboards the president and deans open, and they are concurrent at council time.
- **Fix:**
  1. A closed cycle's approved values are immutable, so cache a per-cycle aggregate snapshot keyed by `(cycle_id, cycle.Version)`. For open cycles, key it by `max(submission.version)`. Use `IMemoryCache` with a size limit.
  2. `UnitQuartersAsync`: one SQL query for all cycles. Join `dc.values v`, `dc.submissions s` (status=5) and `dc.cycles c` (`NOT trial AND quarter IS NOT NULL AND status<>1`), filtered on the unit subtree through `org.units.path LIKE @prefix`, then aggregate per (cycle, indicator) in C#. Supporting index:
     ```sql
     CREATE INDEX ix_submissions_unit_status ON dc.submissions (unit_id, status) INCLUDE (cycle_id);
     ```
  3. Trend: load the needed cycles' values in one `WHERE s.cycle_id = ANY(@ids)` query and filter to the single indicator: `v.indicator_id=@ind`.
  4. Export: reuse the snapshot from `OverviewAsync` (return it internally) instead of calling `SnapshotAsync` again.
  5. Cache reference data per request or for a short time: units, standards and pillars (see PERF-B9).

### PERF-B4: Each autosave costs about 14 round trips, including a scan of the unit's whole approved history
- **Severity:** Medium. **Confidence:** verified.
- **Where:**
  - `FormsService.cs:114-131`, which calls `LoadAsync` (`310-355`: 9 queries) and then `ItemAsync` (`441-446`: targets, university id and documents)
  - plus `settings.IntAsync` (an uncached query)
  - `FormsService.cs:331-337` (the "previous" query)
- **Evidence:**
  - The "previous" query joins values, submissions (`unit_id=@u AND status=5`) and cycles, returns every historical approved value of the unit, and groups in C# to keep the latest per indicator.
  - No index leads with `submissions.unit_id`. The existing indexes are `(cycle_id, unit_id)` and `(cycle_id, status)`.
- **Failure scenario:** at the deadline, about 250 forms × 60–300 fields, with saves fired 800 ms after typing stops, gives several hundred saves per minute × 14 queries. Each "previous" query reads more rows every cycle (by year 5, 20 cycles × the indicators per unit).
- **Fix:**
  1. Add the supporting index:
     ```sql
     CREATE INDEX ix_submissions_unit_status ON dc.submissions (unit_id, status) INCLUDE (cycle_id);
     ```
  2. Rewrite "previous" as `DISTINCT ON`:
     ```sql
     SELECT DISTINCT ON (v.indicator_id) v.indicator_id, v.value
       FROM dc.values v JOIN dc.submissions s ON s.id=v.submission_id JOIN dc.cycles c ON c.id=s.cycle_id
      WHERE s.unit_id=@u AND s.status=5 AND c.id<>@cycle AND c.opened_at < @openedAt AND (@trial OR NOT c.trial)
        AND v.indicator_id = @indicatorId          -- on save only the saved indicator is needed
      ORDER BY v.indicator_id, c.opened_at DESC;
     ```
  3. Make `SaveValueAsync` load a slim context: the one cycle indicator plus its version, the one value row, and the previous value for that indicator only, instead of the full form.
  4. Cache the university id and the settings values.

### DB-3: The "one official cycle per (year, quarter)" rule is only checked in code (read, then insert)
- **Severity:** Medium. **Confidence:** verified.
- **Where:**
  - `CyclesService.cs:202-207` (`EnsureQuarterFreeAsync`: `AnyAsync` then insert)
  - `Tier1DbContext.Collection.cs:27-33`: no constraint, and no CHECK on `quarter`
- **Failure scenario:**
  - Two admins create, or edit into, "Y2027 Q1" at the same time, and both pass the check.
  - `ReviewService` and the scorecard then see two official cycles for one quarter. `university_values` is unique per (indicator, year, quarter), so a transfer from the second cycle silently overwrites the first's official values.
- **Fix:**
  ```sql
  ALTER TABLE dc.cycles ADD CONSTRAINT ck_cycles_quarter CHECK (quarter IS NULL OR quarter BETWEEN 1 AND 4);
  CREATE UNIQUE INDEX ux_cycles_official_quarter ON dc.cycles (target_year, quarter) WHERE NOT trial AND quarter IS NOT NULL;
  ```
  Check existing data first:
  ```sql
  SELECT target_year, quarter, count(*) FROM dc.cycles WHERE NOT trial AND quarter IS NOT NULL GROUP BY 1,2 HAVING count(*)>1;
  ```
  Then map `23505` to `quarter_taken` (409).

### DB-4: Deleting a target, including an approved one, deletes its revision history from PostgreSQL
- **Severity:** Medium. **Confidence:** verified.
- **Where:**
  - `TargetsService.cs:219-233` (`Remove`: `db.TargetRevisions.RemoveRange(...)` then `db.Targets.Remove`), also reached from `SavePlanAsync:166`
  - `DictionaryService.cs:301-304`
- **Evidence:**
  - The FK `target_revisions → targets` is `Restrict`, which shows the intent to keep history, but the code deletes the children explicitly.
  - Only a Mongo audit event remains.
- **Failure scenario:** a committee-approved target is changed several times (each with a decision reference) and then removed in a plan edit. Every previous value and decision reference disappears from the relational store, so reports and the "revisions" panel cannot show it any more.
- **Fix:**
  - Soft-delete targets: add `deleted_at`, `deleted_by`, `delete_reason`, `delete_decision_ref`.
  - Exclude deleted rows with a query filter.
  - Change the unique index to `ux_targets_active ... WHERE deleted_at IS NULL`.
  - Keep `target_revisions` and write a final "deleted" revision row.
  - Hard deletion stays only for never-approved proposals.

### DB-5: Official university values and form values are overwritten in place; PostgreSQL keeps no versions
- **Severity:** Medium. **Confidence:** verified.
- **Where:**
  - `ReviewService.cs:111-126` (`UniversityValue` updated in place; the domain comment `Collection.cs:262` says "previous remains in the log", meaning Mongo only)
  - `FormsService.cs:148-158` (a reopened approved form overwrites the approved `dc.values` rows)
- **Failure scenario:**
  - A council report was produced from the Q2 official value. Later the form is reopened and the value re-transferred.
  - The relational store can no longer reproduce what the council saw, or which value fed the tier. Reconstructing it needs Mongo JSON `changes` strings, which only hold old and new text.
- **Fix:** add append-only history tables written in the same transaction:
  - `dc.university_value_revisions (id bigint identity, university_value_id, value, numerator, denominator, computed_value, note, transferred_by, transferred_at)`
  - `dc.value_revisions (id, value_id, submission_id, kind, value, numerator, denominator, edited_by, edited_at, reason)`, at least for values changed after approval or reopen
  - Index each on `(parent_id, at DESC)`.

### DB-6: `university_values` numeric columns are `numeric` with no precision or scale, unlike every other value column
- **Severity:** Medium. **Confidence:** verified.
- **Where:**
  - `Migrations/20261113070701_UniversityValues.cs:24-27`
  - `Tier1DbContext.Collection.cs:83-93` (no `HasPrecision`)
- **Evidence:** form and target values are `numeric(20,4)`. Official values store the full C# decimal, up to 28 significant digits, for example `66.6666666666666666666666666667`.
- **Failure scenario:**
  - The precision is inconsistent between layers, and exports show long fractions.
  - `ReviewService.cs:157` decides `Transferred` or `Changed` by exact `o.ComputedValue == computed`. If someone later "fixes" the column to `numeric(20,4)` without rounding in code, every row flips to `Changed`.
- **Fix:**
  - In code, round computed values with `decimal.Round(x, 4, MidpointRounding.AwayFromZero)` before storing and before comparing. Then add a migration:
    ```sql
    ALTER TABLE dc.university_values
      ALTER COLUMN value TYPE numeric(20,4) USING round(value,4),
      ALTER COLUMN numerator TYPE numeric(20,4) USING round(numerator,4),
      ALTER COLUMN denominator TYPE numeric(20,4) USING round(denominator,4),
      ALTER COLUMN computed_value TYPE numeric(20,4) USING round(computed_value,4);
    ```
    The table is small (≤ 6k rows), so the rewrite is acceptable.
  - Also add `CHECK (denominator IS NULL OR denominator > 0)`.

### DB-7: `logs.outbox` and `adm.mail_outbox` grow without limit; there is no retention for sent rows
- **Severity:** Medium. **Confidence:** verified.
- **Where:**
  - `RetentionJob.cs:38-46` (purges sessions only)
  - `OutboxDispatcher.cs` (marks rows `sent_at` but never deletes them)
  - mail rows are kept after sending (`MailDispatcher.cs:99-103`)
- **Failure scenario:**
  - Every value save, transition, export and login writes an outbox row with the full JSON payload (text). That is about 0.5–1 M rows a year, several GB over the plan, all duplicated in Mongo.
  - Backups and restore tests (`Maintenance.cs`) grow, vacuum and the unique `event_id` index grow, and `mail_outbox` LIKE scans slow down (PERF-B5).
- **Fix:** add these to `RetentionJob`:
  ```sql
  DELETE FROM logs.outbox o USING logs.chain_anchors a
   WHERE o.sent_at < now() - interval '30 days' AND a.chain=o.collection AND o.chain_seq BETWEEN a.first_seq AND a.last_seq AND a.intact IS TRUE;
  DELETE FROM adm.mail_outbox WHERE (sent_at < now() - interval '180 days') OR (failed_at < now() - interval '365 days' AND audience <> 'beneficiary');
  ```
  Run them in batches (`LIMIT 5000` loops) and make the retention period a setting.

### PERF-B5: The reminder de-duplication uses a prefix LIKE that a B-tree cannot use, and the de-dup key is not deterministic
- **Severity:** Medium. **Confidence:** verified for the query shape; likely for the plan.
- **Where:**
  - `FormNotifier.cs:52` (`m.DedupKey.StartsWith(key)`, which becomes `dedup_key LIKE 'rem:…%'`)
  - `FormNotifier.cs:60` (key `{key}:{sent}`, where `sent` is a global counter that depends on the unordered `forms` list, line 46)
- **Evidence:** the `postgres:18` default collation is `en_US.utf8`, so `ux_mail_outbox_dedup_key` cannot serve the LIKE prefix. The table is never purged (DB-7).
- **Failure scenario:**
  - Every hourly run issues one sequential scan of `mail_outbox` per pending form, about 250 scans per hour at the deadline. The cost grows every cycle.
  - Two instances, or a rerun after a partial failure, generate different `:{n}` suffixes, so the unique index does not stop duplicate reminders.
- **Fix:**
  - Make the key deterministic per recipient: `rem:{submissionId:N}:{yyyyMMdd}:{sha1(email)[..8]}`.
  - Check it with one batched exact-match query: `WHERE dedup_key = ANY(@keys)`.
  - Alternatively, record a row in a `dc.form_reminders (submission_id, day)` table with PK `(submission_id, day)` and use `INSERT ... ON CONFLICT DO NOTHING`.

### PERF-B6: Audit-log searches by target id, related id or reference are not covered by Mongo indexes
- **Severity:** Medium. **Confidence:** verified for the index definitions; likely for the plans.
- **Where:**
  - `MongoLogStore.cs:72-83` (indexes)
  - `MongoLogReader.cs:53-58` (filters)
  - `MongoLogReader.cs:22` (`CountDocuments` limit 100k on every page)
- **Evidence:**
  - `TargetId` filters `$or: [{target.id}, {related.id}]`. The only target index is `(target.type, target.id, ts)`, which leads with `type`, so it cannot be used. Nothing indexes `related.*`.
  - A `$or` with one unindexed branch becomes a collection scan.
  - `Ref` uses a case-insensitive regex (`/^…/i`) on `target.ref` and `related.ref`, which cannot use tight index bounds.
- **Failure scenario:** in year 2 the `audit` collection holds about 1–2 M documents. "Show everything about form X" scans the whole collection twice (count, then find) at w:majority read cost, so it takes seconds.
- **Fix:**
  - Add indexes:
    - `{ "target.id": 1, ts: -1 }`
    - `{ "related.id": 1, ts: -1 }`
    - `{ "related.ref": 1, ts: -1 }`
  - For a case-insensitive prefix, either store `target.refLower` and `related.refLower` at write time (inside the hashed payload, so only for new events) and use a case-sensitive anchored regex on them, or create the ref indexes with collation `{ locale: "en", strength: 2 }` and query with the same collation.
  - Replace the exact count with `EstimatedDocumentCount` when there is no filter, or show "more than 10,000".

### PERF-B7: Transfer All runs one query per row, and a single transfer computes the full breakdown twice
- **Severity:** Medium. **Confidence:** verified.
- **Where:**
  - `ReviewService.cs:90-98` (a loop calling `SaveAsync`, each with `UniversityValues.FirstOrDefaultAsync` at line 111)
  - `ReviewService.cs:79-87` (`RowsAsync`, a full `BreakdownAsync` snapshot, before and after)
- **Failure scenario:** a transfer of 92–300 indicators issues 92–300 extra round trips. Each single transfer also rebuilds two full snapshots (about 20 queries plus in-memory aggregation).
- **Fix:**
  - Load `official` once (`RowsAsync` already has it) and pass the tracked entities into `SaveAsync`.
  - After a single transfer, recompute only that indicator's row from the snapshot already loaded.

### PERF-B8: The in-memory aggregation algorithm repeats work for every (unit × indicator) cell
- **Severity:** Medium, rising to High at 300 indicators and 500 units. **Confidence:** verified.
- **Where:**
  - `ResultsService.cs:298-337` (`Actual`): line 302 rebuilds `HashSet` of all form units on every call, and lines 326-330 scan all units with `Path.StartsWith` on every call
  - `ResultsService.cs:343-344` (`TargetFor`): `[.. s.Targets[...]]` copies a list on every call
  - callers: the heat map (`91-100`), `Result` (twice with the previous cycle), and `BreakdownAsync:197` (a nested `Any` over submissions per college per indicator)
- **Failure scenario:**
  - Overview for the university at 30 colleges × 270 indicators: about 8k `Actual` calls × (500-element HashSet plus a 500-unit scan) is about 8 M operations and 8k allocations, plus the previous cycle.
  - That is about 100–300 ms of CPU per request. It runs inside the trend loop (PERF-B3) and multiplies under concurrent dashboard use.
- **Fix:**
  - Precompute once per snapshot:
    - `FormUnits` (a HashSet)
    - `ContributorsByUnitAndLevel: Dictionary<(Guid unit, OrgUnitKind level), Guid[]>`, built from the parent chain
    - `TargetsByIndicator: Dictionary<Guid, IndicatorTarget[]>`
  - Then `Actual` becomes O(contributors).
  - Better still, do the Sum, Count and RatioOfSums aggregation in SQL: `GROUP BY indicator_id, college_id` with the college derived through the `units.parent_id` join.

### DB-8: Documents use a polymorphic owner (`owner_type` + `owner_id`) with no foreign key, so evidence can be orphaned
- **Severity:** Low-Medium. **Confidence:** verified.
- **Where:**
  - `Tier1DbContext.Collection.cs:94-106`
  - `FormsService.cs:180-182` (`ClearValueAsync` only blocks on *non-voided* documents, then deletes the value)
- **Failure scenario:** a user voids an evidence file and clears the value. `dc.documents` keeps a row whose `owner_id` points to nothing, so integrity checks and exports cannot place it.
- **Fix:** pick one approach.
  - Use nullable typed FK columns: `value_id uuid REFERENCES dc.values ON DELETE RESTRICT`, `college_id uuid REFERENCES org.units` and a plan flag, plus `CHECK (num_nonnulls(value_id, college_id) <= 1)`.
  - Or refuse to clear a value that has any document, voided or not.
  - Also add `CHECK (owner_type IN ('value','structure','plan'))`.

### DB-9: Several relationships are stored as arrays of codes with no referential integrity and no GIN index
- **Severity:** Low-Medium. **Confidence:** verified.
- **Where:**
  - `Initiative.IndicatorCodes` (text[], `Domain/Kpi/Initiatives.cs:40`), queried with `.Contains` in `DictionaryService.cs:305`
  - `DataConflict.Scope` (text[], `Conflicts.cs:34`)
  - `UserRole.ScopeValues` (unit codes, `Access.cs:101`)
- **Failure scenario:**
  - An indicator or unit code is reused or retired, and initiatives, conflicts or role scopes silently point at the wrong or a missing entity.
  - Scope checks depend on a code that no constraint ties to `org.units`. `UniversityStructure.ApplyAsync` already has to rewrite scope arrays by hand.
- **Fix:**
  - Use junction tables:
    - `kpi.initiative_indicators(initiative_id, indicator_id)`, with PK on both and FKs
    - `iam.user_role_scopes(user_role_id, unit_id)`, FK to `org.units` ON DELETE RESTRICT
  - Until then, add GIN indexes (`USING gin (indicator_codes)`, `USING gin (scope_values)`) and a nightly consistency check that reports codes not present in the referenced table.

### DB-10: Index coverage does not match the actual predicates
- **Severity:** Low-Medium. **Confidence:** verified.
- **Indexes that are missing or cannot be used:**

| Predicate (where) | Current state | Fix |
|---|---|---|
| `org.units.path` prefix: scope checks done in C# (`CyclesService.cs:43`, `FormsService.cs:87`, `ResultsService.cs:327`) | `ix_units_path` (`Tier1DbContext.Tier1.cs:55`) is never queried, and under `en_US` collation it could not serve `LIKE 'x%'` anyway | Replace with `CREATE INDEX ix_units_path_pattern ON org.units (path text_pattern_ops);` once scope filtering moves to SQL (PERF-B2), otherwise drop it |
| `dc.submissions` `unit_id`, `status` (`FormsService.cs:331-336`) | none | `(unit_id, status) INCLUDE (cycle_id)` |
| `org.industry_partners.college_id` (`StructureService.cs:68,93`) | only `department_id` is indexed | `CREATE INDEX ix_industry_partners_college_id ON org.industry_partners (college_id);` |
| FK columns used by RESTRICT checks or joins: `kpi.targets.unit_id`, `dc.university_values.cycle_id`, `dc.cycle_indicators.version_id`, `dc.cycle_indicators.indicator_id` (second PK column), `kpi.initiatives.owner_unit_id`, `kpi.indicator_versions.owner_unit_id` | none | add single-column indexes. The tables are small now; this is cheap insurance before the tables grow |
| `dc.university_values` by `(year, quarter)` (`ReviewService.cs:146`) | the unique index leads with `indicator_id` | `(year, quarter)` |

### DB-11: Some business rules have no database constraint
- **Severity:** Low. **Confidence:** verified for the code; likely for the impact.
- **Rule 1: a single-holder position (`StructurePosition.Single`) can have only one active holder.**
  - Today it is read then inserted (`StructureService.cs:116-119`).
  - Fix: copy `single` onto `structure_members`, or use a trigger, plus a `btree_gist` exclusion constraint:
    ```sql
    EXCLUDE USING gist (college_id WITH =, position_id WITH =, coalesce(department_id,'00000000-0000-0000-0000-000000000000') WITH =,
      daterange(start_date, end_date, '[]') WITH &&) WHERE (single)
    ```
- **Rule 2: the `EachCollege` target is ambiguous.**
  - The unique index includes `year`, so several `EachCollege` rows can exist per indicator.
  - `TargetsService.cs:69,289`, `StrategyService.cs:77` and `DictionaryService.cs:98` pick `FirstOrDefault` with no year and no ORDER BY. `TargetLookup` picks by year. Screens and calculations can therefore disagree.
  - Decide whether there is one `EachCollege` per indicator. If so, add `CREATE UNIQUE INDEX ... ON kpi.targets (indicator_id) WHERE kind = 3`. If not, filter by year everywhere.
- **Rule 3: a baseline belongs only to the first plan year.**
  - Add `CHECK (kind <> 1 OR year = 2026)`.

### DB-12: Data typing and constraint hygiene
- **Severity:** Low. **Confidence:** verified.
- `iam.users.email` is unique but case-sensitive (`Tier1DbContext.cs:55`). The app lowercases emails today. Make it `CREATE UNIQUE INDEX ux_users_email_ci ON iam.users (lower(email))`, or use `citext`.
- Integer enum columns have no CHECK (`status`, `step`, `kind`, `level`, `aggregation`). Add range checks on the workflow columns at least:
  - `dc.submissions`: `CHECK (status BETWEEN 1 AND 5 AND step BETWEEN 1 AND 6)`
  - and keep the invariant `CHECK ((status = 5) = (step = 6))`
- `submissions.approved_at` and `status` are not tied together: add `CHECK (status <> 5 OR approved_at IS NOT NULL)`.

### PERF-B9: Reference data and settings are re-read on every request
- **Severity:** Low-Medium. **Confidence:** verified.
- **Where:**
  - `SettingsService.cs:23-28`: one query per key. The scorecard calls it 7 times per request, and a form build calls it 2–3 times.
  - `ScorecardService.ComputeAsync` (about 15 queries, all `UniversityValues`, indicators with all versions) runs on every scorecard request and again inside every Progress request.
  - Home runs about 25 queries (`StandardsAsync` loads every indicator with versions, targets and conflicts).
- **Fix:**
  - Use an `IMemoryCache` for settings (`settings:all`, 60 s, evicted on `SaveAsync`) and for org units, standards and pillars, keyed by a cheap version: for example `SELECT max(xmin::text::bigint)`, or a `ref_version` counter bumped on change, like `AccessState`.
  - Cache the scorecard DTO keyed by `(max(university_values.transferred_at), count(*), the indicators version)`.

### PERF-B10: Lists are unbounded, or truncated with no paging
- **Severity:** Low-Medium. **Confidence:** verified.
- **Where:**
  - `UsersAdminService.cs:66`: `Take(500)` with no paging or total. With about 1.5k accounts, users past the 500th by Arabic name are unreachable except through search. The search is a case-sensitive `LIKE '%x%'` with no Arabic normalization (alef and hamza variants, taa marbuta and haa) and no trigram index.
  - The forms list (PERF-B2), the cycles list (`CyclesService.cs:34-38`, every form of every cycle loaded to count per status; use `GROUP BY cycle_id, status` in SQL) and the conflicts list have no limit either.
- **Fix:**
  - Use `page` and `pageSize` with a maximum of 200, a tie-break on `id`, and a total count computed under the same filters.
  - Add `pg_trgm` with a generated `name_search` column normalized for Arabic, and a GIN trigram index on it.

### PERF-B11: The load test cannot detect the bottlenecks above
- **Severity:** Medium (missing verification). **Confidence:** verified.
- **Where:** `tests/Tier1.E2E/ScorecardAndLoadTests.cs:238-249`
- **Evidence:**
  - Only GETs are issued (home, forms, one form, results, indicators).
  - It runs against the demo dataset: 3 colleges, a few forms, one cycle.
  - It sends no value saves, no submit or approve, no trend or progress requests, and no export.
  - Its acceptance is p95 < 2 s with no error-rate breakdown per write route.
- **What it misses:** PERF-B1 (approve latency), DB-1 (parallel save conflicts), and PERF-B2 and PERF-B3 (growth over cycles).
- **Fix:**
  - Add a data generator: about 250 units, 92–300 indicators, and 12–20 closed quarterly cycles with approved values.
  - Add write scenarios: parallel autosave on one form with 409 rate asserted at 0, and submit or approve across forms.
  - Add a "deadline day" mix (70% saves, 10% transitions, 20% dashboards).
  - Report p50, p95 and p99 per route, the error rate per route, and the Npgsql pool counters (`dotnet-counters` Npgsql `busy-connections`).

### DB-13: Migration, seeding and operations hygiene
- **Severity:** Low. **Confidence:** verified.
- **Data fixes run as seeder logic with marker keys, outside migration history.**
  - `Seeder.cs:178-277` ("once" fixes recorded as `adm.settings` keys like `catalog-fix:*`) is a second, unversioned data-migration mechanism.
  - It is mixed with real settings, has no ordering, and is not wrapped in a single transaction with its access-version bump.
  - Fix: move the marker keys to a dedicated `adm.data_migrations (key PK, applied_at, checksum)` table and run each fix in its own transaction.
- **Seeding has no lock against concurrent runs.**
  - Two processes running `--migrate` or dev startup at once fail on unique keys rather than being serialized. EF's migration lock covers `MigrateAsync` only.
  - Fix: wrap `SeedAsync` in `pg_advisory_lock(<const>)`.
- **Destructive admin commands do not record verifiable before and after counts.**
  - `KpiFramework.ResetAsync` and `UniversityStructure.ApplyAsync` are guarded by `--confirm`, which is good, but neither takes a backup or records a checksum.
  - Fix: make `--reset-kpi` refuse unless `--backup` ran within N minutes, or have it run the backup itself, and log counts before and after.
- **Future indexes need a non-blocking build pattern.**
  - Every migration so far is additive and safe, but there is no pattern yet for building indexes without blocking.
  - Fix: use `migrationBuilder.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ...", suppressTransaction: true)` once `dc.values` or `logs.outbox` hold more than about 1 M rows.

---

## Extension readiness: conventions for new modules and tables

1. **Schema per module.** Use a schema per module: iam, adm, logs, org, kpi, dc, and a new schema for a new module. Use snake_case names (already automatic). Give every constraint and index an explicit name: `ux_`, `ix_`, `ck_`, `fk_`.
2. **Keys.**
   - Business entities use a `uuid` PK generated as `Guid.CreateVersion7()`, which is time-ordered and B-tree friendly.
   - Append-only logs and revisions use a `bigint` identity (`UseIdentityAlwaysColumn`).
   - Immutable codes (unit or indicator codes) are separate unique columns with a CHECK on their format.
3. **Uniqueness belongs in the database.** Every rule of the form "one X per Y" gets a unique, or partial unique, index (filtered `WHERE NOT trial`, `WHERE deleted_at IS NULL`, `WHERE status IN (...)`). App-level `AnyAsync` checks exist only for friendly messages, and `23505` maps to 409 with a specific code.
4. **Concurrency.**
   - Every mutable aggregate has `uint Version` with `IsRowVersion()` (xmin), and the client echoes it back.
   - Do not touch a parent row on child edits unless you intend to serialize them; prefer a conditional `ExecuteUpdate` (DB-1).
   - Serialize counters and sequences with advisory locks or a row lock, never `MAX()+1`.
5. **Foreign keys.**
   - Default to `Restrict`. Use `Cascade` only for pure composition with no history value (grants, sessions, assignment junctions).
   - Do not delete restricted children in code to get around `Restrict`.
   - Index every FK column.
   - No polymorphic `owner_type`/`owner_id`, and no arrays of codes for relationships: use junction tables with FKs.
6. **Types.**
   - Measured and target numbers use `numeric(20,4)` everywhere (`HasPrecision(20,4)`), with rounding to 4 decimal places in C# before storing or comparing.
   - Instants are `DateTimeOffset` (timestamptz, UTC). Calendar days are `DateOnly`. Cairo-local logic stays in `CairoTime`.
   - Integer enums get a CHECK on their range.
   - JSON goes in `jsonb` only for opaque snapshots that are never filtered on.
   - Text gets an explicit `HasMaxLength`.
7. **History.**
   - Anything that feeds an official report or decision gets an append-only `*_revisions` table written in the same transaction (old and new values, reason, decision reference, by, at).
   - Use soft delete (`deleted_at`, `deleted_by`, `delete_reason`) with a partial unique index for approved or decision-bound rows.
   - Mongo audit is the tamper-evident trail, not the queryable history.
8. **Reads.**
   - Apply scope, then filter, sort and page in SQL.
   - Use `AsNoTracking` and project to DTOs.
   - Never load whole tables to filter in C#. The exception is cached reference data that has an explicit version key.
   - Lists take `page` and `pageSize` (maximum 200) with an `id` tie-break.
   - Express scope as `units.path LIKE ANY(@prefixes)` backed by a `text_pattern_ops` index.
9. **Workflow writes.**
   - The state change, the history and event rows, the audit outbox row, and the notification job row commit in one `SaveChanges` or transaction.
   - Recipient expansion and other fan-out happen in background jobs, never in the request.
10. **Growth tables** (outbox, mail, sessions, technical logs). Each needs a retention rule in `RetentionJob`, batched deletes, and an index on the purge predicate.
11. **Migrations.**
    - One migration per change. Review the generated SQL.
    - Expand, migrate, then contract for renames and type changes.
    - Use `CONCURRENTLY` for indexes on large tables. Use `NOT VALID` then `VALIDATE` for new FK and CHECK constraints on populated tables.
    - Run a duplicate or violation query before adding any unique or check constraint.
    - Never edit an applied migration.
    - Data backfills belong in their own migration, or a tracked `adm.data_migrations` step, and must be idempotent and resumable.
12. **Seeding.**
    - Seed reference data by stable codes with insert-if-missing (`ON CONFLICT DO NOTHING`), and never overwrite admin edits.
    - Run under an advisory lock.
    - Demo data is gated by config and kept off production databases (`StartupGuards`).
13. **Mongo.**
    - Each new chained collection is registered in `LogCollections.Chained` and gets the standard index set.
    - Add an index for every new filter field (leading field equal to the filter, then `ts:-1`).
    - Keep payload fields that are used for search normalized (lowercase or reference fields) at write time, because chained documents are immutable.
14. **Tests per schema change.**
    - Run against real PostgreSQL (Testcontainers), never in-memory.
    - Test a constraint violation (expect 409), two concurrent writers, upgrading from the previous migration with representative data, and the plan of the new hot query (`EXPLAIN` on seeded volume).
