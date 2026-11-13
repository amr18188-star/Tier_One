# Token files, tokens and utilities

Everything lives in `web/src/styles/design-system/` (paths relative to the Tier1 root). The CSS files are the source of
truth; `tokens.ts` mirrors them by hand. Tables below were generated from the theme files on 2026-11-13. If a value here
disagrees with the CSS, the CSS wins: re-read the file.

## Contents

1. File map · 2. How scoping works · 3. Primitives · 4. Theme-level tokens · 5. Section tokens ·
6. Utilities · 7. shadcn variable mapping · 8. `tokens.ts` and antd · 9. Scrollbar tokens

## 1. File map

| File | Holds | Scope | Imported by |
|---|---|---|---|
| `web/src/styles/design-system/primitives.css` | Raw palettes as `--color-<family>-<step>` | `:root` (the only global file) | `index.css` |
| `web/src/styles/design-system/themes/navy-gold.css` | `--ds-*` + shadcn variables for the landing look | `[data-theme="navy-gold"]` | `index.css` |
| `web/src/styles/design-system/themes/app-soft.css` | Same names, the old `/app` palette, 14px radius | `[data-theme="app-soft"]` | `index.css` |
| `web/src/styles/design-system/index.css` | Plain-CSS entry: primitives, then each theme (order matters) | — | a feature's Tailwind entry, or any CSS that wants `var(--ds-*)` |
| `web/src/styles/design-system/tailwind-theme.css` | `@theme inline reference`: `--color-ds-*`, primitives, `--radius-ds`, `--font-ds`, `--shadow-ds-*` | emits nothing on `:root` | a feature's Tailwind entry, after `index.css` |
| `web/src/styles/design-system/tokens.ts` | The same values as typed TS constants (real colours for antd) | — | `antd-theme.ts`, `tokens.test.ts`, `LandingPage.tsx` (type) |
| `web/src/styles/design-system/antd-theme.ts` | `antdTheme(name, base, { tone })`: tokens → antd `ThemeConfig` (section 8) | — | `web/src/app/App.tsx` |
| `web/src/styles/design-system/theme.ts` | `ACTIVE_THEME` → `<html data-theme>`, `useDsTheme()`, `setDsTheme()`; imports `index.css` | `<html>` | `web/src/app/App.tsx` |
| `web/src/styles/design-system/tokens.test.ts` | Parses the CSS; fails if `tokens.ts` differs or an AA pair fails | — | vitest |
| `web/src/styles/design-system/scrollbar.css` | Scrollbar tokens and drawing (section 9) | `:root`, `[data-theme]` scopes, element selectors | `web/src/main.tsx`, once, after `app.css` |
| `web/src/styles/design-system/README.md` | Short usage and the add-a-theme steps | — | — |

Each theme file has three blocks: theme level (`[data-theme="x"]`), light scope (`[data-theme="x"]` and
`[data-theme="x"] [data-section="light"]`), dark scope (`[data-theme="x"] [data-section="dark"]` and
`[data-theme="x"][data-section="dark"]`). Only custom properties, no element rules.

`theme.ts` (imported by `App.tsx`) loads `index.css` globally and sets `<html data-theme>`, so every `--ds-*` token
exists on every page, including antd portals under `<body>`. A feature that uses Tailwind utilities imports the tokens
again through its own Tailwind entry (see SKILL.md); the values are the same.

## 2. How scoping works

- `data-theme="<name>"` on a container turns a theme on for its subtree. Default scope inside it: light.
- `data-section="dark"` on any descendant re-scopes the section tokens (backgrounds, surfaces, text, lines, accent,
  tiles, buttons, shadcn core) for that subtree. `data-section="light"` flips back inside a dark block. Put
  `data-section="dark"` on the theme root itself for an all-dark page.
- Theme-level tokens (font, radius, chrome, textures, charts, shadows, focus, selection, shadcn `--radius`, `--chart-*`,
  `--sidebar-*`) are the same in both scopes.
- The Tailwind mapping is `inline`: `bg-ds-surface` compiles to `background-color: var(--ds-surface)`, so the value
  resolves on the element itself and follows the nearest scope at any depth. A dark card in a light section is just
  `<div data-section="dark" className="bg-ds-surface">`.
- `ds-bg-cool` and `ds-bg-warm` are navy in a dark scope, so a section keeps its classes when flipped.
- Don't nest two different `data-theme` values. The section selectors of both themes would match the same element and
  the later file (app-soft) would win on some properties only.
- A token missing from a theme is unset there: its utility paints nothing (transparent background, inherited text).
  Every theme must define every name.

## 3. Primitives

`primitives.css`, on `:root`. Use them only inside theme files. `@N%` elsewhere in this file means
`color-mix(in oklab|srgb, <colour> N%, transparent)`.

