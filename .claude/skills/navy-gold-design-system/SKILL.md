---
name: navy-gold-design-system
description: "Navy & Gold design system and multi-theme CSS tokens for the Tier1 university performance app (Context/Tier1/web: React, antd, Tailwind v4, shadcn/ui). Use it for any visual or styling work in Tier1, in Arabic or English: colours, a theme or a second style system, design tokens, data-theme switching, a landing or hero section, dark and light sections, RTL pages, Tailwind or shadcn styling, cards, buttons, stat tiles, chart colours, 'make it look like the landing page', or an antd theme from the tokens. Arabic cues: ألوان، ثيم، هوية بصرية، ستايل، تصميم الصفحة، صفحة هبوط، هيرو، الوضع الداكن. Also for the runtime Appearance panel (gear in the top bar: each viewer picks the palette and the corner style, saved in localStorage t1.appearance; AppearanceProvider, useAppearance; المظهر، لوحة الألوان), for square, rounded or smooth corners (default CORNERS, --ds-radius-* scale, rounded-ds-*; زوايا حادة أو مستديرة أو ناعمة) and for gradients (hero, band, sidebar, hatches: --ds-gradient-* tokens per theme; تدرج الألوان). Gives the rules (data-theme + data-section + --ds-* tokens, never raw hex, rounded-ds-* radius steps, hairlines, logical RTL utilities), the Tailwind entry without global preflight that keeps antd pages unchanged, shadcn inside a themed subtree, recipes, and how to add a theme. Use it even for 'style this page' or 'change the colours' on a Tier1 screen."
---

# Navy & Gold design system (Tier1)

Tier1's frontend has one set of semantic colour tokens (`--ds-*`) and several themes that fill them. A page turns a
theme on with `data-theme`, and marks its dark blocks with `data-section="dark"`. Components name roles only
(`text-ds-heading`, `bg-ds-surface`), so the same JSX renders as **navy-gold** (the landing look: Cairo, navy
and antique gold) or **app-soft** (the old `/app` palette: softer navy). A new style system is one more
theme file, with no component changes. Corners are a separate axis on `<html data-corners>`: `"square"` (the default,
`CORNERS` in `config.ts`, the navy-gold choice), `"rounded"` or `"smooth"`; any theme works with any style (section
"Corners"). Each viewer picks the theme and the corner style at run time in the Appearance panel (gear in the top bar), saved
in the browser; the public landing always keeps navy-gold + square (section "Runtime appearance").

Paths are relative to the Tier1 root: `F:\Projects\angular\Indecators\Context\Tier1`.

## Where things are

