/**
 * tokens.ts must equal the CSS (primitives.css + themes/*.css), and the pairs antd and the app rely on must pass AA.
 * The CSS is parsed as text: var(--color-*) is resolved from primitives.css, color-mix(in oklab|srgb, X n%, transparent)
 * becomes rgba(X, n/100), color-mix(in srgb, X n%, Y) becomes the mixed 8-bit hex, OKLCH primitives become 8-bit hex
 * (±1 per channel). ../shadcn.css (the one shadcn → token mapping) is checked against every theme at the end.
 */
import { describe, expect, it } from "vitest";
import { antdTheme } from "./antd-theme";
import { CORNERS } from "./config";
import { cornerStyles, corners, isCornerStyle, themes, type SectionTokens, type ThemeName } from "./tokens";

// Read through Node: vitest turns every CSS import (?raw too) into an empty string. The project has no @types/node.
const { readFileSync } = (await import(/* @vite-ignore */ `node:${"fs"}`)) as { readFileSync(path: URL, encoding: "utf8"): string };
const read = (rel: string) => readFileSync(new URL(rel, import.meta.url), "utf8");
const primitivesCss = read("./primitives.css");
const css: Record<ThemeName, string> = { "navy-gold": read("./themes/navy-gold.css"), "app-soft": read("./themes/app-soft.css") };

