# Tier1 frontend review (web/) — engineering, performance, architecture

Scope: `D:\Amr\Tier1\web` (React 18, TS 5.8 strict, antd 5, react-query 5, react-router 7, Vite 6). Read-only review; nothing was built, run or edited.
Method: frontend-engineering (review mode, FE-xx), performance-review (diagnosis only — no measurements were run; sizes come from the committed scratch build `web/.scratch-indicator-groups-build`, dated Nov 15) and architecture-refactoring (assessment only).
Backend contract checks: `src/Tier1.Api/Security/SessionAuthentication.cs`, `Startup/ApiSetup.cs`, `Tier1.Application/Accounts/Sessions.cs`, `Access/UsersAdminService.cs`, `Scorecard/*`.

Confidence: **verified** = I read the code path end to end. **likely** = the mechanism is in the code, but I did not reproduce it in a browser.

## What is already good (keep it)

- Every route is lazy-loaded (`app/App.tsx:35-56`). A `load()` wrapper reloads once on a stale-chunk failure after a deploy. Vendor chunks are split (react, antd, query, dayjs).
- The API client uses an HttpOnly session cookie and an in-memory CSRF token. It fetches the token once with single-flight, retries once on `csrf`, maps network failures to `ApiError(0)` and turns 204 into `null` (`api/client.ts`). Nothing is stored in localStorage.
- The autosave queue (`features/forms/saveQueue.ts`) is well designed: one request in flight per row, version-based optimistic concurrency, sequence numbers that drop stale replies, and a flush before submit and before pillar navigation. It has its own unit tests.
- `QueryState`/`QueryError` give one loading/error/403/404 vocabulary, and a failed refetch does not hide existing data.
- `tsconfig` has `strict`, `noUnusedLocals` and `noUnusedParameters`. There is only one `any` in app code.
- Query keys are consistent: every shared key maps to the same URL and type, so there are no cache collisions today.

---

## Findings (most severe first)

### FE-1 — High — verified — The previous user's cached data survives an in-tab re-login
- **Where:** `shared/session.tsx:40-44` (`reloadSession` calls `invalidateQueries()` + `refetchQueries(["me"])`); `features/auth/LoginPage.tsx:49-50`; `app/App.tsx:128-137` (Gate swaps to LoginPage without a page reload).
- **Evidence:** Logout from the Shell does `window.location.replace` (`app/Shell.tsx:211-218`), which clears memory. But when a session ends any other way (idle expiry, revocation, a logout in another tab), `["me"]` resolves to `null` and Gate shows LoginPage inside the same SPA, with the QueryClient intact. After the next login, `invalidateQueries()` only marks the cache stale. Inactive queries keep user A's data. For B, any query that is `enabled: false` keeps returning A's data indefinitely, for example `usePillarMap` (`shared/pillars.tsx:49-55`, gated on `kpi.strategy:V`) and `["cycles"]` in `ReviewPage.tsx:72`.
- **Failure scenario:** On a shared office PC, A (Tier 1 officer) leaves and the session idles out. B (college data entry) signs in on the same tab and opens Home or Forms. A's cached `["home"]`, `["forms","mine"]` and `["results",…]` render instantly until the refetch lands. Pillar names from A's strategy tree stay visible to B even though B has no `kpi.strategy:V`.
- **Fix:** In `reloadSession`, call `qc.clear()` (or `removeQueries()`) before refetching `["me"]` whenever the user id changes. Simplest is to always clear on login/MFA/password-change success. As a second guard, key the `QueryClientProvider` subtree on `me?.id`.

