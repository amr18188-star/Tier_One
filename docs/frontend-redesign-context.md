# Tier 1 frontend redesign: context, mission, plans and what was built

Last updated: 2026-11-14. Scope: `web/` (the React + antd frontend) of the Tier 1 strategic-performance system for Capital University. The backend (`src/Tier1.*`) was not changed by this work.

This file is the hand-over for anyone (a developer or an AI session) who continues the UI/UX work. It explains how the work was run, the decisions the product owner made, what was implemented where, and what is still open.

---

## 1. Mission and how the work was run

- **Goal:** improve the UI/UX of the whole Tier 1 frontend without breaking behaviour. The work started as "style only", then grew into page redesigns that the product owner approved one by one.
- **How:** one "master chat" (in Arabic) planned the work and handed each task to a separate Claude Code session, then reviewed and relayed the results. Each session used:
  - the `orchestrator` skill: background agents per sub-task, with a ledger in `.claude/orchestrator.md`;
  - `ui-ux-pro-max`: UX rules, colour and typography research, a11y checklists;
  - the project skill `navy-gold-design-system` in `.claude/skills/navy-gold-design-system/` (also in `~/.claude/skills/`): the design-system rules for any later styling work.
- **Ground rules every session followed:**
  - Tokens only: no hex, `rgb()`, `#fff` or fixed radii in new UI.
  - Arabic first (RTL) with English (LTR), using logical CSS properties.
  - Never start or stop the servers without the owner's OK.
  - Never commit or push without the owner's OK. The team commits to `phase-1-foundation` themselves.
  - Never change the backend, the data or the seeding.
  - Run `npx tsc -b` and `npx vite build --outDir <scratch>`. Plain `npm run build` overwrites `src/Tier1.Api/wwwroot`, which the running API serves.

## 2. Runtime environment (dev)

| Piece | Where | Notes |
|---|---|---|
| Frontend (Vite) | http://127.0.0.1:5188 | `npm run dev -- --host 127.0.0.1` in `web/`. Use 127.0.0.1, not localhost (IPv4 only). |
| API | http://127.0.0.1:5150 | Normally run by Codex (`dotnet run`, launchSettings Development). Fallback: `F:\Projects\angular\Indecators\TIER1_DEV_API_START.local.ps1`, which runs detached with seeding off and uses the user secrets for the connection strings and paths. |
| PostgreSQL | 127.0.0.1:5440, DB `tier1` | The only Tier 1 database now. `tier1_demo` was dropped after a backup (see §8). |
| MongoDB (logs) | 127.0.0.1:27017, DB `tier1_logs` | The tracked `appsettings.Development.json` still says 27031, which is wrong locally. |
| Demo accounts | `*@tier1.local` (31 accounts, 14 roles) | The password is `T1_DEMO_PASSWORD` in `.env`. It can change when the DB is re-seeded, so always re-read it. Super admins: `sa1@`, `sa2@`. |

The .NET SDK 10 that `global.json` pins is not installed on the dev machine. The API runs with SDK 11.

## 3. Design system (`web/src/styles/design-system/`)

- **Themes (palettes):** 5 palettes, all defined by the same `--ds-*` tokens:
  - `navy-gold` (default);
  - `app-soft`;
  - `night-gold` (dark mode, with the antd dark algorithm);
  - `indigo-silver`;
  - `graphite-gold`.
  Raw palette values are in `primitives.css`; semantic tokens are in `themes/*.css`.
- **Corners:** one axis, independent of the palette:
  - `square` (default): every radius 0;
  - `rounded`: 4 / 6 / 8 / 12 / 16;
  - `smooth`: 8–24, with pill buttons.
  The scale is `--ds-radius-xs|sm|md|lg|xl|pill|round`. The build-time default is `CORNERS` in `config.ts`.
- **Runtime Appearance panel:** a gear in the top bar (next to the language toggle and the account menu) opens "المظهر / Appearance" with the palettes, the corner style and "استعادة الافتراضي".
  - The choice is saved per viewer in `localStorage` key `t1.appearance` (`AppearanceProvider` / `useAppearance` in `appearance.tsx`) and applied to `<html data-theme data-corners>`.
  - The public landing page stays on `navy-gold`.
