# Tier1 web: UX, UI and accessibility review

Scope: `web/src` (React 18, antd 5, Navy & Gold design system), read from code only. Nothing was run: no browser, screen reader, phone, or contrast meter. Every finding below is **inferred from code**. Treat each one as "needs to be checked in the browser" before you call it fixed.
Method: the skills ux-design, ui-design and ux-accessibility-review (task journeys, 3-click access, forms, states, tables, containers, RTL, WCAG 2.2 AA), plus the project skill navy-gold-design-system.
Target: WCAG 2.2 AA.

## Overall picture

The app is above average for an antd admin app:
- Navigation is filtered by permission (`Shell.tsx:156-198`), and routes are guarded with a "no access" page (`App.tsx:103-110`).
- The shared states are consistent: `QueryState`, `QueryError` (403 and 404 are told apart), and `EmptyNote` with a next action.
- RTL is almost completely logical: one physical `text-align:left` (`styles/app.css:212`, on LTR JSON, which is fine) and one centring `left:50%`.
- Numbers and dates follow one policy: `format.ts`, `ar-EG-u-nu-latn`, Cairo time zone.
- The form autosaves, with a status for each row, conflict handling and a submit preview.
- The review flow has a stepper, history, reject-with-reason and reopen-with-reason.
- The sidebar, the PillarBoard and the form tabs have solid ARIA.

The remaining problems are mostly at the edges: session expiry, evidence, incomplete drafts, closing dialogs, keyboard access to tables, and labels.

## 3-click access (from Home after sign-in, desktop; menu opening counted)

The sidebar is an accordion: a link outside the open group costs one extra click.

| Role | Core task | Path | Clicks | Result |
|---|---|---|---|---|
| Data entry (college or department) | Open the waiting form and enter values | Home › "Forms waiting for you" row | 1 | OK |
| Data entry | Correct a returned form | Home › task card › form › filter "Review notes" | 2–3 | OK |
| Dept rep, college committee, dean, central quality | Approve or return a form | Home › form › Approve or Return (opens a dialog) | 2 (+dialog) | OK |
| Tier-1 file officer | Transfer one indicator to the university | Sidebar group › Review › pillar panel › indicator row › Transfer | 4–5 | **Over the limit** (UX-7) |
| Tier-1 file officer | Transfer all ready values | Sidebar group › Review › "Transfer all ready" | 2–3 (+confirm) | OK |
| Plan follow-up manager | Open or close a cycle | Group › Cycles › Open or Close | 2–3 | OK |
| Plan follow-up manager | Extend one unit's deadline | Group › Forms › Units overview tab › unit › Extend | 4–5 | Over the limit (low frequency) |
| President, VPs, Tier-1 committee | University scorecard and tier | Home "University scorecard" panel › link | 1 | OK |
| Dean | College progress | Group › College progress | 2 | OK |
| Sysadmin | Give a role to a new user | Users › New account › (close dialog) › search › row › Roles tab › add | ≥6 | **Over the limit** (UX-11) |
| Auditor | Search the logs | Group › Logs | 2 | OK |
| Viewer | Results | Group › Results | 2 | OK |

On phones the bottom tab bar holds up to 5 links marked `tab:true` (`Shell.tsx:253`). Review, Cycles and Users are not in it, so on a phone they cost the burger, then the group, then the link (3 clicks).

---

## Findings (most severe first)

### UX-1 · High · confidence medium — A session that expires mid-task fails silently and loses work
- **Where:** `web/src/api/client.ts:83-93`; `web/src/shared/session.tsx:23`. No 401 handler anywhere in the code (grep `401` finds only the session bootstrap).
- **Evidence:** a 401 after the 30-minute idle timeout becomes `ApiError(401, …, title ?? "HTTP 401")`.
  - The form row shows "لم يحفظ: …" (not saved) with a Retry button that can never work (`ValueRow.tsx:154-169`).
  - Approve, return and transfer dialogs show the same raw text.
  - Nothing sends the user back to sign in.