| Family | Steps (value) |
|---|---|
| navy | 950 `#06152a` · 900 `#071a33` · 800 `#0c2545` · 700 `#12345b` · 600 `#18304d` · 500 `#29415f` · `navy-900-rgb` = `7, 26, 51` (comma form, used as `rgba(var(--color-navy-900-rgb), a)`) |
| steel | 400 `#7890ac` |
| mist | 100 `#dce5f0` · 300 `#aab8ca` (replace Tailwind's mist-100/300 where loaded; the rest of Tailwind's mist is cleared in the Tailwind mapping) |
| gold | 200 `#e5c47e` · 400 `#d6b46a` · 500 `#b9944d` · 600 `#a7833d` · 700 `#82652b` (AA small text, not used by the landing) |
| paper | `#ffffff` (the doc's "white"; named so it never overrides Tailwind's `--color-white`) |
| cloud | `#f8f9fb` |
| sand | 50 `#f8f4ea` · 100 `#f8f3e9` · 200 `#f4ebd8` |
| ivory | `#f8f6f1` · 50 `#fcfbf8` |
| ash (Tailwind v4 slate, exact OKLCH) | 100 ≈ `#f1f5f9` · 200 ≈ `#e2e8f0` · 300 ≈ `#cad5e2` · 400 ≈ `#90a1b9` · 500 ≈ `#62748e` · 600 ≈ `#45556c` |
| haze (Tailwind v3 slate hex, chart greys) | 200 `#e2e8f0` · 300 `#cbd5e1` · 400 `#94a3b8` |
| signal | green `#22c55e` · red `#ef4444` |
| soft-navy | 950 `#0d2440` · 900 `#123055` · 800 `#19355a` · 700 `#234a7e` · 500 `#3567b0` · 100 `#e8eef7` · `soft-navy-900-rgb` = `18, 48, 85` (same use) |
| soft-gold | 700 `#8a6d00` · 600 `#ad8700` · 500 `#c9a227` · 300 `#e6c25a` · 100 `#f0dfa8` · 50 `#fffdf4` |
| soft-neutral | 900 `#1b2733` · 700 `#475569` · 600 `#5c6b7a` · 500 `#7d8ea6` · 400 `#9aa8b6` · 300 `#a7b3c0` · 200 `#cddbee` · 150 `#e2e8f0` · 100 `#e3e9f1` · 75 `#edf2f7` · 50 `#f4f7fb` · 25 `#f8fafc` |
| soft-success / danger / info | success 700 `#1c7c3d` · 400 `#64c783`; danger 700 `#b3261e` · 600 `#d0453e` · 400 `#ef6a63`; info 700 `#1e40af` |

The channel triples use the comma `rgba()` form on purpose: only it keeps Chrome's legacy gradient interpolation (the
space form `rgb(7 26 51 / a)` and `color-mix()` shift pixels by one unit). navy-gold also uses three soft primitives so it stays pixel-identical to the old app: `soft-navy-800` (shadows),
`soft-gold-500` (focus outline, selection) and `soft-navy-950` (selection text).

## 4. Theme-level tokens

Same in light and dark scopes.

| Token | navy-gold | app-soft | Use |
|---|---|---|---|
| `--ds-font` | Cairo, Geist Variable, Segoe UI, sans-serif | same | `font-ds` on the page root |
| `--ds-font-heading` | same as `--ds-font` | same | `font-ds-heading` (lets a theme split heading type) |
| `--ds-radius` | `0px` | `0.875rem` (14px) | `rounded-ds` |
| `--ds-chrome` | navy-950 | soft-navy-950 | header and footer (`bg-ds-chrome/95` + blur in the header) |
| `--ds-grid-hero` | navy-600 | soft-navy-800 | colour of the hero grid lines |
| `--ds-grid-dark` | steel-400 | soft-neutral-500 | colour of the dark-section grid lines |
| `--ds-texture-hero` | 1px grid of navy-600 | 1px grid of soft-navy-800 | `backgroundImage` + `backgroundSize: 40px 40px` + `opacity-40` |
| `--ds-texture-dark` | 1px grid of steel-400 | 1px grid of soft-neutral-500 | `backgroundImage` + `backgroundSize: 48px 48px` + `opacity-[0.08]` |
| `--ds-hero-vignette` | radial `rgba(var(--color-navy-900-rgb), a)`, a = .96 / .88 / .65 / .4 | same with `soft-navy-900-rgb` | `background` of the hero overlay |
| `--ds-chart-1` | navy-800 | soft-navy-500 | first series |
| `--ds-chart-2` | gold-400 | soft-gold-600 | second series, highlighted bars |
| `--ds-chart-3` | steel-400 | soft-success-700 | third series |
| `--ds-chart-4` | haze-300 | soft-info-700 | fourth series ("other") |
| `--ds-chart-actual` | gold-500 | soft-gold-600 | actual line |
| `--ds-chart-target` | steel-400 | soft-neutral-500 | target line (dashed) |
| `--ds-chart-area` | gold-400 | soft-gold-500 | gradient stop under the actual line (30% → 1%) |
| `--ds-chart-current` | gold-400 | soft-gold-500 | current point fill |
| `--ds-chart-bg` | paper | paper | point fill and halo |
| `--ds-chart-grid` | haze-200 | soft-neutral-150 | gridlines |
| `--ds-chart-axis` | haze-400 | soft-neutral-600 | axis labels |
| `--ds-chart-up` | signal-green | soft-success-400 | rising line (hero) |
| `--ds-chart-down` | signal-red | soft-danger-400 | falling line (hero only) |
| `--ds-shadow-sm` | `0 8px 24px` soft-navy-800 @8% | same | `shadow-ds-sm` |
| `--ds-shadow-md` | `0 16px 40px` soft-navy-800 @14% | same | `shadow-ds-md` |
| `--ds-focus` | soft-gold-500 @50% | same | `outline-color` on the feature root |
| `--ds-selection-bg` | soft-gold-500 | same | `::selection` background |
| `--ds-selection-fg` | soft-navy-950 | same | `::selection` text |

