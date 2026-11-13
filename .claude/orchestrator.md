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
- [done]    mc-files-git (opus) — restore-here.ps1 never ran (path layout, SDK 10 missing); data/*, wwwroot, .env identical; dev settings would break restart; dumps tracked on GitHub; scripts/*.ps1 + tools deleted by team (8d0bf5d, 385632c); re-checked git by me — 19:04
- [note]    appsettings.Development.json tier1_dev password fails on 5440; .env POSTGRES_PASSWORD works — sent to mc-live-conn and mc-files-git — 17:42
- [done]    report (me) — F:/Projects/angular/Indecators/TIER1_MIGRATION_CHECK.local.md written, no secrets — 19:04

## landing-ds (side session of local_1667722d-7d26-4a56-a876-3b460b5abe6b) — port Home.jsx landing to Tier1 + multi-theme Navy & Gold tokens + skill

Shared brief: side-session-notes/local_1667722d-.../landing-ds-brief.md. Pre-existing dirty: .claude/orchestrator.md, tools/demo_data.py.

- [done]    ds-tokens (opus) — 7 files in web/src/styles/design-system/ + notes/hex-map.md; 213 colour uses mapped; tsc ok; Tailwind 4.3.5 compile ok; data-section=dark|light scheme; a11y fails kept for exactness — 18:17
- [done]    baseline-shots (opus) — toolkit scratchpad/landing/tools (capture.mjs CDP, diff.py, font-cache); 9 pages baselined; 3 runs identical except sec06 text noise <=0.041%; planner login 2/2 ok; only console error = expected 401 /api/me signed out — 18:24
- [done]    ds-scrollbar (opus) — styles/design-system/scrollbar.css + main.tsx:9 import; 14 tokens, per theme + dark sections + modal overlay; antd .ant-table scrollbar-color reset (Chromium); typecheck/test/build ok; 57 baseline imgs: 17 diffs all logo/pager/landing/noise — 19:20
- [done]    landing-port (opus) — 15 files features/landing, App.tsx Routes (!me && / → Landing; * → Login), @fontsource/cairo 5.3.0; fair-font parity 0 px all sections (sec06 noise); raw static font 0.01–0.05%; variable font = Google bytes = 0 px; utilities scoped [data-landing]; quarters wording 17 strings, new footer, analytics 3 changes; typecheck/test/build ok — 19:40
- [done]    ds-skill (opus) — SKILL.md + references/{design-system,token-files,component-recipes}.md, both copies identical, 56 paths ok, recipes compile; brand section; open: README Tailwind entry must match landing's scoped form; scrollbar placeholder — 18:45
- note 18:30: tier1-pager-logo session edits shared/ui.tsx, shared/ui.css, app/Shell.tsx — expected diffs on antd pages; not ours

## tier1-pager-logo (side session of local_1667722d-7d26-4a56-a876-3b460b5abe6b) — shared pager redesign + university logo in signed-in shell

- [done]    pager-redesign (opus) — Pager.tsx (Pager, pageItems, mergeSizes, usePaged) + pager.css (pg-*); DataTable, Logs chain days, ImportModal on it; options std 10/20/30/50/100 merged (Indicators +120, chain days +8), defaults unchanged; Logs server 25 fixed; typecheck ok, 10/10 tests, 440/440 helper checks — 18:30 (swept into commit fb6a3a7 by Mohamed-Farraj 18:30:13, not by us)
- [done]    shell-logo (sonnet) — sidebar: capu-logo-light.webp 44px high, white tile removed; top bar: new capu-logo-dark.webp (62×96, 5.6 KB, transparent, from Frontend/React/public/images/capitalLogo.png) 40px high; alt L(جامعة العاصمة); text kept; typecheck ok; checked by me headless 1440+375 ar/en: brand row 65px, top bar 58px, no h-scroll; I added nowrap+flex:none to .lcp-top-brand ("Tier 1" wrapped at 375) — ~18:27
- [done]    verify (me) — browser pane hidden most of the time (session not in a window), so full-res shots via headless Edge CDP on :5188 (scratchpad/cap/*.mjs); DOM checks per state: 1 aria-current, prev right in ar, 40px targets at 375, no h-scroll — 0 problems; typecheck, tsc -b, 10/10 tests, scratch build ok — 18:48
- [done]    my fixes after review — mobile pager buttons shrank to 32px (flex-basis ignored for intrinsic width; Indicators card only 297px): li width 40, gap 2px, ellipsis 24px; size menu cut 100/120: popupMatchSelectWidth=false; removed dead .dt-foot/.dt-size/.dt-range/.dt-nav from ui.css
- [done]    login logo (parent request) — AuthLayout: capital-university-logo-dark.png (white lettering) in navy panel; Tier1 mark dropped from card (one logo per screen); new shared/auth-logo.css shows the logo in the navy strip at <=900px (was no logo on mobile); capu-logo-light.webp deleted (no refs); tier1-mark.svg kept (favicon in index.html), favicon.png kept (unreferenced)
- [done]    final — 64 pager states x ar/en x 1440/375: 0 problems; login 3 steps ar/en desk/375 one logo each; final typecheck/tsc -b/10 tests/scratch build ok; wwwroot untouched; hand-over: side-session-notes/.../pager-logo.md — 18:58
- [note]    Conflicts page has no route since 0d9a874 (amr18188, 11-13 14:06) — cannot be opened; it still uses DataTable so it gets the new pager
- note 18:35: original logo == web/src/assets/brand/capital-university-logo-dark.png (same MD5); landing-port told to import it, no public/ copy; ds-skill told to add brand section (dark file on light bg, light file on dark bg)
- [note]    logo follow-up from parent 18:30: official PNGs; file names mean background colour (-dark.png = white lettering, -light.png = navy), so sidebar uses -dark.png, top bar -light.png; capu-logo-dark.webp deleted
- note 18:46: logo colours measured — -dark.png = white+gold (dark bg, == original), -light.png = navy+gold (light bg); parent's mapping was reversed; skill + Shell.tsx already right
- [done]    docs-sync (sonnet) — README + SKILL.md + recipes + token-files.md updated (scoped [data-landing] entry, scrollbar §9, -rgb comma values, reference implementation); 394 token values 0 mismatch; skill copies identical (checked by me) — 19:50
- note 18:50: user wording change seasons→quarters sent to landing-port (text exception, after exact parity proof); AuthLayout.tsx logo swap by tier1-pager-logo = expected login diff
- note 18:58: footer redesign (user) sent to landing-port; uses -dark.png (white+gold) — request named -light.png, reversed again
- note 19:05: analytics changes (remove القيادة bar, horizontal KPI rows, RTL-mirrored rising trend) sent to landing-port
- note 19:22: HEAD fb6a3a7 (author Mohamed-Farraj, 18:30) + merge ed61f85 committed WIP of ALL sessions' files (landing, design-system, skill, pager, logo). Not done by this mission. git status is now relative to it.
- [done]    final-verify (opus) — PASS: build ok; fair parity 0 px outside wording/analytics/trend icons; leak 0 px vs direct; routing ok; antd theme unchanged; app-soft recolours 66%; focus ring visible (CTA faint → sent to landing-port); git 44 files: 32 mission, 12 pager/logo, 0 unexpected. Needs rerun on hero v3 final — 20:15
- note 19:41: user committed again a0d5d6e; all mission files are in HEAD; working tree clean except ledger
- [done]    landing-port (resumed 19:55) — header removed; hero min-height 100vh/100svh; logo 56/80/96px top-start; no overlap + CTA in fold at 5 sizes (+3 extra, LTR); anchors ok; typecheck/test/build ok; shots/hero — 20:02 (screens checked by me)

## tier1-runtime-check (side session of local_1667722d-7d26-4a56-a876-3b460b5abe6b) — one API, one DB: inventory, fix dev restart via user secrets, restart :5150, stop :5160, cleanup proposal (no deletes)

- [done]    t1-inventory (opus) — 19 needed / 8 duplicate / 8 unneeded; found old `tier1` DB on 5432; no zz_* DBs; src\Tier1.Api\data absent; notes/tier1-inventory.md; read by me — 19:25
- [stopped] t1-devserver (opus) — wrote user secrets, stopped :5160 (972eaaa4 restarted it 19:16) and old :5150; its WMI restart failed (secrets not loaded -> PG 28P01); stopped by me on master-chat request — 19:22
- [done]    restart-5150 (me) — Start-Process, pid 63264 19:26:51; ready 200, /api/me 401 direct+proxy, 4 conns all tier1, sa1 login 200, outbox 0 unsent; start script fixed; demo left alone per master chat — 19:30
- [done]    landing-port (resumed 20:06) — hero v3: badge + shield note removed, height 100vh/100svh, 2 height media ranges, fits 6 sizes RTL/LTR incl 667x375; --ds-focus-ring token (navy light / gold dark, 2px, :focus-visible); typecheck/test/build ok — 20:35
- [done]    antd-theme (opus) — theme.ts ACTIVE_THEME + html data-theme (observer), antd-theme.ts mapper (radius helper), 19 legacy vars aliased (app.css/ui.css), --ds-sidebar-* 11 tokens, AA ok, tests 61, landing 0 px; open: page-hero primary navy-on-navy (ui.tsx, asked parent), theme.css --band/--r/teal glows hard-coded, /users no access for planner — 21:30
- note 20:15: trend icons also mirrored in hierarchy/data (Icon.tsx) — consistent RTL meaning, accepted; login logo 1,710 px landing-nav vs direct = likely resampling, minor
- note 20:24: tier1-pager-logo edits Shell.tsx + new app/nav.css (sidebar panels) — never edit; expected diff; report any sidebar token names to parent

## sidebar-panels (parent request, 18:58) — sidebar categories as expandable panels
- [done]    nav-panels (opus) — Shell.tsx nav part + new app/nav.css; grid 0fr→1fr, visibility after close, useId per instance, localStorage t1-nav-open + active group; SideNav component (useId), data-open + aria-expanded, badge sum + sr-only when collapsed; typecheck ok, 10/10 tests — 19:28
- note 20:36: API 127.0.0.1:5150 DOWN (nothing listening; vite /api/me 500). Not stopped by us. Asked parent once. antd-theme does non-API checks first.
- note 20:40: parent — :5150 stopped by tier1-runtime-clean (local_15526964…) at ~19:11, being restored; never start/stop APIs; :5160 = demo API (tier1_demo), don't use; parent will message when /api/me = 401
- [note]    API :5150 down (tier1-runtime-clean restarting it, per parent 19:26); signed-in shots wait for /api/me = 401; I do not start/stop the API
- note 20:55: :5150 back (PID 63264, tier1), /api/me 401 via 5188 — antd-theme told to run signed-in captures
- [note]    API back: /api/me 401 at 19:28; nav capture (sa1 + viewer) running
- [done]    nav verify (me) — sa1 + viewer, ar/en, desktop sidebar + 375 drawer: default=active group only, Tab skips collapsed links, Enter/Space toggle, focus-visible ring, SPA route opens group, persisted across reload, badge sum 12 on collapsed group, reduced-motion instant; height per frame open 0→126px ~200ms, close ~190ms; typecheck/tsc -b/10 tests/scratch build ok; only 401 console errors — 19:31

## sidebar-accordion (parent request 19:42) — no count on نماذج الجهات, one group open at a time, indented children
- [done]    me — Shell.tsx: forms badge + its sidebar-only query removed (HomePage keeps its own ['forms','mine'] query); open state = single key (old array value read as its first key); nav.css: links indented 12px inline-start, hairline guide in the gutter, active bar kept at the sidebar edge; typecheck ok; verified sa1+viewer ar/en desk+drawer: one open group, switch animates both (A 252→0 while B 0→126 in ~200ms), header click closes, Tab only reaches open links, Enter/Space ok, route/reload ok, reduced motion instant, labels fit (no wrap), active bar at edge; tsc -b, 10 tests, scratch build ok — 19:45
- [done]    corners-legacy (sonnet) — 143 radii in 8 files → var(--ds-radius-*, original); lg46 xl25 pill23 xs16 md13 sm11 round10; diff = radius lines only (checked by me); typecheck/build ok; left: var(--r) uses (theme.css --r:18px; ui.css --r: var(--ds-radius,16px)) — 21:12
- [running] ds-corners (opus) started 21:32 — after antd-theme: config.ts CORNERS + data-corners, radius scale tokens, mapper/shadcn/tailwind/landing/scrollbar, app.css+ui.css+landing.css radii, strip fallbacks, square vs rounded screenshots, docs
- [done]    radius tokens (19:47) — pager.css .pg-btn/.pg-step 10px → var(--ds-radius-sm, 10px); pager.css focus ring 10px → same; nav.css .lcp-nav-head 10px → same; auth-logo.css none; no circles in my files; fallback = current 10px (not 0) so nothing changes before tokens land; probe: 10px now, follows token (0px → 0, 6px → 6); tsc -b ok, build ok, tests 61/61 (one earlier run showed 10 failed mid-edit by another session, rerun clean)
- [done]    cleanup (me, approved via master chat; no wwwroot rebuild) — demo pid 48992 stopped; tier1_demo + 5432 tier1 dropped after verified -Fc dumps in data\backups; 3 Mongo DBs dropped after verified EJSON exports; zip + extracted copy -> F:\archive\tier1-20261113 (Recycle Bin unsupported here); pycache removed; old data backups archived; final: one API :5150, one DB tier1, Mongo tier1_logs; log in notes/tier1-cleanup.md — 19:50
- [done]    antd-theme (resumed) — ui.tsx PageHeader/Hero data-section=dark + DarkBlock nested ConfigProvider (buttons only, follows useDsTheme); antdDarkBlockTheme(); --ds-band-* tokens; theme.css band/kicker/links/glows → tokens; 14 pages ar/en 1440/375 checked; AA ≥5.55; tests 71; left: home hero teal gradient in ui.css (rule, no buttons) — 22:05 (cycles sbs checked by me)
- note 21:50: ds-corners allowed to add rounded-ds-* className tokens in features/landing/*.tsx only (square parity 0 px required)
- [done]    sidebar tokens (20:07) — colours live in shared/ui.css (not Shell.tsx); overridden in app/nav.css (loads after) with --ds-sidebar-* + old values as fallbacks: bg, text, brand subtitle, links, hover, active + indicator, focus, badge, borders, guide line, group titles, footer buttons; Shell.tsx has no colours; live data-theme switch verified; AA: navy-gold link 9.07, active 12.59, titles 5.56, badge text 8.8; app-soft 11.14/8.93/6.44/6.46; typecheck, tsc -b, 61/61 tests, scratch build ok
- [queued]  seg-instant (opus) — Forms Segmented slides from first option: root cause in FormsPage.tsx + central no-slide (antd-theme.ts Segmented token or .ant-segmented-thumb); after ds-corners + antd-theme follow-up

## sidebar-rail (parent request 20:17) — whole sidebar collapsible to an icon rail with tooltips
- [running] side-rail (opus) — Shell.tsx + app/nav.css; rail 72px ≥961px only, grid-template-columns transition, antd Tooltip hover+focus left in ar / right in en, flat links + dividers, badge dot, logo kept, footer icon-only, t1-side-rail; started 20:17
