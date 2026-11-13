# Component recipes (token style)

Copy-ready JSX for Tier1 (`web/`, React 18 + TS strict). Every colour is a `ds-*` token, so each recipe renders in
navy-gold and app-soft, light and dark, RTL and LTR. Sizes and spacing follow `design-system.md` §3–§5. Examples import
with the `@/` alias (`@/` = `web/src/`); relative imports work too.

`<feature>` and `data-feature` are placeholders: use your feature's folder name and a root attribute of your own
(the landing uses `data-landing`). `L(ar, en)` and `useLang()` come from `@/shared/i18n`.

## Contents

1. Feature CSS entry, scoped base and utilities (copy of `landing.css`) · 2. Page root with theme · 3. Theme switch · 4. Dark and light sections ·
5. Eyebrow + heading + highlight · 6. Cards and tiles with hairlines · 7. Stat number · 8. Buttons · 9. Icon chip,
badge, callouts · 10. Chart colours · 11. shadcn inside a theme · 12. Don't

## 1. Feature CSS entry, scoped base and utilities

```css
/* web/src/features/<feature>/<feature>.css: imported once, by the feature's root component (a lazy route).
   Copy of web/src/features/landing/landing.css with data-landing -> data-feature. Read that file for the full
   comments and the complete scoped base. */
@layer theme;
@import "tailwindcss/theme.css" layer(theme);
@import "../../styles/design-system/index.css";
@import "../../styles/design-system/tailwind-theme.css";
@source "./";

/* rtl:/ltr: from the element's own direction (<html dir> follows the app language, the page root may differ). */
@custom-variant rtl (&:where(:dir(rtl)));
@custom-variant ltr (&:where(:dir(ltr)));

/* Root: every inherited text property the page relies on (body and antd's .ant-app set IBM Plex, 14px, 1.57). */
[data-feature] {
  font-family: var(--ds-font);
  font-size: 1rem; /* html stays 16px: rem utilities assume it */
  font-weight: 400;
  line-height: 1.7;
  color: var(--ds-fg);
  -webkit-text-size-adjust: 100%;
  -webkit-font-smoothing: antialiased;
  overflow-x: clip;
}

/* Scoped base (Tailwind preflight, trimmed). [data-feature] :where(...) = 0,1,0: utilities (0,2,0) always win. */
[data-feature] :where(*),
[data-feature] ::before,
[data-feature] ::after {
  box-sizing: border-box;
  margin: 0;
  padding: 0;
  border: 0 solid var(--ds-border);
  outline-color: var(--ds-focus);
}
[data-feature] :where(h1, h2, h3, h4, h5, h6) { font-size: inherit; font-weight: inherit; }
[data-feature] :where(a) { color: inherit; text-decoration: none; outline-color: var(--ds-focus); } /* beats antd's global link rule */
[data-feature] :where(b, strong) { font-weight: bolder; }
[data-feature] :where(ol, ul, menu) { list-style: none; }
[data-feature] :where(img, svg, video, canvas, audio, iframe, embed, object) { display: block; vertical-align: middle; }
[data-feature] :where(img, video) { max-width: 100%; height: auto; }
[data-feature] :where(button, input, select, optgroup, textarea) {
  font: inherit;
  letter-spacing: inherit;
  color: inherit;
  border-radius: 0;
  background-color: transparent;
  opacity: 1;
}
[data-feature] :where(button) { cursor: pointer; }
[data-feature] :where([hidden]:not([hidden="until-found"])) { display: none !important; }
[data-feature] ::selection { background: var(--ds-selection-bg); color: var(--ds-selection-fg); }

/* shared/ui.css draws a teal :focus-visible ring (0,1,0) and antd sets `a:focus { outline: 0 }` (0,1,1). Out-rank both. */
[data-feature] :where(a, button, [role="button"], [tabindex]):focus-visible {
  outline: auto 1px var(--ds-focus);
  outline-offset: 0;
  border-radius: var(--ds-radius-sm); /* the ring's corner follows CORNERS, like the app's focus ring */
}

/* Utilities, scoped: every one compiles to `[data-feature] .class` (0,2,0). Unlayered on purpose. */
[data-feature] {
  @tailwind utilities source(none);
}
```