- **antd:** `ConfigProvider` is built from the tokens by the mapper (`antd-theme.ts`), so antd, the app CSS, shadcn and the scrollbar always agree.
- **Legacy CSS:** `styles/app.css` and `shared/ui.css` keep their brand variables (`--navy`, `--teal`, …) as aliases to the tokens.
- **Gradients:** `--ds-gradient-hero|sidebar|auth|avatar|progress|callout|surface-subtle`, plus `--ds-hatch-*` and `--ds-shade`, per theme.
- **Status, tier and chart colours:**
  - `--ds-status-*`;
  - `--ds-tier-1…5` (scorecard bands);
  - `--ds-chart-*`;
  - sidebar colours `--ds-sidebar-*` (contrast raised: links at least 7:1).
- **Scrollbar** (`scrollbar.css`): the thumb blends with the surface it sits on, at full width with no glass. Its corners follow the corner switch.
- **shadcn:** 60 Radix-based components in `components/ui`, with `styles/shadcn.css` mapped to the tokens. **Not imported yet.** To use them, add `import "./styles/shadcn.css";` to `main.tsx` after `app.css`.
- **Token audit:** 480 hard-coded colours went to 0 in scope, through 147 new tokens. What is left on purpose:
  - `var(--x, #fallback)` values;
  - the default pillar colour `--pc` in `strategy.css`;
  - the official UN SDG colours in `ScorecardPage.tsx`.
- **Docs:** `styles/design-system/README.md` explains how to add a theme in 5 steps. The skill has the same rules.

## 4. App shell (`app/Shell.tsx`, `app/nav.css`)

- **Logos:** the official university logos are in `assets/brand/`, named by background. `capital-university-logo-dark.png` has white lettering for navy backgrounds; `capital-university-logo-light.png` has navy lettering for light backgrounds. They are used in the sidebar, the top bar, the login screen and the landing page.
- **Sidebar:**
  - Categories are expandable panels: one open at a time, the group of the current page opens itself, and the open group is remembered.
  - Child links are indented by 12px.
  - The number badge on "نماذج الجهات" was removed.
- **Collapsible rail:** a 72px icon rail with one icon per category and tooltips (left in Arabic, right in English). Only the chosen category expands, and it fits 1280×720 without scrolling.
- **Footer and top bar:**
  - The sidebar footer keeps only **Sign out**.
  - Language, the account menu (which also has sign out) and the Appearance gear are in the top bar.

## 5. Shared components

- **`shared/ui.tsx` / `Pager.tsx` / `pager.css`:** one pager for every table: Previous, the page numbers, Next. Rows per page are 10–100 plus each page's own sizes, and each page keeps its old default.
- **`shared/layout.css`:** one spacing rule between filter bars and tables, 16px on desktop and 12px on mobile, using `--ds-space-*`.
- **`shared/PeriodNav.tsx`** (+ `period-nav.css`, `usePeriodParams.ts`, `period.ts`): the single year + quarter picker.
  - The year switcher comes from the plan calendar (2027–2031). The quarter strip slides, and a quarter with no cycle shows as a disabled "لا دورة".
  - Modes: `cycle`, `quarter` and `year`.
  - URL: `cycle=` + `year=`, or `year=` + `quarter=`. Old `cycle=` links still work.
  - Used on Forms, Review, Results, the cycle form and the indicator distribution panel.
- **`shared/AccordionPanel`:** the single-open accordion used by Scorecard and Review. Everything starts collapsed, with the 220ms smooth animation.
- **Numbers and dates:** Western digits everywhere. `AR_LOCALE = "ar-EG-u-nu-latn"` and `uiLocale()` are in `shared/format.ts`, and a test fails on any Arabic-Indic digit. The invisible RTL marks that scrambled dates are stripped.
- **Pillar letters:** "أ ب ج د…" are removed from every page. Pillars use the numbers 01–06 (shared helper `shared/pillars.tsx`).

## 6. Pages: what was built

### Landing page, signed out (`features/landing/`)
- A port of the Navy & Gold landing page from the older React app. It renders at `/` for signed-out visitors; `/login` and any other address show the login screen.
- Changes the owner asked for:
  - "seasons" became **quarters**;
  - no header, and the hero exactly fills the screen, with the logo in its corner;
  - a modern footer with the logo (the faculty name was removed);
  - analytics: the KPI cards are horizontal rows, the clipped "القيادة" bar is gone, and the trend chart reads as rising in RTL.