| Path | What |
|---|---|
| `web/src/styles/design-system/README.md` | Short usage and add-a-theme steps |
| `web/src/styles/design-system/primitives.css` | Raw palettes on `:root` (`--color-navy-900` …). The only global file |
| `web/src/styles/design-system/themes/navy-gold.css` | `[data-theme="navy-gold"]`: `--ds-*` tokens only, light and dark scopes |
| `web/src/styles/design-system/themes/app-soft.css` | `[data-theme="app-soft"]`: same names, the softer palette |
| `web/src/styles/design-system/corners.css` | The corner scale `--ds-radius-xs … xl`, `-pill`, `-round` for `data-corners="square" \| "rounded" \| "smooth"` |
| `web/src/styles/design-system/config.ts` | `CORNERS`: the default corner style (square) |
| `web/src/styles/design-system/index.css` | Plain-CSS entry: primitives, every theme, then `corners.css` |
| `web/src/styles/design-system/tailwind-theme.css` | Tailwind v4 `@theme inline reference`: `bg-ds-*`, `rounded-ds-*`, `font-ds`, `shadow-ds-*`, primitives |
| `web/src/styles/design-system/theme.ts` | The appearance store, on `<html>` before first render: the viewer's `{ theme, corners }` (localStorage `t1.appearance`, defaults `ACTIVE_THEME` + `CORNERS`) → `data-theme` / `data-corners`; `setAppearance()`, `resetAppearance()`, `pinAppearance()`, `BRAND_APPEARANCE`; `useDsTheme()` / `useDsCorners()` (what `<html>` shows); imports `index.css` globally |
| `web/src/styles/design-system/appearance.tsx` | `AppearanceProvider` (wraps the app, builds the antd theme), `useAppearance()`, `usePinnedAppearance()` / `<PinnedAppearance>` |
| `web/src/app/AppearanceMenu.tsx` + `appearance.css` | The Appearance gear in the top bar (`app/Shell.tsx`) and its panel (Popover on desktop, bottom sheet on phones) |
| `web/src/styles/design-system/antd-theme.ts` | `antdTheme(name, base, { tone, corners })`: theme tokens + corner scale → antd `ThemeConfig` (colours, radius) |
| `web/src/styles/design-system/tokens.ts` | Same values as typed TS (antd needs real colours and px): `palette`, `themes`, `corners`; panel registry `themeLabels`, `cornerLabels`, `pillButtons`; `tokens.test.ts` proves it equals the CSS and checks AA pairs |
| `web/src/features/landing/` | The landing page port: the live reference implementation (file list below) |
| `web/src/styles/design-system/scrollbar.css` | Scrollbar tokens and drawing, imported once in `main.tsx` (see Scrollbar) |
| `web/src/styles/shadcn.css` | shadcn's Tailwind entry and the one shadcn variable → `--ds-*` mapping, for every theme (not imported yet) |
| `web/src/components/ui/` | 60 shadcn (radix-nova, RTL) components |
| `web/src/shared/i18n.ts` | Sets `<html lang dir>`; `useLang()`, `L(ar, en)` |
| `web/src/styles/app.css`, `web/src/shared/ui.css`, `web/src/shared/tier1.css`, `web/src/shared/theme.css` | The antd app's styles. The brand variables in the `:root` blocks of `app.css` and `ui.css` are aliases of `--ds-*` (see antd pages) |
| `web/src/app/App.tsx` | `<AppearanceProvider base={fonts}>` around the app (antd follows the look on `<html>`); the landing route inside `<PinnedAppearance look={BRAND_APPEARANCE}>` |

Token files are shared by every page. Edit them only when the task is about tokens (a new role, a new theme), never to
fix one component.

## The rules

1. **Colour only through tokens.** Use a semantic utility (`bg-ds-surface`, `text-ds-body`, `border-ds-border`,
   `stroke-ds-chart-1`) or `var(--ds-…)` in `style`. No hex, no `bg-[#…]`, no `rgb()`, and no Tailwind palette colour
   (`text-slate-500`, `bg-white`) on themed UI. A hard-coded colour can't follow `data-theme` or `data-section`, so the
   second theme breaks silently. Free values: `transparent`, `currentColor` / `bg-current`, `inherit`.
2. **`--ds-*` in components, primitives only in theme files.** `--color-navy-900` and `bg-navy-900` are fixed paint;
   themes map them to roles. If no role fits, add one (below) instead of reaching for a primitive.
3. **One `data-theme`, a `data-section` per dark block.** The site theme is on `<html>` (`theme.ts`: the viewer's
   Appearance choice, `ACTIVE_THEME` by default). A page root may repeat the same value (take it from `useDsTheme()`),
   never a different one; a page with its own fixed look pins it on `<html>` with `usePinnedAppearance(look)` (the landing). Light is the default scope. Put `data-section="dark"` on the header, footer, dark sections, and any dark card
   inside a light section; `data-section="light"` flips back inside a dark block. One class then gives the right value
   in both: `text-ds-heading` is navy on light and white on dark. So no `dark ? … : …` colour ternaries. Never nest two
   different `data-theme` values: both themes' section rules would match.
4. **Shape through the corner scale.** `rounded-ds-xs|sm|md|lg|xl|pill` (CSS: `var(--ds-radius-<step>)`): md for
   buttons, inputs and icon tiles, lg for cards and panels, xl for big panels, pill for chips, badges, bars and status
   dots, xs for swatches. All 0 when square, 4/6/8/12/16/999px when rounded, 8/12/16/20/24/999px when smooth.
   `rounded-ds-round` (50% in every style) only for real circles. Fixed radii (`rounded-lg`, `rounded-full`, `border-radius: 10px`) ignore the switch.
   Don't round a hairline grid without `overflow-hidden`: its filled cells would poke past the curve.
5. **Hairlines, not shadows.** 1px `border-ds-border`. Card groups as a hairline grid: parent
   `grid gap-px border border-ds-border bg-ds-border`, children `bg-ds-surface`. Accent rules:
   `border-s-2 border-ds-accent`. Shadows only `shadow-ds-sm` / `hover:shadow-ds-md`, only on light (Tailwind's own
   `shadow-sm` is a grey default, not the token).