/** selector → { --name: raw value } for every rule block of a stylesheet (no nesting in these files). */
function blocks(text: string): Map<string, Record<string, string>> {
  const out = new Map<string, Record<string, string>>();
  const clean = text.replace(/\/\*[\s\S]*?\*\//g, "");
  for (const m of clean.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    const decls: Record<string, string> = {};
    for (const d of m[2].split(";")) {
      const i = d.indexOf(":");
      if (i < 0) continue;
      const name = d.slice(0, i).trim();
      if (name.startsWith("--")) decls[name] = d.slice(i + 1).replace(/\s+/g, " ").trim();
    }
    out.set(m[1].replace(/\s+/g, " ").trim(), decls);
  }
  return out;
}

const primitives = [...blocks(primitivesCss).values()][0];

function oklchToRgb(l: number, c: number, h: number): number[] {
  const a = c * Math.cos((h * Math.PI) / 180);
  const b = c * Math.sin((h * Math.PI) / 180);
  const L = (l + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const M = (l - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const S = (l - 0.0894841775 * a - 1.291485548 * b) ** 3;
  const lin = [
    4.0767416621 * L - 3.3077115913 * M + 0.2309699292 * S,
    -1.2684380046 * L + 2.6097574011 * M - 0.3413193965 * S,
    -0.0041960863 * L - 0.7034186147 * M + 1.707614701 * S,
  ];
  return lin.map((x) => Math.round(255 * Math.min(1, Math.max(0, x <= 0.0031308 ? 12.92 * x : 1.055 * x ** (1 / 2.4) - 0.055))));
}

const hex = (rgb: number[]) => "#" + rgb.map((n) => n.toString(16).padStart(2, "0")).join("");
function rgbOf(color: string): number[] {
  const h = color.match(/^#([0-9a-f]{6})$/i);
  if (h) return [0, 2, 4].map((i) => parseInt(h[1].slice(i, i + 2), 16));
  const o = color.match(/^oklch\(([\d.]+)% ([\d.]+) ([\d.]+)\)$/);
  if (o) return oklchToRgb(Number(o[1]) / 100, Number(o[2]), Number(o[3]));
  throw new Error("not a solid colour: " + color);
}

/** Resolve a theme value to tokens.ts notation. `scope`: the block whose var(--ds-*) references are substituted first. */
function resolve(value: string, scope?: Record<string, string>): string {
  let v = value;
  for (let depth = 0; scope && /var\(--ds-/.test(v); depth++) {
    if (depth > 5) throw new Error("var(--ds-*) nesting too deep: " + value);
    v = v.replace(/var\((--ds-[a-z0-9-]+)\)/g, (_, n: string) => {
      const s = scope[n];
      if (s === undefined) throw new Error("unknown token " + n);
      return s;
    });
  }
  v = v.replace(/var\((--color-[a-z0-9-]+)\)/g, (_, n: string) => {
    const p = primitives[n];
    if (p === undefined) throw new Error("unknown primitive " + n);
    return p.startsWith("oklch") ? hex(rgbOf(p)) : p;
  });
  v = v.replace(/color-mix\(in (?:oklab|srgb), (#[0-9a-f]{6}) ([\d.]+)%, transparent\)/gi, (_, c: string, pct: string) => {
    const [r, g, b] = rgbOf(c);
    return `rgba(${r}, ${g}, ${b}, ${Number(pct) / 100})`;
  });
  v = v.replace(/color-mix\(in srgb, (#[0-9a-f]{6}) ([\d.]+)%, (#[0-9a-f]{6})\)/gi, (_, a: string, pct: string, b: string) => {
    const w = Number(pct) / 100;
    const y = rgbOf(b);
    return hex(rgbOf(a).map((x, i) => Math.round(x * w + y[i] * (1 - w))));
  });
  return v;
}

/** Equal colours (hex within ±1 per channel, for the OKLCH primitives) or equal strings; in a composite value (a gradient)
 *  the text between the hex colours must match exactly and each hex within ±1. */
function same(a: string, b: string): boolean {
  if (/^#[0-9a-f]{6}$/i.test(a) && /^#[0-9a-f]{6}$/i.test(b)) return rgbOf(a).every((x, i) => Math.abs(x - rgbOf(b)[i]) <= 1);
  const norm = (s: string) => s.replace(/\s+/g, " ").toLowerCase();
  const hexRe = /#[0-9a-f]{6}/g;
  const [x, y] = [norm(a), norm(b)];
  if (x.split(hexRe).join("|") !== y.split(hexRe).join("|")) return false;
  const [hx, hy] = [x.match(hexRe) ?? [], y.match(hexRe) ?? []];
  return hx.length === hy.length && hx.every((h, i) => same(h, hy[i]));
}

const camel = (s: string) => s.replace(/-([a-z0-9])/g, (_, c: string) => c.toUpperCase());

/** --ds-* section token → path in SectionTokens. */
function sectionPath(name: string): string[] | null {
  if (!name.startsWith("--ds-")) return null;
  const n = name.slice(5);
  const btn = n.match(/^btn-(primary|ghost)-(.+)$/);
  if (btn) return [btn[1] === "primary" ? "btnPrimary" : "btnGhost", camel(btn[2])];
  return [camel(n)];
}

function get(obj: unknown, path: string[]): unknown {
  return path.reduce<unknown>((o, k) => (o && typeof o === "object" ? (o as Record<string, unknown>)[k] : undefined), obj);
}

describe.each(Object.keys(themes) as ThemeName[])("tokens.ts matches themes/%s.css", (name) => {
  const b = blocks(css[name]);
  const theme = b.get(`[data-theme="${name}"]`)!;
  const light = b.get(`[data-theme="${name}"], [data-theme="${name}"] [data-section="light"]`)!;
  const dark = b.get(`[data-theme="${name}"] [data-section="dark"], [data-theme="${name}"][data-section="dark"]`)!;
  const t = themes[name];

  it("finds the three blocks", () => {
    expect(theme).toBeDefined();
    expect(light).toBeDefined();
    expect(dark).toBeDefined();
  });

  it("declares no radius (corners are their own axis: corners.css)", () => {
    const radiusNames = [...b.values()].flatMap((decls) => Object.keys(decls)).filter((n) => n === "--radius" || n.startsWith("--ds-radius"));
    expect(radiusNames).toEqual([]);
  });

  it("declares only --ds-* tokens (shadcn variables are mapped once, in ../shadcn.css)", () => {
    const others = [...b.values()].flatMap((decls) => Object.keys(decls)).filter((n) => !n.startsWith("--ds-"));
    expect(others).toEqual([]);
  });

  it("theme-level tokens", () => {
    expect(theme["--ds-font"]).toBe(t.fontFamily);
    const pairs: [string, string][] = [
      ["--ds-chrome", t.chrome],
      ["--ds-grid-hero", t.gridHero],
      ["--ds-grid-dark", t.gridDark],
      ["--ds-chart-1", t.chart.series[0]],
      ["--ds-chart-2", t.chart.series[1]],
      ["--ds-chart-3", t.chart.series[2]],
      ["--ds-chart-4", t.chart.series[3]],
      ["--ds-chart-5", t.chart.series[4]],
      ...(["actual", "target", "area", "current", "bg", "grid", "axis", "up", "down", "college"] as const).map(
        (k): [string, string] => [`--ds-chart-${k}`, t.chart[k]],
      ),
      ["--ds-shadow-sm", t.shadow.sm],
      ["--ds-shadow-md", t.shadow.md],
      ["--ds-focus", t.focus],
      ["--ds-selection-bg", t.selection.bg],
      ["--ds-selection-fg", t.selection.fg],
      ["--ds-shade", t.shade],
      ["--ds-accent-on-dark", t.accentOnDark],
    ];
    for (const [n, ts] of pairs) expect(same(resolve(theme[n]), ts), `${n}: ${resolve(theme[n])} vs ${ts}`).toBe(true);
  });

  it.each(["sidebar", "band", "gradient", "hatch", "status"] as const)("%s tokens, both ways", (group) => {
    const prefix = `--ds-${group}-`;
    const cssNames = Object.keys(theme).filter((n) => n.startsWith(prefix));
    expect(cssNames.map((n) => camel(n.slice(prefix.length))).sort()).toEqual(Object.keys(t[group]).sort());
    for (const n of cssNames) {
      const ts = (t[group] as Record<string, string>)[camel(n.slice(prefix.length))];
      const css = resolve(theme[n], theme);
      expect(same(css, ts), `${n}: ${css} vs ${ts}`).toBe(true);
    }
  });

  it("gradients use tokens only (no raw colour in a gradient value)", () => {
    for (const n of Object.keys(theme).filter((x) => x.startsWith("--ds-gradient-") || x.startsWith("--ds-hatch-"))) {
      expect(theme[n], n).not.toMatch(/#[0-9a-f]{3,8}\b|rgba?\(|oklch\(/i);
    }
  });

  it.each(["light", "dark"] as const)("%s section tokens, both ways", (tone) => {
    const block = tone === "light" ? light : dark;
    const sect: SectionTokens = t.sections[tone];
    const seen = new Set<string>();
    for (const [n, v] of Object.entries(block)) {
      const path = sectionPath(n);
      if (!path) continue; // none: the test above fails on any non --ds-* name
      const ts = get(sect, path);
      expect(typeof ts, `${n} has no field ${path.join(".")} in tokens.ts`).toBe("string");
      expect(same(resolve(v), ts as string), `${n}: ${resolve(v)} vs ${String(ts)}`).toBe(true);
      seen.add(path.join("."));
    }
    const fields = Object.entries(sect).flatMap(([k, v]) => (typeof v === "object" ? Object.keys(v).map((s) => `${k}.${s}`) : [k]));
    expect(fields.filter((f) => !seen.has(f))).toEqual([]);
  });
});

// ---------- contrast (WCAG 2.x) ----------
function luminance(c: string): number {
  return rgbOf(c)
    .map((v) => v / 255)
    .map((v) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4))
    .reduce((s, v, i) => s + v * [0.2126, 0.7152, 0.0722][i], 0);
}
function contrast(a: string, b: string): number {
  const [x, y] = [luminance(a), luminance(b)].sort((p, q) => q - p);
  return (x + 0.05) / (y + 0.05);
}

describe.each(Object.keys(themes) as ThemeName[])("AA pairs in %s", (name) => {
  const t = themes[name];
  const l = t.sections.light;
  const d = t.sections.dark;
  const sb = t.sidebar;
  const text: [string, string, string][] = [
    ["primary button text", l.btnPrimary.fg, l.btnPrimary.bg],
    ["link on surface", l.link, l.surface],
    ["link on page bg (--bg)", l.link, l.bgCool],
    ["link on tile (--teal-50 chips)", l.link, l.tile],
    ["text on page bg", l.fg, l.bgCool],
    ["muted text (--muted) on surface", l.label, l.surface],
    ["muted text on page bg", l.label, l.bgCool],
    ["muted text on --sunk", l.label, l.track],
    ["error text on surface", l.destructive, l.surface],
    ["table header", d.heading, d.bg],
    ["sidebar link", sb.fg, sb.bg],
    ["sidebar group title", sb.muted, sb.bg],
    ["sidebar active link", sb.fgActive, sb.activeBg],
    ["sidebar hovered link", sb.fgActive, sb.hoverBg],
    ["sidebar badge", sb.badgeFg, sb.badgeBg],
  ];
  const ui: [string, string, string][] = [
    ["focus ring on surface", l.focusRing, l.surface],
    ["focus ring on page bg", l.focusRing, l.bgCool],
    ["tabs ink bar", l.icon, l.surface],
    ["sidebar active bar", sb.indicator, sb.activeBg],
    ["sidebar focus ring", sb.focus, sb.bg],
  ];
  it.each(text)("%s ≥ 4.5:1", (_, fg, bg) => expect(contrast(fg, bg)).toBeGreaterThanOrEqual(4.5));
  it.each(ui)("%s ≥ 3:1", (_, fg, bg) => expect(contrast(fg, bg)).toBeGreaterThanOrEqual(3));
});

/** A translucent rgba() token composited over a solid hex background. */
function over(rgba: string, base: string): string {
  const m = rgba.match(/^rgba\((\d+), (\d+), (\d+), ([\d.]+)\)$/);
  if (!m) throw new Error("not an rgba() token: " + rgba);
  const a = Number(m[4]);
  return hex(rgbOf(base).map((b, i) => Math.round(Number(m[i + 1]) * a + b * (1 - a))));
}
const stopsOf = (gradient: string) => gradient.match(/#[0-9a-f]{6}/gi) ?? [];

describe.each(Object.keys(themes) as ThemeName[])("AA for text on the gradients of %s (every stop, glows composited)", (name) => {
  const t = themes[name];
  const { light: l, dark: d } = t.sections;
  const b = t.band;
  const sb = t.sidebar;
  // A glow sits over the stop at its corner: band glow over `from` (top, where the kicker is), accent over `to` (bottom end);
  // the sidebar glow over the top stop. The kicker and links sit at the top, never on the bottom-end accent.
  const band = [b.from, b.via, b.to, over(b.glow, b.from)];
  const bandAll = [...band, over(b.glowAccent, b.to)];
  const auth = [b.via, b.from, t.shade, over(b.glow, b.via), over(b.glowAccent, t.shade)];
  const side = [sb.bgTop, sb.bg, over(sb.glow, sb.bgTop)];
  const cases: [string, string, string[]][] = [
    ["hero / band: heading", d.heading, bandAll],
    ["hero / band: kicker (--ds-eyebrow)", d.eyebrow, band],
    ["hero / band: link (--ds-link)", d.link, band],
    ["plan hero: kicker, rings, rail dots (--ds-accent-on-dark)", t.accentOnDark, band],
    ["active navy tab: icon (--ds-accent-on-dark on --navy)", t.accentOnDark, [l.heading, d.bg]],
    ["sign-in panel: heading", d.heading, auth],
    ["sidebar: link", sb.fg, side],
    ["sidebar: group title", sb.muted, side],
    ["sidebar: active link", sb.fgActive, side],
    ["avatar: initials", l.btnPrimary.fg, stopsOf(t.gradient.avatar)],
    ["callout: title (--ds-link)", l.link, stopsOf(t.gradient.callout)],
    ["callout: text", l.fg, stopsOf(t.gradient.callout)],
    ["chart panel: label", l.label, stopsOf(t.gradient.surfaceSubtle)],
  ];
  it.each(cases)("%s ≥ 4.5:1", (_, fg, bgs) => {
    expect(bgs.length).toBeGreaterThan(1);
    for (const bg of bgs) expect(contrast(fg, bg), `${fg} on ${bg}`).toBeGreaterThanOrEqual(4.5);
  });
  it("--ds-accent-on-dark equals the dark-section eyebrow", () => expect(t.accentOnDark).toBe(d.eyebrow));
  it("data marks: college series and review status ≥ 3:1 on the surface and the track", () => {
    expect(t.chart.college, "legend key must match the --teal bars").toBe(l.btnPrimary.bg);
    for (const c of [t.chart.college, t.status.review]) for (const bg of [l.surface, l.track]) expect(contrast(c, bg), `${c} on ${bg}`).toBeGreaterThanOrEqual(3);
  });
  it("progress fill ≥ 3:1 on the track", () => {
    for (const s of stopsOf(t.gradient.progress)) expect(contrast(s, l.track), s).toBeGreaterThanOrEqual(3);
  });
});

// ---------- corners (corners.css ⇄ `corners` in tokens.ts, config.ts) ----------
describe("corners.css matches tokens.ts", () => {
  const b = blocks(read("./corners.css"));
  const square = b.get(":root, [data-corners=\"square\"]")!;
  const rounded = b.get("[data-corners=\"rounded\"]")!;
  const shared = b.get(":root, [data-corners]")!;
  const px = (v: string) => (v.endsWith("px") ? Number.parseFloat(v) : Number.NaN);

  it("finds the three blocks", () => {
    expect(square).toBeDefined();
    expect(rounded).toBeDefined();
    expect(shared).toBeDefined();
  });

  it.each(cornerStyles)("%s steps, both ways", (style) => {
    const block = style === "square" ? square : rounded;
    const cssSteps = Object.keys(block).map((n) => n.replace("--ds-radius-", ""));
    expect(cssSteps.sort()).toEqual(Object.keys(corners[style]).sort());
    for (const [step, value] of Object.entries(corners[style])) expect(px(block[`--ds-radius-${step}`]), `--ds-radius-${step}`).toBe(value);
  });

  it("square is 0 everywhere; rounded grows step by step", () => {
    expect(Object.values(corners.square).every((v) => v === 0)).toBe(true);
    const r = corners.rounded;
    expect([r.xs, r.sm, r.md, r.lg, r.xl, r.pill]).toEqual([...[r.xs, r.sm, r.md, r.lg, r.xl, r.pill]].sort((x, y) => x - y));
  });

  it("aliases and real circles", () => {
    expect(shared).toEqual({ "--ds-radius-round": "50%", "--ds-radius": "var(--ds-radius-md)", "--radius": "var(--ds-radius-lg)" });
  });

  it("CORNERS is a valid style, square by default (the navy-gold choice)", () => {
    expect(isCornerStyle(CORNERS)).toBe(true);
    expect(CORNERS).toBe("square");
  });
});

describe("antdTheme", () => {
  it("builds from tokens only, keeps the caller's font and maps the corner scale", () => {
    const base = { token: { fontFamily: "X", fontSize: 14 } };
    const ng = antdTheme("navy-gold", base, { corners: "square" });
    expect(ng.token).toMatchObject({ fontFamily: "X", fontSize: 14, colorPrimary: themes["navy-gold"].sections.light.btnPrimary.bg, borderRadius: 0, borderRadiusLG: 0, borderRadiusSM: 0, borderRadiusXS: 0, borderRadiusOuter: 0 });
    expect(ng.components?.Table).toMatchObject({ headerBg: themes["navy-gold"].sections.dark.bg, headerBorderRadius: 0 });
    expect(ng.components?.Progress?.lineBorderRadius).toBe(0);
    const soft = antdTheme("app-soft", base, { corners: "rounded" });
    const r = corners.rounded;
    expect(soft.token).toMatchObject({ colorPrimary: themes["app-soft"].sections.light.btnPrimary.bg, borderRadius: r.md, borderRadiusLG: r.lg, borderRadiusSM: r.sm, borderRadiusXS: r.xs, borderRadiusOuter: r.xs });
    expect(soft.components?.Table?.headerBorderRadius).toBe(r.lg);
    expect(soft.components?.Progress?.lineBorderRadius).toBe(r.pill);
    // Any theme with either corner style; the default is CORNERS.
    expect(antdTheme("navy-gold", base, { corners: "rounded" }).token?.borderRadius).toBe(r.md);
    expect(antdTheme("app-soft", base).token?.borderRadius).toBe(corners[CORNERS].md);
    const values = JSON.stringify([ng, soft]);
    expect(values).not.toContain("var(");
  });
});

// ---------- shadcn (../shadcn.css: the one shadcn variable → --ds-* token mapping, for every theme) ----------
describe("shadcn.css maps every shadcn variable to design tokens", () => {
  const shadcnCss = read("../shadcn.css");
  const sb = blocks(shadcnCss);
  const mapping = sb.get(":where([data-theme], [data-section])")!;
  // blocks() keys the first block by all the text before it (the @import and @source lines), so match the end.
  const twTheme = [...sb].find(([k]) => k.endsWith("@theme inline"))?.[1] ?? {};
  /** These follow data-section, so they must point at section tokens (they flip on a navy band). */
  const sectioned = [
    "--background", "--foreground", "--card", "--card-foreground", "--popover", "--popover-foreground",
    "--primary", "--primary-foreground", "--secondary", "--secondary-foreground", "--ui-muted", "--muted-foreground",
    "--accent", "--accent-foreground", "--destructive", "--border", "--input", "--ring",
  ];
  /** Theme level: the same in both sections. */
  const themeLevel = [
    "--chart-1", "--chart-2", "--chart-3", "--chart-4", "--chart-5",
    "--sidebar", "--sidebar-foreground", "--sidebar-primary", "--sidebar-primary-foreground",
    "--sidebar-accent", "--sidebar-accent-foreground", "--sidebar-border", "--sidebar-ring",
  ];
  const target = (v: string) => mapping[v]?.match(/^var\((--ds-[a-z0-9-]+)\)$/)?.[1] ?? "";
  /** A colour (hex or rgba) at `alpha` × its own alpha, composited over a solid background. */
  const alphaOver = (colour: string, alpha: number, base: string) => {
    const m = colour.match(/^rgba\((\d+), (\d+), (\d+), ([\d.]+)\)$/);
    const [r, g, b, a] = m ? [Number(m[1]), Number(m[2]), Number(m[3]), Number(m[4])] : [...rgbOf(colour), 1];
    return over(`rgba(${r}, ${g}, ${b}, ${a * alpha})`, base);
  };

  it("finds the mapping and the Tailwind map", () => {
    expect(mapping).toBeDefined();
    expect(Object.keys(twTheme).length).toBeGreaterThan(30);
  });

  it("has no raw colour: no hex anywhere (comments included), only var(--ds-*) in the mapping", () => {
    expect(shadcnCss).not.toMatch(/#[0-9a-f]{3,8}\b/i);
    for (const [n, v] of Object.entries(mapping)) expect(v, n).toMatch(/^var\(--ds-[a-z0-9-]+\)$/);
  });

  it("maps exactly the variables its Tailwind map reads, both ways", () => {
    const used = Object.values(twTheme)
      .flatMap((v) => [...v.matchAll(/var\((--[a-z0-9-]+)\)/g)].map((m) => m[1]))
      .filter((n) => !n.startsWith("--ds-") && n !== "--radius");
    expect([...new Set(used)].sort()).toEqual(Object.keys(mapping).sort());
    expect(Object.keys(mapping).sort()).toEqual([...sectioned, ...themeLevel].sort());
  });

  it("radius: --radius comes from corners.css; every step reads the --ds-radius-* scale, never negative", () => {
    expect(mapping["--radius"]).toBeUndefined();
    const steps = Object.entries(twTheme).filter(([n]) => n.startsWith("--radius-"));
    expect(steps.length).toBe(8);
    for (const [n, v] of steps) expect(v, n).toMatch(/^(var\(--ds-radius-(xs|sm|md|xl)\)|var\(--radius\)|calc\(var\(--ds-radius-xl\) \* [\d.]+\))$/);
  });

  it("the dark: variant follows data-section, not a .dark class", () => {
    expect(shadcnCss).toMatch(/@custom-variant dark \(&:is\(\[data-section="dark"\]/);
    expect(shadcnCss.replace(/\/\*[\s\S]*?\*\//g, "")).not.toMatch(/\.dark\b/);
  });

  describe.each(Object.keys(themes) as ThemeName[])("in %s", (name) => {
    const b = blocks(css[name]);
    const theme = b.get(`[data-theme="${name}"]`)!;
    const scopes = {
      light: b.get(`[data-theme="${name}"], [data-theme="${name}"] [data-section="light"]`)!,
      dark: b.get(`[data-theme="${name}"] [data-section="dark"], [data-theme="${name}"][data-section="dark"]`)!,
    };

    it("every target exists: section variables in both scopes, the others at theme level", () => {
      for (const v of sectioned) for (const tone of ["light", "dark"] as const) expect(scopes[tone][target(v)], `${v} → ${target(v)} (${tone})`).toBeDefined();
      for (const v of themeLevel) expect(theme[target(v)], `${v} → ${target(v)}`).toBeDefined();
    });

    it("five distinct chart series", () => {
      const series = themeLevel.filter((v) => v.startsWith("--chart-")).map((v) => resolve(theme[target(v)]));
      expect(new Set(series).size).toBe(5);
    });

    describe.each(["light", "dark"] as const)("%s section, AA", (tone) => {
      const scope = { ...theme, ...scopes[tone] };
      const c = (v: string) => resolve(scope[target(v)], scope);
      const text: [string, string][] = [
        ["--foreground", "--background"],
        ["--card-foreground", "--card"],
        ["--popover-foreground", "--popover"],
        ["--primary-foreground", "--primary"],
        ["--primary", "--background"], // link Button
        ["--secondary-foreground", "--secondary"],
        ["--muted-foreground", "--ui-muted"],
        ["--muted-foreground", "--background"],
        ["--muted-foreground", "--card"],
        ["--accent-foreground", "--accent"],
        ["--destructive", "--background"],
        ["--destructive", "--card"],
        ["--sidebar-foreground", "--sidebar"],
        ["--sidebar-accent-foreground", "--sidebar-accent"],
        ["--sidebar-primary-foreground", "--sidebar-primary"],
      ];
      it.each(text)("%s on %s ≥ 4.5:1", (fg, bg) => expect(contrast(c(fg), c(bg))).toBeGreaterThanOrEqual(4.5));

      // Component recipes: destructive Button / Badge = text-destructive on bg-destructive/10 (dark: /20);
      // outline Button and Input = inherited text on bg-input/30 in a dark section.
      it.each(["--background", "--card"])("destructive Button text on its red tint over %s ≥ 4.5:1", (bg) => {
        expect(contrast(c("--destructive"), alphaOver(c("--destructive"), tone === "light" ? 0.1 : 0.2, c(bg)))).toBeGreaterThanOrEqual(4.5);
      });
      it("outline Button / Input text on its fill ≥ 4.5:1", () => {
        const fill = tone === "light" ? c("--background") : alphaOver(c("--input"), 0.3, c("--background"));
        expect(contrast(c("--foreground"), fill)).toBeGreaterThanOrEqual(4.5);
      });

      const ui: [string, string][] = [["--ring", "--background"], ["--ring", "--card"], ["--sidebar-ring", "--sidebar"]];
      it.each(ui)("%s on %s ≥ 3:1", (fg, bg) => expect(contrast(c(fg), c(bg))).toBeGreaterThanOrEqual(3));
    });
  });
});