What this compiles to (checked against `landing.css`, Tailwind 4.3.5): `[data-feature] .bg-ds-surface { background-color:
var(--ds-surface) }`, specificity 0,2,0. Why this and not `:where()` or `@layer base`:
- Everything unlayered. `app.css`, `web/src/shared/*.css` and antd's CSS-in-JS are unlayered, and an unlayered rule beats
  any `@layer` rule whatever its specificity. A layered base or layered utilities lose to them.
- antd injects global link rules (`:where(.css-…) a:hover`, 0,1,1). A utility under `:where([data-feature])` has only 0,1,0
  and loses to them; `[data-feature] .class` (0,2,0) wins. `[data-feature] :where(…)` base rules stay 0,1,0 so any
  utility beats them.
- `web/src/shared/ui.css` has a teal `:focus-visible` ring on links and buttons: the focus rule above (0,2,0) overrides it.
- Only custom properties are global (Tailwind theme variables and the `--ds-*` tokens). No class or element rule leaks.
- `source(none)` + `@source "./"`: Tailwind scans only the feature folder, so antd pages get no utilities.
- html stays 16px (no `html { font-size }` rule): the rem utilities assume it, and the root sets its own `font-size: 1rem`.
- Utilities match descendants of the root only, never the root element: put utility classes on an inner wrapper.

Key the rules on an attribute of your own, not on `[data-theme]`: if someone later puts `data-theme` on `<html>`, rules
keyed on `[data-theme]` would hit every antd page.

## 2. Page root with theme

```tsx
// web/src/features/<feature>/<Feature>Page.tsx
import "./<feature>.css";
import { useLang } from "@/shared/i18n";

export function FeaturePage() {
  const lang = useLang();
  return (
    <div data-feature="" data-theme="navy-gold" dir={lang === "ar" ? "rtl" : "ltr"} lang={lang}>
      {/* utilities match descendants of [data-feature] only, so they go on this wrapper */}
      <div className="min-h-screen bg-ds-bg text-ds-fg">
        {/* header, sections, footer */}
      </div>
    </div>
  );
}
```

One `data-theme` per page. Light is the default scope. Container: `mx-auto max-w-7xl px-5 sm:px-10`.

## 3. Theme switch (more than one style system)

```tsx
import { useState, type ReactNode } from "react";
import { themeNames, type ThemeName } from "@/styles/design-system/tokens";

export function ThemedPage({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<ThemeName>("navy-gold");
  return (
    <div data-feature="" data-theme={theme}>
      <div className="min-h-screen bg-ds-bg text-ds-fg">
        <select
          value={theme}
          onChange={(e) => setTheme(e.target.value as ThemeName)}
          className="rounded-ds border border-ds-border bg-ds-surface px-3 py-2 text-sm text-ds-heading"
        >
          {themeNames.map((name) => <option key={name} value={name}>{name}</option>)}
        </select>
        {children}
      </div>
    </div>
  );
}
```

Switching the attribute re-themes the whole subtree; the children keep their classes.

## 4. Dark and light sections

```tsx
<header
  data-section="dark"
  className="relative z-30 border-b border-ds-line-soft/10 bg-ds-chrome/95 px-5 py-3 backdrop-blur-md sm:px-10"
>…</header>

{/* dark section with the faint grid texture */}
<section data-section="dark" className="relative overflow-hidden bg-ds-bg px-5 py-24 text-ds-fg sm:px-10 lg:py-28">
  <div
    aria-hidden
    className="pointer-events-none absolute inset-0 opacity-[0.08]"
    style={{ backgroundImage: "var(--ds-texture-dark)", backgroundSize: "48px 48px" }}
  />
  <div className="relative mx-auto max-w-7xl">…</div>
</section>

{/* light sections: plain, cool (data), warm (story / CTA) */}
<section className="bg-ds-bg px-5 py-24 sm:px-10 lg:py-28">…</section>
<section className="bg-ds-bg-cool px-5 py-24 sm:px-10 lg:py-28">…</section>
<section className="bg-ds-bg-warm px-5 py-24 sm:px-10 lg:py-28">…</section>

{/* a dark card inside a light section, and a light panel back inside it */}
<div data-section="dark" className="rounded-ds-lg border border-ds-border bg-ds-bg p-7 text-ds-fg sm:p-8">
  <div data-section="light" className="rounded-ds-md bg-ds-surface p-6 text-ds-fg">…</div>
</div>

<footer data-section="dark" className="border-t border-ds-line-soft/10 bg-ds-chrome px-5 py-6 sm:px-10">
  <div className="mx-auto flex max-w-7xl flex-col items-center justify-between gap-3 text-center md:flex-row md:text-start">
    <p className="text-xs leading-6 text-ds-body">© …</p>
    <span className="text-xs font-semibold tracking-wider text-ds-eyebrow">CAPITAL UNIVERSITY</span>
  </div>
</footer>
```