- The Tailwind utilities are scoped to `[data-landing]` (no global preflight), so the antd pages are unaffected.

### Strategy (`features/strategy/`)
- **Map** (`view=map`): a draggable 3-up pillar carousel (`PillarCarousel.tsx`, embla).
  - Every card is the same height, and pillar titles are fixed at two lines.
  - At the bottom of the page, the mouse wheel keeps moving the carousel without ever trapping the page.
- **Roadmap** (`view=roadmap`):
  - A one-pillar carousel is the filter (`pillar=` in the URL).
  - The type is indicators **or** initiatives (`kind=`); the time filter and the pillar `Select` are gone.
  - Standard rows are expandable panels, all collapsed by default, with an 8px gap between groups.
- **Structure & links board** (`view=board`, `PillarBoard.tsx`, `board.css`): this replaced the old nested "التفاصيل والتعديل" tree.
  - **Columns:** the pillar carousel is the filter, and each standard is a column with an "المؤشرات | المبادرات" toggle. A pillar with a single standard shows both lists side by side when the board is at least 900px wide.
  - **Links:** clicking an item highlights the items linked to it (`InitiativeNode.indicatorCodes`). The active item has a popover of its linked items; each one opens that item's details.
  - **Details pages:** each shows one item only, with a path line back to the board.
  - All 13 create, edit and delete actions are kept; see `explore-ux-proposals/action-map.md` (outside the repo).

### Units' forms list, "نماذج الجهات" (`features/forms/FormsPage.tsx`)
- **PeriodNav** replaces the cycle select.
- **Modes by role:**
  - "مهامي" (tasks) for data entry and department reps: cards with one clear call to action, returned forms first with the reviewer's note.
  - "متابعة الجهات" (monitor) for deans, committees, PM, QA and admins: colleges grouped as expandable rows with their departments inside, approved/total and flags.
  - Users with both get a tab.
- **Filters:** the stage cards are the only status filter. Each card's number and label sit on one row. Beside them: the "مطلوب مني فقط" switch and search.

### Form entry page (`features/forms/FormPage.tsx`, `FormPillars.tsx`, `form-page.css`)
- **Summary cards:** compact pillar cards, 6 per row at 1440px and up, 4 at 1280, 3 at 1024, and a scroll strip on mobile. Each card opens its pillar (`pillar=` in the URL).
- **The pillar panel:**
  - One wide pillar at a time (up to 1200px), with an analysis header band: entered/total, missing, notes and readiness.
  - Sticky Previous/Next buttons at the middle of the screen; a bottom bar on mobile.
  - Drag or swipe switches pillars, but never when the gesture starts in a field and never on a vertical gesture. The drag is clipped to its own area, so it never covers the sidebar.
- **Autosave while typing** (`useValueAutosave.ts`, `saveQueue.ts`):
  - It saves 800ms after typing stops, and also flushes on blur, Enter, a pillar switch and submit.
  - The state is shown live: "جارٍ الحفظ…", "تم الحفظ ✓", or an error with Retry.
  - One save is in flight per field; late answers are ignored, and a conflict asks before overwriting.
  - The API call is unchanged.
- **Notes:** reviewer notes are compact (`NoteCard.tsx`): one "مطلوب التعديل:" label, clamped to 2 lines with "المزيد".

### Scorecard (`features/scorecard/`)
- The pillars view is a single-open accordion, all collapsed by default (`pillar=` in the URL), with no letters.
- The tier colours come from tokens (`--ds-tier-1…5`).

### Review of college indicators, "مراجعة مؤشرات الكليات" (`features/review/`)
- A redesign:
  - PeriodNav in `cycle` mode;
  - state chips that use a dot plus a label;
  - a "0/92 مؤشرات منقولة" summary;
  - pillars as a single-open accordion, all collapsed, each showing "منقول x/y".
- The review actions and permissions are unchanged.

### Smaller changes
- Settings: the technical key no longer shows under each setting.
- Forms: the unit kind is hidden.
- Indicator page: "في شجرة الخطة" became "في الخطة الاستراتيجية".

## 7. Product-owner decisions (keep these)

1. **Visual direction:**
   - Navy & Gold is the identity and the default theme, with square corners by default.
   - Palettes and corners are switchable at runtime by each viewer.