### FE-2 — High — verified — Background polling defeats the 30-minute idle timeout
- **Where:** `shared/session.tsx:27-28` (`refetchInterval: 60_000` on `/api/me`); `app/Shell.tsx:146-151` (`/api/iam/changes?status=Pending` every 60 s). Server side: `Tier1.Application/Accounts/Sessions.cs:101-106` slides `LastSeenAt` on any authenticated request older than 1 minute. `IdleMinutes = 30`, `AbsoluteHours = 12` (`appsettings.json:16-17`).
- **Evidence:** react-query polls while the tab is visible (`refetchIntervalInBackground` defaults to false). Each poll is authenticated, so it refreshes `LastSeenAt`.
- **Failure scenario:** A user leaves an approval screen open on an unattended desktop. The session never idles out and stays valid for the full 12 h absolute lifetime, which defeats the idle policy the backend enforces.
- **Fix:** Pick one of these. (a) Mark polling requests as passive, for example a header `X-Passive: 1`, and have `SessionAuthentication` skip the `LastSeenAt` update for them. (b) Track real user activity on the client (pointer/keyboard) and stop polling after N idle minutes. (c) Drop `/api/me` polling and refresh the session on focus or navigation instead. Add a backend test: passive requests must not extend the idle window.

### FE-3 — High — likely — Session expiry or a language switch unmounts the whole app and loses unsaved drafts; there is no global 401 handling
- **Where:** `app/App.tsx:183` (`<SessionProvider key={lang}>`); `app/App.tsx:128` (Gate replaces Shell with LoginPage when `me` becomes null); `features/forms/useValueAutosave.ts:217-233` (unmount flushes only drafts the server would accept, per `complete()`); `shared/QueryState.tsx:21-36` (401 renders as a generic error alert); `api/client.ts:84-95` (no 401 hook).
- **Evidence:**
  - A 401 (`code: "unauthenticated"`, "Your session has ended") from any query or mutation shows up as an inline error in each panel. Nothing reacts globally until the next `["me"]` poll, up to 60 s later.
  - When that poll returns null, Gate unmounts Shell → FormPage → ValueRows. Drafts that are not "ready" (a ratio with no denominator, an N/A reason under 5 characters, an emptied field) are dropped. Ready drafts are flushed but fail with 401.
  - Toggling the language remounts the same tree (`key={lang}`). That loses drafts that are not ready, plus all local UI state (filters, open drawers, review marks in `FormPage.tsx:44`).
- **Failure scenario:** A data-entry user types 15 values, walks away for 31 minutes, comes back and edits one more. The save fails with "session ended". About a minute later the screen flips to the login form. After signing in, the form reloads from the server, and the values that were not saved are gone.
- **Fix:**
  1. Add a `QueryCache`/`MutationCache` `onError` (or a client hook) that, on `ApiError.status === 401`, sets `["me"]` to null immediately. Show a modal re-login overlay over the Shell instead of unmounting it, so drafts survive. Resume `flushAll()` after re-authentication.
  2. Stop remounting on language change. `L()` is already reactive through `useLang`, and query invalidation already refetches the server texts.
  3. Persist drafts that are not ready to `sessionStorage`, keyed by form and indicator, until they are saved.

### FE-4 — Medium — verified — Every autosaved value triggers a full form refetch
- **Where:** `features/forms/FormPage.tsx:52-59`. `onSaved` patches the item with `setQueryData`, then calls `void refresh()`, which invalidates `["form", id]` and runs a full `GET /api/forms/{id}`. Each row save also re-renders the whole page (`ValueRow` is not memoized, `ValueRow.tsx:22`).
- **Evidence:** A pause of more than 800 ms while typing produces one PUT and then one full form GET (all sections, items, evidence and events). Entering a 120-indicator form costs about 120 PUTs + 120 GETs, and about 240 full re-renders of the visible pillar.
- **Failure scenario:** On slow networks, or for large college forms, the GET traffic and re-renders cause input lag, and the server load grows with the number of entry users at deadline time. (This is not measured; it is a hypothesis to profile.)
- **Fix:** Have `PUT /values` return the form-level summary (`completed`, `blocking`, `actions`, `version`) together with the item, and patch both with `setQueryData`. Or debounce/coalesce the invalidation (one refetch after N seconds of quiet, or on `flushAll`). Then measure with React Profiler and the Network panel on a 100+ item form, and only after that consider `memo(ValueRow)` with stable callbacks.

