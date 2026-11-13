/**
 * tokens.ts must equal the CSS (primitives.css + themes/*.css), and the pairs antd and the app rely on must pass AA.
 * The CSS is parsed as text: var(--color-*) is resolved from primitives.css, color-mix(in oklab|srgb, X n%, transparent)
 * becomes rgba(X, n/100), OKLCH primitives become 8-bit hex (±1 per channel).
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

/** Resolve a theme value to tokens.ts notation. */
function resolve(value: string): string {
  let v = value.replace(/var\((--color-[a-z0-9-]+)\)/g, (_, n: string) => {
    const p = primitives[n];
    if (p === undefined) throw new Error("unknown primitive " + n);
    return p.startsWith("oklch") ? hex(rgbOf(p)) : p;
  });
  v = v.replace(/color-mix\(in (?:oklab|srgb), (#[0-9a-f]{6}) ([\d.]+)%, transparent\)/gi, (_, c: string, pct: string) => {
    const [r, g, b] = rgbOf(c);
    return `rgba(${r}, ${g}, ${b}, ${Number(pct) / 100})`;
  });
  return v;
}

/** Equal colours (hex within ±1 per channel, for the OKLCH primitives) or equal strings. */
function same(a: string, b: string): boolean {
  if (/^#[0-9a-f]{6}$/i.test(a) && /^#[0-9a-f]{6}$/i.test(b)) return rgbOf(a).every((x, i) => Math.abs(x - rgbOf(b)[i]) <= 1);
  return a.replace(/\s+/g, " ").toLowerCase() === b.replace(/\s+/g, " ").toLowerCase();
}

const camel = (s: string) => s.replace(/-([a-z0-9])/g, (_, c: string) => c.toUpperCase());

/** --ds-* section token → path in SectionTokens. */
function sectionPath(name: string): string[] | null {
  if (name === "--destructive") return ["destructive"];
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
      ...(["actual", "target", "area", "current", "bg", "grid", "axis", "up", "down"] as const).map(
        (k): [string, string] => [`--ds-chart-${k}`, t.chart[k]],
      ),
      ["--ds-shadow-sm", t.shadow.sm],
      ["--ds-shadow-md", t.shadow.md],
      ["--ds-focus", t.focus],
      ["--ds-selection-bg", t.selection.bg],
      ["--ds-selection-fg", t.selection.fg],
    ];
    for (const [n, ts] of pairs) expect(same(resolve(theme[n]), ts), `${n}: ${resolve(theme[n])} vs ${ts}`).toBe(true);
  });

  it.each(["sidebar", "band"] as const)("%s tokens, both ways", (group) => {
    const prefix = `--ds-${group}-`;
    const cssNames = Object.keys(theme).filter((n) => n.startsWith(prefix));
    expect(cssNames.map((n) => camel(n.slice(prefix.length))).sort()).toEqual(Object.keys(t[group]).sort());
    for (const n of cssNames) {
      const ts = (t[group] as Record<string, string>)[camel(n.slice(prefix.length))];
      expect(same(resolve(theme[n]), ts), `${n}: ${resolve(theme[n])} vs ${ts}`).toBe(true);
    }
  });

  it.each(["light", "dark"] as const)("%s section tokens, both ways", (tone) => {
    const block = tone === "light" ? light : dark;
    const sect: SectionTokens = t.sections[tone];
    const seen = new Set<string>();
    for (const [n, v] of Object.entries(block)) {
      const path = sectionPath(n);
      if (!path) continue; // shadcn variables other than --destructive
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
