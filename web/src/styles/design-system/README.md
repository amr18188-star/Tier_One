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
| `scrollbar.css` | Site-wide scrollbar in the colours of the surface it sits on: tokens per context (light, dark section, sidebar, modal mask), Firefox fallback. Imported once in `main.tsx`. |
| `theme.ts` | The switches: `ACTIVE_THEME` → `<html data-theme>`, `CORNERS` → `<html data-corners>`, before the first render; `useDsTheme()`, `setDsTheme()`, `useDsCorners()`, `setDsCorners()`. |
| `antd-theme.ts` | `antdTheme(name, base, { corners })`: theme tokens + corner scale → antd `ThemeConfig` (colours and radius). Fed to `ConfigProvider` in `App.tsx`. |
| `tokens.ts` | The same values as typed constants (antd needs real colours and px, not `var()`): `palette`, `themes`, `corners`. |
| `tokens.test.ts` | Parses the CSS and fails if `tokens.ts` differs; checks the AA pairs antd and the app use, and the shadcn mapping. |
| `../shadcn.css` | shadcn's Tailwind entry and the one shadcn variable → `--ds-*` mapping (see "shadcn"). Not imported yet. |

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
   `data-landing` scopes the CSS, `data-ds-page` + `data-theme` mark the themed page root, `dir`/`lang` set the
   direction (the landing is always RTL Arabic; a bilingual page would take them from `useLang()`).
   Without Tailwind, import `index.css` alone and use `var(--ds-…)`.
2. The site theme is already on `<html>` (`theme.ts`). A page root may repeat it (the landing gets it from `useDsTheme()`
   through `App.tsx`), but never with a different value. Switching the attribute re-themes the subtree.
3. Sections: the default scope is light. Put `data-section="dark"` on dark blocks (header, footer, dark sections, a dark card in a light section). `data-section="light"` flips back inside a dark block. The same class then gives the right colour in both: `text-ds-heading` is navy on light and white on dark.
4. Utilities:
   - Semantic (follow theme and section): `bg-ds-bg`, `bg-ds-bg-cool`, `bg-ds-bg-warm`, `bg-ds-surface`, `hover:bg-ds-surface-hover`, `border-ds-border`, `border-ds-accent-border`, `text-ds-heading`, `text-ds-highlight`, `text-ds-body`, `text-ds-eyebrow`, `text-ds-icon`, `bg-ds-tile`, `bg-ds-btn-primary-bg` / `-fg` / `-border` / `-hover-*`, `bg-ds-btn-ghost-*`, `stroke-ds-chart-*` …
   - Opacity modifiers work: `bg-ds-accent/10`, `border-ds-line-soft/15`.
   - Primitives (fixed colours): `bg-navy-900`, `text-gold-500`, `border-ash-200`.
   - Shape: `rounded-ds-xs` … `rounded-ds-xl`, `rounded-ds-pill`, `rounded-ds-round` (circles only), `rounded-ds` (= md);
     every side and corner form works (`rounded-s-ds-lg`, `rounded-t-ds-md` …). They follow `data-corners` (see "Corners").
   - Type and elevation: `font-ds`, `font-ds-heading`, `shadow-ds-sm`, `shadow-ds-md`.
   - Composite tokens for inline styles: `var(--ds-texture-hero)`, `var(--ds-texture-dark)`, `var(--ds-hero-vignette)`,
     and the gradients `var(--ds-gradient-*)` / `var(--ds-hatch-*)` (see "Gradients"; Tailwind: `bg-[image:var(--ds-gradient-hero)]`).
5. shadcn components follow the theme switch, the section (`data-section`) and the corner switch: `styles/shadcn.css`
   maps every shadcn variable to a `--ds-*` token (see "shadcn" below). Portalled content (dialogs, popovers, tooltips,
   select lists) renders under `<body>` with the `<html>` theme in its light scope: put `data-section="dark"` on the
   content element when it belongs to a dark block.

Rules: use `--ds-*` tokens in components, not primitives or hex. Do not nest two different `data-theme` values. `app.css` owns `--muted`; the shadcn muted surface is `--ui-muted`.

## shadcn

`styles/shadcn.css` is the one place that maps shadcn's variables to the design tokens. The theme files declare no
shadcn variable, so every theme (and every new one) works with shadcn with no extra lines. It is not imported yet; to
turn shadcn on, add one line in `src/main.tsx` after `import "./styles/app.css";`:

```ts
import "./styles/shadcn.css";
```