App sidebar (the navy chrome of the signed-in shell; theme level, not per section). Utilities `bg-ds-sidebar-bg`,
`text-ds-sidebar-fg` …; `shared/ui.css` maps the sidebar's legacy `--teal`, `--teal-100`, `--gold` to them.

| Token | navy-gold | app-soft | Use (contrast in navy-gold) |
|---|---|---|---|
| `--ds-sidebar-bg` | navy-950 | soft-navy-950 | sidebar and mobile menu background |
| `--ds-sidebar-fg` | mist-300 | soft-neutral-200 | link text (9.1:1) |
| `--ds-sidebar-fg-active` | paper | paper | hovered / active link (12.6:1 on active bg) |
| `--ds-sidebar-muted` | steel-400 | soft-neutral-400 | group titles (5.6:1) |
| `--ds-sidebar-hover-bg` | navy-800 | soft-navy-800 | link hover |
| `--ds-sidebar-active-bg` | navy-700 | soft-navy-700 | active link |
| `--ds-sidebar-indicator` | gold-400 | soft-gold-500 | active-link bar (6.3:1 on active bg) |
| `--ds-sidebar-border` | navy-500 | paper @14% | dividers |
| `--ds-sidebar-focus` | gold-400 | soft-gold-300 | keyboard focus outline (9.2:1) |
| `--ds-sidebar-badge-bg` / `-fg` | gold-400 / navy-900 | soft-gold-500 / soft-navy-950 | count badges (8.8:1) |

Page band (the navy header strip of antd pages; `shared/theme.css` builds `--band` from these). Theme level; `band` in
`tokens.ts`. Text on the band is white or a dark-section token (the band has `data-section="dark"`).

| Token | navy-gold | app-soft | Use |
|---|---|---|---|
| `--ds-band-from` / `-via` / `-to` | navy-900 / navy-800 / navy-700 | soft-navy-900 / 800 / 700 | 125° linear gradient stops |
| `--ds-band-glow` | steel-400 @30% | soft-navy-500 @38% | radial light at the top start |
| `--ds-band-glow-accent` | gold-400 @18% | soft-gold-500 @18% | radial warm light at the bottom end |

## 5. Section tokens

These change with `data-section`. Utility = any colour prefix + the name without `--`: `--ds-body` → `text-ds-body`,
`--ds-border` → `border-ds-border`, `--ds-chart-1` → `stroke-ds-chart-1`.

#### Backgrounds and surfaces

| Token | navy-gold light | navy-gold dark | app-soft light | app-soft dark | Use |
|---|---|---|---|---|---|
| `--ds-bg` | paper | navy-900 | paper | soft-navy-900 | section / page background |
| `--ds-bg-cool` | cloud | navy-900 | soft-neutral-50 | soft-navy-900 | cool section (data), inner info panel |
| `--ds-bg-warm` | sand-200 | navy-900 | soft-gold-50 | soft-navy-900 | warm section (story, final CTA) |
| `--ds-fg` | navy-900 | paper | soft-neutral-900 | paper | inherited text colour of a section |
| `--ds-surface` | paper | navy-800 | paper | soft-navy-800 | card, panel, dashboard |
| `--ds-surface-hover` | ivory | navy-700 | soft-neutral-25 | soft-navy-700 | hover on cards and cells |
| `--ds-surface-hover-soft` | ivory-50 | navy-700 | soft-neutral-25 | soft-navy-700 | lighter hover (feature rows) |
| `--ds-surface-warm` | sand-100 | navy-800 | paper | soft-navy-800 | cell sitting on a warm section |
| `--ds-callout` | ivory | navy-800 | soft-neutral-75 | soft-navy-800 | inline note fill |

#### Lines and accent

