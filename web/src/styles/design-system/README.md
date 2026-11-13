# Design system tokens

Multi-theme CSS tokens built from the Navy & Gold design system (`Frontend/React/HERO_DESIGN_SYSTEM.md`).
Nothing imports these files yet.

| File | What it holds |
|---|---|
| `primitives.css` | Raw palettes on `:root` (`--color-navy-900`, `--color-gold-400`, `--color-soft-*` …). The only global file. |
| `themes/navy-gold.css` | `[data-theme="navy-gold"]`: the landing look. Square corners, Cairo. Pixel-identical to the old `Home.jsx`. |
| `themes/app-soft.css` | `[data-theme="app-soft"]`: the old `/app` palette (navy #0d2440, gold #c9a227, 14px radius). |
| `index.css` | Plain-CSS entry: primitives, then both themes. Safe to import anywhere. |
| `tailwind-theme.css` | Tailwind v4 `@theme inline reference` mapping, for a Tailwind entry. |
| `tokens.ts` | The same values as typed constants (for an antd `ConfigProvider` later). Keep it in sync by hand. |

## Use

1. Import order in a Tailwind entry (no preflight, like `shadcn.css`):
   ```css
   @layer theme, base, components, utilities;
   @import "tailwindcss/theme.css" layer(theme);
   @import "tailwindcss/utilities.css" layer(utilities) source(none);
   @import "../../styles/design-system/index.css";
   @import "../../styles/design-system/tailwind-theme.css";
   @source "./";
   ```
   Without Tailwind, import `index.css` alone and use `var(--ds-…)`.
2. Put `data-theme="navy-gold"` (or `"app-soft"`) on the page root. Switching the attribute re-themes the subtree.
3. Sections: the default scope is light. Put `data-section="dark"` on dark blocks (header, footer, dark sections, a dark card in a light section). `data-section="light"` flips back inside a dark block. The same class then gives the right colour in both: `text-ds-heading` is navy on light and white on dark.
4. Utilities:
   - Semantic (follow theme and section): `bg-ds-bg`, `bg-ds-bg-cool`, `bg-ds-bg-warm`, `bg-ds-surface`, `hover:bg-ds-surface-hover`, `border-ds-border`, `border-ds-accent-border`, `text-ds-heading`, `text-ds-highlight`, `text-ds-body`, `text-ds-eyebrow`, `text-ds-icon`, `bg-ds-tile`, `bg-ds-btn-primary-bg` / `-fg` / `-border` / `-hover-*`, `bg-ds-btn-ghost-*`, `stroke-ds-chart-*` …
   - Opacity modifiers work: `bg-ds-accent/10`, `border-ds-line-soft/15`.
   - Primitives (fixed colours): `bg-navy-900`, `text-gold-500`, `border-ash-200`.
   - Shape and type: `rounded-ds`, `font-ds`, `font-ds-heading`, `shadow-ds-sm`, `shadow-ds-md`.
   - Composite tokens for inline styles: `var(--ds-texture-hero)`, `var(--ds-texture-dark)`, `var(--ds-hero-vignette)`.
5. shadcn components inside a themed subtree follow it (`--background`, `--primary`, `--ui-muted`, `--radius`, `--chart-*`, `--sidebar-*` are mapped per theme and per section). Portalled content (dialogs, popovers, tooltips) renders outside the subtree: give its content element the same `data-theme`, or set it on `<html>`.

Rules: use `--ds-*` tokens in components, not primitives or hex. Do not nest two different `data-theme` values. `app.css` owns `--muted`; the shadcn muted surface is `--ui-muted`.

## Add a theme in ~5 steps

1. Add any new raw colours to `primitives.css` as `--color-<family>-<step>`. Avoid names Tailwind already uses (`slate`, `white`, `mist` …).
2. Copy `themes/app-soft.css` to `themes/<name>.css` and replace `app-soft` in its three selectors.
3. Give every `--ds-*` and shadcn variable a value in the theme-level, light and dark blocks. Keep every name; a missing token falls back to nothing.
4. Import it in `index.css`, after the other themes.
5. Add `<name>` to `ThemeName` and `themes` in `tokens.ts`, check text contrast (4.5:1 small text, 3:1 large text and UI), then use `data-theme="<name>"`.

New semantic roles: add `--ds-<role>` to every theme, then `--color-ds-<role>: var(--ds-<role>);` to `tailwind-theme.css`.