Alternate dark and light; never two dark sections in a row. Hero: add the `var(--ds-texture-hero)` grid
(`40px 40px`, `opacity-40`) and a `style={{ background: "var(--ds-hero-vignette)" }}` overlay, plus a 4px bottom bar
`absolute inset-x-0 bottom-0 h-1 bg-ds-accent/40`.

## 5. Eyebrow + heading + highlight word

```tsx
type SectionHeadingProps = { eyebrow: string; title: string; highlight: string; description?: string };

export function SectionHeading({ eyebrow, title, highlight, description }: SectionHeadingProps) {
  return (
    <div className="mx-auto mb-12 max-w-3xl text-center">
      {/* new UI: text-ds-eyebrow-aa passes AA at 12px; the landing keeps text-ds-eyebrow for pixel parity */}
      <div className="mb-4 inline-flex items-center gap-3 text-xs font-bold tracking-wider text-ds-eyebrow-aa">
        <span aria-hidden className="h-px w-8 bg-current" />
        {eyebrow}
        <span aria-hidden className="h-px w-8 bg-current" />
      </div>
      <h2 className="font-ds-heading text-3xl font-bold leading-[1.5] tracking-tight text-ds-heading sm:text-4xl lg:text-5xl">
        {title} <span className="text-ds-highlight">{highlight}</span>
      </h2>
      {description && <p className="mt-5 text-sm leading-8 text-ds-body sm:text-base">{description}</p>}
    </div>
  );
}
```

The same component works in light and dark sections: no `dark` prop, no colour ternaries. Hero and final CTA stack the
two parts: `<span className="block">…</span><span className="block text-ds-highlight-display">…</span>`.

## 6. Cards and tiles with hairlines

Hairline grid: the parent paints the 1px lines, the children paint the surface.

```tsx
<div className="grid gap-px overflow-hidden rounded-ds-lg border border-ds-border bg-ds-border md:grid-cols-2 lg:grid-cols-3">
  {items.map((item, i) => (
    <article
      key={item.id}
      className="group flex min-h-[290px] flex-col bg-ds-surface p-7 transition-colors hover:bg-ds-surface-hover sm:p-8"
    >
      <div className="mb-8 flex items-center justify-between">
        <IconTile icon={item.icon} />
        <span className="text-3xl font-light text-ds-index">{String(i + 1).padStart(2, "0")}</span>
      </div>
      <h3 className="text-lg font-bold text-ds-heading">{item.title}</h3>
      <p className="mt-4 text-sm leading-8 text-ds-body">{item.text}</p>
      <div className="mt-auto flex items-center gap-2 pt-6 text-xs font-semibold text-ds-eyebrow">
        <span aria-hidden className="h-px w-6 bg-ds-accent" />
        {item.footer}
      </div>
    </article>
  ))}
</div>
```

Feature row (light), bordered with a gold hover:

```tsx
<article className="group flex gap-5 rounded-ds-lg border border-ds-border p-5 transition-colors hover:border-ds-accent hover:bg-ds-surface-hover-soft sm:p-6">
  <IconTile icon={FileCheck} />
  <div className="min-w-0 flex-1">
    <div className="flex flex-wrap items-center gap-2">
      <h3 className="text-base font-bold text-ds-heading">{title}</h3>
      <span className="text-[10px] text-ds-eyebrow-aa">{englishSubtitle}</span>
    </div>
    <p className="mt-3 text-xs leading-7 text-ds-body sm:text-sm">{text}</p>
  </div>
  <span className="text-sm font-semibold text-ds-index">01</span>
</article>
```

