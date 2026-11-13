---
name: navy-gold-design-system
description: "Navy & Gold design system and multi-theme CSS tokens for the Tier1 university performance app (Context/Tier1/web: React, antd, Tailwind v4, shadcn/ui). Use it for any visual or styling work in Tier1, in Arabic or English: colours, a theme or a second style system, design tokens, data-theme switching, a landing or hero section, dark and light sections, RTL pages, Tailwind or shadcn styling, cards, buttons, stat tiles, chart colours, 'make it look like the landing page', or an antd theme from the tokens. Arabic cues: ألوان، ثيم، هوية بصرية، ستايل، تصميم الصفحة، صفحة هبوط، هيرو، الوضع الداكن. Gives the rules (data-theme + data-section + --ds-* tokens, never raw hex, rounded-ds, hairlines, logical RTL utilities), the Tailwind entry without global preflight that keeps antd pages unchanged, shadcn inside a themed subtree, recipes, and how to add a theme. Use it even for 'style this page' or 'change the colours' on a Tier1 screen."
---

# Navy & Gold design system (Tier1)

Tier1's frontend has one set of semantic colour tokens (`--ds-*`) and several themes that fill them. A page turns a
theme on with `data-theme`, and marks its dark blocks with `data-section="dark"`. Components name roles only
(`text-ds-heading`, `bg-ds-surface`), so the same JSX renders as **navy-gold** (the landing look: square, Cairo, navy
and antique gold) or **app-soft** (the old `/app` palette: softer navy, 14px corners). A new style system is one more
theme file, with no component changes.

Paths are relative to the Tier1 root: `F:\Projects\angular\Indecators\Context\Tier1`.

## Where things are

| Path | What |
|---|---|
| `web/src/styles/design-system/README.md` | Short usage and add-a-theme steps |
| `web/src/styles/design-system/primitives.css` | Raw palettes on `:root` (`--color-navy-900` …). The only global file |
| `web/src/styles/design-system/themes/navy-gold.css` | `[data-theme="navy-gold"]`: `--ds-*` + shadcn variables, light and dark scopes |
| `web/src/styles/design-system/themes/app-soft.css` | `[data-theme="app-soft"]`: same names, the softer palette |
| `web/src/styles/design-system/index.css` | Plain-CSS entry: primitives, then every theme |
| `web/src/styles/design-system/tailwind-theme.css` | Tailwind v4 `@theme inline reference`: `bg-ds-*`, `rounded-ds`, `font-ds`, `shadow-ds-*`, primitives |
| `web/src/styles/design-system/theme.ts` | The one site-wide switch: `ACTIVE_THEME` → `<html data-theme>` (before first render), `useDsTheme()`, `setDsTheme()`; imports `index.css` globally |
| `web/src/styles/design-system/antd-theme.ts` | `antdTheme(name, base, { tone })`: theme tokens → antd `ThemeConfig` (colours, radius) |
| `web/src/styles/design-system/tokens.ts` | Same values as typed TS (antd needs real colours); `tokens.test.ts` proves it equals the CSS and checks AA pairs |
| `web/src/features/landing/` | The landing page port: the live reference implementation (file list below) |
| `web/src/styles/design-system/scrollbar.css` | Scrollbar tokens and drawing, imported once in `main.tsx` (see Scrollbar) |
| `web/src/styles/shadcn.css` | shadcn's Tailwind entry and default variables (not imported yet) |
| `web/src/components/ui/` | 60 shadcn (radix-nova, RTL) components |
| `web/src/shared/i18n.ts` | Sets `<html lang dir>`; `useLang()`, `L(ar, en)` |
| `web/src/styles/app.css`, `web/src/shared/ui.css`, `web/src/shared/tier1.css`, `web/src/shared/theme.css` | The antd app's styles. The brand variables in the `:root` blocks of `app.css` and `ui.css` are aliases of `--ds-*` (see antd pages) |
| `web/src/app/App.tsx` | antd `ConfigProvider`: `antdTheme(useDsTheme(), fonts)`; passes the theme to the landing |

Token files are shared by every page. Edit them only when the task is about tokens (a new role, a new theme), never to
fix one component.

## The rules

