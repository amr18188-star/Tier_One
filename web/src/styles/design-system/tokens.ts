/**
 * Design-system token values for TypeScript. antd-theme.ts builds the antd ConfigProvider theme from them
 * (antd derives hover and active shades in JS, so it needs real colours, not var()).
 * The CSS files are the source of truth:
 *   primitives.css → `palette`, themes/<name>.css → `themes[name]`, corners.css → `corners`.
 * Values are resolved to plain colour strings:
 * - ash-* are Tailwind v4 slate in OKLCH in the CSS; here they are their 8-bit hex.
 * - CSS color-mix(in oklab|srgb, X n%, transparent) is written as rgba(X, n/100) (the same colour).
 * - Gradient tokens (`gradient`, `hatch`) are the CSS value with every var() resolved, layers joined by ", ".
 * Keep both in sync when a value changes: tokens.test.ts parses the CSS and fails on any difference.
 */
export const palette = {
  navy: { 950: "#06152a", 900: "#071a33", 800: "#0c2545", 700: "#12345b", 600: "#18304d", 500: "#29415f" },
  steel: { 400: "#7890ac" },
  mist: { 100: "#dce5f0", 300: "#aab8ca" },
  gold: { 200: "#e5c47e", 400: "#d6b46a", 500: "#b9944d", 600: "#a7833d", 700: "#82652b" },
  paper: "#ffffff",
  cloud: "#f8f9fb",
  sand: { 50: "#f8f4ea", 100: "#f8f3e9", 200: "#f4ebd8" },
  ivory: { DEFAULT: "#f8f6f1", 50: "#fcfbf8" },
  ash: { 100: "#f1f5f9", 200: "#e2e8f0", 300: "#cad5e2", 400: "#90a1b9", 500: "#62748e", 600: "#45556c" },
  haze: { 200: "#e2e8f0", 300: "#cbd5e1", 400: "#94a3b8" },
  signal: { green: "#22c55e", red: "#ef4444" },
  soft: {
    navy: { 950: "#0d2440", 900: "#123055", 800: "#19355a", 700: "#234a7e", 500: "#3567b0", 100: "#e8eef7" },
    gold: { 700: "#8a6d00", 600: "#ad8700", 500: "#c9a227", 300: "#e6c25a", 100: "#f0dfa8", 50: "#fffdf4" },
    neutral: {
      900: "#1b2733", 700: "#475569", 600: "#5c6b7a", 500: "#7d8ea6", 400: "#9aa8b6", 300: "#a7b3c0",
      200: "#cddbee", 150: "#e2e8f0", 100: "#e3e9f1", 75: "#edf2f7", 50: "#f4f7fb", 25: "#f8fafc",
    },
    success: { 700: "#1c7c3d", 400: "#64c783" },
    danger: { 700: "#b3261e", 600: "#d0453e", 400: "#ef6a63" },
    info: { 700: "#1e40af" },
  },
} as const;

export type ThemeName = "navy-gold" | "app-soft";
export type SectionTone = "light" | "dark";

export interface ButtonTokens {
  bg: string;
  fg: string;
  border: string;
  hoverBg: string;
  hoverFg: string;
  hoverBorder: string;
}

/** One section scope: --ds-* tokens that change with data-section. */
export interface SectionTokens {
  bg: string;
  bgCool: string;
  bgWarm: string;
  fg: string;
  surface: string;
  surfaceHover: string;
  surfaceHoverSoft: string;
  surfaceWarm: string;
  callout: string;
  border: string;
  lineSoft: string;
  track: string;
  accent: string;
  accentStrong: string;
  accentBorder: string;
  heading: string;
  highlight: string;
  highlightDisplay: string;
  body: string;
  bodyStrong: string;
  label: string;
  faint: string;
  index: string;
  eyebrow: string;
  eyebrowAa: string;
  icon: string;
  tile: string;
  tileActive: string;
  tileActiveFg: string;
  tileSolid: string;
  tileSolidFg: string;
  live: string;
  /** --ds-focus-ring: keyboard focus outline. */
  focusRing: string;
  /** --ds-link / --ds-link-hover: text links and codes (antd colorLink). */
  link: string;
  linkHover: string;
  /** --ds-destructive: the AA error colour (antd colorError, shadcn --destructive). */
  destructive: string;
  btnPrimary: ButtonTokens;
  btnGhost: ButtonTokens;
}