Raised card on light (the only place a shadow belongs): `rounded-ds-lg border border-ds-accent/50 bg-ds-surface shadow-ds-sm
transition-all hover:border-ds-accent-strong hover:shadow-ds-md`. No shadows in dark sections.

## 7. Stat number

```tsx
<div className="grid gap-px overflow-hidden rounded-ds-lg border border-ds-border bg-ds-border sm:grid-cols-2 lg:grid-cols-4">
  {stats.map((s) => (
    <div key={s.label} className="group flex flex-col items-center bg-ds-surface px-6 py-10 text-center transition-colors hover:bg-ds-surface-hover">
      <IconTile icon={s.icon} className="mb-5" />
      <div className="flex items-baseline gap-2">
        <span className="text-5xl font-bold leading-none tracking-tight text-ds-heading tabular-nums">{s.value}</span>
        <span className="text-sm font-semibold text-ds-eyebrow">{s.suffix}</span>
      </div>
      <p className="mt-3 text-sm text-ds-body">{s.label}</p>
    </div>
  ))}
</div>
```

KPI tile: value `text-3xl font-bold text-ds-heading`, label `text-xs text-ds-body`, note `text-[11px] text-ds-eyebrow-aa`.

## 8. Buttons

```tsx
import { ArrowLeft } from "lucide-react";
import { cn } from "@/lib/utils";

const focus = "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ds-focus";

export const btnPrimary = cn(
  "group inline-flex items-center gap-4 rounded-ds-md border px-8 py-4 text-sm font-bold transition-all",
  "border-ds-btn-primary-border bg-ds-btn-primary-bg text-ds-btn-primary-fg",
  "hover:border-ds-btn-primary-hover-border hover:bg-ds-btn-primary-hover-bg hover:text-ds-btn-primary-hover-fg",
  focus,
);

export const btnGhost = cn(
  "group inline-flex items-center gap-3 rounded-ds-md border px-6 py-3 text-sm font-semibold transition-all",
  "border-ds-btn-ghost-border bg-ds-btn-ghost-bg text-ds-btn-ghost-fg",
  "hover:border-ds-btn-ghost-hover-border hover:bg-ds-btn-ghost-hover-bg hover:text-ds-btn-ghost-hover-fg",
  focus,
);

/** Points forward in the reading direction and nudges on hover. */
export function ForwardArrow() {
  return (
    <ArrowLeft
      aria-hidden
      strokeWidth={1.6}
      className="size-[19px] transition-transform ltr:-scale-x-100 rtl:group-hover:-translate-x-1 ltr:group-hover:translate-x-1"
    />
  );
}

<a href="/start" className={btnPrimary}>{L("ابدأ الآن", "Get started")}<ForwardArrow /></a>
```

The same `btnPrimary` is navy-inverting-to-gold on light and gold-inverting-to-outline inside `data-section="dark"`.
`L(ar, en)` comes from `@/shared/i18n`.

## 9. Icon chip, badge, callouts

```tsx
import type { LucideIcon } from "lucide-react";
import { cn } from "@/lib/utils";

export function IconTile({ icon: Icon, solid = false, className }: { icon: LucideIcon; solid?: boolean; className?: string }) {
  return (
    <span
      className={cn(
        "flex size-12 shrink-0 items-center justify-center rounded-ds-md transition-colors",
        solid
          ? "bg-ds-tile-solid text-ds-tile-solid-fg"
          : "border border-ds-accent-border bg-ds-tile text-ds-icon group-hover:bg-ds-tile-active group-hover:text-ds-tile-active-fg",
        className,
      )}
    >
      <Icon aria-hidden strokeWidth={1.6} className="size-[22px]" />
    </span>
  );
}

{/* badge with a live dot (dark hero): square when CORNERS is square, round when rounded */}
<div className="inline-flex items-center gap-3 rounded-ds-pill border border-ds-line-soft/15 bg-ds-surface/90 px-5 py-3">
  <span aria-hidden className="size-2 rounded-ds-pill bg-ds-live" />
  <span className="text-xs font-medium tracking-wide text-ds-body-strong sm:text-sm">{text}</span>
</div>

{/* inline note (light) */}
<p className="border-s-2 border-ds-accent bg-ds-callout px-4 py-3 text-xs leading-6 text-ds-body">{note}</p>

{/* key panel (light) */}
<div className="rounded-ds-lg border-s-4 border-ds-accent-strong bg-ds-surface p-6 sm:p-8">…</div>

{/* rule note (dark) */}
<div className="flex items-start gap-3 border-s-2 border-ds-accent ps-4">
  <ShieldCheck aria-hidden strokeWidth={1.6} className="size-[18px] shrink-0 text-ds-icon" />
  <p className="text-xs leading-7 text-ds-body">{text}</p>
</div>
```