2. **Building new UI:**
   - Build on tokens only, so every palette and corner style keeps working.
   - Remove pillar letter numbering everywhere; use 01–06.
3. **Interaction patterns:**
   - Every quarter or year picker uses `PeriodNav`.
   - Expandable pillar panels are single-open and start collapsed.
4. **Strategy:** the board replaces the nested tree, and details pages show one item only.
5. **Language and numbers:** Arabic UI with Western digits (1 2 3).
6. **Running the API:** keep Codex's API setup. If the owner asks, start the servers with the start script above.

## 8. Data and infrastructure notes (from the checks on 2026-11-13)

- **Re-seeding:** Codex re-created the `tier1` database at about 21:00 on 2026-11-13 and re-seeded it. The data from before that time is gone. The closest copies:
  - `نقل-المنظومة/backups/tier1.dump`;
  - `data/backups/pre-reset-tier1-*.dump`.
- **Cleanup, approved by the owner:** the old demo API on :5160 was stopped. Then, each after a verified backup in `data/backups/`:
  - `tier1_demo` was dropped;
  - an old `tier1` DB on :5432 was dropped;
  - old Mongo log DBs were dropped;
  - a duplicate project copy was archived.
- **Mongo logs:** the JSONL exports in `نقل-المنظومة/backups/mongo` were never imported.
- **Removed scripts:** the team removed `scripts/*.ps1` and most of `tools/*.py` on 2026-11-13. README steps that use them no longer work.
- **Security:** the DB dumps and Mongo exports in `نقل-المنظومة/backups` are tracked in git and on GitHub, and the demo password appears in a few tracked docs and settings. Check the repo's visibility.

## 9. Open items and follow-ups

- **In progress when this was written:** the 5 contrast fixes the owner approved:
  - pillar chips in the page band;
  - antd success/warning and hint text;
  - chart axis labels;
  - the raw input on Scorecard;
  - translucent text on pillar fills in night-gold.
- **Not committed yet:** commit and push to `phase-1-foundation` are waiting for the owner's OK.
- **Known small points:**
  - **Strategy board:** the "No pillar" slide and the "+n in other pillars" badge exist, but can't show with today's data (no cross-standard links).
  - **Unused code:** `horizonKind` in `features/strategy/model.ts` can be deleted.
  - **Conflicts:** the page has no route (removed in `0d9a874`), but it already uses the shared pager and stage cards.
  - **Target-plan inputs on mobile:** they are 30px tall, below 44px.
  - **Form entry at 1024px:** the pillar panel is about 460px wide because of the side buttons.
  - **Landing a11y:** a few low-contrast gold/grey texts were kept on purpose to match the original. The fix in item 1 above covers the app, not the landing page.
  - **shadcn:** the CSS is mapped but not imported.
- **Reference artefacts** (outside the repo, in `F:\Projects\angular\Indecators\`):
  - `explore-ux-proposals/`: the strategy UX audit, the 3 proposals and the action map;
  - `forms-ux-proposal/index.html`: the approved forms prototype;
  - `TIER1_MIGRATION_CHECK.local.md`;
  - `TIER1_LOGIN_ACCOUNTS.local.md`: local only, it contains credentials, never commit it.

## 10. Session map (Claude Code sessions used)

| Session | What it did |
|---|---|
| `tier1-shadcn-kit` | Installed Tailwind v4 and the 60 shadcn/Radix components with no UI change. |
| `tier1-login-accounts` | Listed every DB account and role, with passwords checked offline. |
| `tier1-migration-check` | Read-only check of the DB connection, the migration package and the seed. |
| `tier1-runtime-clean` | One API and one DB, the user-secrets start script, the cleanup and the outage diagnosis. |
| `tier1-landing-ds` | Landing page, design system, themes, corners, gradients, scrollbar, shadcn mapping, antd mapper, Appearance panel, token audit, the Strategy map and roadmap carousels, sidebar contrast. |
| `tier1-pager-logo` | Pager, logos, sidebar accordion and rail, spacing, the Forms list redesign, PeriodNav, letter removal, the form entry page. |
| `tier1-latin-digits` | Western digits and the date fixes. |
| `tier1-explore-ux` | Strategy UX proposals, the Structure & links board, single-item details, the Scorecard and Review accordions, the Review redesign. |