| Token | navy-gold light | navy-gold dark | app-soft light | app-soft dark | Use |
|---|---|---|---|---|---|
| `--ds-border` | ash-200 | navy-500 | soft-neutral-100 | paper @14% | 1px borders, hairline-grid parent |
| `--ds-line-soft` | navy-900 | paper | soft-navy-800 | paper | translucent lines: always with `/10`, `/15`, `/20` |
| `--ds-track` | ash-100 | navy-700 | soft-neutral-75 | soft-navy-700 | progress-bar track |
| `--ds-accent` | gold-400 | gold-400 | soft-gold-500 | soft-gold-500 | accent lines, rules, highlighted bars; never text on light |
| `--ds-accent-strong` | gold-500 | gold-400 | soft-gold-600 | soft-gold-300 | thick rules, connectors, hover border |
| `--ds-accent-border` | gold-400 @40% | gold-400 @30% | soft-gold-500 @40% | soft-gold-500 @30% | accent border of tiles and notes (alpha built in) |

#### Text

| Token | navy-gold light | navy-gold dark | app-soft light | app-soft dark | Use |
|---|---|---|---|---|---|
| `--ds-heading` | navy-900 | paper | soft-navy-950 | paper | headings, numbers, KPI values |
| `--ds-highlight` | gold-500 | gold-400 | soft-gold-700 | soft-gold-300 | the highlighted words of a heading, code labels |
| `--ds-highlight-display` | gold-500 | gold-200 | soft-gold-700 | soft-gold-100 | stacked display highlight (hero H1, CTA line 2) |
| `--ds-body` | ash-500 | mist-300 | soft-neutral-700 | soft-neutral-200 | body and descriptions |
| `--ds-body-strong` | ash-600 | mist-100 | soft-neutral-900 | soft-navy-100 | subtitles, emphasised body, badge text |
| `--ds-label` | ash-600 | mist-100 | soft-neutral-700 | soft-navy-100 | legend and bar labels |
| `--ds-faint` | ash-400 | steel-400 | soft-neutral-600 | soft-neutral-400 | captions, axis (decorative on light in navy-gold) |
| `--ds-index` | ash-300 | paper @20% | soft-neutral-300 | paper @20% | big decorative index numbers ("01") |
| `--ds-eyebrow` | gold-600 | gold-400 | soft-gold-700 | soft-gold-300 | eyebrows, suffixes, English sub-labels |
| `--ds-eyebrow-aa` | gold-700 | gold-400 | soft-gold-700 | soft-gold-300 | small gold text that must pass AA (changes pixels vs the landing) |

#### Icons, tiles, status

| Token | navy-gold light | navy-gold dark | app-soft light | app-soft dark | Use |
|---|---|---|---|---|---|
| `--ds-icon` | gold-600 | gold-400 | soft-gold-600 | soft-gold-300 | icon colour (`text-ds-icon`, icons use `currentColor`) |
| `--ds-tile` | sand-50 | gold-400 @10% | soft-gold-50 | soft-gold-500 @14% | icon-tile fill (alpha built in on dark) |
| `--ds-tile-active` | gold-400 | gold-400 | soft-gold-500 | soft-gold-500 | tile fill when its `group` card is hovered |
| `--ds-tile-active-fg` | navy-900 | navy-900 | soft-navy-950 | soft-navy-950 | icon on the active tile |
| `--ds-tile-solid` | navy-900 | navy-900 | soft-navy-950 | soft-navy-950 | solid tile fill |
| `--ds-tile-solid-fg` | gold-400 | gold-400 | soft-gold-300 | soft-gold-300 | icon on the solid tile |
| `--ds-live` | signal-green | signal-green | soft-success-700 | soft-success-400 | "live / on track" square dot |
| `--ds-focus-ring` | navy-900 | gold-400 | soft-navy-950 | soft-gold-300 | keyboard focus outline (2px solid); antd `colorPrimaryBorder` |
| `--ds-link` | gold-700 | gold-400 | soft-navy-700 | soft-gold-300 | text links and codes; antd `colorLink`; legacy `--teal-ink` (5.5:1 on white) |
| `--ds-link-hover` | navy-900 | paper | soft-navy-950 | paper | link hover / active |

#### Primary button

On light it is navy and inverts to gold; on dark it is gold and inverts to an outline.

| Token | navy-gold light | navy-gold dark | app-soft light | app-soft dark |
|---|---|---|---|---|
| `--ds-btn-primary-bg` | navy-900 | gold-400 | soft-navy-800 | soft-gold-500 |
| `--ds-btn-primary-fg` | paper | navy-900 | paper | soft-navy-950 |
| `--ds-btn-primary-border` | navy-900 | gold-400 | soft-navy-800 | soft-gold-500 |
| `--ds-btn-primary-hover-bg` | gold-400 | transparent | soft-gold-500 | transparent |
| `--ds-btn-primary-hover-fg` | navy-900 | gold-400 | soft-navy-950 | soft-gold-300 |
| `--ds-btn-primary-hover-border` | gold-400 | gold-400 | soft-gold-500 | soft-gold-500 |

#### Ghost button