/** --ds-sidebar-*: the dark chrome of the signed-in shell (theme level, not per section). */
export interface SidebarTokens {
  bg: string;
  /** Top stop of --ds-gradient-sidebar (the bottom stop is `bg`). */
  bgTop: string;
  /** Top-corner light of --ds-gradient-sidebar. */
  glow: string;
  fg: string;
  fgActive: string;
  muted: string;
  hoverBg: string;
  activeBg: string;
  indicator: string;
  border: string;
  focus: string;
  badgeBg: string;
  badgeFg: string;
}

/** --ds-band-*: the navy page band of antd pages (shared/theme.css --band), theme level. */
export interface BandTokens {
  from: string;
  via: string;
  to: string;
  glow: string;
  glowAccent: string;
}

/**
 * --ds-gradient-*: full background values (theme level). Every gradient of the app CSS reads one of these, so one token
 * re-colours all its rules. Built by `gradients()` below with the same recipe as themes/*.css.
 */
export interface GradientTokens {
  /** Page band (.lcp-head via --band), Home hero (.lcp-hero), plan hero (.sp-hero): band colours. */
  hero: string;
  /** Sidebar and mobile drawer (.lcp-side, .lcp-drawer), verify-page header: sidebar colours. */
  sidebar: string;
  /** Sign-in brand panel (.auth-side): band colours ending in `shade`. */
  auth: string;
  /** Initials circle (.lcp-avatar). */
  avatar: string;
  /** Progress fill (.lcp-prog-bar i). */
  progress: string;
  /** Next-step note (.lcp-next). */
  callout: string;
  /** Light panel behind a chart (.sp-chart). */
  surfaceSubtle: string;
}

/** --ds-hatch-*: striped fills, repeating-linear-gradient 135° with 6px stripes. */
export interface HatchTokens {
  /** Not-started cells of the plan timeline (.sp-road-cell.off). */
  light: string;
  /** Elapsed-time bar of a cycle (.cy-bar.time i). */
  muted: string;
}

export interface ThemeTokens {
  fontFamily: string;
  chrome: string;
  gridHero: string;
  gridDark: string;
  chart: {
    /** --ds-chart-1 … 5: the categorical series (shadcn --chart-1 … 5). */
    series: readonly [string, string, string, string, string];
    actual: string;
    target: string;
    area: string;
    current: string;
    bg: string;
    grid: string;
    axis: string;
    up: string;
    down: string;
    /** --ds-chart-college: the "college's own target" series (DistributionPanel), = sections.light.btnPrimary.bg. */
    college: string;
  };
  /** --ds-status-review: the "under review" status segment (Home definition-status bar). */
  status: { review: string };
  shadow: { sm: string; md: string };
  focus: string;
  selection: { bg: string; fg: string };
  sidebar: SidebarTokens;
  band: BandTokens;
  /** --ds-shade: the dark ink that pillar colours (--pc) deepen toward in their gradients. */
  shade: string;
  /** --ds-accent-on-dark: accent text and marks on navy blocks without data-section="dark" (= sections.dark.eyebrow). */
  accentOnDark: string;
  gradient: GradientTokens;
  hatch: HatchTokens;
  sections: Record<SectionTone, SectionTokens>;
}