Status dots (`rounded-ds-pill`), legend swatches (`rounded-ds-xs`) and tiles (`rounded-ds-md`) are square while
`CORNERS` is square and round off when it is rounded. Use `rounded-ds-round` only for something that must be a circle in
both styles (it would break the square look).

## 10. Chart colours

Hand-written SVG: colour through `className` (`stroke-ds-*`, `fill-ds-*`) or `style`, so it is theme-aware and easy to grep.

```tsx
import { useId } from "react";
import { L } from "@/shared/i18n";

type TrendChartProps = { actual: string; target: string; area: string; points: { x: number; y: number }[] };

export function TrendChart({ actual, target, area, points }: TrendChartProps) {
  const gradientId = useId();
  return (
    <svg viewBox="0 0 600 240" className="h-60 w-full" preserveAspectRatio="none" role="img" aria-label={L("الأداء", "Performance")}>
      <defs>
        <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" style={{ stopColor: "var(--ds-chart-area)", stopOpacity: 0.3 }} />
          <stop offset="100%" style={{ stopColor: "var(--ds-chart-area)", stopOpacity: 0.01 }} />
        </linearGradient>
      </defs>
      <line x1={0} x2={600} y1={60} y2={60} className="stroke-ds-chart-grid" strokeDasharray="4 6" />
      <path d={area} fill={`url(#${gradientId})`} />
      <path d={target} fill="none" className="stroke-ds-chart-target" strokeWidth={2} strokeDasharray="6 7" />
      <path d={actual} fill="none" className="stroke-ds-chart-actual" strokeWidth={3} strokeLinejoin="round" />
      {points.map((p) => (
        <circle key={p.x} cx={p.x} cy={p.y} r={4} className="fill-ds-chart-bg stroke-ds-chart-actual" strokeWidth={2.5} />
      ))}
    </svg>
  );
}
```

Bars (8px; square track while `CORNERS` is square, pill when rounded):

```tsx
<div className="h-2 rounded-ds-pill bg-ds-track">
  <div
    className="h-full rounded-ds-pill transition-[width] duration-700"
    style={{ width: `${value}%`, backgroundColor: highlight ? "var(--ds-chart-2)" : "var(--ds-chart-1)" }}
  />
</div>
```

Categorical order: `--ds-chart-1` → `-2` → `-3` → `-4`. Legend: `size-2.5 bg-ds-chart-1` swatch + `text-xs text-ds-label`.
Donut: `style={{ stroke: "var(--ds-chart-1)" }}` per segment.

shadcn / Recharts (`web/src/components/ui/chart.tsx`): give the config DS colours; shadcn exposes them as
`--color-<key>` on the chart. Don't name a key after a primitive (`gold-400`): it would shadow `--color-gold-400` inside the chart.

```tsx
import type { ChartConfig } from "@/components/ui/chart";

const chartConfig = {
  actual: { label: L("الفعلي", "Actual"), color: "var(--ds-chart-actual)" },
  target: { label: L("المستهدف", "Target"), color: "var(--ds-chart-target)" },
} satisfies ChartConfig;
// <Line dataKey="actual" stroke="var(--color-actual)" /> inside <ChartContainer config={chartConfig}>
```

Libraries that need plain colour strings (canvas, antd charts): read `themes[name].chart` from `tokens.ts`.

## 11. shadcn inside a theme

Needs `web/src/styles/shadcn.css` to be loaded (not imported yet; see SKILL.md).

```tsx
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { DirectionProvider } from "@/components/ui/direction";

