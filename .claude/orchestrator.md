# Orchestrator ledger — Tier1 web

Task: install shadcn/ui (Radix) + Radix into `web/` with no refactor and no visual change.
Baseline: `<scratchpad>/baseline` (git status, src copy, config copies, file hashes).

- [done]    baseline-checks (bg shell) — build ok, typecheck ok, 10/10 tests pass; 4 screenshots + style fingerprints saved
- [done]    deps+config — radix-ui, cva, clsx, tailwind-merge, lucide-react, tailwindcss, @tailwindcss/vite, tw-animate-css, shadcn; vite alias+plugin; tsconfig paths; components.json (radix-nova, rtl); lib/utils.ts; styles/shadcn.css (not imported)
- [failed]  shadcn-add-all (bg shell) — `add --all` stopped at npm ERESOLVE: @shadcn/react needs @types/react>=19; nothing written
- [done]    shadcn-add-61 (bg shell) — added 61 items by name (skipped combobox=@base-ui, message-scroller+questionnaire=@shadcn/react/React19); 60 files (form is empty in radix-nova); strict tsc clean, no fixes; versions pinned exact
- [done]    final-checks (bg shell) — typecheck ok, 10/10 tests, scratch build byte-identical to baseline, shadcn.css compiles alone (no preflight)
- [done]    re-screenshots — login/home/indicators/strategy: same CSS + DOM hashes, no new console errors; git status shows only allowed files
- [done]    shadcn-install (opus, this session) — hand-over note: side-session-notes/.../shadcn-install.md

## login-accounts-extract (side session local_1667722d-7d26-4a56-a876-3b460b5abe6b) — list Tier1 login accounts from the DB, read-only

- [done]    t1-accounts-extract (opus) — tier1: 31 accounts, tier1_demo: 31; all 62 match T1_DEMO_PASSWORD offline (PBKDF2-SHA512); F:/Projects/angular/Indecators/TIER1_LOGIN_ACCOUNTS.local.md written and checked by me — done
- [done*]   t1-login-check (opus) — API uses tier1 DB; :5188 proxy reaches it; ONE attempt sent with empty password by tooling bug -> 401, sa1 failed_logins 0->1 (limit 5); corrected attempt blocked by permission system, not retried; live login NOT proven — 16:23

## tier1-migration-check (side session of local_1667722d-7d26-4a56-a876-3b460b5abe6b) — which DBs the running system uses; is the package fully moved and seeded; read-only

- [done]    mc-live-conn (opus) — :5150 = 5440/tier1/tier1_dev + 27017/tier1_logs, logging ok; :5160 = 5440/tier1_demo + 27017/tier1_demo_logs_20261113165436, 1762 outbox rows stuck (chain collision); 5150 runs on pre-e5517dc settings; re-checked by me — 17:45
- [done]    mc-pg-dumps (opus) — tier1 PASS (copied cluster; only API-written diffs). tier1_demo FAIL: dropped+rebuilt here 16:54–17:20 (demo.ps1 -Reset; demo_data.py edited 17:20:46), 6/5005 dump rows live, units 248->15; spot-checked by me — 17:43
- [done]    mc-mongo (opus) — FAIL: 0 of 31023 tier1_logs and 0 of 7392 demo export docs in Mongo 27017; demo export DB missing; dev settings point at 27031 (down); restore-here.ps1 cannot run from this layout; JSONL line counts re-checked by me — 17:44
- [done]    mc-migrations-security (opus) — 6/6 migrations both DBs; 15 roles, 19 features, SA all; tier1 FAIL: SA+PM kpi.targets caps 7 not 23 (no Delete), spu@ is DE not PM; tier1_demo PASS; spot-checked by me — 17:42
- [done]    mc-org-kpi-demo (opus) — plan tree, 92 KPIs, 460 targets PASS both; org FAIL in demo (15 units, dump had 248); demo 3 cycles ok, 10/14 stages, central units no forms, no plan PDF; KPI counts re-checked by me — 17:45
- [running] mc-files-git (opus) — restore-here.ps1 steps, data/* folders, relative paths, wwwroot, .env, git vs 0d9a874 — started 17:35
- [note]    appsettings.Development.json tier1_dev password fails on 5440; .env POSTGRES_PASSWORD works — sent to mc-live-conn and mc-files-git — 17:42

## landing-ds (side session of local_1667722d-7d26-4a56-a876-3b460b5abe6b) — port Home.jsx landing to Tier1 + multi-theme Navy & Gold tokens + skill

Shared brief: side-session-notes/local_1667722d-.../landing-ds-brief.md. Pre-existing dirty: .claude/orchestrator.md, tools/demo_data.py.

- [done]    ds-tokens (opus) — 7 files in web/src/styles/design-system/ + notes/hex-map.md; 213 colour uses mapped; tsc ok; Tailwind 4.3.5 compile ok; data-section=dark|light scheme; a11y fails kept for exactness — 18:17
- [done]    baseline-shots (opus) — toolkit scratchpad/landing/tools (capture.mjs CDP, diff.py, font-cache); 9 pages baselined; 3 runs identical except sec06 text noise <=0.041%; planner login 2/2 ok; only console error = expected 401 /api/me signed out — 18:24
- [running] ds-scrollbar (opus) — added by parent 2026-11-13: global glassy scrollbar, tokens --scrollbar-* (:root navy/teal; navy-gold gold; app-soft own), styles/design-system/scrollbar.css + one import line; started 18:26
- [running] landing-port (opus) — Part B: features/landing TS port, landing.css, Cairo fontsource, logo, App.tsx route; self pixel-diff vs baseline — started 18:20
- [running] ds-skill (opus) — Part C: .claude/skills/navy-gold-design-system (+ ~/.claude copy) — started 18:20
- note 18:30: tier1-pager-logo session edits shared/ui.tsx, shared/ui.css, app/Shell.tsx — expected diffs on antd pages; not ours

## tier1-pager-logo (side session of local_1667722d-7d26-4a56-a876-3b460b5abe6b) — shared pager redesign + university logo in signed-in shell

- [running] pager-redesign (opus) — new shared Pager (prev · numbers · next, ellipsis, aria-current, 40px touch), merged row options, DataTable + LogsPage chain + ImportModal on one look; owns shared/ui.tsx, shared/Pager.tsx, shared/pager.css — started 18:40
- [done]    shell-logo (sonnet) — sidebar: capu-logo-light.webp 44px high, white tile removed; top bar: new capu-logo-dark.webp (62×96, 5.6 KB, transparent, from Frontend/React/public/images/capitalLogo.png) 40px high; alt L(جامعة العاصمة); text kept; typecheck ok; checked by me headless 1440+375 ar/en: brand row 65px, top bar 58px, no h-scroll; I added nowrap+flex:none to .lcp-top-brand ("Tier 1" wrapped at 375) — 18:58
- [pending] verify (me, browser pane) — screenshots Forms/Indicators/Results/Conflicts/Logs, ar+en, desktop+375, shell logo; typecheck/test/scratch build