const p = palette;
const cairo = '"Cairo", "Geist Variable", "Segoe UI", sans-serif';
const shadow = { sm: "0 8px 24px rgba(25, 53, 90, 0.08)", md: "0 16px 40px rgba(25, 53, 90, 0.14)" } as const;
/** Dark-section --ds-destructive in both themes: color-mix(in srgb, soft-danger-400 55%, paper), resolved (AA on navy). */
const darkDestructive = "#f6ada9";

/** The gradient recipes of themes/*.css, filled with one theme's colours (each pair is from → to). */
function gradients(band: BandTokens, sidebar: SidebarTokens, shade: string, c: Record<"avatar" | "progress" | "callout" | "surfaceSubtle", readonly [string, string]>): GradientTokens {
  return {
    hero: `radial-gradient(60% 90% at 0% 0%, ${band.glow}, transparent 60%), radial-gradient(50% 80% at 100% 100%, ${band.glowAccent}, transparent 60%), linear-gradient(125deg, ${band.from} 0%, ${band.via} 55%, ${band.to} 100%)`,
    sidebar: `radial-gradient(120% 45% at 100% 0%, ${sidebar.glow}, transparent 60%), linear-gradient(180deg, ${sidebar.bgTop} 0%, ${sidebar.bg} 100%)`,
    auth: `radial-gradient(120% 80% at 85% 10%, ${band.glow}, transparent 55%), radial-gradient(90% 70% at 10% 100%, ${band.glowAccent}, transparent 60%), linear-gradient(160deg, ${band.via} 0%, ${band.from} 55%, ${shade} 100%)`,
    avatar: `linear-gradient(135deg, ${c.avatar[0]}, ${c.avatar[1]})`,
    progress: `linear-gradient(90deg, ${c.progress[0]}, ${c.progress[1]})`,
    callout: `linear-gradient(90deg, ${c.callout[0]}, ${c.callout[1]})`,
    surfaceSubtle: `linear-gradient(180deg, ${c.surfaceSubtle[0]}, ${c.surfaceSubtle[1]})`,
  };
}
const hatch = (a: string, b: string) => `repeating-linear-gradient(135deg, ${a} 0 6px, ${b} 6px 12px)`;

const ngSidebar: SidebarTokens = {
  bg: p.navy[950],
  bgTop: p.navy[900],
  glow: "rgba(214, 180, 106, 0.08)",
  fg: p.mist[300],
  fgActive: p.paper,
  muted: p.steel[400],
  hoverBg: p.navy[800],
  activeBg: p.navy[700],
  indicator: p.gold[400],
  border: p.navy[500],
  focus: p.gold[400],
  badgeBg: p.gold[400],
  badgeFg: p.navy[900],
};
const ngBand: BandTokens = { from: p.navy[900], via: p.navy[800], to: p.navy[700], glow: "rgba(214, 180, 106, 0.14)", glowAccent: "rgba(214, 180, 106, 0.18)" };
const softSidebar: SidebarTokens = {
  bg: p.soft.navy[950],
  bgTop: p.soft.navy[900],
  glow: "rgba(201, 162, 39, 0.08)",
  fg: p.soft.neutral[200],
  fgActive: p.paper,
  muted: p.soft.neutral[400],
  hoverBg: p.soft.navy[800],
  activeBg: p.soft.navy[700],
  indicator: p.soft.gold[500],
  border: "rgba(255, 255, 255, 0.14)",
  focus: p.soft.gold[300],
  badgeBg: p.soft.gold[500],
  badgeFg: p.soft.navy[950],
};
const softBand: BandTokens = { from: p.soft.navy[900], via: p.soft.navy[800], to: p.soft.navy[700], glow: "rgba(53, 103, 176, 0.38)", glowAccent: "rgba(201, 162, 39, 0.18)" };

