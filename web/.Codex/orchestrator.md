# Tier 1 UI/UX orchestration

Master chat: `01a1da46-1b44-71e3-8de0-13b130bbe46c` (`Tier 1 — Master UI/UX`).

Scope: `F:\Projects\angular\Indecators\Context\Tier1\web` only. Backend, databases and seeding are outside scope. Read `..\docs\frontend-redesign-context.md` before each task. Do not start or stop the existing servers. Do not commit or push without the owner's explicit instruction.

Each future task gets its own Codex chat, a self-contained brief, a clear file scope and a completion check. The master chat tracks status and reports the result in Arabic.

## Compact dispatch rule (owner correction, 2026-11-14)

- Keep a separate session per task as the owner requested. The master supplies the relevant context in the prompt; the worker must not reconstruct the project from scratch.
- Every brief names the exact page, symptom, desired result, likely files, existing behavior to preserve, and one fitting verification step. Include only the context needed for that task.
- Common context to pass once in a short form: Tier 1 frontend is `web/` (React + antd, Arabic RTL and English LTR); tokens and corner scale live in `src/styles/design-system/`; frontend only, no backend/data edits, no commit/push or server lifecycle changes unless asked.
- For a small CSS or interaction fix, read the named component and adjacent style only. Do not require the full handover document, whole skill file, repository-wide audit, all tests, full build, or 15-theme browser sweep unless the change makes one necessary.
- Use `ui-ux-pro-max` for a specific visual decision when it adds value; use the project design-system rules relevant to the edit. Reuse known findings from this ledger instead of rediscovering them.
- The master waits for completion with compact snapshots, avoids repeated status polling and large browser inventories, and returns one concise result report with the actual verification and any limit.

- [done] `01a1da51-1e09-7582-9709-3f7333f1b8f2` — Tier 1 — UI/UX audit and shadcn plan — recommended the Strategy board's "مرتبط بـ" menu as the first Radix task; no files changed — 2026-11-14.
- [failed] `01a1da61-3ac6-7103-8aeb-9ba53f61c413` — Tier 1 — Start frontend and API — PostgreSQL and MongoDB healthy; API start script lacks its user-secrets file; automatic execution review rejected alternative background launch commands, including Vite — 2026-11-14.
- [failed] `01a1da68-de20-74c0-9154-093a097bae04` — Tier 1 — Run only its servers — Vite is live on 5188 (HTTP 200); API on 5150 blocked by missing `tier1-capu-dev/secrets.json`; PostgreSQL and MongoDB are healthy; old React untouched — 2026-11-14.
- [done] `01a1da7a-8bc9-78b3-a396-a13f135fa56d` — Tier 1 — Restore API secrets and start — created local user secrets without displaying values; API ready on 5150 (HTTP 200), Vite on 5188 (HTTP 200) — 2026-11-14.
- [done] `01a1da7e-bb33-7ed2-9d70-b7a79e4d0afd` — Tier 1 — Theme-aware landing hero and footer — landing now follows Appearance, public control added, logo enlarged; 15 combinations checked, typecheck/739 tests/build passed — 2026-11-14.
- [done] `01a1da88-b50a-7680-8e89-d1643d5c89bc` — Tier 1 — Quiet hero appearance gear — icon only, subtle and translucent, 44px target and focus-visible; typecheck/build passed; live hero preview unavailable while signed in — 2026-11-14.
- [done] `01a1da8c-1a61-7d21-a5a0-59a3c04dec47` — Tier 1 — Remove navbar gear tooltip — Shell uses tooltip={false}; typecheck passed, click toggles panel; focus showed no tooltip — 2026-11-14.
- [done] `01a1da8e-3fd4-7bd1-b4fb-af1de2f4336d` — Tier 1 — Redesign college progress — six overview pillars in desktop row, roadmap-style pillar carousel and expandable standard panels; typecheck, 786 tests, build and desktop/mobile review passed — 2026-11-14.
- [done] `01a1da93-9d3c-74e3-b40b-f6e4ebfddef7` — Tier 1 — Indicators pillar and standard layout — six pillar cards in desktop row and collapsible standards containing indicators; typecheck, 787 tests, build, desktop/mobile RTL/LTR review passed — 2026-11-14.
- [done] `01a1dab1-8dd1-7ec0-9093-7eccfcd7f05a` — Tier 1 — Lottie global loading — lottie-react installed, global session/page loading replaced, light/dark checked; typecheck, 741 tests and scratch build passed; lottie-web eval build warning remains — 2026-11-14.
- [failed] `01a1dab9-1075-7483-a8f2-7821d8c9b056` — Tier 1 — Distinguish progress comparison colors — stopped by usage limit after partial labels edit; completed by follow-up session — 2026-11-14.
- [done] `01a1db5a-13cd-76e3-8a70-df98815d7a9b` — Tier 1 — Resume progress color fix — uses distinct existing chart tokens consistently in legend, bars and sparkline; typecheck, 733 token tests and scratch build passed; live chart blocked by login — 2026-11-14.
- [done] `01a1db5a-0f35-7f62-9abc-092ddf967c4e` — Tier 1 — Restart Vite frontend — API on 5150 healthy; Vite restarted on 5188, both HTTP 200 — 2026-11-14.
- [done] `01a1db60-a443-7f33-ba7f-4d25e9637cd4` — Tier 1 — Rounded corners by default — new/reset appearance uses rounded; stored choices preserved; browser confirmed data-corners=rounded, typecheck/741 tests/build passed — 2026-11-14.
- [done] `01a1db64-6f38-74c3-8411-6e4f4dda1cbf` — Tier 1 — Results pillar card grid — three cards per row, two desktop rows, equal row heights and improved headers; typecheck/build and live desktop/mobile review passed — 2026-11-14.
- [done] `01a1db70-5f65-74e3-b271-291cbe862f41` — Tier 1 — Scroll results to top after college filter — URL/results and top scroll verified for list and map; focus restored after map selection; typecheck/build passed; scratch build cleanup auto-rejected — 2026-11-14.
- [done] `01a1db75-b950-7961-b7ea-3b8ccc9c1af0` — Tier 1 — Group results indicators by standards — collapsible standards by default; flat list for status/standard/search filters; typecheck/test/build and live switching passed; scratch build remains — 2026-11-14.
- [done] `01a1db89-166e-7d81-898a-888b7488a181` — Tier 1 — Remove hero settings gear — landing control and unused styles removed; saved appearance and internal navbar control preserved; typecheck/build and browser check passed — 2026-11-14.
- [done] `01a1dba9-ccdd-73f1-8642-0be1ca78e710` — Tier 1 — Audit login navigation and account switching — read-only: /login resolves home, deep-link login keeps path (may show NoAccess), repeated lazy-load failure may reload-loop; ordinary logout hard reload clears in-memory cache; no live reproduction — 2026-11-14.