6. **Gold means one thing per view.** The highlight word, the primary button, icons, thin rules. Never a large gold
   fill. Green and red only for status (`bg-ds-live`, `ds-chart-up` / `ds-chart-down`).
7. **Type.** `font-ds` on the root (Cairo stack), `font-ds-heading` on headings. Headings line height ≥ 1.5, Arabic
   body ≥ 1.75 (`leading-8` on 14–16px). Cairo must be loaded: it comes from `@fontsource/cairo` (added by the landing
   port; check `web/package.json`). Import the weights you use, Arabic and Latin subsets, in the feature's root component,
   as the landing does. Without it the stack falls back to Segoe UI.
8. **RTL first, LTR ready.** `web/src/shared/i18n.ts` sets `<html dir lang>` from the user's language; a DS root sets
   `dir` and `lang` from `useLang()`. Use logical utilities (`ps-4`, `me-2`, `border-s-4`, `text-start`, `start-0`,
   `rounded-s-ds`), never `pr-` / `border-r` / `text-right` in new code. Forward arrows point left in RTL: mirror with
   `ltr:-scale-x-100`, nudge with `rtl:group-hover:-translate-x-1 ltr:group-hover:translate-x-1`. Text through `L(ar, en)`.
9. **Contrast.** navy-gold keeps some of the original's low-contrast pairs on purpose (pixel identity with the landing).
   Don't copy them into new UI: small gold text on light uses `text-ds-eyebrow-aa`; readable text uses `text-ds-body`,
   not `text-ds-faint`. Light-scope `text-ds-highlight` (gold-500) fails 3:1 even for large text, and no AA highlight
   token exists yet: ask before adding one. Measured list: `references/design-system.md` §11.1.

## Tailwind without global preflight

Each feature that uses the design system has its own Tailwind entry, imported by the feature's root component (a lazy
route), never by `main.tsx`, `app.css` or a shared file. Everything is scoped to a root attribute of the feature
(`data-<feature>`; the landing uses `data-landing`). The working file is `web/src/features/landing/landing.css`; copy it
and rename the attribute. Full file with the scoped base: recipe 1 in `references/component-recipes.md`.

```css
/* web/src/features/<feature>/<feature>.css (skeleton) */
@layer theme;
@import "tailwindcss/theme.css" layer(theme);
@import "../../styles/design-system/index.css";
@import "../../styles/design-system/tailwind-theme.css";
@source "./";
/* [data-feature] root text props, then [data-feature] :where(…) base rules, then the focus rule: recipe 1 */
[data-feature] {
  @tailwind utilities source(none); /* every utility compiles to `[data-feature] .class`, unlayered */
}
```

Root element (copied from `LandingPage.tsx`): `<div data-landing="" data-ds-page="" data-theme="navy-gold" dir="rtl" lang="ar">`
with the utility classes on the first inner `<div>` (utilities match descendants only).

Why it looks like this:
- **No preflight.** `@import "tailwindcss"` includes it, and preflight resets every element in the document, so the
  antd screens, `app.css` and the login page would change. Vite never unloads CSS after navigation: anything a feature
  loads stays in the document for the session, on every page. Only custom properties are global.
- **Utilities scoped as `[data-feature] .class` (0,2,0), unlayered.** They can't restyle an antd page that uses the same
  class name (`grow` at `web/src/features/review/ReviewPage.tsx:269`, `grid`, `w-full`). `app.css`, `web/src/shared/*.css`
  and antd's CSS-in-JS are unlayered, and an unlayered rule beats every `@layer` rule whatever its specificity, so
  `@layer` utilities or an `@layer base` reset lose to them. antd also injects global link rules
  (`:where(.css-…) a:hover`, 0,1,1): a `:where([data-feature])` scope (0,1,0) loses to those, the 0,2,0 scope wins.
- **Base rules as `[data-feature] :where(…)` (0,1,0).** Reset the browser defaults (heading and paragraph margins, link
  colour, `button` chrome, bullets, inline images) and set `outline-color` (`--ds-focus`) and `::selection`
  (`--ds-selection-*`) there; every utility out-ranks them. A focus rule at 0,2,0 beats `web/src/shared/ui.css`'s teal
  `:focus-visible` ring (loaded once the signed-in shell has been visited).