<DirectionProvider dir="rtl">
  <div data-feature="" data-theme="navy-gold">
    <Card>{/* paper, navy text: --card, --card-foreground from the theme; corners from --radius (corners.css) */}
      <CardHeader><CardTitle>{L("ملخص", "Summary")}</CardTitle></CardHeader>
      <CardContent>
        <Button>{L("حفظ", "Save")}</Button>{/* navy; gold inside data-section="dark" */}
      </CardContent>
    </Card>

    <Dialog>
      <DialogTrigger asChild><button className={btnGhost}>{L("فتح", "Open")}</button></DialogTrigger>
      {/* portalled under <body>: repeat the theme (and data-section if it should be dark) and the root attribute */}
      <DialogContent data-theme="navy-gold" data-feature="">
        <DialogTitle>…</DialogTitle>
        <div className="text-sm leading-7 text-ds-body">{/* feature utilities work from here down */}</div>
      </DialogContent>
    </Dialog>
  </div>
</DirectionProvider>
```

- Same for `PopoverContent`, `TooltipContent`, `SelectContent`, `DropdownMenuContent`, `SheetContent`: pass `data-theme`
  and the root attribute. The content element itself takes shadcn's classes; this feature's utilities apply to its children.
- Feature utilities are unlayered and beat shadcn's layered ones on the same element. Avoid fixed-radius and shadow
  class names (`rounded-lg`, `rounded-xl`, `shadow-sm`) anywhere in the feature folder, comments included: the scan would
  generate them with Tailwind's defaults and they would override the matching shadcn parts on the page.
- Don't add `rounded-*` overrides to shadcn parts; the theme's `--radius` sets them.
- `asChild` on a plain element (as above) is safe on React 18; around a shadcn component the ref is lost.

## 12. Don't

| Don't | Do instead | Why |
|---|---|---|
| Raw colours: `bg-[#…]`, `style={{ color: "#…" }}`, `rgb(…)`, hex in SVG attributes | a `ds-*` utility or `var(--ds-…)` | hard-coded colours ignore `data-theme` / `data-section` |
| Tailwind palette colours on themed UI: `text-slate-500`, `bg-white`, `border-gray-200` | `text-ds-body`, `bg-ds-surface`, `border-ds-border` | Tailwind's scale is fixed |
| Primitives in components: `bg-navy-900`, `var(--color-gold-400)` | the semantic role; add a role if none fits | primitives are fixed paint for theme files |
| `rounded-lg`, `rounded-xl`, `rounded-full`, `rounded-md`, `border-radius: 10px` on DS elements | `rounded-ds-md` / `-lg` / `-pill` …, `var(--ds-radius-<step>)` | fixed radii ignore the CORNERS switch (only real circles use `rounded-ds-round`) |
| `rounded-ds-*` on a hairline grid without `overflow-hidden` | add `overflow-hidden`, or leave it square | the filled cells poke past the rounded corner |
| `shadow-sm`, `shadow-md`, shadows in dark sections | `shadow-ds-sm` / `shadow-ds-md`, light scope only | Tailwind's shadows are the grey defaults |
| `dark ? "text-white" : "text-navy-900"` ternaries | one class + `data-section` | the scope picks the value |
| Global preflight, or `@import "tailwindcss"` anywhere | the entry in §1 (theme + scoped utilities, `source(none)`) | preflight resets every element; antd pages change |
| Importing a Tailwind entry from `main.tsx`, `app.css` or a shared file | import it from the feature's root component | keeps the CSS with the feature |
| `@import "tailwindcss/utilities.css"` unscoped (global, layered), or utilities under `:where([data-feature])` or in `@layer` | `[data-feature] { @tailwind utilities source(none); }`, unlayered | global utilities restyle antd pages that share a class name, and lose inside your page to unlayered `web/src/styles/app.css`, `web/src/shared/*.css` and antd's link rules (0,1,1) |
| Utility classes on the root element itself | put them on the first inner wrapper | scoped utilities only match descendants of the root |
| `!important` to win a clash | scope the rule under the root, or rename the class | it also wins over every theme and section |
| Nesting two `data-theme` values | one theme per subtree | both themes' section rules match |
| A modifier on an alpha token: `border-ds-accent-border/50` | `border-ds-accent/50` | the alphas multiply |
| Physical sides in new code: `pr-4`, `border-r-2`, `text-right`, `left-0` | `ps-4`, `border-s-2`, `text-start`, `start-0` | one stylesheet for RTL and LTR |
| Editing a token file to fix one component | add a semantic role in every theme | token files are shared by every page |