export const themes = {
  "navy-gold": {
    fontFamily: cairo,
    chrome: p.navy[950],
    gridHero: p.navy[600],
    gridDark: p.steel[400],
    chart: {
      series: [p.navy[800], p.gold[400], p.steel[400], p.haze[300], p.gold[500]],
      actual: p.gold[500],
      target: p.steel[400],
      area: p.gold[400],
      current: p.gold[400],
      bg: p.paper,
      grid: p.haze[200],
      axis: p.haze[400],
      up: p.signal.green,
      down: p.signal.red,
      college: p.navy[900],
    },
    status: { review: p.gold[600] },
    shadow,
    focus: "rgba(201, 162, 39, 0.5)",
    selection: { bg: p.soft.gold[500], fg: p.soft.navy[950] },
    sidebar: ngSidebar,
    band: ngBand,
    shade: p.navy[950],
    accentOnDark: p.gold[400],
    gradient: gradients(ngBand, ngSidebar, p.navy[950], {
      avatar: [p.navy[700], p.navy[900]],
      progress: [p.navy[900], p.navy[700]],
      callout: [p.sand[50], p.ivory[50]],
      surfaceSubtle: [p.cloud, p.paper],
    }),
    hatch: { light: hatch(p.ash[100], p.paper), muted: hatch(p.steel[400], p.mist[300]) },
    sections: {
      light: {
        bg: p.paper,
        bgCool: p.cloud,
        bgWarm: p.sand[200],
        fg: p.navy[900],
        surface: p.paper,
        surfaceHover: p.ivory.DEFAULT,
        surfaceHoverSoft: p.ivory[50],
        surfaceWarm: p.sand[100],
        callout: p.ivory.DEFAULT,
        border: p.ash[200],
        lineSoft: p.navy[900],
        track: p.ash[100],
        accent: p.gold[400],
        accentStrong: p.gold[500],
        accentBorder: "rgba(214, 180, 106, 0.4)",
        heading: p.navy[900],
        highlight: p.gold[500],
        highlightDisplay: p.gold[500],
        body: p.ash[500],
        bodyStrong: p.ash[600],
        label: p.ash[600],
        faint: p.ash[400],
        index: p.ash[300],
        eyebrow: p.gold[600],
        eyebrowAa: p.gold[700],
        icon: p.gold[600],
        tile: p.sand[50],
        tileActive: p.gold[400],
        tileActiveFg: p.navy[900],
        tileSolid: p.navy[900],
        tileSolidFg: p.gold[400],
        live: p.signal.green,
        focusRing: p.navy[900],
        link: p.gold[700],
        linkHover: p.navy[900],
        destructive: p.soft.danger[700],
        btnPrimary: { bg: p.navy[900], fg: p.paper, border: p.navy[900], hoverBg: p.gold[400], hoverFg: p.navy[900], hoverBorder: p.gold[400] },
        btnGhost: { bg: "transparent", fg: p.navy[900], border: "rgba(7, 26, 51, 0.2)", hoverBg: p.gold[400], hoverFg: p.navy[900], hoverBorder: p.gold[400] },
      },
      dark: {
        bg: p.navy[900],
        bgCool: p.navy[900],
        bgWarm: p.navy[900],
        fg: p.paper,
        surface: p.navy[800],
        surfaceHover: p.navy[700],
        surfaceHoverSoft: p.navy[700],
        surfaceWarm: p.navy[800],
        callout: p.navy[800],
        border: p.navy[500],
        lineSoft: p.paper,
        track: p.navy[700],
        accent: p.gold[400],
        accentStrong: p.gold[400],
        accentBorder: "rgba(214, 180, 106, 0.3)",
        heading: p.paper,
        highlight: p.gold[400],
        highlightDisplay: p.gold[200],
        body: p.mist[300],
        bodyStrong: p.mist[100],
        label: p.mist[100],
        faint: p.steel[400],
        index: "rgba(255, 255, 255, 0.2)",
        eyebrow: p.gold[400],
        eyebrowAa: p.gold[400],
        icon: p.gold[400],
        tile: "rgba(214, 180, 106, 0.1)",
        tileActive: p.gold[400],
        tileActiveFg: p.navy[900],
        tileSolid: p.navy[900],
        tileSolidFg: p.gold[400],
        live: p.signal.green,
        focusRing: p.gold[400],
        link: p.gold[400],
        linkHover: p.paper,
        destructive: darkDestructive,
        btnPrimary: { bg: p.gold[400], fg: p.navy[900], border: p.gold[400], hoverBg: "transparent", hoverFg: p.gold[400], hoverBorder: p.gold[400] },
        btnGhost: { bg: "transparent", fg: p.paper, border: "rgba(255, 255, 255, 0.2)", hoverBg: p.gold[400], hoverFg: p.navy[900], hoverBorder: p.gold[400] },
      },
    },
  },
  "app-soft": {
    fontFamily: cairo,
    chrome: p.soft.navy[950],
    gridHero: p.soft.navy[800],
    gridDark: p.soft.neutral[500],
    chart: {
      series: [p.soft.navy[500], p.soft.gold[600], p.soft.success[700], p.soft.info[700], p.soft.danger[600]],
      actual: p.soft.gold[600],
      target: p.soft.neutral[500],
      area: p.soft.gold[500],
      current: p.soft.gold[500],
      bg: p.paper,
      grid: p.soft.neutral[150],
      axis: p.soft.neutral[600],
      up: p.soft.success[400],
      down: p.soft.danger[400],
      college: p.soft.navy[800],
    },
    status: { review: p.soft.gold[700] },
    shadow,
    focus: "rgba(201, 162, 39, 0.5)",
    selection: { bg: p.soft.gold[500], fg: p.soft.navy[950] },
    sidebar: softSidebar,
    band: softBand,
    shade: p.soft.navy[950],
    accentOnDark: p.soft.gold[300],
    gradient: gradients(softBand, softSidebar, p.soft.navy[950], {
      avatar: [p.soft.navy[700], p.soft.navy[950]],
      progress: [p.soft.navy[800], p.soft.navy[500]],
      callout: [p.soft.gold[50], p.paper],
      surfaceSubtle: [p.soft.neutral[25], p.paper],
    }),
    hatch: { light: hatch(p.soft.neutral[75], p.paper), muted: hatch(p.soft.neutral[500], p.soft.neutral[300]) },
    sections: {
      light: {
        bg: p.paper,
        bgCool: p.soft.neutral[50],
        bgWarm: p.soft.gold[50],
        fg: p.soft.neutral[900],
        surface: p.paper,
        surfaceHover: p.soft.neutral[25],
        surfaceHoverSoft: p.soft.neutral[25],
        surfaceWarm: p.paper,
        callout: p.soft.neutral[75],
        border: p.soft.neutral[100],
        lineSoft: p.soft.navy[800],
        track: p.soft.neutral[75],
        accent: p.soft.gold[500],
        accentStrong: p.soft.gold[600],
        accentBorder: "rgba(201, 162, 39, 0.4)",
        heading: p.soft.navy[950],
        highlight: p.soft.gold[700],
        highlightDisplay: p.soft.gold[700],
        body: p.soft.neutral[700],
        bodyStrong: p.soft.neutral[900],
        label: p.soft.neutral[700],
        faint: p.soft.neutral[600],
        index: p.soft.neutral[300],
        eyebrow: p.soft.gold[700],
        eyebrowAa: p.soft.gold[700],
        icon: p.soft.gold[600],
        tile: p.soft.gold[50],
        tileActive: p.soft.gold[500],
        tileActiveFg: p.soft.navy[950],
        tileSolid: p.soft.navy[950],
        tileSolidFg: p.soft.gold[300],
        live: p.soft.success[700],
        focusRing: p.soft.navy[950],
        link: p.soft.navy[700],
        linkHover: p.soft.navy[950],
        destructive: p.soft.danger[700],
        btnPrimary: { bg: p.soft.navy[800], fg: p.paper, border: p.soft.navy[800], hoverBg: p.soft.gold[500], hoverFg: p.soft.navy[950], hoverBorder: p.soft.gold[500] },
        btnGhost: { bg: "transparent", fg: p.soft.navy[800], border: "rgba(25, 53, 90, 0.2)", hoverBg: p.soft.gold[500], hoverFg: p.soft.navy[950], hoverBorder: p.soft.gold[500] },
      },
      dark: {
        bg: p.soft.navy[900],
        bgCool: p.soft.navy[900],
        bgWarm: p.soft.navy[900],
        fg: p.paper,
        surface: p.soft.navy[800],
        surfaceHover: p.soft.navy[700],
        surfaceHoverSoft: p.soft.navy[700],
        surfaceWarm: p.soft.navy[800],
        callout: p.soft.navy[800],
        border: "rgba(255, 255, 255, 0.14)",
        lineSoft: p.paper,
        track: p.soft.navy[700],
        accent: p.soft.gold[500],
        accentStrong: p.soft.gold[300],
        accentBorder: "rgba(201, 162, 39, 0.3)",
        heading: p.paper,
        highlight: p.soft.gold[300],
        highlightDisplay: p.soft.gold[100],
        body: p.soft.neutral[200],
        bodyStrong: p.soft.navy[100],
        label: p.soft.navy[100],
        faint: p.soft.neutral[400],
        index: "rgba(255, 255, 255, 0.2)",
        eyebrow: p.soft.gold[300],
        eyebrowAa: p.soft.gold[300],
        icon: p.soft.gold[300],
        tile: "rgba(201, 162, 39, 0.14)",
        tileActive: p.soft.gold[500],
        tileActiveFg: p.soft.navy[950],
        tileSolid: p.soft.navy[950],
        tileSolidFg: p.soft.gold[300],
        live: p.soft.success[400],
        focusRing: p.soft.gold[300],
        link: p.soft.gold[300],
        linkHover: p.paper,
        destructive: darkDestructive,
        btnPrimary: { bg: p.soft.gold[500], fg: p.soft.navy[950], border: p.soft.gold[500], hoverBg: "transparent", hoverFg: p.soft.gold[300], hoverBorder: p.soft.gold[500] },
        btnGhost: { bg: "transparent", fg: p.paper, border: "rgba(255, 255, 255, 0.2)", hoverBg: p.soft.gold[500], hoverFg: p.soft.navy[950], hoverBorder: p.soft.gold[500] },
      },
    },
  },
} as const satisfies Record<ThemeName, ThemeTokens>;

