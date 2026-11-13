# Design system tokens

Multi-theme CSS tokens built from the Navy & Gold design system (`Frontend/React/HERO_DESIGN_SYSTEM.md`).
One theme drives the whole site (app, login screens, landing, antd): `theme.ts` puts it on `<html data-theme>` and loads
`index.css` globally; `scrollbar.css` is imported once in `src/main.tsx`; feature Tailwind entries (`landing.css`) import
the tokens again for their utilities. Corners are a second, independent axis: `CORNERS` in `config.ts` → `<html data-corners>`
(see "Corners" below).

| File | What it holds |
|---|---|
| `primitives.css` | Raw palettes on `:root` (`--color-navy-900`, `--color-gold-400`, `--color-soft-*` …). |
| `themes/navy-gold.css` | `[data-theme="navy-gold"]`: the landing look, Cairo. Pixel-identical to the old `Home.jsx` with square corners. |
| `themes/app-soft.css` | `[data-theme="app-soft"]`: the old `/app` palette (navy #0d2440, gold #c9a227). |
| `corners.css` | The corner scale `--ds-radius-xs … xl`, `-pill`, `-round` for `data-corners="square" \| "rounded"`. |
| `config.ts` | `CORNERS`: the one line that makes the whole site square or rounded. |
| `index.css` | Plain-CSS entry: primitives, both themes, then `corners.css`. Imported globally by `theme.ts`. |
| `tailwind-theme.css` | Tailwind v4 `@theme inline reference` mapping, for a Tailwind entry. |
| `scrollbar.css` | Site-wide glass scrollbar: tokens, per-theme colours, Firefox fallback. Imported once in `main.tsx`. |
| `theme.ts` | The switches: `ACTIVE_THEME` → `<html data-theme>`, `CORNERS` → `<html data-corners>`, before the first render; `useDsTheme()`, `setDsTheme()`, `useDsCorners()`, `setDsCorners()`. |
| `antd-theme.ts` | `antdTheme(name, base, { corners })`: theme tokens + corner scale → antd `ThemeConfig` (colours and radius). Fed to `ConfigProvider` in `App.tsx`. |
| `tokens.ts` | The same values as typed constants (antd needs real colours and px, not `var()`): `palette`, `themes`, `corners`. |
| `tokens.test.ts` | Parses the CSS and fails if `tokens.ts` differs; checks the AA pairs antd and the app use. |

## Use

1. Tailwind entry: copy `src/features/landing/landing.css` (the working one) and rename `data-landing`. The skeleton:
   ```css
   @layer theme;
   @import "tailwindcss/theme.css" layer(theme);
   @import "../../styles/design-system/index.css";
   @import "../../styles/design-system/tailwind-theme.css";
   @source "./";                                    /* with source(none) below: scan only this folder */

   [data-landing] { /* root text props: font, 1rem size, line-height, colour, … */ }
   [data-landing] :where(*), [data-landing] ::before, [data-landing] ::after { box-sizing: border-box; margin: 0; padding: 0; border: 0 solid var(--ds-border); }
   [data-landing] :where(a) { color: inherit; text-decoration: none; }   /* and h1-h6, ul, img, button … */
   [data-landing] :where(a, button, [role="button"], [tabindex]):focus-visible { outline: auto 1px var(--ds-focus); }

   [data-landing] {
     @tailwind utilities source(none);              /* every utility becomes `[data-landing] .class` */
   }
   ```
   Why:
   - Only custom properties (Tailwind theme variables and `--ds-*`) are global. Vite never unloads CSS, so any global class
     or element rule would restyle the login screen and the antd pages for the rest of the session.
   - Utilities are scoped as `[data-landing] .class` (0,2,0) and left unlayered. `app.css` is unlayered, and an unlayered
     rule beats every `@layer` rule, so layered utilities (the old `utilities.css layer(utilities)` entry) lose to it
     and leak to other pages.
   - antd injects global link rules (`:where(.css-…) a:hover`, 0,1,1); the 0,2,0 scope beats them, `:where()` scoping would not.
   - `ui.css` draws a teal `:focus-visible` ring on links and buttons; the focus rule above overrides it.
   - `source(none)` + `@source "./"` keep the scan to the feature folder. No preflight is imported.
   - html stays 16px: the rem utilities assume it, and `.ant-app` is 14px, so the root sets `font-size: 1rem` itself.

   Page root, as in `LandingPage.tsx`:
   ```tsx
   <div data-landing="" data-ds-page="" data-theme="navy-gold" dir="rtl" lang="ar">
     <div dir="rtl" className="min-h-screen w-full bg-ds-bg text-ds-fg">{/* utilities go on this inner element */}</div>
   </div>
   ```
   `data-landing` scopes the CSS, `data-ds-page` + `data-theme` let the page scrollbar follow the theme, `dir`/`lang` set the
   direction (the landing is always RTL Arabic; a bilingual page would take them from `useLang()`).
   Without Tailwind, import `index.css` alone and use `var(--ds-…)`.
2. The site theme is already on `<html>` (`theme.ts`). A page root may repeat it (the landing gets it from `useDsTheme()`
   through `App.tsx`, so its scrollbar rule matches), but never with a different value. Switching the attribute re-themes the subtree.
3. Sections: the default scope is light. Put `data-section="dark"` on dark blocks (header, footer, dark sections, a dark card in a light section). `data-section="light"` flips back inside a dark block. The same class then gives the right colour in both: `text-ds-heading` is navy on light and white on dark.
4. Utilities:
   - Semantic (follow theme and section): `bg-ds-bg`, `bg-ds-bg-cool`, `bg-ds-bg-warm`, `bg-ds-surface`, `hover:bg-ds-surface-hover`, `border-ds-border`, `border-ds-accent-border`, `text-ds-heading`, `text-ds-highlight`, `text-ds-body`, `text-ds-eyebrow`, `text-ds-icon`, `bg-ds-tile`, `bg-ds-btn-primary-bg` / `-fg` / `-border` / `-hover-*`, `bg-ds-btn-ghost-*`, `stroke-ds-chart-*` …
   - Opacity modifiers work: `bg-ds-accent/10`, `border-ds-line-soft/15`.
   - Primitives (fixed colours): `bg-navy-900`, `text-gold-500`, `border-ash-200`.
   - Shape: `rounded-ds-xs` … `rounded-ds-xl`, `rounded-ds-pill`, `rounded-ds-round` (circles only), `rounded-ds` (= md);
     every side and corner form works (`rounded-s-ds-lg`, `rounded-t-ds-md` …). They follow `data-corners` (see "Corners").
   - Type and elevation: `font-ds`, `font-ds-heading`, `shadow-ds-sm`, `shadow-ds-md`.
   - Composite tokens for inline styles: `var(--ds-texture-hero)`, `var(--ds-texture-dark)`, `var(--ds-hero-vignette)`.
5. shadcn components inside a themed subtree follow it (`--background`, `--primary`, `--ui-muted`, `--chart-*`, `--sidebar-*` are mapped per theme and per section; `--radius` comes from `corners.css`). Portalled content (dialogs, popovers, tooltips) renders outside the subtree: give its content element the same `data-theme`, or set it on `<html>`.

Rules: use `--ds-*` tokens in components, not primitives or hex. Do not nest two different `data-theme` values. `app.css` owns `--muted`; the shadcn muted surface is `--ui-muted`.

## Site theme and antd

- **One switch.** `theme.ts`: `ACTIVE_THEME = "navy-gold"`. When the module loads (imported by `App.tsx`, before the first
  render) it sets `<html data-theme>` and imports `index.css`. Every `--ds-*` token, the legacy aliases below, antd
  portals (modals, drawers, dropdowns, messages under `<body>`), the landing and the scrollbar follow it. `useDsTheme()`
  watches the attribute (MutationObserver), so changing it at run time (devtools, or `setDsTheme("app-soft")`) re-themes antd too.
- **Mapper.** `antdTheme(name, base, { tone })` in `antd-theme.ts`. Colours and radius only, every value from `tokens.ts`;
  `base` carries App.tsx's fonts (IBM Plex Sans Arabic, 14px) unchanged. A new theme re-themes antd as soon as it has its
  entry in `tokens.ts`; nothing in the mapper names a theme. `tone: "dark"` builds a full dark theme for a nested
  `ConfigProvider` (dark-section tokens + `darkAlgorithm`); nothing uses it yet.
- **Navy blocks on light pages.** The page band (`PageHeader`, `.lcp-head`) and the home hero (`Hero`, `.lcp-hero`) in
  `shared/ui.tsx` carry `data-section="dark"`, and their actions sit in `DarkBlock`: a nested `ConfigProvider` with
  `antdDarkBlockTheme(useDsTheme())`. That sets only Button colours from the dark-section tokens (primary gold-400 with
  navy-900 text, links gold-400). Fields keep the page theme, and radius comes from the parent provider.
- **Radius** comes from the corner style, not the theme: `antdTheme(name, base, { corners })` (App.tsx passes
  `useDsCorners()`, default `CORNERS`) reads `corners[style]` in `tokens.ts` through `antdRadii(scale)`: `borderRadius` = md,
  `borderRadiusLG` = lg, `SM` = sm, `XS` = xs, `Outer` = xs; Table `headerBorderRadius` = lg, `stickyScrollBarBorderRadius`
  and Progress `lineBorderRadius` = pill. Square: all 0. Rounded: 8 / 12 / 6 / 4 / 4, pill 999.

| antd token | From `themes[name]` (light section unless noted) | navy-gold |
|---|---|---|
| `colorPrimary`, Button `primaryColor` | `btnPrimary.bg`, `btnPrimary.fg` | navy-900, paper |
| `colorPrimaryBorder` (antd's `:focus-visible` outline) | `focusRing` | navy-900 |
| `colorLink`, `colorLinkHover` / `Active` | `link`, `linkHover` | gold-700, navy-900 |
| `colorInfo`, `colorInfoBg`, `colorInfoBorder` | `heading`, `bgCool`, `border` | navy-900, cloud, ash-200 |
| `colorError` | `destructive` (`--destructive`) | soft-danger-700 |
| `colorSuccess`, `colorWarning` | antd defaults (no AA status token yet) | — |
| `colorText`, `colorTextHeading`, `colorTextSecondary` | `fg`, `heading`, `label` | navy-900, navy-900, ash-600 |
| `colorBorder`, `colorBorderSecondary` | `border` | ash-200 |
| `colorBgLayout`, `colorBgContainer`, `colorBgElevated` | `bgCool`, `surface`, `surface` | cloud, paper, paper |
| `colorBgSpotlight` (tooltips) | `sections.dark.bg` | navy-900 |
| `borderRadius*`, Table header, Progress bar | `corners[style]` via `antdRadii` (not the theme) | 0 (square) |
| Button default: colour, bg, border, hover / active | `btnGhost.fg`, `surface`, `btnGhost.border`, `btnGhost.hover*` | navy-900, paper, navy-900 @20%, gold-400 fill + navy text |
| Input, InputNumber, DatePicker, Select: hover / active border, focus shadow | `accentStrong`, `focusRing`, `focus` | gold-500, navy-900, soft-gold-500 @50% |
| Select option selected / active | `tile` + `heading`, `surfaceHover` | sand-50, ivory |
| Table header, sort / filter hover, split | `sections.dark`: `bg` + `heading`, `surface` / `surfaceHover`, `border` | navy-900 + paper, navy-800 / navy-700, navy-500 |
| Table row hover, sorted column, borders | `surfaceHover`, `bgCool`, `border` | ivory, cloud, ash-200 |
| Tag default | `bgCool`, `fg` | cloud, navy-900 |
| Modal, Drawer | `surface`; title `heading` | paper; navy-900 |
| Tabs: item, hover / selected, ink bar | `label`, `heading`, `icon` (≥ 3:1) | ash-600, navy-900, gold-600 |
| Segmented: track, item, selected | `track`, `label`, `btnPrimary.bg` / `.fg` | ash-100, ash-600, navy-900 / paper |

The app does not use antd Menu or Layout (the shell is custom), so they have no tokens.

**Legacy aliases.** The brand variables of the antd app are aliases of light-section tokens, with the old hex as the
`var()` fallback (used only if no theme is loaded):

| Legacy variable | Token | navy-gold |
|---|---|---|
| `--navy` (app.css) | `--ds-heading` | navy-900 |
| `--navy-2` | `--ds-chrome` | navy-950 |
| `--teal` | `--ds-btn-primary-bg` | navy-900 |
| `--teal-ink` | `--ds-link` | gold-700 |
| `--bg` | `--ds-bg-cool` | cloud |
| `--surface` | `--ds-surface` | paper |
| `--sunk` | `--ds-track` | ash-100 |
| `--ink` | `--ds-fg` | navy-900 |
| `--muted` | `--ds-label` | ash-600 |
| `--line` | `--ds-border` | ash-200 |
| `--r` (shared/ui.css and shared/theme.css) | `--ds-radius-xl` (corner scale) | 0 square, 16px rounded |
| `--teal-50` | `--ds-tile` | sand-50 |
| `--teal-100` | `--ds-accent` | gold-400 |
| `--navy-50` | `--ds-track` | ash-100 |
| `--gold` | `--ds-accent` | gold-400 |
| `--gold-50` | `--ds-bg-warm` | sand-200 |
| `--gold-ink` | `--ds-eyebrow-aa` | gold-700 |
| `--row-alt` | `--ds-bg-cool` | cloud |
| `--hair` | `--ds-border` | ash-200 |
| `--teal`, `--teal-100`, `--gold` inside `.lcp-side` / `.lcp-drawer .ant-drawer-body` | `--ds-sidebar-indicator`, `-focus`, `-badge-bg` | gold-400 |

Unchanged on purpose: status colours (`--warn*`, `--ok*`, `--crit*`), fonts, `--shadow*`, `--space-*`, `--side-w`.
`shared/theme.css`: `--band` is built from the `--ds-band-*` tokens; inside the band (`data-section="dark"`) the kicker
and `code` use `--ds-eyebrow`, links `--ds-link`, and the primary-button glow `--ds-btn-primary-bg` at 45%; the open-cycle
card glow is `--teal` at 10%. Other colours written directly in rules (not through a variable) do not follow the
theme (for example the home hero background in `shared/ui.css`). Radii in the app CSS all use the corner scale.

**Sidebar tokens** (theme level; the signed-in shell's navy sidebar): `--ds-sidebar-bg`, `-fg`, `-fg-active`, `-muted`,
`-hover-bg`, `-active-bg`, `-indicator`, `-border`, `-focus`, `-badge-bg`, `-badge-fg` (utilities `bg-ds-sidebar-bg` …).

**Band tokens** (theme level; the page band's gradient): `--ds-band-from`, `-via`, `-to` (navy-gold: navy-900 → 800 → 700),
`--ds-band-glow` (steel-400 @30%, top start), `--ds-band-glow-accent` (gold-400 @18%, bottom end).

## Corners (square | rounded)

**Change this one line** in `config.ts`:

```ts
export const CORNERS: CornerStyle = "square"; // or "rounded"
```

`theme.ts` puts it on `<html data-corners>` before the first render. Without TypeScript, set the attribute in devtools or
override in CSS: `:root[data-corners="rounded"] { … }`. No attribute means square. A live change of the attribute
re-themes antd too (`useDsCorners()` watches it, like `useDsTheme()`).

| Token | Utility | square | rounded | Use |
|---|---|---|---|---|
| `--ds-radius-xs` | `rounded-ds-xs` | 0 | 4px | bars, swatches, small codes, active-link bars |
| `--ds-radius-sm` | `rounded-ds-sm` | 0 | 6px | code pills, focus rings, small buttons, inner boxes |
| `--ds-radius-md` = `--ds-radius` | `rounded-ds-md`, `rounded-ds` | 0 | 8px | buttons, inputs, icon tiles, list rows (antd `borderRadius`) |
| `--ds-radius-lg` = shadcn `--radius` | `rounded-ds-lg` | 0 | 12px | cards, panels, tables, modals (antd `borderRadiusLG`) |
| `--ds-radius-xl` = legacy `--r` | `rounded-ds-xl` | 0 | 16px | page bands, large panels, hero |
| `--ds-radius-pill` | `rounded-ds-pill` | 0 | 999px | chips, count badges, progress bars, status dots, scrollbar thumb |
| `--ds-radius-round` | `rounded-ds-round` | 50% | 50% | real circles only: avatars, step numbers, chart dots |

**Themes and corners combine freely.** Themes hold colours only; no theme file declares a radius (`tokens.test.ts` fails if
one does). navy-gold was designed square and app-soft comes from the rounded `/app`, but any theme works with either
style: `navy-gold` + `rounded`, `app-soft` + `square`. The two switches are `ACTIVE_THEME` (theme.ts) and `CORNERS` (config.ts).

**What follows the switch:** antd (global `borderRadius*` tokens, Table header, sticky scrollbar, Progress bar; every antd
component that reads them: Button, Input, Select and its dropdown, DatePicker, Tag, Segmented, Modal, Popover, Tooltip,
Card, Checkbox, Skeleton …), shadcn (`--radius` and `rounded-xs … 4xl` in `styles/shadcn.css`, all from the scale, never
negative), Tailwind `rounded-ds*`, the landing (cards, icon tiles, CTA buttons, chips, bars, dots), the scrollbar thumb,
and the legacy CSS (`--r`, `styles/app.css`, `shared/ui.css`, `shared/theme.css`, `shared/tier1.css`, feature CSS).
The old px values were mapped to the nearest step: ≤4px xs, 5–6 sm, 7–9 md, 10–13 lg, ≥14 xl, 99px/999px and
half-height count badges pill, 50% round.

**Stays the same in both styles:** real circles (`--ds-radius-round`), antd internals with no radius token (Switch track,
Radio, Badge dot and count, Spin, Timeline dots, Drawer edges), shadcn's own `rounded-full` and fixed `rounded-[2px]` /
`[4px]` in `src/components/ui`, and hairline grids on the landing (`gap-px` over `bg-ds-border` with filled cells: they
need `overflow-hidden` before they can be rounded).

New rules: use a scale token (`var(--ds-radius-lg)`, `rounded-ds-lg`), never a px radius; `--ds-radius-round` only for
true circles. A subtree can take its own style (`<div data-corners="rounded">`): the `--ds-radius-*` tokens, `rounded-ds*`
and the scrollbar follow it; antd and the legacy `--r` follow `<html>` only.

## Scrollbar

`scrollbar.css` holds the tokens; its top comment is the full reference. Tokens: `--scrollbar-size` 12px, `-gap` 3px,
`-gap-hover` 2px, `-radius` `var(--ds-radius-pill)` (square thumb when square, glass pill when rounded), `-min-thumb` 40px, `-track` transparent, `-thumb`, `-thumb-hover`, `-thumb-active`,
`-highlight`, `-hairline`, `-overlay-thumb`, `-overlay-thumb-hover`, `-overlay-thumb-active`.

| Scope | Thumb rest / hover / active |
|---|---|
| Main app (`:root`) | navy 56% / teal / teal-ink |
| Dark sidebar (`.lcp-side`, drawer body, `.json`) | white 36% / `#7fd1d5` 90% / teal-100 |
| Modal overlay (`.ant-modal-wrap` bar) | white 90% / white / teal-50 |
| navy-gold light | gold-600 96% / gold-700 90% / gold-700 |
| navy-gold dark (`data-section="dark"`) | gold-400 55% / gold-400 90% / gold-200 |
| app-soft light | soft-navy-950 55% / soft-gold-600 / soft-gold-700 |
| app-soft dark | soft-gold-300 55% / soft-gold-500 / soft-gold-300 |

- The page scrollbar follows the theme through `html:has([data-ds-page][data-theme="navy-gold"])` (same for `app-soft`).
  Keep `data-ds-page` and `data-theme` on the page root. A new theme adds a light and a dark rule with the five colour tokens.
- Firefox gets `scrollbar-width: thin` + `scrollbar-color` inside `@supports not selector(::-webkit-scrollbar)` (no hover, no shape).
- Hidden scrollers stay hidden (`.lcp-seg`, `.f-pipe`, `.sp-tabs`, `.sp-rail`). `forced-colors: active` gets the system scrollbar.

## Reference implementation

`src/features/landing/`: `LandingPage.tsx` (root and imports), `landing.css` (Tailwind entry), `LandingChrome.tsx` (header and
footer), `HeroSection.tsx`, `OverviewSection.tsx`, `PillarsSection.tsx`, `AnalyticsSection.tsx`, `HierarchySection.tsx`,
`DataManagementSection.tsx`, `IntegrationSection.tsx`, `CtaSection.tsx`, `SectionHeading.tsx`, `Icon.tsx`, `charts.tsx`,
`sections.ts`.

The landing matches the original `Home.jsx` except for three deliberate user changes: quarters wording instead of seasons,
the redesigned footer, and the analytics section (القيادة bar removed, horizontal KPI rows, RTL-mirrored trend chart).

## Logos

`src/assets/brand/capital-university-logo-dark.png` is white + gold: use it on dark backgrounds (`data-section="dark"`).
`capital-university-logo-light.png` is navy + gold: use it on light backgrounds. Import them as Vite assets.

## Add a theme in ~5 steps

1. Add any new raw colours to `primitives.css` as `--color-<family>-<step>`. Avoid names Tailwind already uses (`slate`, `white`, `mist` …).
2. Copy `themes/app-soft.css` to `themes/<name>.css` and replace `app-soft` in its three selectors.
3. Give every `--ds-*` and shadcn variable a value in the theme-level, light and dark blocks. Keep every name; a missing token falls back to nothing.
   No radius: corners come from `corners.css` whatever the theme.
4. Import it in `index.css`, after the other themes and before `corners.css`.
5. Add `<name>` to `ThemeName` and `themes` in `tokens.ts`, then run `npx vitest run src/styles/design-system`: it fails
   on any value that differs from the CSS and on any AA pair below 4.5:1 (text) or 3:1 (UI). antd needs nothing else:
   `antdTheme` reads the new entry. Set `ACTIVE_THEME` in `theme.ts` (or `<html data-theme>`) to use it.

New semantic roles: add `--ds-<role>` to every theme, then `--color-ds-<role>: var(--ds-<role>);` to `tailwind-theme.css`,
and the field to `tokens.ts` (the test checks both ways).
