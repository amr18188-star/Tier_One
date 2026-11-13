# Navy & Gold — Hero Design System (Tier1 copy)

Copied from `Frontend/React/HERO_DESIGN_SYSTEM.md` (the old React app, read-only) on 2026-11-13.
The spec text is unchanged. Lines marked **Tier1:** and the "Tier1 token" columns are additions: they name the
token that carries each colour in `web/src/styles/design-system/`. Paths are relative to the Tier1 root
(`F:\Projects\angular\Indecators\Context\Tier1`) unless they start with `Frontend/`.

Token notation in the added columns: `--color-x` is a primitive (`primitives.css`); `ds-x` is the semantic
token `--ds-x` (utility `bg-ds-x`, `text-ds-x` …); "light" / "dark" is the section scope under
`data-theme="navy-gold"`; `@40%` means `color-mix(… 40%, transparent)`.

## Contents

1. Identity · 2. Color tokens · 3. Typography · 4. Shape, borders, elevation · 5. Layout and spacing ·
6. Backgrounds and texture · 7. Iconography and motion · 8. Components · 9. Data visualisation ·
10. Drop-in tokens · 11. Accessibility · 12. RTL and bilingual rules · 13. Do and don't

Extracted from the old app's landing page (`Frontend/React/src/Layout/Home.jsx`), checked against its live render.
Use this file as the single reference when you rebuild the look.

**Tier1:** the port of that page lives in the folder `web/src/features/landing/` (being written; once it exists it is
the live reference implementation). The tokens are in `web/src/styles/design-system/`.

> **Scope.** This is the *landing / hero* identity. The old signed-in app under `/app` used a different, softer palette
> (`Frontend/React/src/styles/tokens.css`: navy `#0d2440`, gold `#c9a227`, 14px radius). Don't mix the two.
>
> **Tier1:** that softer palette is the theme `app-soft` (`web/src/styles/design-system/themes/app-soft.css`). Both themes
> share every token name, so "don't mix" means: one `data-theme` per subtree, never nested.

---

## 1. Identity in one paragraph

Institutional, calm, and authoritative. A **deep midnight navy** carries the weight. **Antique gold** is the single accent, used for the one idea that matters in each block: the highlighted half of a headline, the primary button, icons, and thin rules. Every shape is **square**: no rounded corners anywhere, and even status dots and legend swatches are squares. Structure comes from **1px hairlines and faint grids**, not shadows. Dark navy sections alternate with white, cool-grey and warm-sand sections. The layout is **RTL-first Arabic** (Cairo) with small uppercase English labels as a secondary voice.

**Five rules that define the look**

1. Navy for mass, gold for meaning. Gold never fills more than one element per view, apart from thin lines and icons.
2. `border-radius: 0` on everything except charts (donut, data points). **Tier1:** `rounded-ds` (`--ds-radius`, 0 in navy-gold).
3. Hairline dividers (1px) and grid textures instead of drop shadows.
4. Two-tone headlines: a neutral line followed by a gold highlight.
5. Generous Arabic line height: 1.5 for headings, 2.0 for body.

---

## 2. Color tokens

### 2.1 Navy (brand mass, dark surfaces)

| Token | Hex | Tier1 token | Role |
|---|---|---|---|
| `navy-950` | `#06152A` | `--color-navy-950` → `ds-chrome`, shadcn `--sidebar` | Header and footer chrome. Header uses it at 95% + `backdrop-blur-md`. |
| `navy-900` | `#071A33` | `--color-navy-900` → dark `ds-bg`; light `ds-heading`, `ds-fg`, `ds-btn-primary-bg`, `ds-tile-solid`, `ds-tile-active-fg` | **Brand base.** Dark section background, heading text on light, text on gold buttons. |
| `navy-800` | `#0C2545` | `--color-navy-800` → dark `ds-surface`, `ds-callout`; `ds-chart-1` | Raised surface on dark (cards, badges at 90%). First chart series. |
| `navy-700` | `#12345B` | `--color-navy-700` → dark `ds-surface-hover`, `ds-track` | Card hover on dark. |
| `navy-600` | `#18304D` | `--color-navy-600` → `ds-grid-hero` (inside `--ds-texture-hero`) | Hero grid lines only. |
| `navy-500` | `#29415F` | `--color-navy-500` → dark `ds-border` | Borders and 1px dividers on dark. |
| `steel-400` | `#7890AC` | `--color-steel-400` → `ds-chart-target`, `ds-chart-3`, `ds-grid-dark` (inside `--ds-texture-dark`), dark `ds-faint` | Muted data (target line), background grid on dark sections at 7–8%. |

### 2.2 Gold (the single accent)

| Token | Hex | Tier1 token | Role |
|---|---|---|---|
| `gold-200` | `#E5C47E` | `--color-gold-200` → dark `ds-highlight-display` | Display highlight on dark (second line of the hero H1). Lighter, so huge type doesn't feel heavy. |
| `gold-400` | `#D6B46A` | `--color-gold-400` → `ds-accent`; dark `ds-highlight`, `ds-eyebrow`, `ds-icon`, `ds-btn-primary-bg`; `ds-accent-border` (dark @30%, light @40%); `ds-tile-active`; `ds-chart-2`, `ds-chart-area`, `ds-chart-current`; shadcn `--ring` | **Primary accent.** CTA fill, icons and eyebrows on dark, highlight words on dark, accent borders (at 30 / 40 / 50% opacity), active chart point. |
| `gold-500` | `#B9944D` | `--color-gold-500` → light `ds-highlight`, `ds-highlight-display`, `ds-accent-strong`; `ds-chart-actual` | Highlight words on light, "actual" data line, connector lines, thick accent rules (`border-r-4`). |
| `gold-600` | `#A7833D` | `--color-gold-600` → light `ds-eyebrow`, `ds-icon` | Eyebrow labels, captions, and icons on light surfaces. Use **only bold ≥ 18.7px or regular ≥ 24px** (see §11). |
| `gold-700` ★ | `#82652B` | `--color-gold-700` → light `ds-eyebrow-aa` | **Recommended addition, not in the original.** AA-safe gold for *small* text on white / sand / cream (≥ 4.6:1 on all three). |