### FE-5 — Medium — verified — Progress page bars depend on CSS that only the Scorecard chunk loads
- **Where:** `features/progress/ProgressPage.tsx:214` renders `<span className="sc-bar"><i className="c100|c90|c70|clow">`. `.sc-bar` (display, height 8 px) and `.c100/.c90/.c70/.clow` are defined only in `features/scorecard/scorecard.css:6-13`, which only `ScorecardPage.tsx:19` imports. `progress.css:78` only overrides the width.
- **Failure scenario:** College users (who have no `/scorecard` link, `Shell.tsx:163`) open "College progress" first. The achievement bars have height 0, so they are invisible and the tone colours are missing. They appear only if the user happened to visit Scorecard earlier in the session. The opposite also happens: scorecard class names leak globally once that chunk loads.
- **Fix:** Move the `sc-bar` and score-tone classes to a shared stylesheet, for example `shared/score.css` imported by a shared `<ScoreBar>` component used by both pages. Add a test like `tokens.test.ts` that fails when a feature uses a class defined only in another feature's CSS.

### FE-6 — Medium — verified — The cache invalidation map is scattered and incomplete
- **Where (examples):**
  - Form approve/return/extend invalidate only `["forms"]` (`FormPage.tsx:65, 349, 362, 374`).
  - Reopen invalidates nothing besides `["form", id]` (`FormPage.tsx:366-372`).
  - Review transfer invalidates `["review"]` and `["scorecard"]` but not `["results"]` or `["home"]` (`ReviewPage.tsx:105-106, 347-348`).
  - `ChangesPage.tsx:61` invalidates everything.
- **Evidence:** A final approval changes results, progress, the review rows and home KPIs. Today this is masked because most queries keep `staleTime: 0` and refetch on mount. The exceptions show stale data within their window: `["units"]` and `["indicators","all"]` (300 s in `Shell.tsx:402-403`), and `["strategy"]` (60 s in `shared/pillars.tsx:54`). The reopened form also still shows as "Approved" in the cached `/forms` list (the back link) until it refetches.
- **Failure scenario:** The dean approves a form and goes back to the forms list or home. The old status and counters flash from cache. Any page given a `staleTime` later will silently show stale results after an approval.
- **Fix:** Centralise this as one `invalidate.afterFormDecision(qc)` / `afterTransfer(qc)` per domain event, next to the query-key factory (FE-7), covering `forms`, `form`, `home`, `results`, `progress`, `review` and `scorecard`. Call it from every mutation's `onSuccess`, and route the direct `api.post` calls in modals through `useMutation`.

### FE-7 — Medium — verified — No query-key factory or feature API hooks; fetchers are duplicated inline
- **Where:** 117 inline `queryKey:` literals. `["cycles"]` + `/api/cycles` is defined in 7 places (Cycles, Forms, Home ×2, AdminAnalytics, Results, Review). `["strategy"]` in 6. `["units"]` in 7 (`UnitsPage` uses `["units", showClosed]`, a different shape under the same prefix). `["results", undefined, undefined]` is hand-built in 3 files. `api/*.ts` holds types only; no feature has an `api/` module.
- **Failure scenario:** Someone adds `select`, a query parameter or a different response type at one call site. The other six sites then share a cache entry with incompatible data, a type-unsafe cache collision. `staleTime` already differs per call site for the same key (`["strategy"]`: 60 s in pillars, 0 elsewhere).
- **Fix:** Add `features/<f>/api.ts` per feature exporting `keys` (for example `cycleKeys.all`, `resultsKeys.list({cycleId, unitId})`) and hooks (`useCycles()`, `useResults(params)`, `useStrategyTree()`), each with its `staleTime`. Migrate one key at a time; this is behaviour-preserving.