1. **Colour only through tokens.** Use a semantic utility (`bg-ds-surface`, `text-ds-body`, `border-ds-border`,
   `stroke-ds-chart-1`) or `var(--ds-…)` in `style`. No hex, no `bg-[#…]`, no `rgb()`, and no Tailwind palette colour
   (`text-slate-500`, `bg-white`) on themed UI. A hard-coded colour can't follow `data-theme` or `data-section`, so the
   second theme breaks silently. Free values: `transparent`, `currentColor` / `bg-current`, `inherit`.
2. **`--ds-*` in components, primitives only in theme files.** `--color-navy-900` and `bg-navy-900` are fixed paint;
   themes map them to roles. If no role fits, add one (below) instead of reaching for a primitive.
3. **One `data-theme`, a `data-section` per dark block.** The site theme is on `<html>` (`theme.ts`, `ACTIVE_THEME`).
   A page root may repeat the same value (take it from `useDsTheme()`), never a different one. Light is the default scope. Put `data-section="dark"` on the header, footer, dark sections, and any dark card
   inside a light section; `data-section="light"` flips back inside a dark block. One class then gives the right value
   in both: `text-ds-heading` is navy on light and white on dark. So no `dark ? … : …` colour ternaries. Never nest two
   different `data-theme` values: both themes' section rules would match.
4. **Shape through `rounded-ds`.** Cards, tiles, buttons, badges, inputs, panels. It is 0 in navy-gold and 14px in
   app-soft. Fixed radii (`rounded-lg`, `rounded-xl`, `rounded-full`) ignore the theme. Only chart marks are round.
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

`web/src/styles/design-system/scrollbar.css` (imported once in `main.tsx`) draws one glass scrollbar site-wide. Tokens
(inherited, so a subtree re-colours its own scrollers): `--scrollbar-size` 12px, `-gap` 3px, `-gap-hover` 2px, `-radius`
999px, `-min-thumb` 40px, `-track`, `-thumb`, `-thumb-hover`, `-thumb-active`, `-highlight`, `-hairline`, `-overlay-thumb*`.
A themed page re-colours the page scrollbar through `html:has([data-ds-page][data-theme="…"])`, so keep `data-ds-page` on
the root; Firefox gets `scrollbar-width: thin` in an `@supports` fallback; hidden scrollers (`.lcp-seg`, `.f-pipe`,
`.sp-tabs`, `.sp-rail`) stay hidden. Values per scope: `references/token-files.md` §9.

## shadcn inside a themed subtree

- The theme files set shadcn's variables per theme and per section (`--background`, `--primary`, `--ui-muted`,
  `--border`, `--ring`, `--radius`, `--chart-*`, `--sidebar-*`). A `<Button>` or `<Card>` under `data-theme="navy-gold"`
  is navy and square; inside `data-section="dark"` its primary turns gold. shadcn's radius scale multiplies `--radius`,
  so don't add `rounded-*` overrides to shadcn parts.
- shadcn's utilities come from `web/src/styles/shadcn.css`, which nothing imports yet. Using shadcn needs
  `import "./styles/shadcn.css";` in `web/src/main.tsx` after `app.css`. That edits a shared file: confirm with the user.
- Muted surface is `--ui-muted` (`bg-muted`), because `app.css` owns `--muted` (a text colour).
- **Portals need the attribute too.** Radix renders Dialog, Sheet, Popover, Tooltip, Select and DropdownMenu content
  under `<body>`, outside the subtree, where shadcn's teal `:root` defaults apply. Pass `data-theme` (and
  `data-section="dark"` if needed) plus the feature's root attribute to the content element:
  `<DialogContent data-theme="navy-gold" data-feature="">`; feature utilities then work on its children. `<html>` now
  carries `data-theme`, which covers the colours; if `shadcn.css` is ever imported, load it before the DS CSS
  (in `main.tsx` before `App`), because `:root` and `[data-theme]` tie on specificity.
- The feature's utilities are unlayered and beat shadcn's layered ones. Keep fixed-radius and shadow class names
  (`rounded-lg`, `shadow-sm`) out of the feature folder, comments included: the scan would generate them with
  Tailwind's defaults and they would override the matching shadcn parts on the page.