- **`source(none)` + `@source "./"`.** Tailwind scans only the feature folder. Automatic detection would scan all of
  `web/` and emit utilities for every class-like word in the antd pages.
- **html stays 16px.** Don't set `html { font-size }`; the root sets `font-size: 1rem` for its own subtree, because
  antd's `.ant-app` is 14px.
- `tailwindcss/theme.css` puts Tailwind's defaults on `:root` inside `@layer theme`. The only name it shares with
  `app.css` is `--font-mono`, and `app.css` wins. `tailwind-theme.css` is `reference`, so it emits nothing on `:root`.
- `rtl:` / `ltr:` come from `@custom-variant … :dir()` in the entry (as in `landing.css`), not Tailwind's `[dir]` form.

## Scrollbar

`web/src/styles/design-system/scrollbar.css` (imported once in `main.tsx`) colours every scrollbar like the surface
it sits on: transparent track (the surface itself), a thumb that is a quiet tone of that surface
(`color-mix(in srgb, SURFACE p%, INK)`), full track width, corner `--ds-radius-xs`. Contexts: light surfaces
(`--ds-bg-cool` + `--ds-body`, 65/50/38), dark sections (`--ds-bg` + `--ds-body`, 74/62/52), sidebar / rail / mobile
drawer / `.json` (`--ds-sidebar-bg` + `--ds-sidebar-fg`, 76/66/58), the page bar beside the sidebar in Arabic on desktop
(sidebar palette on the sidebar gradient), the modal mask (white 35/55/70%). Rest about 1.5:1 against the track, hover
about 2:1. A new theme needs nothing; a new dark container joins the sidebar rule. Firefox gets `scrollbar-width: thin`
with the same colours; hidden scrollers (`.lcp-seg`, `.f-pipe`, `.sp-tabs`, `.sp-rail`) stay hidden. Values and rules:
`references/token-files.md` §9.

## shadcn inside a themed subtree

- **One mapping, every theme.** `web/src/styles/shadcn.css` maps each shadcn variable to a `--ds-*` token
  (`--primary` → `--ds-btn-primary-bg`, `--ring` → `--ds-focus-ring`, `--chart-n` → `--ds-chart-n` …; table in
  `references/token-files.md` §7). Theme files declare no shadcn variable (`tokens.test.ts` fails if one does), so a new
  theme needs no shadcn lines. shadcn follows `ACTIVE_THEME` (`<html data-theme>`), `data-section` and `CORNERS`:
  `--radius` comes from `corners.css` (= `--ds-radius-lg`) and `rounded-xs … xl` read the same scale (2xl–4xl are
  xl × 1.25–1.75; never negative). A `<Button>` is navy on a light section and gold with navy text inside
  `data-section="dark"`; a `<Card>` is paper on light and navy-800 on dark. Don't add `rounded-*` overrides to shadcn parts.
- The mapping is declared on `:where([data-theme], [data-section])` in `@layer base`: it re-resolves in every section,
  and with specificity 0 in a layer it never overrides a theme, whatever the import order.
- shadcn's `dark:` variant = inside the nearest `data-section="dark"` (no `.dark` class). Text colour is inherited: a dark
  band sets its own (`text-ds-fg`). `shadcn.css` carries a small preflight for `[data-slot]` parts only (fonts, colour and
  fill of form controls, UA borders), so outline, ghost and link buttons don't turn grey with black text.
- shadcn's utilities come from `web/src/styles/shadcn.css`, which nothing imports yet. Using shadcn needs
  `import "./styles/shadcn.css";` in `web/src/main.tsx` after `app.css`. That edits a shared file: confirm with the user.
- Muted surface is `--ui-muted` (`bg-muted`), because `app.css` owns `--muted` (a text colour).
- **Portals.** Radix renders Dialog, Sheet, Popover, Tooltip, Select and DropdownMenu content under `<body>`, outside the
  subtree. `<html data-theme>` gives them the theme in its light scope. For a dark block pass `data-section="dark"`, and
  the feature's root attribute for its utilities, to the content element: `<DialogContent data-section="dark" data-feature="">`.
- Charts: `--chart-*` are theme level and made for light surfaces (navy-gold `--chart-1` is navy-800, the colour of a
  dark card). Put shadcn charts on a light surface.