### FE-8 — Medium — verified — Dead UI layer and unused dependencies
- **Evidence:**
  - `src/components/ui/` has 60 shadcn files (7,128 lines). No file outside that folder imports any of them; the only references are exclusions in `format.test.ts:93` and `tokens.test.ts:585`.
  - These packages are used **only** inside `components/ui`: `recharts` (chart.tsx), `cmdk`, `vaul`, `sonner` + `next-themes`, `react-day-picker`, `react-resizable-panels`, `input-otp`, `radix-ui`, `lucide-react`, `class-variance-authority`, `cn`, `tailwind-merge`/`clsx` (also in the unused `lib/utils.ts`).
  - `shiki` is used only by `lib/highlighter.tsx` (unused). `hooks/use-highlighted-code.ts` is unused. `date-fns` and `libphonenumber-js` have **zero** imports, yet `vite.config.ts:17` still defines a `libphonenumber` chunk.
  - `features/conflicts/{ConflictsPage,AgendaPage,ConflictDrawer}.tsx` are unreachable: their route was removed (`App.tsx` comment).
  - Both dayjs (antd) and date-fns are installed; only dayjs is used.
  - Three font families load: IBM Plex Sans Arabic for the app, Noto Kufi for display, and Cairo for landing **plus** the loading label (`shared/AppLoading.tsx:2`).
- **Impact:** Tree-shaking keeps most of this out of the bundle. The cost is install size, `tsc` time, a larger supply-chain surface (`lottie-react` is the only caret-ranged dependency, `^3.1.2`) and two competing component systems for anyone adding a feature.
- **Fix:** Decide on antd as the component system (the design-system skill already scopes shadcn to themed subtrees). Delete the unused `components/ui/*`, `lib/highlighter.tsx`, `hooks/use-highlighted-code.ts`, `lib/utils.ts`, the conflicts pages and the `libphonenumber` chunk rule, then `npm uninstall` the orphaned packages. If shadcn stays for future pages, keep only the components actually adopted and add them back with `npx shadcn add` when needed.

### FE-9 — Medium — likely — The entry chunk carries the Lottie engine and an extra font for the loading spinner
- **Where:** `shared/AppLoading.tsx:1-3` is imported statically by `App.tsx:8` as the fallback for every Suspense boundary. `lottie-web` is about 300 KB minified (`node_modules/lottie-web/build/player/lottie.min.js` = 305,704 B). In the committed build the entry chunk `index-*.js` is 411 KB raw / 103 KB gzip and contains the Lottie runtime (string match), on top of `antd` at 1.22 MB raw.
- **Failure scenario:** The first visit to `/` (landing) or `/login` on a mobile or slow connection downloads and parses about 300 KB of animation runtime before any route chunk, just to show a spinner. LCP and TTI get worse on the public page. (Not measured; run Lighthouse with mobile throttling before and after the change.)
- **Fix:** Use a CSS/SVG spinner for the Suspense fallbacks. If the animation is a brand requirement, lazy-load it (`lazy(() => import("lottie-react"))` behind a CSS spinner) or switch to `lottie_light`. Drop the Cairo 600 import from `AppLoading`, or reuse IBM Plex.