- RTL: wrap in `DirectionProvider dir="rtl"` (`web/src/components/ui/direction.tsx`) and `TooltipProvider`.
- React 18: shadcn wrappers are not `forwardRef`. `asChild` around a shadcn component loses the ref, so popups can't
  anchor; use `asChild` on a plain element.

## antd pages

The whole site runs on one theme (user decision 2026-11-13: `navy-gold`). Switch it in one place:
`ACTIVE_THEME` in `web/src/styles/design-system/theme.ts` (or `<html data-theme>` at run time; antd follows through a
MutationObserver in `useDsTheme()`).

- **antd** gets its `ConfigProvider` theme from `antdTheme(name, base)` (`antd-theme.ts`): colours and radius from
  `tokens.ts`, fonts from `base` in `App.tsx` (IBM Plex Sans Arabic 14px, unchanged; no Cairo on antd pages). Light-section
  tokens for app surfaces; Table header from dark-section tokens. Radius in one helper, `antdRadii(r)`. A new theme needs
  only its `tokens.ts` entry. Full token → antd table: `references/token-files.md` §8 and the design-system README.
- **Legacy variables** (`--navy`, `--teal`, `--teal-ink`, `--bg`, `--ink`, `--muted`, `--line`, `--hair`, `--gold`, `--r` …
  in the `:root` blocks of `app.css` and `shared/ui.css`) are aliases of `--ds-*` with the old hex as fallback, so the
  antd app's own CSS follows the theme. Status colours (`--warn*`, `--ok*`, `--crit*`) are fixed. Inside the sidebar
  (`.lcp-side`, drawer body) `--teal`, `--teal-100`, `--gold` map to `--ds-sidebar-*`.
- **Sidebar tokens** (theme level): `--ds-sidebar-bg`, `-fg`, `-fg-active`, `-muted`, `-hover-bg`, `-active-bg`,
  `-indicator`, `-border`, `-focus`, `-badge-bg`, `-badge-fg`.
- **Band tokens** (theme level): `--ds-band-from`, `-via`, `-to`, `-glow`, `-glow-accent`. `shared/theme.css` builds
  `--band` (the navy page header strip) from them.
- Colours and radii written directly in legacy rules (gradients with teal glow, `border-radius: 10px` …) don't follow the
  theme; convert them to tokens when you touch those rules.
- **Navy block inside an antd page.** Put `data-section="dark"` on the block, so `var(--ds-*)` in its CSS gives the dark
  values, and wrap its antd actions in `DarkBlock` (`shared/ui.tsx`): a nested `ConfigProvider` with
  `antdDarkBlockTheme(useDsTheme())`, which sets Button colours only (gold primary, navy text, gold links). `PageHeader`
  and `Hero` already do this. For a block whose fields should go dark too, use `antdTheme(name, {}, { tone: "dark" })`.
- Don't style antd parts with page CSS when a token or component token can do it; add the mapping to `antd-theme.ts`.

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
3. Give every `--ds-*` and shadcn variable a value in all three blocks. Keep every name: a missing token is unset, so its
   utility paints nothing.
4. Import the file in `index.css`, after the other themes.
5. Add `<name>` to `ThemeName` and to `themes` in `tokens.ts` (the `satisfies` check lists missing fields), then run
   `npx vitest run src/styles/design-system` in `web/` (fails on any CSS ≠ TS value and on the AA pairs). antd follows
   automatically. Use it via `ACTIVE_THEME` in `theme.ts`.

Contrast check: per scope, every text token on every background it sits on, 4.5:1 for small text and 3:1 for large text
(≥ 24px, or ≥ 18.7px bold), UI borders and chart marks. Composite alpha tokens over their background first. The pair
list and a small WCAG function are in `references/token-files.md` §8.

New semantic role: add `--ds-<role>` to every theme (theme level, or both scopes), `--color-ds-<role>: var(--ds-<role>);`
to `tailwind-theme.css`, and the field to `tokens.ts`.

## Before you finish

- Search your changed files for raw colours: `rg -n "#[0-9a-fA-F]{3,8}\b|\[#|rgba?\(" web/src/features/<feature>`.
- Flip `data-theme` between `navy-gold` and `app-soft`, and the language between Arabic and English; both must hold.
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
