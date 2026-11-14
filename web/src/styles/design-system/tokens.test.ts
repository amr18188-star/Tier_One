/**
 * tokens.ts must equal the CSS (primitives.css + themes/*.css), and the pairs antd and the app rely on must pass AA.
 * The CSS is parsed as text: var(--color-*) is resolved from primitives.css, color-mix(in oklab|srgb, X n%, transparent)
 * becomes rgba(X, n/100), color-mix(in srgb, X n%, Y) becomes the mixed 8-bit hex, OKLCH primitives become 8-bit hex
 * (±1 per channel). ../shadcn.css (the one shadcn → token mapping) is checked against every theme at the end.
 */
import { describe, expect, it } from "vitest";
import { antdTheme } from "./antd-theme";
import { CORNERS } from "./config";
import { cornerLabels, cornerStyles, corners, isCornerStyle, pillButtons, themeLabels, themeNames, themes, type CornerStyle, type SectionTokens, type ThemeName } from "./tokens";

// Read through Node: vitest turns every CSS import (?raw too) into an empty string. The project has no @types/node.
const { readFileSync, readdirSync } = (await import(/* @vite-ignore */ `node:${"fs"}`)) as {
  readFileSync(path: URL, encoding: "utf8"): string;
  readdirSync(path: URL, options: { recursive: true }): string[];
};
const read = (rel: string) => readFileSync(new URL(rel, import.meta.url), "utf8");
const primitivesCss = read("./primitives.css");
/** Every registered theme has its file themes/<name>.css, imported by index.css. */
const css = Object.fromEntries(themeNames.map((n) => [n, read(`./themes/${n}.css`)])) as Record<ThemeName, string>;

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
const kebab = (s: string) => s.replace(/[A-Z]/g, (c) => "-" + c.toLowerCase());

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
    expect(theme["--ds-color-scheme"], "--ds-color-scheme = scheme").toBe(t.scheme);
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
      ...(["accent", "value", "track", "ringTrack", "gridSoft", "gridFaint", "off", "offKey", "targetLine"] as const).map(
        (k): [string, string] => [`--ds-chart-${kebab(k)}`, t.chart[k]],
      ),
      ["--ds-shadow-sm", t.shadow.sm],
      ["--ds-shadow-md", t.shadow.md],
      ["--ds-focus", t.focus],
      ["--ds-selection-bg", t.selection.bg],
      ["--ds-selection-fg", t.selection.fg],
      ["--ds-shade", t.shade],
      ["--ds-accent-on-dark", t.accentOnDark],
      ["--ds-ink-fill", t.inkFill],
    ];
    for (const [n, ts] of pairs) expect(same(resolve(theme[n]), ts), `${n}: ${resolve(theme[n])} vs ${ts}`).toBe(true);
  });

  it.each(["sidebar", "band", "gradient", "hatch", "status", "shadow", "onDark", "auth", "score", "tier", "heat", "neutral", "unit", "tag", "app", "pillar"] as const)("%s tokens, both ways", (group) => {
    const prefix = `--ds-${kebab(group)}-`;
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

// ---------- every theme defines every token (a new palette may not leave one out; a missing token falls back to nothing) ----------
describe("every theme defines the --ds-* tokens the other themes define", () => {
  const names = Object.keys(themes) as ThemeName[];
  const scopes = (name: ThemeName) => {
    const b = blocks(css[name]);
    return {
      theme: Object.keys(b.get(`[data-theme="${name}"]`) ?? {}),
      light: Object.keys(b.get(`[data-theme="${name}"], [data-theme="${name}"] [data-section="light"]`) ?? {}),
      dark: Object.keys(b.get(`[data-theme="${name}"] [data-section="dark"], [data-theme="${name}"][data-section="dark"]`) ?? {}),
    };
  };
  const all = Object.fromEntries(names.map((n) => [n, scopes(n)])) as Record<ThemeName, ReturnType<typeof scopes>>;
  it.each(["theme", "light", "dark"] as const)("%s scope: same names in every theme file", (scope) => {
    const union = [...new Set(names.flatMap((n) => all[n][scope]))].sort();
    expect(union.length).toBeGreaterThan(0);
    for (const n of names) expect(union.filter((x) => !all[n][scope].includes(x)), `missing in themes/${n}.css`).toEqual([]);
  });
  it("light and dark sections declare the same names", () => {
    for (const n of names) expect([...all[n].dark].sort(), n).toEqual([...all[n].light].sort());
  });
  it("tokens.ts: every theme has the same groups and keys", () => {
    const shape = (o: unknown, at = ""): string[] =>
      o && typeof o === "object" ? Object.entries(o as Record<string, unknown>).flatMap(([k, v]) => (typeof v === "object" ? shape(v, `${at}${k}.`) : [`${at}${k}`])) : [];
    const ref = shape(themes[names[0]]).sort();
    for (const n of names.slice(1)) expect(shape(themes[n]).sort(), n).toEqual(ref);
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
    ["white text on the navy fill (legacy --navy fills: table heads, active tabs and filters)", t.onDark.fg, t.inkFill],
    ["table header text on the navy fill", d.heading, t.inkFill],
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
    ["hero / band: paragraph (--ds-on-dark-body)", t.onDark.body, band],
    ["hero / band: secondary text (--ds-on-dark-muted)", t.onDark.muted, band],
    ["active navy tab: caption (--ds-on-dark-soft)", t.onDark.soft, [t.inkFill, d.bg]],
    ["sign-in panel: body (--ds-auth-body)", t.auth.body, auth],
    ["sign-in panel: subtitle (--ds-auth-muted)", t.auth.muted, auth],
    ["sign-in panel: copyright (--ds-auth-faint)", t.auth.faint, auth],
    ["active navy tab: icon (--ds-accent-on-dark on --ds-ink-fill)", t.accentOnDark, [t.inkFill, d.bg]],
    ["sign-in panel: heading", d.heading, auth],
    ["sidebar: link", sb.fg, side],
    ["sidebar: group title", sb.muted, side],
    ["sidebar: active link", sb.fgActive, side],
    ["avatar: initials (--ds-on-dark-fg)", t.onDark.fg, stopsOf(t.gradient.avatar)],
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

// ---------- sidebar navigation (shared/ui.css + app/nav.css): the expanded sidebar, the rail and the mobile drawer use the same tokens ----------
describe.each(Object.keys(themes) as ThemeName[])("sidebar navigation in %s: readable links, a distinct active link, a visible hover", (name) => {
  const sb = themes[name].sidebar;
  /** --ds-gradient-sidebar: top stop, bottom stop, and the glow over the top stop. */
  const side = [sb.bgTop, sb.bg, over(sb.glow, sb.bgTop)];
  const fills = [...side, sb.hoverBg, sb.activeBg];
  const worst = (fg: string, bgs: string[]) => Math.min(...bgs.map((bg) => contrast(fg, bg)));
  it("link text ≥ 7:1 on every stop, and quieter than the active / hovered text", () => {
    expect(worst(sb.fg, side)).toBeGreaterThanOrEqual(7);
    expect(contrast(sb.fgActive, sb.fg), "active text vs link text").toBeGreaterThanOrEqual(1.3);
  });
  it("active link text ≥ 7:1 on the active fill; hovered link and group header ≥ 7:1 on the hover fill", () => {
    expect(contrast(sb.fgActive, sb.activeBg)).toBeGreaterThanOrEqual(7);
    expect(contrast(sb.fgActive, sb.hoverBg)).toBeGreaterThanOrEqual(7);
  });
  it("group titles and the brand subtitle ≥ 4.5:1 on every stop, and quieter than the links", () => {
    expect(worst(sb.muted, side)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(sb.fg, sb.muted), "link text vs group title").toBeGreaterThanOrEqual(1.2);
  });
  it("fills: hover shows on every stop (≥ 1.2:1); active stands apart from the sidebar (≥ 1.6:1) and from hover (≥ 1.3:1)", () => {
    expect(worst(sb.hoverBg, side), "hover vs sidebar").toBeGreaterThanOrEqual(1.2);
    expect(worst(sb.activeBg, side), "active vs sidebar").toBeGreaterThanOrEqual(1.6);
    expect(contrast(sb.activeBg, sb.hoverBg), "active vs hover").toBeGreaterThanOrEqual(1.3);
  });
  it("active bar ≥ 3:1 against its neighbours: every stop, the active fill, the hover fill (rail group button)", () => {
    expect(worst(sb.indicator, fills)).toBeGreaterThanOrEqual(3);
  });
  it("badge text ≥ 4.5:1; badge and rail dot ≥ 3:1 on the sidebar, hover and active fills", () => {
    expect(contrast(sb.badgeFg, sb.badgeBg)).toBeGreaterThanOrEqual(4.5);
    expect(worst(sb.badgeBg, fills)).toBeGreaterThanOrEqual(3);
  });
  it("focus ring ≥ 3:1 on the sidebar, hover and active fills", () => {
    expect(worst(sb.focus, fills)).toBeGreaterThanOrEqual(3);
  });
  it("scrollbar thumb (scrollbar.css: 76% bg + 24% fg) ≥ 1.5:1 on the bg", () => {
    const thumb = hex(rgbOf(sb.bg).map((x, i) => Math.round(x * 0.76 + rgbOf(sb.fg)[i] * 0.24)));
    expect(contrast(thumb, sb.bg)).toBeGreaterThanOrEqual(1.5);
  });
});

// ---------- palettes: scheme, the navy fill, pillar colours, status colours ----------
/** OKLab (Björn Ottosson) of an 8-bit colour; Euclidean distance there ≈ perceived difference (0.02 ≈ just noticeable). */
function oklab(c: string): number[] {
  const [r, g, b] = rgbOf(c).map((v) => v / 255).map((v) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
  const lms = [
    0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b,
    0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b,
    0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b,
  ].map((x) => Math.cbrt(x));
  return [
    0.2104542553 * lms[0] + 0.793617785 * lms[1] - 0.0040720468 * lms[2],
    1.9779984951 * lms[0] - 2.428592205 * lms[1] + 0.4505937099 * lms[2],
    0.0259040371 * lms[0] + 0.7827717662 * lms[1] - 0.808675766 * lms[2],
  ];
}
const deltaE = (a: string, b: string) => Math.hypot(...oklab(a).map((x, i) => x - oklab(b)[i]));
const pct = (v: string) => Number.parseFloat(v) / 100;
const mixHex = (a: string, w: number, b: string) => hex(rgbOf(a).map((x, i) => Math.round(x * w + rgbOf(b)[i] * (1 - w))));

// The pillar data colours, read from shared/pillars.tsx (PALETTE + NEUTRAL), and tint() itself.
const pillarsTsx = read("../../shared/pillars.tsx");
const pillarData = [...pillarsTsx.matchAll(/\{ c: "(#[0-9a-f]{6})", t: "(#[0-9a-f]{6})" \}/gi)].map((m) => ({ c: m[1].toLowerCase(), t: m[2].toLowerCase() }));
/** --pc / --pt as tint() builds them under a theme (on a light-scope element: --ds-surface = sections.light.surface). */
function pillarOn(name: ThemeName, d: { c: string; t: string }) {
  const { pillar, sections } = themes[name];
  const pc = mixHex(d.c, pct(pillar.keep), pillar.lift);
  const pt = mixHex(d.t, pct(pillar.tintKeep), mixHex(pc, pct(pillar.tintMix), sections.light.surface));
  return { pc, pt };
}

describe("pillar colours meet the palette through --ds-pillar-* (shared/pillars.tsx tint())", () => {
  it("reads the six pillar colours and the neutral one, and tint() mixes them with the tokens only", () => {
    expect(pillarData.length).toBe(7);
    expect(pillarsTsx).toMatch(/"--pc": `color-mix\(in srgb, \$\{p\.c\} var\(--ds-pillar-keep, 100%\), var\(--ds-pillar-lift, transparent\)\)`/);
    expect(pillarsTsx).toMatch(/"--pt": `color-mix\(in srgb, \$\{p\.t\} var\(--ds-pillar-tint-keep, 100%\), color-mix\(in srgb, var\(--pc\) var\(--ds-pillar-tint-mix, 0%\), var\(--ds-surface, transparent\)\)\)`/);
  });
  it.each(themeNames.filter((n) => themes[n].scheme === "light"))("%s (light): the data colours exactly (100%% / 100%%)", (name) => {
    for (const d of pillarData) expect(pillarOn(name, d)).toEqual({ pc: d.c, pt: d.t });
  });
  it.each(themeNames.filter((n) => themes[n].scheme === "dark"))("%s (dark): readable on the dark card, white text on the colour, no light tint", (name) => {
    const l = themes[name].sections.light;
    for (const d of pillarData) {
      const { pc, pt } = pillarOn(name, d);
      expect(contrast(pc, l.surface), `${d.c} → ${pc} on the card`).toBeGreaterThanOrEqual(3); // marks, codes, numbers
      expect(contrast("#ffffff", pc), `white on ${pc}`).toBeGreaterThanOrEqual(3); // pillar heads, step numbers, bars
      expect(contrast(pc, pt), `${pc} on its tint ${pt}`).toBeGreaterThanOrEqual(3); // code chips
      expect(contrast(l.fg, pt), `text on the tint ${pt}`).toBeGreaterThanOrEqual(4.5); // rows and cards filled with --pt
      expect(luminance(pt), `tint ${pt} stays dark`).toBeLessThan(0.05);
    }
  });
  // --pf: the pillar colour as a fill under white text (pill bands, pillar heads, step numbers, board badges, timeline bars).
  it.each(themeNames)("%s: white text on pillar fills (--pf) and the band chips (.lcp-head .pill-code)", (name) => {
    const t = themes[name];
    const bandStops = [t.band.from, t.band.via, t.band.to, t.sections.dark.bg];
    for (const d of pillarData) {
      const { pc, pt } = pillarOn(name, d);
      const pf = mixHex(pc, 1 - pct(t.pillar.fillMix), t.shade);
      if (t.scheme === "light") expect(pf, "a light palette fills with the data colour exactly").toBe(pc);
      else {
        // pillar heads: 80% white labels on a 13% white stats box, under the 7% white corner circle; 90% white names and numbers
        const stats = over("rgba(255, 255, 255, 0.13)", over("rgba(255, 255, 255, 0.07)", pf));
        expect(contrast(over("rgba(255, 255, 255, 0.8)", stats), stats), `80% white on the stats box over ${pf}`).toBeGreaterThanOrEqual(4.5);
        expect(contrast(over("rgba(255, 255, 255, 0.78)", pf), pf), `78% white on ${pf}`).toBeGreaterThanOrEqual(4.5);
        expect(contrast("#ffffff", pf), `white on ${pf}`).toBeGreaterThanOrEqual(4.5);
      }
      // band chip: the tint where the palette keeps it, else a light one; text = the colour deepened with the navy ink
      const chip = mixHex(pt, pct(t.pillar.tintKeep), mixHex(pc, 0.1, t.onDark.fg));
      const ink = mixHex(pc, pct(t.pillar.keep) - 0.15, t.inkFill);
      expect(contrast(ink, chip), `band chip text ${ink} on ${chip}`).toBeGreaterThanOrEqual(4.5);
      for (const b of bandStops) expect(contrast(chip, b), `band chip ${chip} on the band ${b}`).toBeGreaterThanOrEqual(3);
      if (t.scheme === "light") expect(chip, "a light palette keeps the chip fill").toBe(pt);
    }
  });
});

describe.each(themeNames)("palette rules in %s", (name) => {
  const t = themes[name];
  const { light: l, dark: d } = t.sections;

  it("navy fill: = the light heading in a light palette, a separate dark fill in a dark one", () => {
    if (t.scheme === "light") expect(t.inkFill).toBe(l.heading);
    else {
      expect(luminance(t.inkFill)).toBeLessThan(0.05);
      expect(t.inkFill, "antd table header (dark-section bg) = the CSS table heads").toBe(d.bg);
      expect(contrast(t.inkFill, l.surface), "the navy fill stands out from the card").toBeGreaterThanOrEqual(1.25);
    }
  });

  it("the four achievement states (محقق / قريب / متأخر / ناقص) are distinct from each other and from the pillar colours", () => {
    const states = { ok: t.status.ok, near: t.status.near, late: t.status.late, gap: t.status.gap };
    const keys = Object.keys(states) as (keyof typeof states)[];
    for (const [i, a] of keys.entries()) for (const b of keys.slice(i + 1)) expect(deltaE(states[a], states[b]), `${a} vs ${b}`).toBeGreaterThanOrEqual(0.09);
    for (const k of keys) for (const p of pillarData) expect(deltaE(states[k], pillarOn(name, p).pc), `${k} vs pillar ${p.c}`).toBeGreaterThanOrEqual(0.07);
    expect(new Set(Object.values(states)).size).toBe(4);
  });

  it("antd: darkAlgorithm for a dark palette only; success / warning from the status inks when statusToAntd", () => {
    const a = antdTheme(name, {}, { corners: "square" });
    expect(a.algorithm !== undefined, "algorithm").toBe(t.scheme === "dark");
    if (t.statusToAntd) {
      expect(a.token).toMatchObject({
        colorSuccess: t.status.okInk, colorSuccessBg: t.status.okSoft, colorSuccessBorder: t.status.okBorder,
        colorWarning: t.status.nearInk, colorWarningBg: t.status.nearSoft, colorWarningBorder: t.status.nearBorder,
      });
      for (const [fg, bg] of [[t.status.okInk, t.status.okSoft], [t.status.nearInk, t.status.nearSoft], [t.status.okInk, l.surface], [t.status.nearInk, l.surface]])
        expect(contrast(fg, bg), `${fg} on ${bg}: antd success / warning Tag and text`).toBeGreaterThanOrEqual(4.5);
    }
    else {
      // antd's preset fills, held by the green / gold Tag tokens; the text is the status ink
      expect(a.token).toMatchObject({
        colorSuccess: t.status.okInk, colorSuccessText: t.status.okInk, colorSuccessBg: t.tag.positiveBg, colorSuccessBorder: t.tag.positiveBorder,
        colorWarning: t.status.nearInk, colorWarningText: t.status.nearInk, colorWarningBg: t.tag.pendingBg, colorWarningBorder: t.tag.pendingBorder,
      });
      for (const [fg, bg] of [[t.status.okInk, t.tag.positiveBg], [t.status.nearInk, t.tag.pendingBg], [t.status.okInk, l.surface], [t.status.nearInk, l.surface]])
        expect(contrast(fg, bg), `${fg} on ${bg}: antd success / warning Tag and text`).toBeGreaterThanOrEqual(4.5);
    }
    if (t.scheme === "dark") {
      expect(a.token?.colorTextLightSolid, "text on solid primary / danger fills").toBe(l.btnPrimary.fg);
      expect(contrast(l.btnPrimary.fg, l.destructive), "danger button text").toBeGreaterThanOrEqual(4.5);
      expect(a.components?.Tooltip?.colorTextLightSolid, "tooltip text").toBe(d.heading);
      expect(contrast(d.heading, d.bg), "tooltip on the spotlight").toBeGreaterThanOrEqual(4.5);
    }
  });
});

describe.each(themeNames)("%s: AA text on every Tag fill", (name) => {
  const t = themes[name];
  it.each(["pending", "positive", "negative", "caution", "info", "special", "highlight", "category"] as const)("%s Tag ≥ 4.5:1", (r) => {
    expect(contrast(t.tag[`${r}Fg`], t.tag[`${r}Bg`])).toBeGreaterThanOrEqual(4.5);
  });
});

describe("antd success / warning and hint text are AA in every palette", () => {
  it.each(themeNames)("%s: success / warning text is the status ink (antd's defaults are 1.8–2.3:1 as text)", (name) => {
    const t = themes[name];
    expect(antdTheme(name).token).toMatchObject({ colorSuccess: t.status.okInk, colorSuccessText: t.status.okInk, colorWarning: t.status.nearInk, colorWarningText: t.status.nearInk });
  });
  it.each(["navy-gold", "app-soft"] as const)("%s keeps antd's success / warning fills (= its green / gold Tag fills)", (name) => {
    expect(themes[name].statusToAntd).toBe(false);
    expect(themes[name].tag).toMatchObject({ positiveBg: "#f6ffed", positiveBorder: "#b7eb8f", pendingBg: "#fffbe6", pendingBorder: "#ffe58f" });
  });
  it.each(themeNames.filter((n) => themes[n].scheme === "light"))("%s (light): hints in the label colour, light algorithm", (name) => {
    const t = themes[name];
    const l = t.sections.light;
    const a = antdTheme(name);
    expect(a.algorithm).toBeUndefined();
    expect(a.token?.colorTextLightSolid).toBeUndefined();
    expect(a.token).toMatchObject({ colorTextDescription: l.label, colorTextTertiary: l.label, colorTextPlaceholder: l.body });
    expect(a.token?.colorTextQuaternary, "disabled text stays antd's").toBeUndefined();
    // form extras, "optional", Typography secondary: on cards, the page, drawer sections and muted boxes
    for (const bg of [l.surface, l.bgCool, l.track, t.app.mutedBg]) expect(contrast(l.label, bg), `hint ${l.label} on ${bg}`).toBeGreaterThanOrEqual(4.5);
    // placeholders sit in fields (the card colour)
    expect(contrast(l.body, l.surface), `placeholder ${l.body} on ${l.surface}`).toBeGreaterThanOrEqual(4.5);
  });
});

describe.each(themeNames)("%s: chart labels", (name) => {
  const t = themes[name];
  const backs = [t.chart.bg, t.chart.off, ...stopsOf(t.gradient.surfaceSubtle)];
  it("axis and year labels (--ds-neutral-axis) ≥ 4.5:1 on the chart panel, its off-plan area and the point fill", () => {
    for (const b of backs) expect(contrast(t.neutral.axis, b), `axis ${t.neutral.axis} on ${b}`).toBeGreaterThanOrEqual(4.5);
  });
  it("the actual-value label: achieved / near in the status inks, behind in its mark, ≥ 4.5:1", () => {
    for (const b of backs) for (const c of [t.status.okInk, t.status.nearInk, t.status.late]) expect(contrast(c, b), `${c} on ${b}`).toBeGreaterThanOrEqual(4.5);
  });
});

describe.each(themeNames.filter((n) => themes[n].scheme === "dark"))("dark palette %s: dark everywhere, data readable", (name) => {
  const t = themes[name];
  const { light: l, dark: d } = t.sections;
  it("page, cards, fields, popups, tracks, muted boxes and table fills are dark; text is light", () => {
    const surfaces = [l.bg, l.bgCool, l.bgWarm, l.surface, l.surfaceHover, l.surfaceHoverSoft, l.surfaceWarm, l.callout, l.track, l.tile, t.app.mutedBg, t.app.faintBg,
      t.app.rowHover, t.app.rowSelected, t.app.tableGroup, t.app.topbar, t.app.noteBg, t.app.partnerBg, t.app.printDesk, t.heat.none, t.chart.bg, t.chart.track, t.chart.off,
      t.status.okSoft, t.status.nearSoft, t.status.lateSoft, t.status.okTint, t.status.nearTint, t.status.lateTint, t.status.flagBg, t.status.zeroSoft, t.status.exemptSoft,
      t.score.fullSoft, t.score.highSoft, t.score.midSoft, t.score.lowSoft, t.heat["1"], t.heat["2"], t.heat["3"], t.heat["4"], t.unit.universityBg, t.unit.sectorBg, t.unit.collegeBg,
      ...(["pending", "positive", "negative", "caution", "info", "special", "highlight", "category"] as const).map((r) => t.tag[`${r}Bg`]), ...stopsOf(t.hatch.light),
      ...stopsOf(t.gradient.callout), ...stopsOf(t.gradient.surfaceSubtle), d.bg, d.surface, t.inkFill, t.sidebar.bg];
    for (const s of surfaces) expect(luminance(s), s).toBeLessThan(0.08);
    for (const fg of [l.fg, l.heading, l.body, l.label, l.bodyStrong, t.chart.value, t.app.printInk, t.app.codeFg]) expect(luminance(fg), fg).toBeGreaterThan(0.3);
  });
  it("hairlines and field borders stay visible on the card (≥ 1.4:1)", () => {
    expect(contrast(l.border, l.surface)).toBeGreaterThanOrEqual(1.4);
  });
  it("status, score, tier, chart, unit and neutral marks ≥ 3:1 on the card", () => {
    const marks = [t.status.ok, t.status.near, t.status.late, t.status.gap, t.status.plan, t.status.progress, t.status.info, t.status.share, t.status.caution, t.status.review,
      t.status.nearBar, t.status.lateBar, t.score.fullMark, t.score.highMark, t.score.highBar, t.score.midMark, t.score.lowMark, ...(["1", "2", "3", "4", "5"] as const).map((k) => t.tier[k]), ...t.chart.series,
      t.chart.actual, t.chart.target, t.chart.up, t.chart.down, t.chart.accent, t.chart.targetLine, t.chart.college, t.unit.university, t.unit.sector, t.unit.administration,
      t.unit.center, t.unit.college, t.unit.institute, t.unit.department, t.neutral.mark, t.neutral.base, t.neutral.actual];
    for (const c of marks) expect(contrast(c, l.surface), c).toBeGreaterThanOrEqual(3);
  });
  it("text on its soft fill ≥ 4.5:1 (chips, heat cells, unit chips, Tags)", () => {
    const pairs: [string, string][] = [
      [t.status.okInk, t.status.okSoft], [t.status.nearInk, t.status.nearSoft], [t.status.lateInk, t.status.lateSoft], [t.status.zeroInk, t.status.zeroSoft],
      [t.status.exemptInk, t.status.exemptSoft], [t.score.fullInk, t.score.fullSoft], [t.score.highInk, t.score.highSoft], [t.score.midInk, t.score.midSoft],
      [t.score.lowInk, t.score.lowSoft], [t.status.lateInk, t.heat["1"]], [t.status.nearInk, t.heat["2"]], [t.status.okInk, t.heat["3"]], [t.heat["4Ink"], t.heat["4"]],
      [t.unit.university, t.unit.universityBg], [t.unit.sector, t.unit.sectorBg], [t.unit.college, t.unit.collegeBg], ["#ffffff", t.status.nearMark],
      ...(["1", "2", "3", "4", "5"] as const).map((k): [string, string] => [t.tier.ink, t.tier[k]]),
      [l.link, t.app.noteBg], [l.label, t.app.mutedBg], [t.app.codeFg, t.app.codeBg], [t.app.printInk, l.surface], [l.eyebrowAa, l.bgWarm],
      ...(["pending", "positive", "negative", "caution", "info", "special", "highlight", "category"] as const).map((r): [string, string] => [t.tag[`${r}Fg`], t.tag[`${r}Bg`]]),
    ];
    for (const [fg, bg] of pairs) expect(contrast(fg, bg), `${fg} on ${bg}`).toBeGreaterThanOrEqual(4.5);
  });
});

describe("legacy CSS: no white text on a --navy fill (it is the heading colour, light in a dark palette; use --ds-ink-fill)", () => {
  const root = new URL("../../", import.meta.url);
  const files = readdirSync(root, { recursive: true })
    .map((f) => f.replace(/\\/g, "/"))
    .filter((f) => f.endsWith(".css") && !f.startsWith("components/ui") && !f.startsWith("features/landing") && !f.startsWith("styles/design-system"));
  it.each(files)("%s", (f) => {
    const text = readFileSync(new URL(f, root), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
    const bad = [...text.matchAll(/([^{}]+)\{([^{}]*)\}/g)]
      .filter((m) => /background(-color)?:\s*var\(--navy\)/.test(m[2]) && /color:\s*var\(--ds-on-dark-fg\)/.test(m[2]))
      .map((m) => m[1].trim());
    expect(bad).toEqual([]);
  });
});

// ---------- corners (corners.css ⇄ `corners` in tokens.ts, config.ts) ----------
describe("corners.css matches tokens.ts", () => {
  const b = blocks(read("./corners.css"));
  const styleBlocks: Record<CornerStyle, Record<string, string>> = {
    square: b.get("[data-corners=\"square\"]")!,
    rounded: b.get(":root, [data-corners=\"rounded\"]")!,
    smooth: b.get("[data-corners=\"smooth\"]")!,
  };
  const shared = b.get(":root, [data-corners]")!;
  const px = (v: string) => (v.endsWith("px") ? Number.parseFloat(v) : Number.NaN);

  it("finds a block per style and the shared one", () => {
    for (const style of cornerStyles) expect(styleBlocks[style], style).toBeDefined();
    expect(shared).toBeDefined();
  });

  it.each(cornerStyles)("%s steps, both ways", (style) => {
    const block = styleBlocks[style];
    const cssSteps = Object.keys(block).map((n) => n.replace("--ds-radius-", ""));
    expect(cssSteps.sort()).toEqual(Object.keys(corners[style]).sort());
    for (const [step, value] of Object.entries(corners[style])) expect(px(block[`--ds-radius-${step}`]), `--ds-radius-${step}`).toBe(value);
  });

  it("square is 0 everywhere; rounded and smooth grow step by step", () => {
    expect(Object.values(corners.square).every((v) => v === 0)).toBe(true);
    for (const r of [corners.rounded, corners.smooth]) {
      expect([r.xs, r.sm, r.md, r.lg, r.xl, r.pill]).toEqual([...[r.xs, r.sm, r.md, r.lg, r.xl, r.pill]].sort((x, y) => x - y));
    }
  });

  it("square and rounded keep their scale; smooth is 8 / 12 / 16 / 20 / 24, pill 999", () => {
    expect(corners.square).toEqual({ xs: 0, sm: 0, md: 0, lg: 0, xl: 0, pill: 0 });
    expect(corners.rounded).toEqual({ xs: 4, sm: 6, md: 8, lg: 12, xl: 16, pill: 999 });
    expect(corners.smooth).toEqual({ xs: 8, sm: 12, md: 16, lg: 20, xl: 24, pill: 999 });
    expect(pillButtons).toEqual({ square: false, rounded: false, smooth: true });
  });

  it("every theme and corner style has a name in both languages (the Appearance panel lists them)", () => {
    for (const name of Object.keys(themes) as ThemeName[]) expect(themeLabels[name], name).toMatchObject({ ar: expect.any(String), en: expect.any(String) });
    expect(Object.keys(cornerLabels)).toEqual(cornerStyles);
  });

  it("scrollbar thumb: the xs step, a full pill when smooth", () => {
    const sb = blocks(read("./scrollbar.css"));
    expect(sb.get(":root, [data-corners]")?.["--scrollbar-radius"]).toBe("var(--ds-radius-xs)");
    expect(sb.get("[data-corners=\"smooth\"]")?.["--scrollbar-radius"]).toBe("var(--ds-radius-pill)");
  });

  it("aliases and real circles", () => {
    expect(shared).toEqual({ "--ds-radius-round": "50%", "--ds-radius": "var(--ds-radius-md)", "--radius": "var(--ds-radius-lg)" });
  });

  it("CORNERS is a valid style, rounded by default", () => {
    expect(isCornerStyle(CORNERS)).toBe(true);
    expect(CORNERS).toBe("rounded");
    // Explicit choices must override :root's rounded fallback on <html>.
    const selectors = [...b.keys()];
    expect(selectors.indexOf(":root, [data-corners=\"rounded\"]")).toBeLessThan(selectors.indexOf("[data-corners=\"square\"]"));
    expect(selectors.indexOf(":root, [data-corners=\"rounded\"]")).toBeLessThan(selectors.indexOf("[data-corners=\"smooth\"]"));
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

  it("smooth: the 8–24 scale, pill buttons (every size) and tags; square and rounded keep the global button and tag radius", () => {
    const s = corners.smooth;
    const smooth = antdTheme("navy-gold", {}, { corners: "smooth" });
    expect(smooth.token).toMatchObject({ borderRadius: s.md, borderRadiusLG: s.lg, borderRadiusSM: s.sm, borderRadiusXS: s.xs, borderRadiusOuter: s.xs });
    expect(smooth.components?.Button).toMatchObject({ borderRadius: s.pill, borderRadiusLG: s.pill, borderRadiusSM: s.pill });
    expect(smooth.components?.Tag?.borderRadiusSM).toBe(s.pill);
    expect(smooth.components?.Table?.headerBorderRadius).toBe(s.lg);
    for (const style of ["square", "rounded"] as const) {
      const t = antdTheme("app-soft", {}, { corners: style });
      // Button LG / SM repeat the global lg / sm, so a nested `algorithm: true` (antdDarkBlockTheme) cannot re-derive them.
      const btn = Object.fromEntries(Object.entries(t.components?.Button ?? {}).filter(([k]) => k.startsWith("borderRadius")));
      expect(btn, style).toEqual({ borderRadiusLG: t.token?.borderRadiusLG, borderRadiusSM: t.token?.borderRadiusSM });
      expect(Object.keys(t.components?.Tag ?? {}).filter((k) => k.startsWith("borderRadius")), style).toEqual([]);
    }
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