### FE-10 — Medium — verified — UserDrawer spins forever on error
- **Where:** `features/users/UserDrawer.tsx:41-42` (`!u || !catalog.data ? <Spin/>`) and `:353` (`if (!eff.data) return <Spin />`).
- **Failure scenario:** For a 403 (an admin without `adm.grants` opening another admin's reserved account), a 404 (the user was deleted in another tab) or a 500, the drawer shows an endless spinner with no message or retry.
- **Fix:** Wrap the content in `<QueryState query={[user, catalog]}>`, and use `Panel query={eff}`, like the rest of the app.

### FE-11 — Medium — verified — Route permissions are defined twice and already disagree
- **Where:** Navigation visibility is in `app/Shell.tsx:156-195`; route guards are in `app/App.tsx:139-163`; the search targets are in `Shell.tsx:402-412`.
- **Evidence:**
  - `/scorecard` appears in the nav only when `scopeOf("dc.results","V") === "A"`, but the route guard allows any `dc.results:V`.
  - Global search links colleges to `/structure/:id` when the user has `org.units:V`, but that route requires `org.structure:V`. A user with units but not structure clicks a search result and lands on NoAccess.
  - `/users` and `/logs` repeat the same feature lists in both files.
- **Fix:** Create one `app/routes.tsx` registry: `{ path, element, need: Need[], nav?: {group, icon, label, tab, visible?(perms)} }`. Generate both `<Routes>` and the Shell groups from it, and gate global-search groups on the target route's `need`. Add a unit test asserting that every nav item's target route is reachable with the same permissions.

### FE-12 — Medium — verified — API client gaps: correlation ID dropped, raw "HTTP 5xx", indiscriminate retries, no cancellation
- **Where:** `api/client.ts:26-28, 92`; `app/App.tsx:58-60`; server `Startup/ApiSetup.cs:149-150` returns `{status, code, title, traceId}` and documents that the traceId "appears to the user".
- **Evidence:**
  - `ApiError` keeps only `status`, `code` and `title`, so `traceId` is discarded. A 500 tells the user to "contact support", but there is no reference to quote.
  - A non-JSON 502/504 from a proxy surfaces as the untranslated text "HTTP 502".
  - A JSON content type with an empty body throws a raw `SyntaxError`.
  - `retry: 1` applies to every query error, including 401/403/404/409, which doubles requests and delays the error UI.
  - `queryFn` never forwards react-query's `signal`, so cancelled or obsolete requests keep running (search, filters, period switches).
- **Fix:**
  - Add `traceId` to `ApiError` and show it in `QueryError` and the mutation alerts ("Reference: …").
  - Map `status >= 500` with no body to a localised message.
  - Guard `r.json()`.
  - Set `retry: (n, e) => !(e instanceof ApiError && e.status >= 400 && e.status < 500) && n < 1`.
  - Accept an optional `{ signal }` in `get` and pass it from the query hooks (FE-7).

### FE-13 — Medium — verified — No lint and no interaction-level tests for the riskiest flows
- **Evidence:**
  - There is no ESLint config or dependency, yet `App.tsx:17` carries an `eslint-disable` comment. Hook dependency arrays are deliberately partial (`useValueAutosave.ts:215, 233`; `PillarBoard.tsx:198, 253`) with nothing to flag accidental ones.
  - Vitest runs with `environment: "node"`, and there is no jsdom or Testing Library.
  - The 11 test files cover pure models, the API client, SaveQueue and design tokens, plus one static-markup test. Nothing covers `useValueAutosave` (the React integration), FormPage submit/approve, `Guard`/nav permissions, session expiry, or the CSRF retry under concurrency.
  - There are no E2E tests.
- **Fix:**
  - Add `eslint` + `typescript-eslint` + `eslint-plugin-react-hooks` (rule `exhaustive-deps` as a warning, with justified inline disables) and wire it into `npm run lint` and CI.
  - Add `jsdom` + `@testing-library/react` for: the autosave hook (pending drafts survive a 401, conflict resolve), FormPage submit with `flushAll`, Guard and the nav registry (FE-11), and the reloadSession cache clear (FE-1).
  - Add a small Playwright smoke test (login → form entry → submit → approve) against the real API.

### FE-14 — Low — verified — The users list is silently truncated at 500
- **Where:** Server `Access/UsersAdminService.cs:66` (`Take(500)`); client `features/users/UsersPage.tsx:19, 24`, where the "Accounts" stat equals `users.data.length`.
- **Failure scenario:** With more than 500 staff, the page reports "500 accounts" and some users can only be found by searching. Nothing tells the admin that the list is partial.
- **Fix:** Return `{items, total}` with paging (as LogsPage does), or at least a `truncated` flag that the UI shows.

### FE-15 — Low — verified — Build artifacts and tool files are committed
- **Evidence:** `git ls-files` lists 256 files under `web/.scratch-indicator-groups-build/` and `web/.scratch-results-scroll-build/` (about 8.6 MB of hashed JS/CSS/fonts), plus `web/.Codex/orchestrator.md`.
- **Fix:** `git rm -r --cached` them and add `web/.scratch-*/` and `web/.Codex/` to `.gitignore`.

### FE-16 — Low — verified — Formatting and visual rules are duplicated across features
- **Evidence:**
  - An identical `fmt(v, unit)` exists in `ProgressPage.tsx:21`, `ReviewPage.tsx:41` and `ScorecardPage.tsx:49`.
  - `Ring` exists twice (`ScorecardPage.tsx:113`, `PlanMap.tsx:18`) and `Bar` three times (`ScorecardPage.tsx:125`, `ProgressPage.tsx:130`, `forms/parts.tsx:11`).
  - The achievement tone thresholds 100/90/70 are repeated in `ScorecardPage.tsx:128, 135, 415` and `ProgressPage.tsx:214`.
  - The RAG thresholds 85/70 (`ScorecardPage.tsx:50`) re-implement the backend `ScorecardRules.RagOf`, even though `PillarScore.rag` is already sent by the server.
- **Fix:** Add `shared/score.ts(x)` with `formatValue`, `scoreTone`, `<ScoreBar>` and `<Ring>`. Have the server send `rag` per indicator, or derive it from one shared helper.

### FE-17 — Low — verified — Features import each other's internals
- **Evidence:**
  - `users/UserDrawer.tsx:11` imports `ReasonModal` from the **page module** `matrix/MatrixPage.tsx:242`.
  - `progress/ProgressPage.tsx:12` imports `strategy/PillarCarousel`.
  - `home/AdminAnalytics.tsx:18-19` imports `strategy/model` and `strategy/PlanMap`.
  - `review` and `scorecard` import `pillarNum` from `strategy/model`.
- **Impact:** These create chunk coupling (opening Users pulls in Matrix page code) and hidden contracts between features.
- **Fix:** Move `ReasonModal` next to `DecisionModal` in `shared/`, `PillarCarousel` to `shared/`, and `pillarNum` to `shared/pillars.tsx`. Expose cross-feature widgets through a feature `index.ts` (its public API).

### FE-18 — Low — verified — Server state is copied into local state without resync
- **Where:** `ReviewPage.tsx:96` (`seenCycles` copied from `r.cycles` in an effect) and `ReviewPage.tsx:337` (`useState(row.computed)` in `Detail`).
- **Failure scenario:** While a Detail panel is open, another college's approval changes `row.computed`. The input keeps the old value, so `adjusted` becomes true and the officer is asked for a "manual adjustment" note for a value they never changed.
- **Fix:** Reset with `key={row.computed}` on `Detail`, or track "user edited" separately. Derive `seenCycles` from `useQuery` data (with `keepPreviousData`) instead of an effect.

### FE-19 — Low — likely — Decision modals can double-submit with the Enter key
- **Where:** `shared/DecisionModal.tsx:16-22`. `confirmLoading` disables the OK button, but pressing Enter in the `Input` (decisionRef) calls `form.submit()` → `run.mutate()` again while the first request is still pending.
- **Fix:** In `onFinish`, return early when `run.isPending`.

### FE-20 — Low — verified — Inconsistent URL-state handling and module-level mutable state
- **Evidence:** Three patterns coexist: `useSearchState`/`usePeriodParams` (Forms, Review), raw `useSearchParams` with hand-built `URLSearchParams` (PillarBoard, IndicatorsPage, ScorecardPage), and local state only (`UsersPage` search, `ReviewPage` filter `state`/`q`, which are lost on back-navigation). `PillarBoard.tsx:42` keeps `let lastTabAt` at module scope, written by a document listener.
- **Fix:** Standardise on `useSearchState` for filters and selections that should survive navigation, and move `lastTabAt` into a ref or a small `useLastTabTime()` hook.

### FE-21 — Info — Re-render hot spots and virtualization
- No list in the app is unbounded on the client except Users (capped at 500, FE-14). Logs are server-paged. Forms, review, scorecard and the pillar board render one pillar at a time (tens of rows), so virtualization is **not** recommended now.
- The measurable hot spot is FE-4. Second, `PillarBoard` rebuilds `ctx` (`PillarBoard.tsx:343`) and every `Item` on each focus change. Profile with React Profiler before adding `memo`.

---

## Oversized components — concrete split boundaries

**`features/strategy/PillarBoard.tsx` (982 lines)**
- `pillarBoard.model.ts`: the pure helpers `findItem`, `linksOf` and the text helpers (lines 75-158). Unit-test them like `strategy/model.test.ts`.
- `usePillarBoardUrl.ts`: the pillar/focus URL sync and the Esc handler (lines 179-236), plus the `lastTabAt` hook.
- `BoardLayout.tsx`: `Board`, `BoardBar`, `Columns`, `Column`, `SplitBody`, `Panels` (lines 334-662).
- `StandardBody.tsx`: `TypeToggle`, `StandardMenu`, `StandardBody` (lines 663-782).
- `BoardItem.tsx`: `Item`, `LinkedMenu`, `ResultChip` (lines 783-982).
- Replace the local `pillarMap` (173-177) with `usePillarMap()` or a pure `pillarMapOf(tree)` in `shared/pillars`.

**`features/scorecard/ScorecardPage.tsx` (593 lines)**
- Keep the page shell (header, tabs, QueryState) in `ScorecardPage.tsx`.
- `views/TierView.tsx` (+ `Gauge`, `PillarCard`, `Furthest`), `views/PillarsView.tsx` (+ `PillarPanel`, `PillarStandards`), `views/PssView.tsx`, `views/SdgView.tsx`, `views/TrendView.tsx` (+ `TrendChart`), and `IndicatorDrawer.tsx`.
- Move `Ring`, `Bar` and `cellTone` to `shared/score.tsx` (FE-16).

**`features/forms/FormPage.tsx` (445 lines)**
- `useFormPage(id)`: the query, `onSaved`, the mutations and the centralised invalidation (FE-6).
- `usePillarNavigation(pillars, autosave)`: `goPillar`, `scrollTo`, swipe and `goToMissing` (lines 92-157).
- `FormActionDock.tsx` (lines 291-342).
- `FormDialogs.tsx`: `SubmitModal`, `ApproveModal`, `ExtendModal`, plus the return/reopen `DecisionModal` wiring (lines 344-445).

**`features/review/ReviewPage.tsx` (447 lines)**
- `useReviewPeriod()`: the cycles query, `usePeriodParams` and the cycle fallback (lines 66-87).
- `PillarHeader.tsx`, `IndicatorRow.tsx`, and `TransferDetail.tsx` (`Detail`, lines 333-447, with the FE-18 fix).

**`app/Shell.tsx` (426 lines)**
- `app/routes.tsx` registry (FE-11), `SideNav.tsx` (325-395), `GlobalSearch.tsx` (397-426), and `useRailState()` (rail and open-group localStorage, 34-76 and 127-143).

---

## Refactoring plan (incremental, behaviour-preserving, ordered)

Each step is one PR with build, typecheck, test and lint green. Bug fixes (FE-1/2/3/5/10/12/18/19) ship as separate PRs from pure refactors, as the architecture-refactoring skill requires.

0. **Safety net.** Add ESLint (react-hooks) and jsdom + Testing Library. Write characterisation tests for: FormPage autosave → submit; the approve dialog; Guard and nav visibility per role; the reloadSession behaviour; the ReviewPage transfer flow.
1. **Fix the security and data-loss bugs first** (separate PRs): FE-1 (clear the cache on identity change), FE-2 (passive polling), FE-3 (global 401 → re-login overlay, no `key={lang}` remount), FE-10, FE-5.
2. **Housekeeping, no behaviour change:** remove the committed scratch builds and `.Codex` (FE-15); delete the dead shadcn layer, unused hooks/lib, the conflicts pages and orphaned dependencies (FE-8); pin `lottie-react`.
3. **Query layer:** add `features/<f>/api.ts` with key factories and hooks (FE-7). Migrate key by key: `cycles` → `strategy` → `results` → `units` → `forms`/`form` → the rest. Then add the domain-event invalidation helpers (FE-6) and route modal `api.post` calls through `useMutation`.
4. **API client hardening:** `traceId`, retry policy, `signal`, non-JSON errors (FE-12).
5. **Route registry:** create `app/routes.tsx` as the single source for routes, nav, tabs and search gating (FE-11), and split the Shell.
6. **Shared visual primitives:** `shared/score.tsx` and `shared/score.css` (FE-16, which also closes FE-5); move cross-feature widgets to `shared/` (FE-17).
7. **Split the large components** along the boundaries above, one file per PR, moving code first and then editing it (separate commits).
8. **Performance:** the FE-4 contract change (PUT returns the summary) and FE-9 (lazy Lottie). Measure before and after with Profiler, Network and Lighthouse; claim no gains without numbers.
9. **Optional tightening:** `noUncheckedIndexedAccess` in tsconfig, and fixing the resulting `!.` sites (101 non-null assertions in `features/`).

---

## Extension readiness — template for a new feature module

```
src/features/<feature>/
  index.ts              // public API: default page export(s) + widgets other features may use
  api.ts                // keys + hooks: <feature>Keys, use<Thing>(), useSave<Thing>() — the ONLY place with URLs
  types.ts              // DTOs if not shared (otherwise src/api/<domain>.ts)
  model.ts              // pure logic (grouping, derivations) — no React
  model.test.ts
  <Feature>Page.tsx     // orchestrates: hooks + layout + dialogs; no fetch, no business rules
  components/           // presentational parts (memo only after profiling)
  <feature>.css         // classes prefixed with a feature prefix; shared looks go to shared/*.css
  <Feature>Page.test.tsx  // jsdom + Testing Library: loading/empty/error/403 + main action
```

Conventions:
1. **Data:** never call `api.*` from components. Use `api.ts` hooks with key factories (`[feature, "list", params]`, `[feature, "detail", id]`). Set `staleTime` in the hook. Pass `signal` through.
2. **Mutations:** use `useMutation` in `api.ts`. `onSuccess` calls the shared domain invalidation helper (for example `afterFormDecision(qc)`). Optimistic updates only with rollback in `onError`. Send `version` for concurrency on every state change.
3. **States:** wrap reads in `QueryState`/`Panel query`/`AccordionPanel query`. Handle empty and first-use with `EmptyNote`. Never use a bare `<Spin/>` on `!data`.
4. **Permissions:** register the route once in `app/routes.tsx` with `need: ["feature.code:V"]` and nav metadata. Gate in-page actions with `usePermissions().can(...)` from the server's `actions` flags where they exist. The server stays the authority.
5. **URL state:** filters, tabs and selection go through `useSearchState`, so back/refresh/deep links work.
6. **UI kit:** antd components plus design-system tokens (`--ds-*`, `data-theme`). Do not add a second component library. Text goes through `L(ar, en)`.
7. **Forms:** antd `Form` with rules that mirror (but do not replace) server validation. Show `errorText(e)` plus `traceId`. Disable while pending and guard `onFinish` against re-entry. Long forms autosave through `SaveQueue`.
8. **Tests:** model unit tests, one page test per permission state, and a Playwright smoke test when the feature is a core journey.
9. **Lazy route:** `const XPage = load(() => import("../features/x/XPage"))` in the registry. Keep heavy libraries (charts, animation) inside the feature chunk.