- The feature's utilities are unlayered and beat shadcn's layered ones. Keep fixed-radius and shadow class names
  (`rounded-lg`, `shadow-sm`) out of the feature folder, comments included: the scan would generate them with
  Tailwind's defaults and they would override the matching shadcn parts on the page.
- RTL: wrap in `DirectionProvider dir="rtl"` (`web/src/components/ui/direction.tsx`) and `TooltipProvider`.
- React 18: shadcn wrappers are not `forwardRef`. `asChild` around a shadcn component loses the ref, so popups can't
  anchor; use `asChild` on a plain element.

## Runtime appearance (Appearance panel)

Each viewer chooses the palette and the corner style; CSS tokens, antd, shadcn and the scrollbar change at once, no reload.

- **Store** (`theme.ts`): `{ theme, corners }` in one localStorage key, **`t1.appearance`** (JSON). Read when the module
  loads (before the first render), each field validated against `themes` / `corners` in `tokens.ts` with a fallback to
  `ACTIVE_THEME` / `CORNERS`, then put on `<html data-theme>` / `data-corners`. Every storage access is in try/catch
  (blocked storage: defaults, changes last for the tab). `setAppearance({ theme?, corners? })` saves and applies,
  `resetAppearance()` removes the key, `pinAppearance(look)` shows a page's own look until released. `setDsTheme()` /
  `setDsCorners()` are shortcuts. Defaults stay `ACTIVE_THEME` (theme.ts) and `CORNERS` (config.ts).
- **React** (`appearance.tsx`): `<AppearanceProvider base={antdBase}>` in `App.tsx` renders the antd `ConfigProvider` from
  the look on `<html>`; `DarkBlock` providers inherit it. `useAppearance()` → `{ theme, corners, shown, isDefault,
  setTheme, setCorners, reset }`.
- **Landing = brand page.** Its route is wrapped in `<PinnedAppearance look={BRAND_APPEARANCE}>` (navy-gold + square, set
  before paint, released on unmount), so it never changes and no other theme's `[data-theme] [data-section]` rule can reach
  it (0 px parity under every setting). Login, MFA, change-password and the signed-in app follow the viewer's choice.
- **Panel** (`app/AppearanceMenu.tsx`): a gear in the top bar between the language toggle and the account menu (same size
  and style as the language button, tooltip + `aria-label` "المظهر / Appearance", visible at every width). Popover under
  the button from md (768px) up, bottom sheet (antd Drawer) on phones, no dim. The sidebar footer keeps only "تسجيل الخروج".
  Palette cards are built from `themes` + `themeLabels`, each preview inside its own `data-theme` (theme-level and
  light tokens only, no `data-section` inside); corner control "حاد · مستدير · ناعم" (`cornerLabels`); "استعادة الافتراضي".
  a11y: a labelled dialog (popover: non-modal, Tab out of either end or Escape closes and refocuses the gear; sheet: modal
  with focus trap), focus on the checked palette, `role="radiogroup"` / `role="radio"` + `aria-checked`, roving tabindex,
  arrows mirrored in Arabic, Home / End. Wrapped in an error boundary (a panel bug hides only the button). Tokens only in its CSS.
- **A new palette shows up by itself**: its `themes` entry plus a `themeLabels` name (TypeScript requires one).

## antd pages

The site theme on `<html data-theme>` is the viewer's choice (above); the default for everyone is `ACTIVE_THEME` in
`web/src/styles/design-system/theme.ts` (navy-gold, user decision 2026-11-13). antd follows the attribute through a
MutationObserver in `useDsTheme()`, also for a change made in devtools.

- **antd** gets its `ConfigProvider` theme from `antdTheme(name, base)` (`antd-theme.ts`), built by `AppearanceProvider`:
  colours and radius from `tokens.ts`, fonts from `base` in `App.tsx` (IBM Plex Sans Arabic 14px, unchanged; no Cairo on
  antd pages). Light-section tokens for app surfaces; Table header from dark-section tokens. Radius from the corner style,
  not the theme: `antdRadii(corners[style])` (borderRadius md, LG lg, SM sm, XS/Outer xs; Table header lg; Progress bar
  pill); in smooth (`pillButtons`) Button and Tag radii are pill too. `<html data-corners>` re-themes antd live. A new theme needs only its `tokens.ts` entry. Full token → antd table: `references/token-files.md` §8 and the design-system README.
