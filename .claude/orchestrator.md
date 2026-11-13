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
- [done]    ds-scrollbar (opus) — styles/design-system/scrollbar.css + main.tsx:9 import; 14 tokens, per theme + dark sections + modal overlay; antd .ant-table scrollbar-color reset (Chromium); typecheck/test/build ok; 57 baseline imgs: 17 diffs all logo/pager/landing/noise — 19:20
- [running] landing-port (opus) — Part B: features/landing TS port, landing.css, Cairo fontsource, logo, App.tsx route; self pixel-diff vs baseline — started 18:20
- [done]    ds-skill (opus) — SKILL.md + references/{design-system,token-files,component-recipes}.md, both copies identical, 56 paths ok, recipes compile; brand section; open: README Tailwind entry must match landing's scoped form; scrollbar placeholder — 18:45
- note 18:30: tier1-pager-logo session edits shared/ui.tsx, shared/ui.css, app/Shell.tsx — expected diffs on antd pages; not ours

## tier1-pager-logo (side session of local_1667722d-7d26-4a56-a876-3b460b5abe6b) — shared pager redesign + university logo in signed-in shell

- [done]    pager-redesign (opus) — Pager.tsx (Pager, pageItems, mergeSizes, usePaged) + pager.css (pg-*); DataTable, Logs chain days, ImportModal on it; options std 10/20/30/50/100 merged (Indicators +120, chain days +8), defaults unchanged; Logs server 25 fixed; typecheck ok, 10/10 tests, 440/440 helper checks — 18:30 (swept into commit fb6a3a7 by Mohamed-Farraj 18:30:13, not by us)
- [done]    shell-logo (sonnet) — sidebar: capu-logo-light.webp 44px high, white tile removed; top bar: new capu-logo-dark.webp (62×96, 5.6 KB, transparent, from Frontend/React/public/images/capitalLogo.png) 40px high; alt L(جامعة العاصمة); text kept; typecheck ok; checked by me headless 1440+375 ar/en: brand row 65px, top bar 58px, no h-scroll; I added nowrap+flex:none to .lcp-top-brand ("Tier 1" wrapped at 375) — ~18:27
- [done]    verify (me) — browser pane hidden most of the time (session not in a window), so full-res shots via headless Edge CDP on :5188 (scratchpad/cap/*.mjs); DOM checks per state: 1 aria-current, prev right in ar, 40px targets at 375, no h-scroll — 0 problems; typecheck, tsc -b, 10/10 tests, scratch build ok — 18:48
- [done]    my fixes after review — mobile pager buttons shrank to 32px (flex-basis ignored for intrinsic width; Indicators card only 297px): li width 40, gap 2px, ellipsis 24px; size menu cut 100/120: popupMatchSelectWidth=false; removed dead .dt-foot/.dt-size/.dt-range/.dt-nav from ui.css
- [done]    login logo (parent request) — AuthLayout: capital-university-logo-dark.png (white lettering) in navy panel; Tier1 mark dropped from card (one logo per screen); new shared/auth-logo.css shows the logo in the navy strip at <=900px (was no logo on mobile); capu-logo-light.webp deleted (no refs); tier1-mark.svg kept (favicon in index.html), favicon.png kept (unreferenced)
- [note]    Conflicts page has no route since 0d9a874 (amr18188, 11-13 14:06) — cannot be opened; it still uses DataTable so it gets the new pager
- note 18:35: original logo == web/src/assets/brand/capital-university-logo-dark.png (same MD5); landing-port told to import it, no public/ copy; ds-skill told to add brand section (dark file on light bg, light file on dark bg)
- [note]    logo follow-up from parent 18:30: official PNGs; file names mean background colour (-dark.png = white lettering, -light.png = navy), so sidebar uses -dark.png, top bar -light.png; capu-logo-dark.webp deleted
- note 18:46: logo colours measured — -dark.png = white+gold (dark bg, == original), -light.png = navy+gold (light bg); parent's mapping was reversed; skill + Shell.tsx already right
- [queued]  docs-sync — after landing-port + ds-scrollbar: README entry = landing's final form, scrollbar section in README + skill token-files.md (both copies)
- note 18:50: user wording change seasons→quarters sent to landing-port (text exception, after exact parity proof); AuthLayout.tsx logo swap by tier1-pager-logo = expected login diff
- note 18:58: footer redesign (user) sent to landing-port; uses -dark.png (white+gold) — request named -light.png, reversed again
- note 19:05: analytics changes (remove القيادة bar, horizontal KPI rows, RTL-mirrored rising trend) sent to landing-port
- note 19:22: HEAD fb6a3a7 (author Mohamed-Farraj, 18:30) + merge ed61f85 committed WIP of ALL sessions' files (landing, design-system, skill, pager, logo). Not done by this mission. git status is now relative to it.
- [queued]  final-verify — after landing-port: login via /login (toolkit's / login is now the landing)