| Token | navy-gold light | navy-gold dark | app-soft light | app-soft dark |
|---|---|---|---|---|
| `--ds-btn-ghost-bg` | transparent | transparent | transparent | transparent |
| `--ds-btn-ghost-fg` | navy-900 | paper | soft-navy-800 | paper |
| `--ds-btn-ghost-border` | navy-900 @20% | paper @20% | soft-navy-800 @20% | paper @20% |
| `--ds-btn-ghost-hover-bg` | gold-400 | gold-400 | soft-gold-500 | soft-gold-500 |
| `--ds-btn-ghost-hover-fg` | navy-900 | navy-900 | soft-navy-950 | soft-navy-950 |
| `--ds-btn-ghost-hover-border` | gold-400 | gold-400 | soft-gold-500 | soft-gold-500 |

## 6. Utilities

Available in a Tailwind entry that imports `tailwind-theme.css` (see SKILL.md for the entry).

**Semantic colours** (follow `data-theme` and `data-section`). Every Tailwind colour prefix works with every
`ds-<role>`: `bg-`, `text-`, `border-` (and `border-s-`, `border-e-`, `border-t-`, `border-b-` …), `divide-`,
`outline-`, `ring-`, `fill-`, `stroke-`, `decoration-`, `caret-`, `accent-`, `placeholder-`, `from-` / `via-` / `to-`,
`shadow-` (shadow colour). Roles: every section token above, plus the theme-level `ds-chrome`, `ds-chart-1` … `ds-chart-4`,
`ds-chart-actual`, `ds-chart-target`, `ds-chart-area`, `ds-chart-current`, `ds-chart-bg`, `ds-chart-grid`, `ds-chart-axis`,
`ds-chart-up`, `ds-chart-down`, `ds-focus`, `ds-selection-bg`, `ds-selection-fg`, `ds-grid-hero`, `ds-grid-dark`, and the
eleven `ds-sidebar-*` tokens.

**Opacity modifiers** work: `bg-ds-accent/10` → `color-mix(in oklab, var(--ds-accent) 10%, transparent)`;
`border-ds-line-soft/15`, `bg-ds-surface/90`, `bg-ds-chrome/95`. Don't add a modifier to a token that already carries alpha
(`ds-accent-border`, `ds-btn-ghost-border`, `ds-focus`, and in dark scopes `ds-tile`, `ds-index`, app-soft `ds-border`):
the alphas multiply.

**Primitives** (fixed colours, do not re-theme; for theme files and rare fixed-brand needs): `navy-950/900/800/700/600/500`,
`steel-400`, `mist-100/300`, `gold-200/400/500/600/700`, `paper`, `cloud`, `sand-50/100/200`, `ivory`, `ivory-50`,
`ash-100…600`, `haze-200/300/400`, `signal-green`, `signal-red`, `soft-navy-950/900/800/700/500/100`,
`soft-gold-700/600/500/300/100/50`. The `soft-neutral`, `soft-success`, `soft-danger` and `soft-info` primitives have no
utilities (CSS variables only).

**Shape, type, elevation:** `rounded-ds` and every radius form (`rounded-s-ds`, `rounded-e-ds`, `rounded-t-ds`,
`rounded-ss-ds` …), `font-ds`, `font-ds-heading`, `shadow-ds-sm`, `shadow-ds-md`.

**Composite tokens** (no utility; use in `style`): `var(--ds-texture-hero)`, `var(--ds-texture-dark)`, `var(--ds-hero-vignette)`.

**Tailwind's own scale still exists** in the entry (`tailwindcss/theme.css`): `slate-*`, `white`, `rounded-lg`, `shadow-sm`,
`font-sans` … They do not follow the theme. Don't use them for themed colour, shape or elevation. Spacing, sizing,
layout, typography sizes and breakpoints are Tailwind defaults and fine to use.

## 7. shadcn variable mapping

shadcn components (`web/src/components/ui/`) read these variables. The theme files set them per theme and scope, so
shadcn inside `data-theme` follows it. `web/src/styles/shadcn.css` holds the defaults on `:root` (the teal app palette),
the `@theme inline` map (`--color-primary: var(--primary)`, `--color-muted: var(--ui-muted)` …) and the radius scale
(`--radius-sm … 4xl` = `calc(var(--radius) * n)`), so `--radius: 0px` squares every shadcn part.