- **Legacy variables** (`--navy`, `--teal`, `--teal-ink`, `--bg`, `--ink`, `--muted`, `--line`, `--hair`, `--gold` …
  in the `:root` blocks of `app.css` and `shared/ui.css`) are aliases of `--ds-*` with the old hex as fallback, so the
  antd app's own CSS follows the theme. `--r` (ui.css, theme.css) is `var(--ds-radius-xl)`, and every radius in the
  app CSS is a scale token (map: `corners-map.md` in the session notes). Status colours (`--warn*`, `--ok*`, `--crit*`) are fixed. Inside the sidebar
  (`.lcp-side`, drawer body) `--teal`, `--teal-100`, `--gold` map to `--ds-sidebar-*`.
- **Sidebar tokens** (theme level): `--ds-sidebar-bg`, `-fg`, `-fg-active`, `-muted`, `-hover-bg`, `-active-bg`,
  `-indicator`, `-border`, `-focus`, `-badge-bg`, `-badge-fg`, and `-bg-top` / `-glow` for its gradient.
- **Band tokens** (theme level): `--ds-band-from`, `-via`, `-to`, `-glow`, `-glow-accent` (navy-gold glows: gold-400 @14%
  and @18%). They feed `--ds-gradient-hero`; `shared/theme.css` `--band` is that token.
- **Gradients come from tokens only** (section "Gradients"). Text or shadow colours still written as hex in legacy rules
  don't follow the theme; convert them to tokens when you touch those rules. New radii: a scale token, never px.

## Gradients

Every gradient in the app CSS reads a theme-level token (full `background` value), so the theme or one token re-colours
all of them. Values per theme: `references/token-files.md` §4a and the design-system README "Gradients".

| Token | Role (rules) |
|---|---|
| `--ds-gradient-hero` | page band `.lcp-head` (`--band`), Home hero `.lcp-hero`, plan hero `.sp-hero`; from `--ds-band-*` |
| `--ds-gradient-sidebar` | sidebar and mobile drawer (`.lcp-side`, `.lcp-drawer .ant-drawer-body`), `.verify-head`; from `--ds-sidebar-bg-top` → `-bg` + `-glow` |
| `--ds-gradient-auth` | sign-in brand panel `.auth-side`; band via → from → `--ds-shade`, band glows |
| `--ds-gradient-avatar`, `-progress`, `-callout`, `-surface-subtle` | `.lcp-avatar`, `.lcp-prog-bar i`, `.lcp-next`, `.sp-chart` |
| `--ds-hatch-light`, `--ds-hatch-muted` | stripes: not-started timeline cells, elapsed-time bar |
| `--ds-shade` (colour) | dark stop that pillar colours deepen toward |

- **Use:** `background: var(--ds-gradient-hero)`, `style={{ background: "var(--ds-gradient-hero)" }}`, or
  `bg-[image:var(--ds-gradient-hero)]`. Never a hex, `rgb()` or primitive in a gradient outside a theme file.
- **Pillar colours are data** (`--pc` / `--pt`, set per element by `tint()`). A token on `[data-theme]` resolves its `var()`
  there, so it can't read them: these rules keep the recipe and take only the other stop from a token,
  `color-mix(in srgb, var(--pc) 72%, var(--ds-shade))` or `var(--pt) → var(--ds-surface)`. Status hatches keep `--g-*`.
- **New theme:** copy the gradient block of `app-soft.css` (hero, sidebar, auth need no change); set `--ds-shade`,
  `--ds-sidebar-bg-top` (darker than the hover bg), `--ds-sidebar-glow` (≤ 8%, group titles stay 4.5:1) and the colours of
  avatar, progress, callout, surface-subtle and the hatches. In `tokens.ts` pass the same colours to `gradients()` and
  `hatch()`. `tokens.test.ts` resolves every `var()`, rejects a raw colour in a gradient token, and checks AA for text on
  every stop with the glows composited.
- **Accents on navy and data marks:** `--ds-accent-on-dark` (gold-400 / soft-gold-300) for text and marks on a navy block
  without `data-section="dark"` (plan hero kicker, rings, rail dots, active navy tab icon); `--ds-chart-college` and
  `--ds-status-review` for the two data colours that were teal (DistributionPanel legend, Home status bar), read as
  `var(--…)` in the TSX style. Accent shadows: `color-mix(in srgb, var(--teal) N%, transparent)`, never an rgba hex.