- **Where.** The mapping sits on `:where([data-theme], [data-section])`, in `@layer base`. A custom property resolves
  its `var()` where it is declared, so it is declared on every element that re-scopes the tokens: inside
  `data-section="dark"` it resolves to the dark-section tokens. `:where()` has specificity 0 and the block is layered,
  so it can never override a theme or page rule, whatever the import order (harness check: both orders compute the same).
- **Switches.** Theme: `ACTIVE_THEME` → `<html data-theme>`. Sections: `data-section`. Corners: `--radius` comes from
  `corners.css` (= `--ds-radius-lg`), `rounded-xs … xl` read `--ds-radius-xs … xl`, `2xl`–`4xl` are xl × 1.25 / 1.5 / 1.75:
  all 0 when square, never negative.
- **dark: variant.** There is no `.dark` class. shadcn's `dark:` classes (for example `dark:bg-input/30` on fields and
  outline buttons) apply inside the nearest `data-section="dark"`; a `data-section="light"` inside it flips back. Three
  levels deep (dark › light › dark) the colours are still right, only those `dark:` tweaks are skipped.
- **Text colour is inherited.** A dark band sets its own text colour (`text-ds-fg` or `color: var(--ds-fg)`), like the
  landing sections; outline, ghost buttons and typed text take it.
- **Scoped preflight.** There is no global preflight (antd and `app.css` keep their element styles). The base layer resets
  only shadcn parts (`[data-slot]`): box model, UA borders, and font, colour and fill of form controls. Without it the
  browser paints outline, ghost and link buttons grey with black Arial text, also on a navy band.

| shadcn variable | Token | Follows |
|---|---|---|
| `--background` | `--ds-bg` | section |
| `--foreground`, `--card-foreground`, `--popover-foreground` | `--ds-fg` | section |
| `--card`, `--popover` | `--ds-surface` | section |
| `--primary` / `--primary-foreground` | `--ds-btn-primary-bg` / `--ds-btn-primary-fg` | section |
| `--secondary`, `--ui-muted` (`bg-muted`) | `--ds-track` | section |
| `--secondary-foreground`, `--accent-foreground` | `--ds-heading` | section |
| `--muted-foreground` | `--ds-label` (AA on the muted surface too; `--ds-body` is not) | section |
| `--accent` (menu and select item focus) | `--ds-surface-hover` (antd `optionActiveBg`) | section |
| `--destructive` | `--ds-destructive` (antd `colorError`) | section |
| `--border`, `--input` | `--ds-border` (antd `colorBorder`) | section |
| `--ring` | `--ds-focus-ring` | section |
| `--chart-1` … `--chart-5` | `--ds-chart-1` … `--ds-chart-5` | theme |
| `--sidebar` / `--sidebar-foreground` | `--ds-sidebar-bg` / `--ds-sidebar-fg` | theme |
| `--sidebar-primary` / `-primary-foreground` | `--ds-sidebar-badge-bg` / `--ds-sidebar-badge-fg` | theme |
| `--sidebar-accent` / `-accent-foreground` | `--ds-sidebar-active-bg` / `--ds-sidebar-fg-active` | theme |
| `--sidebar-border`, `--sidebar-ring` | `--ds-sidebar-border`, `--ds-sidebar-focus` | theme |
| `--radius` | `--ds-radius-lg` (`corners.css`) | corners |