| shadcn variable | navy-gold light | navy-gold dark | app-soft light | app-soft dark |
|---|---|---|---|---|
| `--background` | paper | navy-900 | soft-neutral-50 | soft-navy-900 |
| `--foreground` | navy-900 | paper | soft-neutral-900 | soft-neutral-25 |
| `--card` | paper | navy-800 | paper | soft-navy-800 |
| `--card-foreground` | navy-900 | paper | soft-neutral-900 | soft-neutral-25 |
| `--popover` | paper | navy-800 | paper | soft-navy-800 |
| `--popover-foreground` | navy-900 | paper | soft-neutral-900 | soft-neutral-25 |
| `--primary` | navy-900 | gold-400 | soft-navy-800 | soft-gold-300 |
| `--primary-foreground` | paper | navy-900 | paper | soft-navy-950 |
| `--secondary` | cloud | navy-700 | soft-navy-100 | soft-navy-700 |
| `--secondary-foreground` | navy-900 | paper | soft-navy-800 | soft-neutral-25 |
| `--ui-muted` | cloud | navy-800 | soft-neutral-75 | soft-navy-800 |
| `--muted-foreground` | ash-500 | mist-300 | soft-neutral-700 | soft-neutral-200 |
| `--accent` | gold-400 | navy-700 | soft-gold-50 | soft-navy-700 |
| `--accent-foreground` | navy-900 | paper | soft-gold-700 | soft-gold-100 |
| `--destructive` | soft-danger-700 | soft-danger-400 | soft-danger-700 | soft-danger-400 |
| `--border` | ash-200 | navy-500 | soft-neutral-100 | paper @14% |
| `--input` | ash-200 | navy-500 | soft-neutral-100 | paper @18% |
| `--ring` | gold-400 | gold-400 | soft-gold-500 | soft-gold-300 |

| shadcn variable (theme level) | navy-gold | app-soft |
|---|---|---|
| `--radius` | `0px` | `0.875rem` |
| `--chart-1` | navy-800 | soft-navy-500 |
| `--chart-2` | gold-400 | soft-gold-600 |
| `--chart-3` | steel-400 | soft-success-700 |
| `--chart-4` | haze-300 | soft-info-700 |
| `--chart-5` | gold-500 | soft-danger-600 |
| `--sidebar` | navy-950 | soft-navy-950 |
| `--sidebar-foreground` | paper | paper |
| `--sidebar-primary` | gold-400 | soft-gold-500 |
| `--sidebar-primary-foreground` | navy-900 | soft-navy-950 |
| `--sidebar-accent` | navy-800 | paper @10% |
| `--sidebar-accent-foreground` | paper | paper |
| `--sidebar-border` | navy-500 | paper @14% |
| `--sidebar-ring` | gold-400 | soft-gold-300 |

Notes:
- The muted surface is `--ui-muted` because `web/src/styles/app.css` owns `--muted` (a text colour on antd pages).
- `--destructive` is a soft danger red: navy-gold's `signal-red` fails AA as text (3.8:1).
- `:root` (shadcn.css) and `[data-theme]` have the same specificity. On a descendant element the theme wins by
  inheritance. `<html>` carries `data-theme` (`theme.ts`), so if `shadcn.css` is ever imported it must load before the
  design-system CSS (import it in `main.tsx`, which runs before `App.tsx` imports `theme.ts`).
- Nothing else in `web/src` declares these names (checked 2026-11-13: only `shadcn.css`, not imported), so the theme's
  shadcn variables on `<html>` clash with nothing.

## 8. `tokens.ts` and antd

`web/src/styles/design-system/tokens.ts` exports:
- `palette`: every primitive as a plain colour string (ash in 8-bit hex).
- `themes`: per theme `fontFamily`, `radius` (px number), `chrome`, `gridHero`, `gridDark`, `chart` (`series`, `actual`,
  `target`, `area`, `current`, `bg`, `grid`, `axis`, `up`, `down`), `shadow`, `focus`, `selection`,
  `sidebar` (the `--ds-sidebar-*` tokens), `sections.light` / `sections.dark` (every section token, camelCase, buttons as
  `btnPrimary` / `btnGhost`, plus `focusRing`, `link`, `linkHover` and `destructive` from `--destructive`).
- `themeNames`, `isThemeName()`, and the types `ThemeName`, `SectionTone`, `ThemeTokens`, `SectionTokens`,
  `SidebarTokens`, `ButtonTokens`.
- `color-mix(…, X n%, transparent)` is written as `rgba(X, n/100)`. `tokens.test.ts` parses the CSS and fails on any
  difference in either direction (a CSS token without a field, a field without a CSS token, a different value); the
  `satisfies Record<ThemeName, ThemeTokens>` check makes TypeScript report a missing field.

Use it where CSS variables can't reach: antd (below), canvas charts, print/PDF, values computed in JS.

**antd is wired to the tokens.** `antd-theme.ts` → `antdTheme(name, base = {}, { tone = "light" } = {})` returns a
`ThemeConfig`; `App.tsx` calls it with `useDsTheme()` and its fonts:

```tsx
const antdBase: ThemeConfig = { token: { fontFamily: '"IBM Plex Sans Arabic", "Segoe UI", Tahoma, Arial, sans-serif', fontSize: 14 } };
const dsTheme = useDsTheme();                                   // <html data-theme>, re-renders on change
const theme = useMemo(() => antdTheme(dsTheme, antdBase), [dsTheme]);
<ConfigProvider direction={…} locale={…} theme={theme}>…</ConfigProvider>
```