### 2.3 Light surfaces (warm and cool)

| Token | Hex | Tier1 token | Role |
|---|---|---|---|
| `white` | `#FFFFFF` | `--color-paper` → light `ds-bg`, `ds-surface`; dark `ds-heading`, `ds-fg`; `ds-chart-bg` | Default light section and card. |
| `cloud` | `#F8F9FB` | `--color-cloud` → light `ds-bg-cool`; shadcn `--secondary`, `--ui-muted` | Cool off-white section background (analytics). Inner info panels. |
| `sand-200` | `#F4EBD8` | `--color-sand-200` → light `ds-bg-warm` | Warm section background (hierarchy, final CTA). |
| `sand-100` | `#F8F3E9` | `--color-sand-100` → light `ds-surface-warm` | Stat cells sitting on sand. |
| `sand-50` | `#F8F4EA` | `--color-sand-50` → light `ds-tile` | Icon-tile fill on light. |
| `ivory` | `#F8F6F1` | `--color-ivory` → light `ds-surface-hover`, `ds-callout` | Hover tint on white cells; inline callout fill. |
| `ivory-50` | `#FCFBF8` | `--color-ivory-50` → light `ds-surface-hover-soft` | Feature-card hover fill. |

### 2.4 Text

| Context | Token | Hex | Tier1 token | Use |
|---|---|---|---|---|
| On dark | `white` | `#FFFFFF` | `--color-paper` → dark `ds-heading`, `ds-fg` | Headings, card titles. |
| On dark | `mist-100` | `#DCE5F0` | `--color-mist-100` → dark `ds-body-strong`, `ds-label` | Subtitle, hero eyebrow, emphasized body. |
| On dark | `mist-300` | `#AAB8CA` | `--color-mist-300` → dark `ds-body` | Body and description text, footer copy. |
| On dark | `white/20` | — | dark `ds-index` (alpha built in) | Big decorative index numbers ("01"). |
| On light | `navy-900` | `#071A33` | `--color-navy-900` → light `ds-heading`, `ds-fg` | Headings, numbers, KPI values. |
| On light | `slate-600` | `#45556C` | `--color-ash-600` → light `ds-label`, `ds-body-strong` | Labels (chart legend, bar labels). |
| On light | `slate-500` | `#62748E` | `--color-ash-500` → light `ds-body` | Body and descriptions. |
| On light | `slate-400` | `#90A1B9` | `--color-ash-400` → light `ds-faint` | Captions, axis labels (decorative only, 2.6:1). |
| On light | `slate-300` | `#CAD5E2` | `--color-ash-300` → light `ds-index` | Muted index numbers. |

`slate-*` values are Tailwind v4's (OKLCH, sampled from the render). With Tailwind v3 they become `#475569 / #64748B / #94A3B8 / #CBD5E1`, which is close enough.