Resolved: navy-gold light: primary navy-900 on paper, ring navy-900; dark: primary gold-400 with navy-900 text, card
navy-800, ring gold-400. app-soft light: primary soft-navy-800, ring soft-navy-950; dark: primary soft-gold-500, card
soft-navy-800, ring soft-gold-300. Two tokens were added for this: `--ds-chart-5` (navy-gold gold-500, app-soft
soft-danger-600, the old `/app` fifth series) and `--ds-destructive` (light soft-danger-700; dark soft-danger-400 mixed
55% with paper, so red text and shadcn's 20% red tint stay AA on navy).

Charts: the series are theme level, made for light surfaces. On a navy card navy-gold's `--chart-1` (navy-800) is the
card colour itself; put charts on a light surface.

`tokens.test.ts` checks `shadcn.css`: no hex (comments included), every value is `var(--ds-*)`, it maps exactly the
variables its `@theme inline` reads, every target exists in every theme (section variables in both scopes), five
distinct chart series, radius steps never negative, and AA in both sections of both themes: text on background, card,
popover, primary, secondary, muted and accent, the destructive and outline button recipes (4.5:1), ring on background
and card (3:1), and the sidebar pairs.

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
| `colorError` | `destructive` (`--ds-destructive`, also shadcn `--destructive`) | soft-danger-700 |
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
`shared/theme.css`: `--band` is `var(--ds-gradient-hero)`; inside the band (`data-section="dark"`) the kicker
and `code` use `--ds-eyebrow`, links `--ds-link`, and the primary-button glow `--ds-btn-primary-bg` at 45%; the open-cycle
card glow is `--teal` at 10%. Every gradient in the app CSS reads a gradient token (see "Gradients"). Colours written
directly in non-gradient rules (some text and shadow colours) do not follow the theme. Radii in the app CSS all use the corner scale.

**Sidebar tokens** (theme level; the signed-in shell's navy sidebar): `--ds-sidebar-bg`, `-fg`, `-fg-active`, `-muted`,
`-hover-bg`, `-active-bg`, `-indicator`, `-border`, `-focus`, `-badge-bg`, `-badge-fg` (utilities `bg-ds-sidebar-bg` …),
and for its gradient `-bg-top` (top stop; `-bg` is the bottom) and `-glow` (top-corner light).

**Band tokens** (theme level; the page band's gradient): `--ds-band-from`, `-via`, `-to` (navy-gold: navy-900 → 800 → 700),
`--ds-band-glow` (gold-400 @14%, top start), `--ds-band-glow-accent` (gold-400 @18%, bottom end).

## Gradients

Every gradient in the app CSS reads one theme token, so changing the theme (or one token) re-colours all of them. The
tokens are theme level, in `themes/<name>.css`, as full `background` values (recipe + colours). In `tokens.ts` they are
`gradient`, `hatch` and `shade`, built by `gradients()` and `hatch()` with the same recipe.

| Token | navy-gold | app-soft | Used by |
|---|---|---|---|
| `--ds-gradient-hero` | 125° band from → via → to (navy-900 → 800 → 700), glow top start gold-400 @14%, glow bottom end gold-400 @18% | soft-navy-900 → 800 → 700, soft-navy-500 @38%, soft-gold-500 @18% | page band `.lcp-head` (through `--band`), Home hero `.lcp-hero`, plan hero `.sp-hero` |
| `--ds-gradient-sidebar` | 180° `--ds-sidebar-bg-top` navy-900 → `--ds-sidebar-bg` navy-950, `--ds-sidebar-glow` gold-400 @8% top right | soft-navy-900 → 950, soft-gold-500 @8% | `.lcp-side`, `.lcp-drawer .ant-drawer-body` (`shared/ui.css`, `app/nav.css`), `.verify-head` |
| `--ds-gradient-auth` | 160° band via → from → `--ds-shade` (navy-800 → 900 → 950), band glows | soft-navy-800 → 900 → 950, band glows | sign-in brand panel `.auth-side` |
| `--ds-gradient-avatar` | 135° navy-700 → navy-900 | soft-navy-700 → soft-navy-950 | `.lcp-avatar` |
| `--ds-gradient-progress` | 90° navy-900 → navy-700 | soft-navy-800 → soft-navy-500 | `.lcp-prog-bar i` |
| `--ds-gradient-callout` | 90° sand-50 → ivory-50 | soft-gold-50 → paper | `.lcp-next` |
| `--ds-gradient-surface-subtle` | 180° cloud → paper | soft-neutral-25 → paper | `.sp-chart` |
| `--ds-hatch-light` | 135° 6px stripes ash-100 / paper | soft-neutral-75 / paper | not-started timeline cells `.sp-road-cell.off` |
| `--ds-hatch-muted` | 135° 6px stripes steel-400 / mist-300 | soft-neutral-500 / soft-neutral-300 | elapsed-time bar `.cy-bar.time i` |
| `--ds-shade` (a colour) | navy-950 | soft-navy-950 | dark stop of the pillar gradients (below) |

- **Pillar colours are data.** `--pc` / `--pt` are set on each element by `tint()` (`shared/pillars.tsx`). A token on
  `[data-theme]` can't read them (a custom property resolves its `var()` where it is declared), so these rules keep their
  recipe and take only the other stop from a token: `.sp-pillar > header` and `.pill-band`
  `color-mix(in srgb, var(--pc) 72%, var(--ds-shade))`, `.sp-pillar.loose > header` 60%, `.st-Completed .sp-road-initbar` 70%,
  `.sp-dhead` `var(--pt) → var(--ds-surface)`. The status hatch `.sp-statebar .d-gap` keeps `--g-gap` and mixes it with `--ds-surface`.
- **Use** `background: var(--ds-gradient-hero)` in CSS, `style={{ background: "var(--ds-gradient-hero)" }}`, or
  `bg-[image:var(--ds-gradient-hero)]`. Never write a colour (hex, `rgb()`, a primitive) in a gradient outside a theme file.
- **New theme:** copy the gradient block of `app-soft.css`. Hero, sidebar and auth need no change (they read the band and
  sidebar tokens); set `--ds-shade`, `--ds-sidebar-bg-top`, `--ds-sidebar-glow` and the primitives inside avatar, progress,
  callout, surface-subtle and the two hatches. In `tokens.ts` pass the same colours to `gradients(band, sidebar, shade, { … })`
  and `hatch(a, b)`. Keep `--ds-sidebar-bg-top` darker than `--ds-sidebar-hover-bg`, and the sidebar glow at about 8% or less
  (the group titles must stay at 4.5:1 under it).
- **Checks** (`tokens.test.ts`): every `var()` of a gradient token is resolved and compared with `tokens.ts` both ways; a
  raw colour inside a gradient token fails; AA (4.5:1) for text on every stop, with the glows composited over the stop
  under them: heading, kicker (`--ds-eyebrow`) and link on the hero, heading on the sign-in panel, link, active link and
  group title on the sidebar, initials on the avatar, link and text on the callout, label on the chart panel; progress fill
  3:1 on the track. Lowest navy-gold values: hero kicker 4.6:1 (gold-400 on navy-700 + accent glow), sidebar group title
  4.7:1 (steel-400 under the glow).
- **Accents on navy, data marks** (theme level, no teal left in the app CSS): `--ds-accent-on-dark` (navy-gold gold-400,
  app-soft soft-gold-300; = dark-section `--ds-eyebrow`) for navy blocks without `data-section="dark"`: plan hero kicker,
  rings and rail dots, active navy tab icon. The Home hero kicker uses `--ds-eyebrow` (the hero is a dark section).
  `--ds-chart-college` (navy-900 / soft-navy-800, = light `--ds-btn-primary-bg`, so it matches the `--teal` bars) for the
  "college's own target" legend key; `--ds-status-review` (gold-600 / soft-gold-700, ≥ 3:1 on surface and track) for the
  "under review" segment on Home. Accent shadows mix the element's own colour: `color-mix(in srgb, var(--teal) N%, transparent)`.
- **New gradient role:** add `--ds-gradient-<role>` to every theme, the field to `GradientTokens` and `gradients()` in
  `tokens.ts`, and an AA case in `tokens.test.ts` if text sits on it.

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
| `--ds-radius-xs` | `rounded-ds-xs` | 0 | 4px | bars, swatches, small codes, active-link bars, scrollbar thumb |
| `--ds-radius-sm` | `rounded-ds-sm` | 0 | 6px | code pills, focus rings, small buttons, inner boxes |
| `--ds-radius-md` = `--ds-radius` | `rounded-ds-md`, `rounded-ds` | 0 | 8px | buttons, inputs, icon tiles, list rows (antd `borderRadius`) |
| `--ds-radius-lg` = shadcn `--radius` | `rounded-ds-lg` | 0 | 12px | cards, panels, tables, modals (antd `borderRadiusLG`) |
| `--ds-radius-xl` = legacy `--r` | `rounded-ds-xl` | 0 | 16px | page bands, large panels, hero |
| `--ds-radius-pill` | `rounded-ds-pill` | 0 | 999px | chips, count badges, progress bars, status dots |
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

`scrollbar.css`, imported once in `src/main.tsx` after `app.css`; its top comment is the full reference.
A scrollbar takes the colours of the surface it sits on. The track is that surface: it is transparent, so the
scroller's own background shows through (the sidebar gradient included). The thumb is a quiet tone of the same surface,
`color-mix(in srgb, SURFACE p%, INK)` with the surface's own text colour, one step stronger on hover and while dragged. It
fills the whole track width (no inset, no floating pill, no sheen) and its corner follows `CORNERS`
(`--ds-radius-xs`: square, or 4px when rounded).

Tokens: `--scrollbar-size` 10px (= thumb width), `--scrollbar-radius` `var(--ds-radius-xs)`, `--scrollbar-min-thumb`
40px, `--scrollbar-track` transparent, `--scrollbar-thumb` / `-thumb-hover` / `-thumb-active`, the sidebar palette
`--scrollbar-sidebar-thumb` / `-hover` / `-active`, and `--scrollbar-overlay-thumb` / `-hover` / `-active` (modal mask).

| Context | Selectors | SURFACE + INK | p rest / hover / active | navy-gold rest / hover | app-soft rest / hover |
|---|---|---|---|---|---|
| Light: the page, cards, tables, modal content, drawers, dropdowns | `:root`, `[data-theme]`, `[data-theme] [data-section="light"]` | `--ds-bg-cool` + `--ds-body` | 65 / 50 / 38 | `#c4cad5` / `#adb6c4` | `#b7bec8` / `#9ea6b2` |
| Dark sections: landing hero, navy bands | `[data-theme] [data-section="dark"]` | `--ds-bg` + `--ds-body` | 74 / 62 / 52 | `#31435a` / `#45566c` | `#435c7d` / `#59718f` |
| Sidebar, rail, mobile menu drawer, `.json` | `.lcp-side`, `.lcp-drawer .ant-drawer-body`, `.json` (tokens `--scrollbar-sidebar-thumb`, `-hover`, `-active`) | `--ds-sidebar-bg` + `--ds-sidebar-fg` | 76 / 66 / 58 | `#2d3c50` / `#3e4c60` | `#3b506a` / `#4e627b` |
| Page bar beside the sidebar (Arabic desktop, width > 960px) | `html[dir="rtl"]:has(.lcp > .lcp-side) > body` (WebKit), `html` (Firefox) | the sidebar palette; track = `--ds-gradient-sidebar` | 76 / 66 / 58 | as sidebar | as sidebar |
| Modal mask (`.ant-modal-wrap`, its own bar only) | tokens `--scrollbar-overlay-thumb`, `-hover`, `-active` | white over the mask | 35% / 55% / 70% | `#b2b2b3` / `#c9caca` | same |

Thumb vs track, measured in Chromium (rest / hover): navy-gold light 1.57 / 1.94 on the page, 1.65 / 2.05 on white;
dark 1.73 / 2.33 on `--ds-bg`, 1.52 / 2.05 on a dark card; sidebar 1.61-1.63 / 2.06-2.10 (also the page bar beside it);
app-soft light 1.74 / 2.29, dark 1.94 / 2.65, sidebar 1.86-1.89 / 2.46-2.49; modal mask 1.65 / 2.13. Target: about 1.5:1 at
rest (non-text UI that is present but quiet), stronger on hover.

- A new theme needs nothing here: every rule reads the theme's own tokens. A new dark container that is not a
  `[data-section="dark"]` block: add its selector to the sidebar rule, or give it its own SURFACE/INK pair.
- Override per container: `.my-list { --scrollbar-size: 6px; }` (the collapsed rail uses 6px, in `app/nav.css`).
- Chromium draws the page (viewport) scrollbar from `body`'s `::-webkit-scrollbar` parts, not `html`'s; tokens on `html`
  reach it by inheritance, a page-only override goes on `body`'s parts.
- Chrome 121+ ignores `::-webkit-scrollbar` on elements whose `scrollbar-color` / `scrollbar-width` is not `auto`, so the
  file resets them on `.ant-table`, `.ant-segmented` and the date-picker time column (Chromium branch only). Select,
  Cascader and TreeSelect virtual lists draw their own thumb: coloured through `--rc-virtual-list-scrollbar-bg`, corner
  forced over its inline 99px radius.
- Firefox: `scrollbar-width: thin` + `scrollbar-color` with the same colours (no hover state, no corner), only inside
  `@supports not selector(::-webkit-scrollbar)`. Browsers do not animate scrollbar parts: state changes are instant.
- `forced-colors: active`: no custom styling; the browser draws system-coloured scrollbars. Hidden scrollers stay hidden
  (`.lcp-seg`, `.f-pipe`, `.sp-tabs`, `.sp-rail`, antd Anchor, Tailwind `no-scrollbar`).

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
3. Give every `--ds-*` token a value in the theme-level, light and dark blocks. Keep every name; a missing token falls back to nothing.
   No shadcn variables: `styles/shadcn.css` maps them to these tokens for every theme (the test fails on any other name).
   No radius: corners come from `corners.css` whatever the theme. Gradients: keep the recipes, change the colours ("Gradients").
4. Import it in `index.css`, after the other themes and before `corners.css`.
5. Add `<name>` to `ThemeName` and `themes` in `tokens.ts`, then run `npx vitest run src/styles/design-system`: it fails
   on any value that differs from the CSS and on any AA pair below 4.5:1 (text) or 3:1 (UI). antd needs nothing else:
   `antdTheme` reads the new entry. Set `ACTIVE_THEME` in `theme.ts` (or `<html data-theme>`) to use it.

New semantic roles: add `--ds-<role>` to every theme, then `--color-ds-<role>: var(--ds-<role>);` to `tailwind-theme.css`,
and the field to `tokens.ts` (the test checks both ways).
