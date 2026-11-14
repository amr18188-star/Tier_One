# Tier 1 UI/UX orchestration

Master chat: `01a1da46-1b44-71e3-8de0-13b130bbe46c` (`Tier 1 — Master UI/UX`).

Scope: `F:\Projects\angular\Indecators\Context\Tier1\web` only. Backend, databases and seeding are outside scope. Read `..\docs\frontend-redesign-context.md` before each task. Do not start or stop the existing servers. Do not commit or push without the owner's explicit instruction.

Each future task gets its own Codex chat, a self-contained brief, a clear file scope and a completion check. The master chat tracks status and reports the result in Arabic.

- [done] `01a1da51-1e09-7582-9709-3f7333f1b8f2` — Tier 1 — UI/UX audit and shadcn plan — recommended the Strategy board's "مرتبط بـ" menu as the first Radix task; no files changed — 2026-11-14.
- [failed] `01a1da61-3ac6-7103-8aeb-9ba53f61c413` — Tier 1 — Start frontend and API — PostgreSQL and MongoDB healthy; API start script lacks its user-secrets file; automatic execution review rejected alternative background launch commands, including Vite — 2026-11-14.
- [failed] `01a1da68-de20-74c0-9154-093a097bae04` — Tier 1 — Run only its servers — Vite is live on 5188 (HTTP 200); API on 5150 blocked by missing `tier1-capu-dev/secrets.json`; PostgreSQL and MongoDB are healthy; old React untouched — 2026-11-14.
- [done] `01a1da7a-8bc9-78b3-a396-a13f135fa56d` — Tier 1 — Restore API secrets and start — created local user secrets without displaying values; API ready on 5150 (HTTP 200), Vite on 5188 (HTTP 200) — 2026-11-14.
- [done] `01a1da7e-bb33-7ed2-9d70-b7a79e4d0afd` — Tier 1 — Theme-aware landing hero and footer — landing now follows Appearance, public control added, logo enlarged; 15 combinations checked, typecheck/739 tests/build passed — 2026-11-14.
- [done] `01a1da88-b50a-7680-8e89-d1643d5c89bc` — Tier 1 — Quiet hero appearance gear — icon only, subtle and translucent, 44px target and focus-visible; typecheck/build passed; live hero preview unavailable while signed in — 2026-11-14.
- [done] `01a1da8c-1a61-7d21-a5a0-59a3c04dec47` — Tier 1 — Remove navbar gear tooltip — Shell uses tooltip={false}; typecheck passed, click toggles panel; focus showed no tooltip — 2026-11-14.
- [done] `01a1da8e-3fd4-7bd1-b4fb-af1de2f4336d` — Tier 1 — Redesign college progress — six overview pillars in desktop row, roadmap-style pillar carousel and expandable standard panels; typecheck, 786 tests, build and desktop/mobile review passed — 2026-11-14.
- [done] `01a1da93-9d3c-74e3-b40b-f6e4ebfddef7` — Tier 1 — Indicators pillar and standard layout — six pillar cards in desktop row and collapsible standards containing indicators; typecheck, 787 tests, build, desktop/mobile RTL/LTR review passed — 2026-11-14.