- **Impact:**
  - Data entry users and reviewers on long forms lose typed values and repeat their work.
  - Error messages say nothing about the session ending (the skill says to tell an input error, a permission refusal and an expired session apart).
- **Fix:**
  - In `finish()`, on 401 for any call other than `/api/auth/me`, publish a `sessionExpired` event.
  - In `SessionProvider`, show one modal: "انتهت جلستك، سجّل الدخول للمتابعة" (your session ended; sign in to continue), with a sign-in form in place, or a redirect to `/login?next=<path>`.
  - Keep the unsaved drafts: the autosave queue keeps them in memory; re-run `flushAll()` after the user signs in again.
  - Optionally warn 2 minutes before the idle timeout.

### A11Y-1 · High · confidence high — Indicator dictionary and Results rows open by mouse only
- **Where:**
  - `web/src/shared/ui.tsx:221` (`onRow → onClick`, no tabindex and no key handler on desktop);
  - used by `features/indicators/IndicatorsPage.tsx:119` and `features/results/ResultsPage.tsx:99`;
  - their columns contain no link or button (`IndicatorsPage.tsx:122-145`, `ResultsPage.tsx:102-125`).
  - The card mode for narrow screens does handle Enter and Space (`ui.tsx:204-211`), so only desktop is affected.
- **Impact:** keyboard and screen-reader users cannot open an indicator card (plan manager, committee) or a result's detail drawer (president, VPs, deans). Severity is critical for those users. Global search is only a partial workaround.
- **Fix:** in `DataTable`, when `onRowClick` is set, render the first data column as a real link or button. Follow the `UsersPage.tsx:51` pattern: `<Button type="link" className="cell-link">`, or `<Link>` for navigation. Keep the row click for mouse users. Add a test that asserts the focusable element exists.

### UX-2 · High · confidence high — Evidence upload: no way to remove a file, no limits shown, and the button is hidden without explanation
- **Where:** `web/src/features/forms/ValueRow.tsx:132` (`editable && !!item.value`) and `:212-248`.
- **Evidence:**
  - Files can be added but never voided or replaced from the form. Voided evidence exists in the data (`e.voided`), but nothing in the UI sets it.
  - Allowed types are only in the `accept` attribute. The 20 MB limit (stated in `docs/user-guide.md:21`) is never shown and not checked before upload.
  - The upload error is a plain `<span>` with no live region.
  - "إرفاق دليل" (attach evidence) appears only after a value is saved, with no hint why. The row meanwhile says "الدليل مطلوب" (evidence required).
- **Impact:** a data entry user who attaches the wrong file cannot fix it, and the reviewer sees the wrong evidence. A large file is uploaded in full before it is rejected.
- **Fix:**
  - Add a "إبطال" (void) action on each attached file, with a reason, using the void pattern from `StructureDocs`.
  - Add `beforeUpload` checks for size and type, with an Arabic message ("الملف أكبر من 20 ميجابايت" — the file is larger than 20 MB).
  - Show a one-line hint under the button: types and size.
  - Put the upload error in `role="alert"`.
  - Before a value exists, show the disabled button with a hint: "أدخل القيمة أولًا ثم أرفق الدليل" (enter the value first, then attach the evidence).

### UX-3 · High · confidence medium — Incomplete values are dropped silently; leaving inside the app has no warning
- **Where:** `web/src/features/forms/useValueAutosave.ts:44-49` (`complete()`: reason under 5 characters, or a ratio without both parts, is never sent), `:87-97` (`beforeunload` only), `:249-256` (on unmount it flushes, and still skips anything incomplete). The app uses `BrowserRouter`, so `useBlocker` is not available.
- **Evidence:**
  - A user picks "غير متاح" (not available) and types "N/A", or fills only the numerator. Nothing is saved, and no message says why.
  - The row status keeps saying "لم يُدخل بعد" (not entered yet).
  - Clicking "النماذج" (forms) in the back link, the sidebar or global search unmounts the page and the draft is lost.
  - The browser warning only covers a reload or closing the tab.