**Tier1:** the v4 slate steps are the primitives `--color-ash-100 … 600` (exact OKLCH; named `ash` so they never override
Tailwind's own `slate`). The v3 hex that `Home.jsx` hard-codes in SVG are `--color-haze-200/300/400`.

### 2.5 Lines and tracks on light

| Token | Hex | Tier1 token | Use |
|---|---|---|---|
| `slate-200` | `#E2E8F0` | `--color-ash-200` → light `ds-border`; gridlines: `--color-haze-200` → `ds-chart-grid` | Default 1px borders and chart gridlines. |
| `slate-100` | `#F1F5F9` | `--color-ash-100` → light `ds-track` | Progress-bar track. |
| `gold-400 / 30–50%` | — | `ds-accent-border` (light @40%, dark @30%); for 50% use `border-ds-accent/50` | Accent borders on cards and icon tiles. |

### 2.6 Signal colors (use sparingly)

| Token | Hex | Tier1 token | Use |
|---|---|---|---|
| `signal-green` | `#22C55E` | `--color-signal-green` → `ds-live`, `ds-chart-up` | "Live / on track" square dot, rising line in the hero background. |
| `signal-red` | `#EF4444` | `--color-signal-red` → `ds-chart-down` | Falling line in the hero background **only**. Never used in UI chrome. |

### 2.7 Semantic map (what to reach for)

| Role | Dark section | Light section | Tier1 token (same class in both) |
|---|---|---|---|
| Background | `navy-900` | `white` / `cloud` / `sand-200` | `bg-ds-bg` / `bg-ds-bg-cool` / `bg-ds-bg-warm` |
| Surface (card) | `navy-800` | `white` (on sand/cloud) | `bg-ds-surface` |
| Surface hover | `navy-700` | `ivory` / `ivory-50` | `hover:bg-ds-surface-hover` / `hover:bg-ds-surface-hover-soft` |
| Border / divider | `navy-500` | `slate-200` | `border-ds-border` |
| Accent border | `gold-400/30` | `gold-400/40–50` | `border-ds-accent-border` (or `border-ds-accent/50`) |
| Heading | `white` | `navy-900` | `text-ds-heading` |
| Highlight word | `gold-400` (`gold-200` for the H1) | `gold-500` | `text-ds-highlight` (`text-ds-highlight-display` for stacked display lines) |
| Body | `mist-300` | `slate-500` | `text-ds-body` |
| Eyebrow / caption | `gold-400` | `gold-600` (large) / `gold-700` (small) | `text-ds-eyebrow` / `text-ds-eyebrow-aa` |
| Icon | `gold-400` | `gold-600` | `text-ds-icon` |

---

## 3. Typography

**Font:** [Cairo](https://fonts.google.com/specimen/Cairo), loaded weights 300–900, used for Arabic *and* Latin. Fallback: `'Geist Variable', 'Segoe UI', sans-serif`.

```html
<link rel="preconnect" href="https://fonts.googleapis.com" />
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
<link href="https://fonts.googleapis.com/css2?family=Cairo:wght@300;400;500;600;700;800&display=swap" rel="stylesheet" />
```

**Tier1:** no Google Fonts link. The stack is `--ds-font` / `--ds-font-heading` (utilities `font-ds`, `font-ds-heading`). Tier1
self-hosts fonts with fontsource; Cairo comes from `@fontsource/cairo` (being added by the landing port; check
`web/package.json`). Without it the stack falls back to Segoe UI.

Weights actually used: **300** (decorative numbers), **400** (body), **500** (badge text), **600** (subtitle, ghost buttons, suffixes), **700** (all headings, buttons, eyebrows, numbers). Nothing heavier.

### Type scale

| Style | Size (mobile → desktop) | Weight | Line height | Tracking | Color | Tier1 colour token |
|---|---|---|---|---|---|---|
| **Display / H1** | 36 → 48 (sm) → 60 (md) → 72px (lg) | 700 | 1.5 | −0.025em | line 1 `white`, line 2 `gold-200` | `ds-heading` + `ds-highlight-display` |
| **Section H2** | 30 → 36 (sm) → 48px (lg) | 700 | 1.5 | −0.025em | `navy-900` / `white` + highlight | `ds-heading` + `ds-highlight` |
| Hero subtitle | 18 → 20px | 600 | 32px | — | `mist-100` | `ds-body-strong` |
| Panel H3 | 20–24px | 700 | relaxed | — | `navy-900` | `ds-heading` |
| Card H3 | 18px | 700 | — | — | `white` / `navy-900` | `ds-heading` |
| Body lead | 14 → 16px | 400 | **32px** | — | `mist-300` / `slate-500` | `ds-body` |
| Body small | 12 → 14px | 400 | 28px | — | same | `ds-body` |
| Eyebrow (section) | 12px, UPPERCASE English | 700 | 16px | 0.05em | `gold-400` / `gold-600` | `ds-eyebrow` |
| Eyebrow (hero badge) | 12 → 14px | 500 | 20px | 0.025em | `mist-100` | `ds-body-strong` |
| Stat number | 48px | 700 | 1 | −0.025em | `navy-900` | `ds-heading` |
| KPI number | 30px | 700 | — | — | `navy-900` | `ds-heading` |
| Number suffix | 14px | 600 | — | — | `gold-600` | `ds-eyebrow` |
| Index number "01" | 30px (dark) / 20px (light) | **300** | — | — | `white/20` / `gold-400` | dark `ds-index` / light `ds-accent` |
| Code label "PSS" | 24px | 700 | — | 0.1em | `gold-400` | `ds-highlight` |
| English sub-label | 10px | 400 | — | — | `gold-600` | `ds-eyebrow` |
| Micro / axis | 10–11px | 400 | 24px | — | `slate-400` / `gold-600` | `ds-faint` / `ds-eyebrow` |

**Headline pattern:** title and highlight sit on one line in section H2s, and stack as two `display:block` lines in the hero and the final CTA.

```html
<h2>رؤية موحدة <span class="text-gold-500">للأداء المؤسسي</span></h2>
```

**Tier1:** `<h2 className="text-ds-heading">رؤية موحدة <span className="text-ds-highlight">للأداء المؤسسي</span></h2>`.

---

## 4. Shape, borders, elevation

| Property | Value |
|---|---|
| Border radius | **0 everywhere.** Status dots (`8×8`) and legend swatches are squares too. **Tier1:** `rounded-ds`. |
| Border width | 1px default. 2px for inline callout rules, 4px for the panel accent rule and the hero's bottom bar. |
| Accent rule | `border-inline-start` in gold (`border-r-2` / `border-r-4` in RTL). Marks quotes, notes and key panels. **Tier1:** `border-s-2` / `border-s-4` + `border-ds-accent` / `border-ds-accent-strong`. |
| Hairline grid | Parent `background = border color` + `gap: 1px`, children carry the surface color. Gives perfect 1px dividers between cards with no doubled borders. **Tier1:** `gap-px border border-ds-border bg-ds-border` + children `bg-ds-surface`. |
| Shadows | Nearly flat. `shadow-sm` on the analytics dashboard and hierarchy cards, `hover:shadow-md`. No shadows on dark sections. **Tier1:** `shadow-ds-sm` / `hover:shadow-ds-md` (the old app's navy-tinted shadows; Tailwind's own `shadow-sm` is grey). |
| Blur | Header only: `navy-950/95` + `backdrop-blur-md`. **Tier1:** `bg-ds-chrome/95 backdrop-blur-md`. |

---

## 5. Layout and spacing

| Token | Value |
|---|---|
| Container | `max-width: 1280px` (`max-w-7xl`), centered. Final CTA card: `max-w-5xl`. |
| Side gutter | 20px mobile (`px-5`), 40px from 640px (`sm:px-10`). |
| Section padding | 96px vertical (`py-24`), 112px from 1024px (`lg:py-28`). |
| Section heading block | `max-w-3xl`, centered, 48px below it (`mb-12`). |
| Hero | `min-height: 650px`, 700px from 1024px. Content centered; H1 `max-w-5xl`; paragraphs `max-w-3xl`. |
| Card padding | 28px (`p-7`) → 32px (`sm:p-8`). Feature rows 20 → 24px. |
| Icon tile | 48×48 (`h-12 w-12`); 56×56 for the CTA emblem. |
| Common gaps | 8, 12, 16, 24px (`gap-2/3/4/6`); 56px between two-column halves (`gap-14`). |
| Grids | Stats 1 → 2 → 4 cols · Pillars 1 → 2 → 3 · KPIs 2 → 4 · Dashboard 2/3 + 1/3. |

**Tier1:** spacing and layout are not tokens; use these Tailwind utilities directly (they do not change per theme).

### Section rhythm (top to bottom)

```
Header      navy-950 / 95% + blur                         (dark)   Tier1: data-section="dark", bg-ds-chrome/95
Hero        navy-900  + grid + wave chart + vignette     (dark)   data-section="dark", bg-ds-bg
Overview    white                                         (light)  bg-ds-bg
Pillars     navy-900  + faint grid                        (dark)   data-section="dark", bg-ds-bg
Analytics   cloud #F8F9FB                                 (light, cool)  bg-ds-bg-cool
Hierarchy   sand  #F4EBD8                                 (light, warm)  bg-ds-bg-warm
Data        white                                         (light)  bg-ds-bg
Integration navy-900  + faint grid                        (dark)   data-section="dark", bg-ds-bg
Final CTA   sand  #F4EBD8 with a white card               (light, warm)  bg-ds-bg-warm + bg-ds-surface
Footer      navy-950                                      (dark)   data-section="dark", bg-ds-chrome
```

Alternate dark and light, and never place two dark sections next to each other.

---

## 6. Backgrounds and texture

### Hero (four stacked layers)

1. **Base:** `#071A33`. **Tier1:** `bg-ds-bg` in a dark section.
2. **Grid:** 1px `#18304D` lines every **40px**, layer opacity **0.4**. **Tier1:** `style={{ backgroundImage: "var(--ds-texture-hero)", backgroundSize: "40px 40px" }}` + `opacity-40`.
3. **Wave chart:** two full-bleed SVG sine lines (green rising, red falling), 2px stroke, Gaussian glow (`stdDeviation 3`), area fill fading from 15% / 12% to 0. Layer opacity **0.6**. `preserveAspectRatio="none"`. **Tier1:** `stroke-ds-chart-up` / `stroke-ds-chart-down`; gradient stops `style={{ stopColor: "var(--ds-chart-up)" }}`.
4. **Vignette:** centered radial overlay that hides the texture behind the text and lets it show at the edges:
   `radial-gradient(ellipse at center, rgba(7,26,51,.96) 0%, rgba(7,26,51,.88) 40%, rgba(7,26,51,.65) 75%, rgba(7,26,51,.4) 100%)`
   **Tier1:** `style={{ background: "var(--ds-hero-vignette)" }}`.
5. **Bottom bar:** 4px strip of `gold-400` at 40% along the bottom edge. **Tier1:** `bg-ds-accent/40`.

### Other dark sections

Grid of `#7890AC` 1px lines every **48px** at **7–8%** opacity. No wave, no vignette.
**Tier1:** `style={{ backgroundImage: "var(--ds-texture-dark)", backgroundSize: "48px 48px" }}` + `opacity-[0.08]`.

```css
.texture-grid-hero  { background-image: linear-gradient(to right,#18304D 1px,transparent 1px), linear-gradient(to bottom,#18304D 1px,transparent 1px); background-size: 40px 40px; opacity: .4; }
.texture-grid-dark  { background-image: linear-gradient(to right,#7890AC 1px,transparent 1px), linear-gradient(to bottom,#7890AC 1px,transparent 1px); background-size: 48px 48px; opacity: .08; }
.hero-vignette      { background: radial-gradient(ellipse at center, rgba(7,26,51,.96) 0%, rgba(7,26,51,.88) 40%, rgba(7,26,51,.65) 75%, rgba(7,26,51,.4) 100%); }
```

**Tier1:** these three are `--ds-texture-hero`, `--ds-texture-dark` and `--ds-hero-vignette` (theme-level composites, defined per theme).

---

## 7. Iconography and motion

**Icons:** line icons on a 24×24 viewBox, `stroke="currentColor"`, `stroke-width: 1.6`, round caps and joins, no fill. Rectangles are drawn with `rx=0` to match the square language. Sizes are 15–27px (17 in the header, 19 in CTAs, 22–23 in tiles). [Lucide](https://lucide.dev) matches the style if you set `strokeWidth={1.6}`.

**Tier1:** `lucide-react` is installed (shadcn). Colour icons through the text colour (`text-ds-icon`).

**Motion:** quiet and short.

| Interaction | Effect |
|---|---|
| Buttons, cards, tiles | Color swap only, Tailwind default 150ms (`transition-colors` / `transition-all`). No lift, no scale. |
| CTA arrow | Nudges 4px toward the reading direction on hover (`group-hover:-translate-x-1` in RTL). |
| Icon tile in a hovered card | Fills with `gold-400` and the icon turns `navy-900`. **Tier1:** `group-hover:bg-ds-tile-active group-hover:text-ds-tile-active-fg`. |
| Progress bars | Width animates over 700ms. |
| Entrance animations | None. |

---

## 8. Components

The recipes use the token utilities from §10 (Tailwind v4 `@theme` or v3 `extend.colors`). Swap them for the hex values if you don't use Tailwind.

> **Tier1:** the recipes below are the original ones, kept for reference. They use fixed primitive colours
> (`bg-navy-950`, `text-gold-600`, `text-slate-500`, `border-white/10`) and physical sides (`border-r`, `pr-4`), so they
> do not follow `data-theme` or `data-section`. For Tier1 code use `component-recipes.md`: same anatomy, `ds-*` tokens,
> logical sides. The tables below carry the token equivalents.

### Header

```html
<header class="relative z-30 border-b border-white/10 bg-navy-950/95 px-5 py-3 backdrop-blur-md sm:px-10">
  <div class="mx-auto flex max-w-7xl items-center justify-between gap-6">
    <img src="logo.png" alt="…" class="h-14 w-auto sm:h-16" />
    <a class="inline-flex items-center gap-3 border border-white/20 px-6 py-3 text-sm font-semibold text-white transition-all
              hover:border-gold-400 hover:bg-gold-400 hover:text-navy-900">تسجيل الدخول <!-- arrow 17 --></a>
  </div>
</header>
```

**Tier1:** `<header data-section="dark">` with `border-ds-line-soft/10 bg-ds-chrome/95`; the link uses the ghost button tokens.

### Buttons

| Variant | Rest | Hover | Where | Tier1 tokens |
|---|---|---|---|---|
| **Primary (gold)** | `bg-gold-400 border-gold-400 text-navy-900` | `bg-transparent text-gold-400` (inverts to outline) | On dark | `ds-btn-primary-*` inside `data-section="dark"` |
| **Primary (navy)** | `bg-navy-900 border-navy-900 text-white` | `bg-gold-400 border-gold-400 text-navy-900` | On light | the same `ds-btn-primary-*` classes in a light scope |
| **Ghost** | `border-white/20 text-white` | `bg-gold-400 border-gold-400 text-navy-900` | Header / dark | `ds-btn-ghost-*` |

Shared: `group inline-flex items-center gap-4 border px-8 py-4 text-sm font-bold transition-all`, plus an arrow icon with `transition-transform group-hover:-translate-x-1`. The ghost button uses `gap-3 px-6 py-3 font-semibold`.

### Hero eyebrow badge

```html
<div class="mb-6 inline-flex items-center gap-3 border border-white/15 bg-navy-800/90 px-5 py-3">
  <span class="h-2 w-2 bg-signal-green"></span>
  <span class="text-xs font-medium tracking-wide text-mist-100 sm:text-sm">…</span>
</div>
```

**Tier1:** `border-ds-line-soft/15 bg-ds-surface/90`, dot `bg-ds-live`, text `text-ds-body-strong`.

### Section eyebrow (flanked by rules)

```html
<div class="mb-4 inline-flex items-center gap-3 text-xs font-bold tracking-wider text-gold-600"> <!-- dark: text-gold-400 -->
  <span class="h-px w-8 bg-current"></span>THE FRAMEWORK<span class="h-px w-8 bg-current"></span>
</div>
```

**Tier1:** `text-ds-eyebrow` in both scopes; the rules keep `bg-current`.

### Section heading

```html
<div class="mx-auto mb-12 max-w-3xl text-center">
  <!-- eyebrow -->
  <h2 class="text-3xl font-bold leading-[1.5] tracking-tight text-navy-900 sm:text-4xl lg:text-5xl">
    Title <span class="text-gold-500">highlight</span>   <!-- dark: text-white + text-gold-400 -->
  </h2>
  <p class="mt-5 text-sm leading-8 text-slate-500 sm:text-base">…</p> <!-- dark: text-mist-300 -->
</div>
```

**Tier1:** `text-ds-heading`, `text-ds-highlight`, `text-ds-body`; no dark/light ternaries.

### Icon tiles

| Variant | Classes | Tier1 tokens |
|---|---|---|
| On dark | `h-12 w-12 flex items-center justify-center border border-gold-400/30 bg-gold-400/10 text-gold-400` | `border-ds-accent-border bg-ds-tile text-ds-icon` (section picks the values) |
| On light | `… border border-gold-400/40 bg-sand-50 text-gold-600` | same classes as on dark |
| Solid | `… bg-navy-900 text-gold-400` | `bg-ds-tile-solid text-ds-tile-solid-fg` |
| Hover (inside a `group` card) | `group-hover:bg-gold-400 group-hover:text-navy-900 transition-colors` | `group-hover:bg-ds-tile-active group-hover:text-ds-tile-active-fg` |

### Dark card grid (hairline dividers)

```html
<div class="grid gap-px border border-navy-500 bg-navy-500 md:grid-cols-2 lg:grid-cols-3">
  <article class="group flex min-h-[290px] flex-col bg-navy-800 p-7 transition-colors hover:bg-navy-700 sm:p-8">
    <div class="mb-8 flex items-center justify-between">
      <!-- icon tile (dark) -->
      <span class="text-3xl font-light text-white/20">01</span>
    </div>
    <h3 class="text-lg font-bold text-white">…</h3>
    <p class="mt-4 text-sm leading-8 text-mist-300">…</p>
    <div class="mt-auto flex items-center gap-2 pt-6 text-xs font-semibold text-gold-400">
      <span class="h-px w-6 bg-gold-400"></span>Footer label
    </div>
  </article>
</div>
```

**Tier1:** grid `border-ds-border bg-ds-border`; card `bg-ds-surface hover:bg-ds-surface-hover`; number `text-ds-index`; title
`text-ds-heading`; text `text-ds-body`; footer `text-ds-eyebrow` + rule `bg-ds-accent`.

### Stat cell (light)

```html
<div class="group flex flex-col items-center px-6 py-10 text-center transition-colors hover:bg-ivory">
  <!-- icon tile (light) with group-hover fill, mb-5 -->
  <div class="flex items-baseline gap-2">
    <span class="text-5xl font-bold tracking-tight text-navy-900">92</span>
    <span class="text-sm font-semibold text-gold-600">مؤشرًا</span>
  </div>
  <p class="mt-3 text-sm text-slate-500">Label</p>
</div>
```

Group stat cells inside a single `border border-slate-200` wrapper with internal 1px dividers.

**Tier1:** `hover:bg-ds-surface-hover`, value `text-ds-heading`, suffix `text-ds-eyebrow`, label `text-ds-body`, wrapper `border-ds-border`.

### Feature row (light)

```html
<article class="group flex gap-5 border border-slate-200 p-5 transition-colors hover:border-gold-400 hover:bg-ivory-50 sm:p-6">
  <!-- icon tile (light) -->
  <div class="min-w-0 flex-1">
    <div class="flex flex-wrap items-center gap-2">
      <h3 class="text-base font-bold text-navy-900">Arabic title</h3>
      <span class="text-[10px] text-gold-600">English Subtitle</span>
    </div>
    <p class="mt-3 text-xs leading-7 text-slate-500 sm:text-sm">…</p>
  </div>
  <span class="text-sm font-semibold text-slate-300">01</span>
</article>
```

**Tier1:** `border-ds-border hover:border-ds-accent hover:bg-ds-surface-hover-soft`; subtitle `text-ds-eyebrow`; number `text-ds-index`.

### Callouts

| Variant | Classes | Tier1 tokens |
|---|---|---|
| Note on dark (hero) | `flex items-start gap-4 border border-gold-400/30 bg-navy-800/90 px-5 py-4` + shield icon `text-gold-400` + text `text-xs sm:text-sm leading-7 text-mist-100` | `border-ds-accent-border bg-ds-surface/90` + `text-ds-icon` + `text-ds-body-strong` |
| Inline note on light | `border-r-2 border-gold-400 bg-ivory px-4 py-3 text-[11px] leading-6 text-slate-500` | `border-s-2 border-ds-accent bg-ds-callout text-ds-body` |
| Key panel on light | `border-r-4 border-gold-500 bg-white p-6 sm:p-8` with eyebrow + H3 `text-2xl` + body | `border-s-4 border-ds-accent-strong bg-ds-surface` |
| Rule note on dark | `flex items-start gap-3 border-r-2 border-gold-400 pr-4` + icon + `text-xs leading-7 text-mist-300` | `border-s-2 border-ds-accent ps-4` + `text-ds-icon` + `text-ds-body` |

### Dashboard shell (light)

`border border-slate-200 bg-white shadow-sm`. The header row is `border-b border-slate-200 px-6 py-5 sm:px-8` with a green square dot and an uppercase `text-xs font-semibold` label above an `text-lg font-bold` title. The filter chip is `border border-slate-200 px-3 py-2 text-xs text-slate-500` with an icon.

**Tier1:** `border-ds-border bg-ds-surface shadow-ds-sm`; dot `bg-ds-live`; chip `border-ds-border text-ds-body`.

### Footer

```html
<footer class="border-t border-white/10 bg-navy-950 px-5 py-6 sm:px-10">
  <div class="mx-auto flex max-w-7xl flex-col items-center justify-between gap-3 text-center md:flex-row md:text-right">
    <p class="text-xs leading-6 text-mist-300">© …</p>
    <span class="text-xs font-semibold tracking-wider text-gold-400">CAPITAL UNIVERSITY</span>
  </div>
</footer>
```

**Tier1:** `<footer data-section="dark">` with `border-ds-line-soft/10 bg-ds-chrome`; copy `text-ds-body`; brand `text-ds-eyebrow`; `md:text-start`.

---

## 9. Data visualisation

| Element | Spec | Tier1 token |
|---|---|---|
| Categorical order | `navy-800 #0C2545` → `gold-400 #D6B46A` → `steel-400 #7890AC` → `slate-300 #CBD5E1` | `ds-chart-1` → `ds-chart-2` → `ds-chart-3` → `ds-chart-4` (shadcn `--chart-1…4`) |
| Actual line | `gold-500 #B9944D`, 3px, round joins. Points r=4, white fill, 2.5px gold-500 stroke. | `stroke-ds-chart-actual`; points `fill-ds-chart-bg stroke-ds-chart-actual` |
| Current point | r=7, `gold-400` fill, 3px white stroke. | `fill-ds-chart-current stroke-ds-chart-bg` |
| Target line | `steel-400`, 2px, dashed `6 7`. | `stroke-ds-chart-target` |
| Area under actual | Vertical gradient `gold-400` 30% → 1%. | `stopColor: var(--ds-chart-area)` |
| Gridlines | `#E2E8F0`, 1px, dashed `4 6`. | `stroke-ds-chart-grid` |
| Axis labels | `#94A3B8`, 10px (12px from `sm`). | `fill-ds-chart-axis` |
| Bars | 8px tall (`h-2`), square, track `slate-100`. Navy by default, gold for the bars you want to highlight. | track `bg-ds-track`; bars `var(--ds-chart-1)` / `var(--ds-chart-2)` |
| Donut | r=75, stroke 22, starts at 12 o'clock. Center value 36px bold navy plus 12px caption. | segments `var(--ds-chart-1…4)`; value `text-ds-heading`; caption `text-ds-body` |
| Legend | Square 8–10px swatch (or a 20×2px dash for target lines) + 12px `slate-600` label. | label `text-ds-label` |

**Tier1:** never put `var()` in SVG presentation attributes (`fill="var(--…)"`); use `className="fill-ds-…"` or `style`.

---

## 10. Drop-in tokens

**Tier1:** already done, do not paste these blocks.
- 10.1 lives in `web/src/styles/design-system/primitives.css` (same values in lower case, plus `paper`, `ash-*`, `haze-*`,
  `navy-900-rgb` and the `soft-*` palette for app-soft).
- 10.2 lives in `web/src/styles/design-system/tailwind-theme.css` as `@theme inline reference` (values via `var()`, nothing
  emitted on `:root`). Never `@import "tailwindcss"` in Tier1: it includes preflight, which changes the antd pages.
- 10.3 does not apply: Tier1 uses Tailwind 4.3.5.
- `--font-sans` → `--ds-font`; `--radius` → `--ds-radius` (plus shadcn `--radius`, set per theme). `--container`,
  `--section-y`, `--gutter` are not tokens in Tier1: use `max-w-7xl`, `py-24 lg:py-28`, `px-5 sm:px-10`.

### 10.1 Plain CSS (framework-agnostic, works in Angular)

```css
:root {
  /* Navy */
  --color-navy-950: #06152A;
  --color-navy-900: #071A33;
  --color-navy-800: #0C2545;
  --color-navy-700: #12345B;
  --color-navy-600: #18304D;
  --color-navy-500: #29415F;
  --color-steel-400: #7890AC;
  /* Text on dark */
  --color-mist-100: #DCE5F0;
  --color-mist-300: #AAB8CA;
  /* Gold */
  --color-gold-200: #E5C47E;
  --color-gold-400: #D6B46A;
  --color-gold-500: #B9944D;
  --color-gold-600: #A7833D;
  --color-gold-700: #82652B; /* AA-safe small gold text on light */
  /* Light surfaces */
  --color-cloud: #F8F9FB;
  --color-sand-200: #F4EBD8;
  --color-sand-100: #F8F3E9;
  --color-sand-50: #F8F4EA;
  --color-ivory: #F8F6F1;
  --color-ivory-50: #FCFBF8;
  /* Signal */
  --color-signal-green: #22C55E;
  --color-signal-red: #EF4444;

  --font-sans: 'Cairo', 'Geist Variable', 'Segoe UI', sans-serif;
  --radius: 0;
  --container: 1280px;
  --section-y: 6rem;     /* 7rem ≥ 1024px */
  --gutter: 1.25rem;     /* 2.5rem ≥ 640px */
}
```

### 10.2 Tailwind v4 (`@theme` in your main CSS)

```css
@import "tailwindcss";

@theme {
  --font-sans: 'Cairo', 'Geist Variable', 'Segoe UI', sans-serif;

  --color-navy-950: #06152A;
  --color-navy-900: #071A33;
  --color-navy-800: #0C2545;
  --color-navy-700: #12345B;
  --color-navy-600: #18304D;
  --color-navy-500: #29415F;
  --color-steel-400: #7890AC;
  --color-mist-100: #DCE5F0;
  --color-mist-300: #AAB8CA;
  --color-gold-200: #E5C47E;
  --color-gold-400: #D6B46A;
  --color-gold-500: #B9944D;
  --color-gold-600: #A7833D;
  --color-gold-700: #82652B;
  --color-cloud: #F8F9FB;
  --color-sand-200: #F4EBD8;
  --color-sand-100: #F8F3E9;
  --color-sand-50: #F8F4EA;
  --color-ivory: #F8F6F1;
  --color-ivory-50: #FCFBF8;
  --color-signal-green: #22C55E;
  --color-signal-red: #EF4444;
}
```

### 10.3 Tailwind v3 (`tailwind.config.js`)

```js
module.exports = {
  theme: {
    extend: {
      fontFamily: { sans: ['Cairo', 'Geist Variable', 'Segoe UI', 'sans-serif'] },
      colors: {
        navy:  { 950: '#06152A', 900: '#071A33', 800: '#0C2545', 700: '#12345B', 600: '#18304D', 500: '#29415F' },
        steel: { 400: '#7890AC' },
        mist:  { 100: '#DCE5F0', 300: '#AAB8CA' },
        gold:  { 200: '#E5C47E', 400: '#D6B46A', 500: '#B9944D', 600: '#A7833D', 700: '#82652B' },
        sand:  { 50: '#F8F4EA', 100: '#F8F3E9', 200: '#F4EBD8' },
        ivory: { DEFAULT: '#F8F6F1', 50: '#FCFBF8' },
        cloud: '#F8F9FB',
        signal: { green: '#22C55E', red: '#EF4444' },
      },
    },
  },
};
```

If the target project uses shadcn/ui, set `--radius: 0` and map `--primary → navy-900`, `--accent → gold-400`, `--ring → gold-400`.

**Tier1:** done per theme and per section in `themes/navy-gold.css`. In a dark section `--primary` is gold-400 and
`--accent` is navy-700. Full table in `token-files.md`.

---

## 11. Accessibility notes (measured)

| Pair | Ratio | Verdict | Tier1 tokens |
|---|---|---|---|
| `white` on `navy-900` | 17.4 | ✅ AAA | dark `ds-heading` on `ds-bg` |
| `mist-100` on `navy-900` | 13.7 | ✅ AAA | dark `ds-body-strong` on `ds-bg` |
| `mist-300` on `navy-900` / `navy-800` | 8.7 / 7.6 | ✅ AAA | dark `ds-body` on `ds-bg` / `ds-surface` |
| `gold-200` on `navy-900` | 10.4 | ✅ AAA | dark `ds-highlight-display` on `ds-bg` |
| `gold-400` on `navy-900` / `navy-800` | 8.8 / 7.8 | ✅ AAA | dark `ds-eyebrow`, `ds-icon`, `ds-highlight` |
| `navy-900` on `gold-400` (CTA) | 8.8 | ✅ AAA | dark `ds-btn-primary-fg` on `ds-btn-primary-bg` |
| `steel-400` on `navy-900` | 5.3 | ✅ AA | dark `ds-faint` on `ds-bg` |
| `slate-500` on `white` / `cloud` | 4.8 / 4.5 | ✅ AA (just) | light `ds-body` on `ds-bg` / `ds-bg-cool` |
| `slate-500` on `sand-200` | 4.0 | ⚠️ large text only | light `ds-body` on `ds-bg-warm` |
| `gold-600` on `white` | 3.5 | ⚠️ large/bold only. The original uses it at 10–12px: switch small text to `gold-700`. | light `ds-eyebrow`, `ds-icon` (small text: `ds-eyebrow-aa`) |
| `gold-600` on `sand-200` | 3.0 | ⚠️ large only | light `ds-eyebrow` on `ds-bg-warm` |
| `gold-500` on `white` | 2.8 | ❌ fails even for large text. Fine for lines and decoration. For H2 highlights on light, prefer `gold-600` (bold 30px+ passes 3:1) or `gold-700`. | light `ds-highlight`, `ds-highlight-display`, `ds-chart-actual` |
| `gold-400` on `white` | 2.0 | ❌ decoration and borders only, never text | light `ds-accent` |
| `slate-400` on `white` | 2.6 | ❌ decorative captions only | light `ds-faint` |
| `gold-700` on `white` / `sand-200` / `sand-50` | 5.5 / 4.6 / 5.0 | ✅ AA | light `ds-eyebrow-aa` |

Focus ring: the page itself defines none. Its links only get the browser's default `:focus-visible` outline, tinted by this repo's global `outline-ring/50` (gold at 50%). In a new project, set one explicitly so it survives the port: `:focus-visible { outline: 2px solid var(--color-gold-400); outline-offset: 2px; }`.

**Tier1:** that inherited tint is `--ds-focus` (`soft-gold-500` at 50%), and `::selection` is `--ds-selection-bg` /
`--ds-selection-fg`. Apply them on the feature root (see the scoped base layer in `component-recipes.md`).

### 11.1 Measured in Tier1 under navy-gold — kept on purpose for pixel identity with the original

Measured WCAG 2.x ratios (alpha composited) by the token pass. Thresholds: text 4.5, large text 3 (≥ 24px, or ≥ 18.7px
bold), UI and graphics 3. These are the original page's colours. Fixing them changes pixels, so navy-gold keeps them on
purpose, for pixel identity with the original. New UI should not copy these pairings: pick a passing token (for small gold
text on light, `text-ds-eyebrow-aa`: 5.5:1 on white, 4.6 on sand).

| Pair (scope: token on token) | Original value | Ratio | Needs | Where on the landing |
|---|---|---|---|---|
| light: `ds-body` on `ds-bg-warm` | `#62748e` on `#f4ebd8` | 4.02 | 4.5 | hierarchy section description |
| light: `ds-body` on `ds-surface-warm` | `#62748e` on `#f8f3e9` | 4.31 | 4.5 | stat pair labels 12px |
| light: `ds-body` on `ds-callout` | `#62748e` on `#f8f6f1` | 4.41 | 4.5 | inline note 11px |
| light: `ds-faint` on `ds-surface` | `#90a1b9` on `#ffffff` | 2.63 | 4.5 | chart captions, x-axis 10–12px |
| light: `ds-chart-axis` on `ds-chart-bg` | `#94a3b8` on `#ffffff` | 2.56 | 4.5 | y-axis values 10px |
| light: `ds-eyebrow` on `ds-bg` | `#a7833d` on `#ffffff` | 3.53 | 4.5 | eyebrows 12px, suffix 14px, notes 10–11px |
| light: `ds-eyebrow` on `ds-bg-cool` | `#a7833d` on `#f8f9fb` | 3.35 | 4.5 | analytics eyebrow 12px |
| light: `ds-eyebrow` on `ds-bg-warm` | `#a7833d` on `#f4ebd8` | 2.97 | 4.5 | hierarchy / CTA eyebrow 12px |
| light: `ds-highlight` on `ds-bg` | `#b9944d` on `#ffffff` | 2.84 | 3 (large) | h2 highlight 30–48px bold |
| light: `ds-highlight` on `ds-bg-cool` | `#b9944d` on `#f8f9fb` | 2.69 | 3 (large) | analytics h2 highlight |
| light: `ds-highlight` on `ds-bg-warm` | `#b9944d` on `#f4ebd8` | 2.39 | 3 (large) | hierarchy h2 highlight |
| light: `ds-highlight-display` on `ds-surface` | `#b9944d` on `#ffffff` | 2.84 | 3 (large) | CTA h2 line 2 |
| light: `ds-chart-actual` on `ds-chart-bg` | `#b9944d` on `#ffffff` | 2.84 | 3 (UI) | actual line |
| light: `ds-chart-2` on `ds-chart-bg` | `#d6b46a` on `#ffffff` | 1.98 | 3 (UI) | gold bars, donut |
| light: `ds-chart-4` on `ds-chart-bg` | `#cbd5e1` on `#ffffff` | 1.48 | 3 (UI) | donut "other" |
| dark: `ds-btn-ghost-border` on `ds-chrome` | `#384455` on `#06152a` (composited) | 1.85 | 3 (UI) | header login outline |

Decorative only (no requirement): index numbers (`ds-index` light 1.5, dark 1.9; light `ds-accent` numbers 2.0) and 1px
dividers. The header login outline fails 3:1, but the link is identified by its text. All dark-scope text passes AA
(lowest: `ds-body` on `ds-surface-hover`, 6.25).

app-soft: the only failure is the same decorative ghost outline on chrome (1.89). Its fourth chart series passes 3:1 on white.

---

## 12. RTL and bilingual rules

- Set `dir="rtl"` and `lang="ar"` on the root. Content is Arabic. Eyebrows, codes ("PSS", "SDGs") and sub-labels are uppercase English.
- The arrow icon points **left** (forward in RTL). Mirror it in LTR (`scaleX(-1)`), and flip the hover nudge (`translate-x-1`).
- The original uses physical sides (`border-r`, `pr-4`, `text-right`). In new projects use **logical** utilities (`border-s`, `ps-4`, `text-start`) so one stylesheet serves both directions.
- Keep Arabic line heights: never tighter than 1.5 on headings or 1.75 on body.

**Tier1:** `web/src/shared/i18n.ts` sets `lang` and `dir` on `<html>` from the user's language (`useLang()`, `L(ar, en)`).
Mirror with the `ltr:` / `rtl:` variants (`ltr:-scale-x-100`, `rtl:group-hover:-translate-x-1 ltr:group-hover:translate-x-1`).

---

## 13. Do and don't

**Do**

- Pair every navy block with exactly one gold focal point.
- Use the 1px hairline grid (`gap-px` on a border-colored parent) for card groups.
- Put an uppercase English eyebrow above every section title.
- Alternate dark and light sections, using warm sand for "story" sections and cool cloud for data.
- Keep icons as thin line icons inside square tiles.

**Don't**

- Round corners, use pills, or add soft drop shadows on dark backgrounds.
- Use gold as large background fills (the CTA button is the only filled gold surface).
- Introduce new hues. Green and red appear only as status signals.
- Set body text in gold on light backgrounds below 18px (use `gold-700` if you must).
- Animate on scroll, scale cards, or bounce. Motion stays a quiet color swap.