| antd | From `themes[name]` (light section unless noted) |
|---|---|
| `colorPrimary`, Button `primaryColor` | `btnPrimary.bg`, `btnPrimary.fg` |
| `colorPrimaryBorder` (antd's focus-visible outline) | `focusRing` |
| `colorLink`, `colorLinkHover`, `colorLinkActive` | `link`, `linkHover`, `linkHover` |
| `colorInfo`, `colorInfoBg`, `colorInfoBorder` | `heading`, `bgCool`, `border` |
| `colorError` | `destructive`; `colorSuccess` / `colorWarning` stay antd defaults (no AA status token) |
| `colorText`, `colorTextHeading`, `colorTextSecondary` | `fg`, `heading`, `label` |
| `colorBorder`, `colorBorderSecondary` | `border` |
| `colorBgLayout`, `colorBgContainer`, `colorBgElevated`, `colorBgSpotlight` | `bgCool`, `surface`, `surface`, `sections.dark.bg` |
| `borderRadiusLG` / `borderRadius` / `SM` / `XS` / `Outer` | `antdRadii(radius)`: r, r − 2, r − 4, r − 6, r − 4 (≥ 0) |
| Button default (colour, bg, border, hover and active) | `btnGhost.fg`, `surface`, `btnGhost.border`, `btnGhost.hoverFg` / `hoverBg` / `hoverBorder` |
| Input, InputNumber, DatePicker: `hoverBorderColor`, `activeBorderColor`, `activeShadow` | `accentStrong`, `focusRing`, `0 0 0 2px` + `focus` |
| Select: same borders, `activeOutlineColor`, `optionSelectedBg` / `Color`, `optionActiveBg` | `focus`, `tile` / `heading`, `surfaceHover` |
| Table: `headerBg`, `headerColor`, sort / filter bg, `headerSplitColor` | `sections.dark`: `bg`, `heading`, `surface` / `surfaceHover`, `border` |
| Table: `rowHoverBg`, `bodySortBg`, `borderColor`, `headerBorderRadius` | `surfaceHover`, `bgCool`, `border`, 0 |
| Tag `defaultBg`, `defaultColor` | `bgCool`, `fg` |
| Modal `contentBg`, `headerBg`, `titleColor`; Drawer `colorBgElevated` | `surface`, `surface`, `heading`; `surface` |
| Tabs `itemColor`, hover / selected / active, `inkBarColor` | `label`, `heading`, `icon` (≥ 3:1) |
| Segmented `trackBg`, `itemColor`, `itemHoverColor`, `itemSelectedBg` / `Color` | `track`, `label`, `heading`, `btnPrimary.bg` / `.fg` |

- A new theme re-themes antd as soon as it has its `tokens.ts` entry; the mapper never names a theme.
- `tone: "dark"` reads `sections.dark` and adds antd's `darkAlgorithm`: a full dark theme for a nested `ConfigProvider`.
  Nothing uses it yet.
- `antdDarkBlockTheme(name)`: Button colours only, from `sections.dark` (`colorPrimary` btnPrimary.bg, `primaryColor`
  btnPrimary.fg, `colorLink` / hover link / linkHover, `colorText` fg, component `algorithm: true` for hover and focus
  shades). No radius, so corners come from the parent. Used by `DarkBlock` in `shared/ui.tsx` around the actions of
  `PageHeader` (`.lcp-head`) and `Hero` (`.lcp-hero`), which also carry `data-section="dark"`.
- Fonts, sizes, spacing: not touched (`base` wins for anything it sets).
- The app has no antd Menu or Layout, so neither has tokens.

**Legacy variables.** The `:root` blocks of `web/src/styles/app.css` and `web/src/shared/ui.css` alias the brand
variables to light-section tokens (old hex as `var()` fallback): `--navy` → `--ds-heading`, `--navy-2` → `--ds-chrome`,
`--teal` → `--ds-btn-primary-bg`, `--teal-ink` → `--ds-link`, `--bg` → `--ds-bg-cool`, `--surface` → `--ds-surface`,
`--sunk` → `--ds-track`, `--ink` → `--ds-fg`, `--muted` → `--ds-label`, `--line` → `--ds-border`, `--r` → `--ds-radius`,
`--teal-50` → `--ds-tile`, `--teal-100` → `--ds-accent`, `--navy-50` → `--ds-track`, `--gold` → `--ds-accent`,
`--gold-50` → `--ds-bg-warm`, `--gold-ink` → `--ds-eyebrow-aa`, `--row-alt` → `--ds-bg-cool`, `--hair` → `--ds-border`.
Inside `.lcp-side` and `.lcp-drawer .ant-drawer-body`: `--teal` → `--ds-sidebar-indicator`, `--teal-100` →
`--ds-sidebar-focus`, `--gold` → `--ds-sidebar-badge-bg`. Status (`--warn*`, `--ok*`, `--crit*`), fonts, shadows and
spacing are fixed. `shared/theme.css`: `--band` comes from `--ds-band-*`; on the band the kicker and `code` use
`--ds-eyebrow`, links `--ds-link`, and the primary glow `--ds-btn-primary-bg` @45%. The open-cycle card glow is `--teal` @10%.

Contrast check for a new theme (WCAG 2; take the hex values from `palette`; composite alpha colours over their background
first):

```js
// node -e "…" or a scratch .mjs file
const lum = (hex) => {
  const [r, g, b] = hex.replace("#", "").match(/../g).map((h) => {
    const c = parseInt(h, 16) / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};
const ratio = (fg, bg) => {
  const [hi, lo] = [lum(fg), lum(bg)].sort((a, b) => b - a);
  return (hi + 0.05) / (lo + 0.05);
};
// needs ≥ 4.5 small text, ≥ 3 large text (≥ 24px, or ≥ 18.7px bold) and UI / chart marks
console.log(ratio("#475569", "#ffffff").toFixed(2)); // app-soft light ds-body on ds-bg
```

Pairs to check per scope: `heading`, `body`, `body-strong`, `label`, `eyebrow`, `eyebrow-aa`, `highlight`, `icon` on
`bg`, `bg-cool`, `bg-warm`, `surface`, `surface-warm`, `callout`; each button `fg` on its `bg` (and hover pair);
`tile-active-fg` on `tile-active`; `tile-solid-fg` on `tile-solid`; `chart-*` on `chart-bg` (3:1); `border` around inputs (3:1).

## 9. Scrollbar tokens

`web/src/styles/design-system/scrollbar.css`, imported once in `web/src/main.tsx` after `app.css`. Plain CSS, no Tailwind,
every primitive has a hex fallback. One glass scrollbar for the site: transparent track, floating pill thumb (a transparent
ring plus `background-clip: padding-box`), translucent fill with a soft white sheen and a 1px hairline. The thumb grows from
6px to 8px under the pointer. The top comment of the file is the source of truth.

Shape tokens (on `:root`, inherited, so a subtree can override them on its own scroller):

| Token | Value | Meaning |
|---|---|---|
| `--scrollbar-size` | `12px` | hit area (width of a vertical bar, height of a horizontal one) |
| `--scrollbar-gap` | `3px` | transparent ring around the thumb (visible thumb = size - 2 x gap) |
| `--scrollbar-gap-hover` | `2px` | ring under the pointer / while dragged |
| `--scrollbar-radius` | `999px` | pill |
| `--scrollbar-min-thumb` | `40px` | shortest thumb |
| `--scrollbar-track` | `transparent` | track fill (transparent everywhere) |

Colour tokens per scope (rest / hover / active; the sheen and hairline are the other two):

| Scope (selector) | `--scrollbar-thumb` | `-thumb-hover` | `-thumb-active` |
|---|---|---|---|
| Main app (`:root`) | `--navy` 56% | `--teal` | `--teal-ink` |
| Dark sidebar (`.lcp-side`, `.lcp-drawer .ant-drawer-body`, `.json`) | white 36% | `#7fd1d5` 90% | `--teal-100` |
| Modal overlay (`.ant-modal-wrap` bar only; tokens `--scrollbar-overlay-thumb`, `-hover`, `-active`) | white 90% | white | `--teal-50` |
| navy-gold light (`[data-theme="navy-gold"]`, `[data-section="light"]`) | gold-600 96% | gold-700 90% | gold-700 |
| navy-gold dark (`[data-section="dark"]`) | gold-400 55% | gold-400 90% | gold-200 |
| app-soft light | soft-navy-950 55% | soft-gold-600 | soft-gold-700 |
| app-soft dark | soft-gold-300 55% | soft-gold-500 | soft-gold-300 |

`--scrollbar-highlight` (the sheen) is white 34% on light scopes and white 22% on dark ones; `--scrollbar-hairline` is the
scope's navy at 14-16% on light, white 12-14% on dark.

Rules:
- Page scrollbar: the `html` element is outside the theme root, so each theme also lists
  `html:has([data-ds-page][data-theme="<name>"])` in its light rule. Mark the page root with `data-ds-page` and
  `data-theme`. Without `:has()` support the page keeps the main-app palette.
- A new theme adds a light and a dark rule with the five colour tokens (`-thumb`, `-thumb-hover`, `-thumb-active`,
  `-highlight`, `-hairline`).
- Chromium and Safari use `::-webkit-scrollbar`; Chrome 121+ ignores it on elements that set `scrollbar-color` or
  `scrollbar-width`, so the stylesheet resets those on `.ant-table`, `.ant-segmented` and the date-picker time column.
- Firefox: `scrollbar-width: thin` + `scrollbar-color`, only inside `@supports not selector(::-webkit-scrollbar)`. No hover
  state, no shape.
- `forced-colors: active`: no custom styling; the browser draws system-coloured scrollbars.
- Hidden scrollers stay hidden: `.lcp-seg`, `.f-pipe`, `.sp-tabs`, `.sp-rail` (and antd Anchor, Tailwind `no-scrollbar`).