- **Impact:** data entry users believe a value or reason was saved. The form stays blocked or loses work.
- **Fix:**
  - Show the rule inline when a draft is not ready: "اكتب سببًا من 5 أحرف على الأقل لحفظه" (write a reason of at least 5 characters to save it), or "أدخل المقام لحساب النسبة" (enter the denominator to compute the ratio).
  - Give the row status a third state: "غير محفوظ — ناقص" (not saved — incomplete).
  - Move to a data router (`createBrowserRouter`) and use `useBlocker` while `unsaved > 0`, with a dialog listing the incomplete rows.
  - Until then, intercept the PageHeader back link and the sidebar `NavLink` clicks through an `AutosaveContext.confirmLeave()`.

### A11Y-2 · High · confidence high — Core data-entry fields are labelled only by placeholder
- **Where:**
  - `ValueRow.tsx:115-117`: the reason for "not available" or "not applicable" has a placeholder and no `aria-label`.
  - `ValueRow.tsx:120-121`: the explanation for the reviewer, required when a warning shows.
  - `ValueRow.tsx:140-141`: the reviewer's note for "needs correction".
  - 13 more single-line cases, e.g. `features/logs/LogsPage.tsx:100-112` (action, reference, IP, correlation ID), plus `IndicatorsPage`, `ResultsPage`, `ProgressPage`, `MatrixPage`, `NewIndicatorModal`.
- **Impact:** screen readers announce an unnamed edit field (WCAG 1.3.1, 3.3.2, 4.1.2). For sighted users the instruction disappears once they start typing. This affects data entry users, reviewers and auditors.
- **Fix:**
  - Give each field a visible short label: "سبب عدم التوفر" (reason not available), "سبب عدم الانطباق" (reason not applicable), "تفسير للمراجع" (explanation for the reviewer), "ما المطلوب تصحيحه" (what needs correcting).
  - At minimum, add an `aria-label` and tie the issue messages to the field with `aria-describedby`.
  - For the log filters, wrap them in `<label>` or a `Form` with `layout="inline"`.
  - Keep placeholders only for search boxes.