/** The value to put on data-theme. */
export const themeNames = Object.keys(themes) as ThemeName[];

export function isThemeName(value: unknown): value is ThemeName {
  return typeof value === "string" && Object.prototype.hasOwnProperty.call(themes, value);
}

// ---------- Corners: a second axis, independent of the theme (corners.css, switch in config.ts) ----------

/** The value to put on data-corners. */
export type CornerStyle = "square" | "rounded";
export type CornerStep = "xs" | "sm" | "md" | "lg" | "xl" | "pill";

/**
 * --ds-radius-<step> in px, per corner style (corners.css). --ds-radius-round is 50% in both styles (real circles)
 * and --ds-radius is an alias of md, so neither is listed here.
 */
export const corners = {
  square: { xs: 0, sm: 0, md: 0, lg: 0, xl: 0, pill: 0 },
  rounded: { xs: 4, sm: 6, md: 8, lg: 12, xl: 16, pill: 999 },
} as const satisfies Record<CornerStyle, Record<CornerStep, number>>;

export type CornerScale = Readonly<Record<CornerStep, number>>;

export const cornerStyles = Object.keys(corners) as CornerStyle[];

export function isCornerStyle(value: unknown): value is CornerStyle {
  return typeof value === "string" && Object.prototype.hasOwnProperty.call(corners, value);
}