- **Navy block inside an antd page.** Put `data-section="dark"` on the block, so `var(--ds-*)` in its CSS gives the dark
  values, and wrap its antd actions in `DarkBlock` (`shared/ui.tsx`): a nested `ConfigProvider` with
  `antdDarkBlockTheme(useDsTheme())`, which sets Button colours only (gold primary, navy text, gold links). `PageHeader`
  and `Hero` already do this. For a block whose fields should go dark too, use `antdTheme(name, {}, { tone: "dark" })`.
- Don't style antd parts with page CSS when a token or component token can do it; add the mapping to `antd-theme.ts`.

## Corners (square | rounded | smooth)

The viewer picks the style in the Appearance panel. The default is one line in `web/src/styles/design-system/config.ts`:

```ts
export const CORNERS: CornerStyle = "square"; // or "rounded" | "smooth"
```

`theme.ts` puts the style on `<html data-corners>` before the first render. Without TS: set the attribute in devtools, or
`:root[data-corners="rounded"] { … }` in CSS. No attribute = square.

| Token | Utility | square | rounded | smooth | Use |
|---|---|---|---|---|---|
| `--ds-radius-xs` | `rounded-ds-xs` | 0 | 4px | 8px | bars, swatches, small codes, scrollbar thumb |
| `--ds-radius-sm` | `rounded-ds-sm` | 0 | 6px | 12px | code pills, focus rings, inner boxes |
| `--ds-radius-md` = `--ds-radius` | `rounded-ds-md`, `rounded-ds` | 0 | 8px | 16px | buttons, inputs, icon tiles (antd `borderRadius`) |
| `--ds-radius-lg` = shadcn `--radius` | `rounded-ds-lg` | 0 | 12px | 20px | cards, panels, tables, modals (antd `borderRadiusLG`) |
| `--ds-radius-xl` = legacy `--r` | `rounded-ds-xl` | 0 | 16px | 24px | page bands, hero, large panels |
| `--ds-radius-pill` | `rounded-ds-pill` | 0 | 999px | 999px | chips, count badges, progress bars, status dots |
| `--ds-radius-round` | `rounded-ds-round` | 50% | 50% | 50% | real circles only (avatars, step numbers, chart dots) |

- **Smooth** = the 8–24px scale, pill buttons and tags in antd (`pillButtons` in `tokens.ts`), a fully round scrollbar
  thumb (`--scrollbar-radius: var(--ds-radius-pill)`), and pill default / large buttons and badges in shadcn (20px ≥ half height).
- **Themes and corners combine freely.** Theme files hold no radius (`tokens.test.ts` fails if one does), so
  `navy-gold` + `smooth` and `app-soft` + `square` both work. Defaults: `ACTIVE_THEME` (theme.ts) and `CORNERS`.
- **Follows the style:** antd (global radius tokens, Table header, Progress; Button / Tag in smooth), shadcn, `rounded-ds*`,
  the scrollbar thumb, and all legacy app CSS. The landing has the same tokens but stays square (it pins `BRAND_APPEARANCE`).
- **Stays the same:** real circles, antd internals without a radius token (Switch, Radio, Badge, Spin, Timeline dots,
  Drawer edges), shadcn's own `rounded-full` / `rounded-[2px]` in `components/ui`, hairline grids on the landing.
- A subtree may take its own style (`data-corners="rounded"` on a div): tokens, `rounded-ds*` and the scrollbar follow;
  antd and `--r` follow `<html>` only.

## Brand / logo

Official logos: `web/src/assets/brand/` (223×336 PNG, transparent). The file name says the background the logo sits on
(artwork colours measured 2026-11-13):

| File | Artwork | Use on |
|---|---|---|
| `capital-university-logo-dark.png` | white + gold | dark backgrounds: navy sections, the dark header and footer (`data-section="dark"`) |
| `capital-university-logo-light.png` | navy + gold | light backgrounds: white, ivory, sand, cloud sections |

Import them as Vite assets, never copy them into `public/`:
`import logo from "@/assets/brand/capital-university-logo-light.png";` then
`<img src={logo} alt={L("جامعة العاصمة", "Capital University")} className="h-14 w-auto sm:h-16" />`. The landing header
uses the `-dark` file: it is byte-identical to the logo of the original page. A component that can sit in either scope
takes the tone as a prop; don't recolour the artwork with CSS filters. More in `references/design-system.md` §14.

## Add a theme