### A11Y-3 · Medium-High · confidence high — Why can't I send or approve? The answer is behind hover or nowhere
- **Where:** `FormPage.tsx:308-310` (the Popover listing what blocks sending uses antd's default `trigger="hover"`); `:323-328` (a Tooltip on a disabled button); `:333` (Approve is disabled while indicators are flagged, with no reason given).
- **Impact:**
  - Keyboard users cannot open the list of blockers. The user guide's FAQ itself says to hover (`user-guide.md:90`).
  - A reviewer who flagged rows sees a dead Approve button with no explanation.
- **Fix:**
  - Use `trigger={["hover","click"]}` and make the blockers a link that jumps to the first blocking row (reuse `goToMissing`).
  - Next to the disabled Approve, show the text "ألغِ تعليم المؤشرات أو أعِد النموذج" (unmark the indicators or return the form).
  - Do not rely on a Tooltip on a disabled control: show the reason as visible text in the dock.

### UX-4 · Medium · confidence high — Dialogs and drawers with typed input close on Esc or a click outside, with no warning
- **Where:**
  - 23 `<Modal>` and 9 `<Drawer>`; no `maskClosable={false}` or dirty-check anywhere (grep finds none).
  - Worst cases: `shared/DecisionModal.tsx:18` (return note, close reason, reopen reason); `features/indicators/DefinitionDrawer.tsx:59` (about 15 definition fields); `strategy/StrategyForms.tsx:28`; `cycles/CyclesPage.tsx` `CycleDrawer`; `FormPage.tsx:431` `ExtendModal`.
- **Impact:** the plan manager loses a long indicator definition. A reviewer loses a detailed return note.
- **Fix:**
  - Use a shared `useDirtyClose(form)` hook. If `form.isFieldsTouched()`, ask "تجاهل التغييرات؟" (discard changes?) before `onClose`. Otherwise set `maskClosable={false}`.
  - Following ui-design 5-ب, move `DefinitionDrawer` (a long multi-section form) to a route, `/indicators/:code/edit`.

### UX-5 · Medium · confidence high — Validation messages that hide the real rule
- **Where:**
  - `shared/DecisionModal.tsx:23`: `min: 5` with the message "هذا الحقل مطلوب" (this field is required), so typing 3 characters shows "required".
  - `review/ReviewPage.tsx:438`: Transfer is disabled until the note has 5 or more characters, with no hint.
  - `useValueAutosave.ts:47`: the same 5-character rule, never shown.
- **Impact:** reviewers and the Tier-1 officer can't tell why an action won't run.
- **Fix:** write the specific message "اكتب 5 أحرف على الأقل" (write at least 5 characters), add a live character counter (`showCount`), and show the reason next to any disabled primary button.

### A11Y-4 · Medium · confidence high — Route changes are invisible to assistive technology, and there is no skip link
- **Where:** `index.html:9` has a fixed `<title>`; no `document.title` anywhere; `app/Shell.tsx:302` has `<main>` without `tabIndex`, without focus on navigation, and without a skip link before the sidebar and top bar (about 20 tab stops).
- **Impact:** screen-reader users don't hear that the page changed (WCAG 2.4.2, 2.4.1). The browser history and tabs all show the same title.
- **Fix:**
  - Add a `usePageTitle(title)` hook, called from `PageHeader`: `${title} — منظومة Tier 1`.
  - On each `pathname` change, focus the page `h1` (`tabIndex=-1`).
  - Add `<a class="skip-link" href="#main">تخطَّ إلى المحتوى</a>` (skip to content) and give `<main id="main">`.

### UX-6 · Medium · confidence high — The account editor is a 1,100 px drawer with no URL and spins forever on error
- **Where:** `features/users/UserDrawer.tsx:39-41`. It shows `<Spin/>` whenever `!u || !catalog.data`, so a failed query never shows an error. It holds tabs, tables, a permission matrix and nested `ReasonModal`s.
- **Impact:**
  - The sysadmin gets an endless spinner when the API fails.
  - The view can't be linked to, and the browser Back button closes nothing.
  - Under ui-design 5-ب this content belongs on a page.
- **Fix:**
  - Short term: show `<QueryState query={[user, catalog]}>`.
  - Better: move it to a route, `/users/:id`, with tabs for Roles, Overrides and Effective permissions.

### A11Y-5 · Medium · confidence high — Data available only on hover, and shown by colour alone
- **Where:**
  - `review/ReviewPage.tsx:306-316`: college bars use a Tooltip on a `span` inside the row button; the tone (ok, near, low) is colour only; at most 8 colleges are shown, without a "+n" marker.
  - `scorecard/ScorecardPage.tsx:380-384`: the year cells `<i className={tone}>` show their values only in a Tooltip.
  - `ScorecardPage.tsx:373`: the row handles Enter but not Space.
- **Impact:** keyboard, touch and screen-reader users can't read per-college or per-year values (WCAG 1.4.1, 1.3.1). This hits the committee and the president's office.
- **Fix:**
  - Add a visually hidden text summary in each cell ("السنة 2: 84%" — year 2: 84%), or `aria-label` on a `role="img"` wrapper.
  - Show "+n" when the list is cut.
  - Add a small glyph or pattern for "below" so the meaning doesn't rest on colour.
  - Handle Space on rows that have `role="row"` and `tabIndex`, or put a button inside the row.

### UX-7 · Medium · confidence high — Review: a single transfer takes 4–5 clicks
- **Where:** `review/ReviewPage.tsx:134` (pillar panels start collapsed, a product-owner decision, `frontend-redesign-context.md:167`) and `:243` (each row also collapsed).
- **Impact:** the Tier-1 officer opens a pillar and then each indicator to transfer "Changed" or "Ready" rows.
- **Fix, keeping the start-collapsed decision:**
  - Make the header stats "جاهز للنقل / تغير بعد النقل" (ready to transfer / changed after transfer) buttons that filter the list and open the first match.
  - Or add "التالي الجاهز" (next ready), like "التالي الناقص" (next missing) in the form.

### UX-8 · Medium · confidence high — Tables have no sorting, and some don't scale
- **Where:**
  - No `sorter` in the whole app.
  - `UsersPage.tsx:28`: the search runs only on Enter (`onSearch`), and the whole user list is loaded and paged in the browser.
  - `IndicatorsPage` shows 9 columns plus `#` (above the 5–7 guide).
  - `DataTable` paging is client-side; `ui.tsx:184` resets the page through `resetKey` (good).
- **Impact:** the plan manager can't sort indicators by "missing items", baseline or status. Admins can't sort users by last sign-in.
- **Fix:**
  - Add `sorter` (and the server `sort` parameter where the table pages on the server) on the identity, status, date and number columns, with `aria-sort` (antd provides it).
  - Search as the user types, with a debounce.
  - Move "Conflicts" and "Missing" to the detail view or behind column choice.

### UI-1 · Medium · confidence high — The same idea has different names, and English mode shows Arabic
- **Where:**
  - Performance labels: Results "محقق/قريب/متأخر" (`HomePage.tsx:136-139`); Progress "في المستوى/دون المستهدف/لا بيانات"; Scorecard "في المستوى/دون المستوى/لا قيمة بعد" (`ScorecardPage.tsx:42-46`, Arabic only); SDG "قوي/قيد التطوير/معرض للخطر".
  - Arabic-only maps: `shared/labels.ts:10+` (`text:` literals for every status), `forms/ImportModal.tsx:14-20`, and the server fields `titleAr` / `hintAr` / `messageAr` rendered as they are (`HomePage.tsx:100`, `ValueRow.tsx:129-130`).
- **Impact:** a president comparing Results with the Scorecard sees "دون المستهدف" (below target) next to "دون المستوى" (below level) for the same state. The English UI mixes in Arabic statuses.
- **Fix:**
  - Agree one performance glossary in `labels.ts` (with `docs/frontend-enums.md`) and reuse it on every page.
  - Turn the label maps into functions of `L()`, or `{ar,en}` pairs.
  - Have the server return messages in the request language (it already receives `Accept-Language`).

### UX-9 · Medium · confidence medium — Form on phones: three fixed bars squeeze the content and can cover the focused field
- **Where:** `shared/ui.css:82` (sticky top bar, 64 px); `features/forms/form-page.css:113,158` (sticky dock: pillar bar plus a row of large buttons, above the tab bar); `shared/ui.css:301-302` (fixed tab bar, about 66 px). No `scroll-padding` is set.
- **Impact:** a data entry user on a phone, with the on-screen keyboard open, may have the input or its error under the dock (WCAG 2.4.11 Focus Not Obscured). There is also little room to read.
- **Fix:**
  - Hide `.lcp-tabbar` on `/forms/:id`.
  - Set `scroll-padding-block: 64px <dock height>` on the scroller.
  - Fold the dock into one row on narrow screens: status text plus a primary button, with the rest in a "⋯" menu.
  - Check at 320 px and 375 px with the keyboard open.

### A11Y-6 · Low-Medium · confidence high — Motion ignores "reduce motion" in places
- **Where:** `shared/AppLoading.tsx:12` (Lottie `autoplay loop` on every page load); transitions without a reduced-motion guard in `review/review.css:40`, `shared/theme.css:128,176,196`, `shared/ui.css:53,126,157`; `scrollIntoView({behavior:"smooth"})` at `FormPage.tsx:115`.
- **Fix:**
  - Use `autoplay={!prefersReducedMotion()}` and `loop={false}` under reduced motion, or show a static frame.
  - Wrap the transforms in `@media (prefers-reduced-motion: no-preference)`.
  - Use `behavior: reduced ? "auto" : "smooth"`.

### UX-10 · Low-Medium · confidence high — The user guide and the UI have drifted apart
- **Where:**
  - `docs/user-guide.md:51`: "سجل التعارضات" (conflicts log) for the plan manager, but the route was removed (`App.tsx:148`). `ConflictsPage.tsx:56` still links to `/conflicts/agenda`.
  - `:21`: the 20 MB limit, which the UI never shows.
  - `:90`: "مرر على" (hover over).
  - `:15`: "يسارًا الإدخال، ويمينًا…" (input on the left, … on the right), physical sides.
- **Fix:**
  - Update the guide in the same PR as the UI.
  - Use logical wording ("في بداية الصف / نهايته" — at the start / end of the row).
  - Delete or re-route the conflicts code.

### UX-11 · Low-Medium · confidence high — A new account needs a hunt before it can get a role; roles show as codes
- **Where:** `users/UsersPage.tsx:105-113` (the success dialog only says to assign a role in the Roles tab); `:69` (tags show `SA`, `SYS`, `AUD`… as raw codes).
- **Fix:**
  - After creation, open `UserDrawer` (or `/users/:id`) on the Roles tab.
  - Show the role names (`catalog.roles[].nameAr`), with the code as secondary text.

### UI-2 · Low · confidence high — A link wraps a button in 8 places
- **Where:** for example `cycles/CyclesPage.tsx:89-90`, `indicators/IndicatorPage.tsx:73`, `scorecard/ScorecardPage.tsx:589`, `shared/QueryState.tsx:30`, `conflicts/ConflictsPage.tsx:56`.
- **Impact:** two tab stops and one invalid interactive nesting (`<a><button>`); screen readers announce it twice.
- **Fix:** `<Button href=…>`, or `<Link className="ant-btn …">`, or `navigate` in the button's `onClick`.

### UI-3 · Low · confidence high — Direction slips in the plan document viewer and the carousel
- **Where:** `strategy/PlanDocumentPage.tsx:67,71`: `RightOutlined` for Previous and `LeftOutlined` for Next, whatever the language, so the arrows are wrong in English; Next is never disabled on the last page. `strategy/PillarCarousel.tsx:298`: "واصل التمرير لأسفل" (keep scrolling down) next to a sideways arrow.
- **Fix:**
  - Use the `Pager.tsx:103-104` pattern (`lang === "ar" ? … : …`).
  - Use the page count from the PDF to disable Next on the last page.
  - Use `ArrowDownOutlined` for "scroll down".

### UI-4 · Low · confidence high — Design-system leftovers (overall compliance is good)
- **Where:**
  - Raw hex in TSX: `shared/pillars.tsx:13-20` (pillar palette) and `scorecard/ScorecardPage.tsx:451` (17 SDG colours). These are data colours, but they escape `tokens.test.ts` contrast checks and theme lifting.
  - Hex and `rgba()` fallbacks inside `var()`: `app/nav.css` (22 hex, 11 rgba), `shared/ui.css:10-23`, `styles/app.css:7-16,220`. These are harmless while the tokens exist, but they hide a missing token.
  - Static inline styles: about 70 `style={{…}}` in total. Most are dynamic widths; the static ones include `FormPage.tsx:307-309` (colour, padding, `ul` sizes), `ReviewPage.tsx:430` (`width:180`), `ValueRow.tsx:47,108` and `LogsPage.tsx:101-112` (widths).
  - `.sp-judge .anticon { color: var(--gold) }` (`strategy/strategy.css:380`) is a gold icon on a light surface: likely under 3:1 for a meaningful graphic.
- **Fix:**
  - Move the pillar and SDG palettes into `tokens.ts` as data tokens (`--ds-pillar-n`, `--ds-sdg-n`) so the AA test covers them.
  - Drop the hex fallbacks once `tokens.test.ts` proves every token is defined.
  - Move the static styles into classes.
  - Use `--ds-eyebrow-aa` or the ink token for the icon.

### A11Y-7 · Low · confidence high — Small naming gaps
- **Where:**
  - `app/Shell.tsx:274`: the language button's accessible name is "EN" or "ع". Add `aria-label={L("English","العربية")}` and `lang` on the text.
  - `matrix/MatrixTable.tsx:110`: "تبديل الصف" (toggle row) without the feature name. Use ``aria-label={`${L("تبديل الصف","Toggle row")}: ${f.name}`}``.
  - `ValueRow.tsx:64`: "تعريف المؤشر" (indicator definition) is repeated for every row. Add the code: ``aria-label={`${L("تعريف المؤشر","Definition")} ${item.code}`}``.
  - `Shell.tsx:420-424`: the global search has a placeholder and `aria-label="بحث"` (search), with no hint that it needs 2 characters. Add `aria-describedby`.

### UX-12 · Low · confidence medium — Messages don't last long enough to confirm an outcome
- **Where:** `message.success` for submit, transfer and transfer-all (`FormPage.tsx:66`, `ReviewPage.tsx:104,346`); `UserDrawer.tsx` shows "تم." (done) after sensitive account actions.
- **Impact:** the toast disappears. The page does update (status tag, stepper), so this is minor, but "تم." doesn't say what changed.
- **Fix:**
  - Use specific success text ("أوقف الحساب وسُجل السبب" — account deactivated and the reason logged).
  - For submit, move focus to the updated status or stepper, which already shows the next owner.

---

## Extension readiness: the checklist every new screen should follow

**Container and route**
- [ ] Creating, fully editing, uploading, or any form with more than 3 fields is a **route** (`/x/new`, `/x/:id/edit`), not a Modal or Drawer.
- [ ] Modals are for confirmations and 1–3 field decisions only (`DecisionModal`). No modal opens from another modal.
- [ ] A Drawer shows the details of one record next to its list, and must still work by URL (`?open=id`).
- [ ] Every Modal or Drawer with input uses a dirty-close guard (UX-4). The action button names the action ("اعتماد" approve, "حذف" delete, "نقل" transfer), never "موافق" (OK).

**Page skeleton**
- [ ] `PageHeader` with `title` (it also sets `document.title`, A11Y-4), a one-line `subtitle`, at most 4 `stats`, and **one** primary action in `extra`.
- [ ] Register the route in `App.tsx` inside `<Guard need=[…]>`, add the nav item to the matching group in `Shell.tsx` (filtered with `perms.can`), add the `SECTIONS` prefix in `shared/ui.tsx`, and decide whether it gets `tab:true` for phones.
- [ ] Check the 3-click path from Home for each role that uses the screen. Add a Home task card (`/api/home` `tasks`) for anything that is "waiting for me".
- [ ] Keep state that should survive Back or sharing in the URL (`useSearchState`, `usePeriodParams`). Period pickers use `PeriodNav`.

**States**
- [ ] Data goes through `Panel query=…`, `QueryState` or `AccordionPanel query`: loading skeleton, error with Retry, 403 and 404 handled by `QueryError`.
- [ ] Two separate empty states: "nothing yet", with the next action, and "no match", with reset filters (`EmptyNote` + `FilterMeta`).
- [ ] Disabled primary actions show the reason as visible text, not only a Tooltip.
- [ ] Mutations: show the error next to the action (`Alert` in the dialog) and keep the user's input. Never claim success before the server answers. Success text says what changed and who acts next.
- [ ] Session expiry is handled globally (UX-1). Don't catch a 401 locally.

**Forms**
- [ ] Every field has a visible `label` (antd `Form.Item label`, or `<label>`). Placeholders only in search boxes.
- [ ] Rules carry a specific Arabic message that states the real limit ("5 أحرف على الأقل" — at least 5 characters, "بعد تاريخ البدء" — after the start date). Use `showCount` for text with a minimum or maximum length.
- [ ] Use `dir="ltr"` on email, code, URL and number inputs, and `className="ltr"` on displayed codes and numbers. Use `<bdi>` for user-typed mixed text.
- [ ] Autosave screens: show a status for each field, block in-app navigation while something is unsaved, and say why an incomplete value isn't saved.
- [ ] Uploads: show types and size before upload, check in `beforeUpload`, give progress and errors with `role="alert"`, and offer void or replace with a reason.

**Tables and lists**
- [ ] Use `DataTable` (shared pager, cards on phones through `renderCard`, `resetKey` going back to page 1).
- [ ] When rows are clickable, put a real link or button in the first column (A11Y-1).
- [ ] 5–7 visible columns; `sorter` on the identity, status, date and number columns. Search with a debounce; state whether the search is server-wide or limited to the page.
- [ ] Status is shown as text plus colour (`Tag` with a label from the shared glossary in `labels.ts`, UI-1).

**Design system (navy-gold skill)**
- [ ] Colours only through `--ds-*` and `var(--…)`, or antd tokens from `antd-theme.ts`. No hex or `rgb()` in TSX or CSS. Data palettes live in `tokens.ts`.
- [ ] Radius only through `--ds-radius-*` or `rounded-ds-*`. Hairlines rather than shadows. Gold means one thing per view; small gold text uses `--ds-eyebrow-aa`.
- [ ] Logical properties only (`margin-inline-*`, `inset-inline-*`, `text-align: start`, `ms`, `me`, `ps`, `pe`). Arrows follow `lang` (the `Pager.tsx:103` pattern).
- [ ] New CSS goes in a feature file. No static `style={{}}` except for data-driven values (`--pc`, widths).
- [ ] Flip all 5 palettes and 3 corner styles in the Appearance panel, and both Arabic and English, before merging.

**Accessibility (WCAG 2.2 AA)**
- [ ] Icon-only buttons have an `aria-label` naming the action **and** its object. Decorative icons have `aria-hidden`.
- [ ] Nothing is reachable only on hover: Tooltip or Popover content that carries data or reasons also appears on focus or click, or as text.
- [ ] Custom widgets use native `button` or `a`. If you must use `role="row"` or `role="button"` with `tabIndex`, handle both Enter and Space.
- [ ] Animations sit inside `prefers-reduced-motion: no-preference`. Lottie and smooth scrolling are off under reduced motion.
- [ ] Check at 320 px: no horizontal page scroll; the sticky or fixed dock does not hide the focused field.
- [ ] Add `aria-live="polite"` for counts and save status, and `role="alert"` for blocking errors. Don't announce every keystroke.

**Done means**
- [ ] Typecheck plus the vitest suites in `src/styles/design-system` pass.
- [ ] The happy path and one failure path were tried by keyboard only, in Arabic and in English, on desktop and at 375 px.
- [ ] `docs/user-guide.md` is updated for the affected role.

## Not checked (limits of this review)
- No live run: contrast was not measured, there was no screen-reader or phone test, and no real form data was used.
- The landing page (`features/landing`) was skipped, since it deliberately keeps its low-contrast pairs.
- The bodies of `Explorer`, `PlanMap`, `ResultsPage`, `ProgressPage`, `ConflictDrawer` and `UnitsPage` were only partly read.
- I did not check whether antd's `ar_EG` locale supplies Arabic default validation messages for rules without a `message` (e.g. `UsersPage.tsx:124-125`, `LoginPage.tsx:178`).