1. Add new raw colours to `primitives.css` as `--color-<family>-<step>` with a family prefix of your own (like `soft-*`).
   Avoid names that Tailwind, `app.css` or an existing family here already use (`slate`, `white`, `gray`, `navy` …).
2. Copy `themes/app-soft.css` to `themes/<name>.css` and replace `app-soft` in its three selectors (theme level, light
   scope, dark scope).
3. Give every `--ds-*` token a value in all three blocks. Keep every name: a missing token is unset, so its
   utility paints nothing. No radius: corners come from `corners.css` for every theme. No shadcn variables: `shadcn.css`
   maps them for every theme. Gradients: keep the recipes, change only the colours (section "Gradients").
4. Import the file in `index.css`, after the other themes and before `corners.css`.
5. Add `<name>` to `ThemeName` and to `themes` in `tokens.ts` (the `satisfies` check lists missing fields), and its panel
   name to `themeLabels` (`{ ar, en }`), then run `npx vitest run src/styles/design-system` in `web/` (fails on any
   CSS ≠ TS value and on the AA pairs). antd follows automatically, and the Appearance panel lists the theme with a live
   preview. To make it everyone's default, set `ACTIVE_THEME` in `theme.ts`.

Contrast check: per scope, every text token on every background it sits on, 4.5:1 for small text and 3:1 for large text
(≥ 24px, or ≥ 18.7px bold), UI borders and chart marks. Composite alpha tokens over their background first. The pair
list and a small WCAG function are in `references/token-files.md` §8.

New semantic role: add `--ds-<role>` to every theme (theme level, or both scopes), `--color-ds-<role>: var(--ds-<role>);`
to `tailwind-theme.css`, and the field to `tokens.ts`.

## Before you finish

- Search your changed files for raw colours: `rg -n "#[0-9a-fA-F]{3,8}\b|\[#|rgba?\(" web/src/features/<feature>`;
  every `gradient(` must read `var(--ds-gradient-*)`, `var(--ds-hatch-*)` or only `var()` stops.
- Flip the palette (every theme) and the corner style (`square`, `rounded`, `smooth`) in the Appearance panel or on
  `<html>`, and the language between Arabic and English; all must hold, with no layout shift between the corner styles.
- Check an antd page (indicators, strategy) still looks the same after your CSS loads (feature CSS must not restyle it).
- `npm run typecheck` in `web/`. To test a build use `npx vite build --outDir <scratch dir> --emptyOutDir`: plain
  `npm run build` writes into `src/Tier1.Api/wwwroot`, which the API serves. Don't stop the user's dev server (5188) or API.

## References: when to read which

- `references/component-recipes.md`: read before writing markup. Feature CSS entry with the scoped reset, page root,
  theme switch, sections, heading with highlight, hairline cards, stat numbers, buttons, icon chips and callouts, chart
  colours, shadcn inside a theme (portals), and a don't list.
- `references/token-files.md`: read to pick a token, when a colour looks wrong, or when adding a theme or role. Every
  token with values in both themes and scopes, all utilities, opacity rules, section mechanics, shadcn mapping,
  `tokens.ts` and antd, contrast check.
- `references/design-system.md`: read when designing a new screen or matching the landing look. The full Navy & Gold
  spec (identity, palette with token names, type scale, spacing, section rhythm, textures, icons and motion, component
  anatomy, charts, accessibility with measured failures, RTL).
- `web/src/features/landing/`: the working example of all of the above. Read `landing.css` and `LandingPage.tsx` before
  starting a new feature.

## Reference implementation

Folder `web/src/features/landing/`: `LandingPage.tsx` (root: `data-landing`, `data-ds-page`, `data-theme`, `dir`, `lang`;
imports Cairo and `landing.css`), `landing.css` (the Tailwind entry), `LandingChrome.tsx` (header, footer),
`HeroSection.tsx`, `OverviewSection.tsx`, `PillarsSection.tsx`, `AnalyticsSection.tsx`, `HierarchySection.tsx`,
`DataManagementSection.tsx`, `IntegrationSection.tsx`, `CtaSection.tsx`, `SectionHeading.tsx`, `Icon.tsx`, `charts.tsx`,
`sections.ts`. It matches the original `Home.jsx` except for three deliberate user changes: quarters wording instead of
seasons, the redesigned footer, and the analytics section (القيادة bar removed, horizontal KPI rows, RTL-mirrored trend
chart).
