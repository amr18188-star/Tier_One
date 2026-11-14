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
  /** App palette (primitives.css --color-app-*): the app's hand-written colours before the token audit. */
  app: {
    "blue-130": "#d3ddeb", "blue-210": "#b9c6d8", "blue-180": "#c4d0e0", "teal-110": "#d5f1f2", "green-290": "#86e3ae", "amber-350": "#f6c453",
    "red-210": "#ffa394", "amber-870": "#3b2a05", "blue-215": "#b9c7dc", "blue-185": "#c3cfe1", "blue-290": "#9fb0c9", "amber-355": "#e6b867",
    "orange-440": "#d29e4c", "blue-30": "#f6f8fb", "blue-940": "#030c1e", "blue-890": "#101828", "blue-810": "#16294a", "blue-830": "#102446",
    "grey-990": "#000000", "green-600": "#2e9e63", "green-740": "#1c6a43", "green-80": "#e2f3e8", "green-250": "#a6d8bb", "green-40": "#f1faf4",
    "green-270": "#9fd3b5", "amber-500": "#d9a227", "orange-720": "#8f4500", "amber-60": "#fff4e0", "amber-250": "#ecd193", "amber-40": "#fdf8ec",
    "amber-590": "#b5841a", "amber-510": "#d9a520", "red-530": "#c2412d", "red-600": "#b42318", "red-50": "#fdecea", "red-200": "#efb4aa",
    "red-30": "#fdf3f1", "red-470": "#d1553f", "red-20": "#fff8f7", "red-230": "#e0b0a8", "orange-510": "#d07a2a", "blue-330": "#8ea9c8",
    "blue-190": "#c4ccd8", "slate-390": "#8d99ab", "blue-280": "#aab6c6", "blue-430": "#5aa9c9", "blue-620": "#2b5797", "blue-500": "#4a78b5",
    "amber-505": "#ffa500", "blue-50": "#e8f0fb", "blue-660": "#1d4f91", "indigo-40": "#f1eefb", "purple-560": "#5b3fa0", "green-110": "#d7f0e0",
    "green-780": "#145c36", "green-470": "#59b77f", "lime-90": "#e9f6dc", "lime-750": "#3d6b16", "lime-400": "#9ccc65", "lime-520": "#7cb342",
    "amber-90": "#fdf1d3", "amber-760": "#7a5600", "amber-370": "#f2c14e", "red-70": "#fde4df", "red-640": "#9b2c1c", "red-350": "#e57364",
    "green-700": "#1f7a4c", "green-580": "#4f9a3a", "amber-480": "#c9973f", "red-550": "#b8452f", "red-75": "#fde2df", "amber-80": "#fff0d4",
    "green-115": "#d9efe2", "green-240": "#a9dcc0", "green-820": "#0f4d2e", "blue-55": "#edf1f5", "slate-410": "#8a94a3", "slate-360": "#98a2b0",
    "slate-350": "#9aa4b2", "blue-270": "#b0b9c6", "slate-250": "#b5bdc8", "slate-355": "#9aa5b4", "slate-320": "#a3adba", "slate-470": "#7b8794",
    "slate-590": "#5b6576", "blue-200": "#c3cbd6", "blue-250": "#b6bfcb", "amber-520": "#c99a2e", "blue-70": "#e6ecf2", "blue-75": "#e8edf3",
    "blue-90": "#e3e8ee", "blue-80": "#e6ebf1", "blue-40": "#f1f3f7", "blue-85": "#e7eaf0", "blue-750": "#1f3a5f", "blue-60": "#e9eef5",
    "purple-530": "#6b4fa0", "purple-40": "#f3effa", "teal-630": "#2f8f83", "orange-570": "#c27a1a", "blue-550": "#2f6db5", "blue-45": "#eef4fb",
    "blue-505": "#3f8fbf", "slate-400": "#8a97a6", "orange-575": "#d48806", "amber-50": "#fffbe6", "amber-220": "#ffe58f", "lime-660": "#389e0d",
    "lime-40": "#f6ffed", "lime-260": "#b7eb8f", "red-560": "#cf1322", "red-35": "#fff1f0", "red-190": "#ffa39e", "orange-580": "#d46b08",
    "amber-55": "#fff7e6", "orange-220": "#ffd591", "blue-560": "#0958d9", "blue-65": "#e6f4ff", "blue-220": "#91caff", "purple-610": "#531dab",
    "purple-30": "#f9f0ff", "purple-180": "#d3adf7", "teal-680": "#08979c", "teal-50": "#e6fffb", "teal-280": "#87e8de", "indigo-560": "#1d39c4",
    "blue-35": "#f0f5ff", "blue-160": "#adc6ff", "blue-20": "#fafbfc", "blue-95": "#f6f9fb", "teal-40": "#f3f8f7", "blue-100": "#e6ebf2",
    "blue-105": "#f4f6f9", "teal-60": "#e8f6f6", "amber-65": "#fbf5e6", "blue-880": "#0f1b2e", "blue-110": "#dce6f2", "blue-115": "#e9edf2",
    "grey-930": "#111111", "red-540": "#c0392b", "grey-660": "#525659",
  },
  /** Later palettes (primitives.css --color-night-*, --color-midnight-*, --color-silver-*, --color-graphite-*). */
  night: { 990: "#03080f", 950: "#060d18", 930: "#08111f", 925: "#0a1424", 900: "#0f1a2c", 875: "#132034", 850: "#16243a", 800: "#1a2a43", 760: "#1d3152", 700: "#263c5e", 650: "#2b3c57", 600: "#3a5275", 500: "#56698a", 400: "#8493a9", 300: "#a3b1c4", 200: "#c3cedc", 100: "#dce3ed", 50: "#eef2f8", "blue-500": "#5b8bd6", "blue-400": "#7aa3e3", "blue-300": "#8fb4ec", "blue-200": "#b6cff4", "steel-400": "#8ea4c2", "green-400": "#45bd7f", "green-300": "#86dfab", "lime-400": "#9cc95e", "lime-300": "#c0e18f", "amber-400": "#e8b542", "amber-300": "#f3c86e", "amber-700": "#7d5a10", "orange-400": "#e8883a", "orange-300": "#f6b37a", "red-400": "#ef5f5f", "red-300": "#ff9f90", "purple-400": "#a68ce2", "purple-300": "#c6b2f2", "cyan-400": "#4cc4cc", "cyan-300": "#86dde2", "geek-300": "#a3b3ff", "geek-400": "#7f93f0" },
  midnight: { 990: "#0b0d29", 950: "#12153c", 900: "#1b1f52", 800: "#252a68", 700: "#31377d", 600: "#3e4696", 500: "#5560b0", 400: "#7c85c7", 100: "#e7e9f5", 50: "#f3f4fa", 25: "#f8f8fc" },
  silver: { 50: "#f6f7f9", 100: "#eceef2", 200: "#dee1e7", 300: "#c8cdd7", 400: "#a9b0bd", 500: "#868e9e", 600: "#6b7385", 700: "#545b6c", 800: "#3d4352" },
  graphite: { 990: "#0d0f12", 950: "#15181c", 900: "#1d2126", 800: "#272c32", 700: "#333941", 600: "#454c56", 500: "#5c6470", 400: "#858c97", 350: "#a0a6af", 300: "#b5bac1", 200: "#dadde1", 100: "#eceef0", 50: "#f5f6f7", 25: "#fafafb" },
  ink: { "teal-700": "#0b6b70" },
} as const;

export type ThemeName = "navy-gold" | "app-soft" | "night-gold" | "indigo-silver" | "graphite-gold";
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

/** Token-audit groups (2026-11-14): CSS --ds-<group>-<key>, theme level. Roles: new-tokens.md and README "Audit tokens". */
export type OnDarkKey = "fg" | "body" | "muted" | "soft" | "code" | "ok" | "warn" | "bad" | "now" | "nowInk";
export type AuthKey = "muted" | "body" | "faint" | "icon" | "iconTint" | "page" | "logoShadow";
export type ShadowKey = "ink" | "navy" | "band" | "mark";
export type StatusKey = "ok" | "okInk" | "okSoft" | "okBorder" | "okTint" | "doneBorder" | "near" | "nearInk" | "nearSoft" | "nearBorder" | "nearTint" | "nearMark" | "nearBar" | "late" | "lateInk" | "lateSoft" | "lateBorder" | "lateTint" | "lateBar" | "flagBg" | "vacantBorder" | "gap" | "plan" | "none" | "noneInk" | "draft" | "progress" | "info" | "share" | "caution" | "zeroSoft" | "zeroInk" | "exemptSoft" | "exemptInk";
export type ScoreKey = "fullSoft" | "fullInk" | "fullMark" | "highSoft" | "highInk" | "highMark" | "highBar" | "midSoft" | "midInk" | "midMark" | "lowSoft" | "lowInk" | "lowMark";
export type TierKey = "1" | "2" | "3" | "4" | "5" | "ink";
export type HeatKey = "1" | "2" | "3" | "4" | "4Ink" | "none";
export type NeutralKey = "mark" | "axis" | "faint" | "off" | "na" | "stopped" | "lead" | "base" | "actual" | "arrow" | "dot";
export type ChartKey = "accent" | "value" | "track" | "ringTrack" | "gridSoft" | "gridFaint" | "off" | "offKey" | "targetLine";
export type UnitKey = "university" | "universityBg" | "sector" | "sectorBg" | "administration" | "center" | "college" | "collegeBg" | "institute" | "department";
export type TagKey = "pendingFg" | "pendingBg" | "pendingBorder" | "positiveFg" | "positiveBg" | "positiveBorder" | "negativeFg" | "negativeBg" | "negativeBorder" | "cautionFg" | "cautionBg" | "cautionBorder" | "infoFg" | "infoBg" | "infoBorder" | "specialFg" | "specialBg" | "specialBorder" | "highlightFg" | "highlightBg" | "highlightBorder" | "categoryFg" | "categoryBg" | "categoryBorder";
export type PillarKey = "keep" | "lift" | "tintKeep" | "tintMix" | "fillMix";
export type AppKey = "mutedBg" | "faintBg" | "rowHover" | "rowSelected" | "tableGroup" | "topbar" | "noteBg" | "partnerBg" | "codeBg" | "codeFg" | "printDesk" | "printInk" | "pdfIcon" | "pdfFrame";

export interface ThemeTokens {
  /** Page scheme. "dark": the default (light) scope holds dark surfaces, and antd runs its darkAlgorithm (antd-theme.ts). */
  scheme: "light" | "dark";
  /** antd success / warning fills (antd-theme.ts): true = the status chips (okSoft / okBorder, nearSoft / nearBorder); false = antd's
   *  preset fills, held by the green / gold Tag tokens (navy-gold, app-soft). The text is the status ink (okInk / nearInk) in both. */
  statusToAntd: boolean;
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
  } & Record<ChartKey, string>;
  /** --ds-status-review: the "under review" status segment (Home definition-status bar). */
  status: { review: string } & Record<StatusKey, string>;
  shadow: { sm: string; md: string } & Record<ShadowKey, string>;
  focus: string;
  selection: { bg: string; fg: string };
  sidebar: SidebarTokens;
  band: BandTokens;
  /** --ds-shade: the dark ink that pillar colours (--pc) deepen toward in their gradients. */
  shade: string;
  /** --ds-accent-on-dark: accent text and marks on navy blocks without data-section="dark" (= sections.dark.eyebrow). */
  accentOnDark: string;
  /** --ds-ink-fill: the navy fill under white text (legacy --navy as a background: table heads, active tabs and filters,
   *  selected segments). = sections.light.heading in a light palette; a dark palette has a separate fill. */
  inkFill: string;
  gradient: GradientTokens;
  hatch: HatchTokens;
  /** Token-audit groups (theme level): --ds-on-dark-*, --ds-auth-*, --ds-score-*, --ds-tier-*, --ds-heat-*, --ds-neutral-*, --ds-unit-*, --ds-tag-*, --ds-app-*. */
  onDark: Record<OnDarkKey, string>;
  auth: Record<AuthKey, string>;
  score: Record<ScoreKey, string>;
  tier: Record<TierKey, string>;
  heat: Record<HeatKey, string>;
  neutral: Record<NeutralKey, string>;
  unit: Record<UnitKey, string>;
  tag: Record<TagKey, string>;
  app: Record<AppKey, string>;
  /** --ds-pillar-*: how pillar data colours meet this palette (shared/pillars.tsx tint()): --pc = colour keep% toward lift,
   *  --pt = own tint tintKeep%, else the colour tintMix% into --ds-surface; --pf (fills under white text) = --pc with fillMix%
   *  of --ds-shade. Percentages as CSS strings; lift is a colour. */
  pillar: Record<PillarKey, string>;
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
  fg: "#c1ccdb", // color-mix(in srgb, mist-100 45%, mist-300)
  fgActive: p.paper,
  muted: "#94a6bd", // color-mix(in srgb, mist-300 55%, steel-400)
  hoverBg: p.navy[700],
  activeBg: "#315073", // color-mix(in srgb, navy-700 70%, steel-400)
  indicator: p.gold[200],
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
  muted: "#b4c1d0", // color-mix(in srgb, soft-neutral-200 35%, soft-neutral-300)
  hoverBg: "#204576", // color-mix(in srgb, soft-navy-700 80%, soft-navy-900)
  activeBg: "#2b5795", // color-mix(in srgb, soft-navy-500 45%, soft-navy-700)
  indicator: p.soft.gold[300],
  border: "rgba(255, 255, 255, 0.14)",
  focus: p.soft.gold[300],
  badgeBg: p.soft.gold[300],
  badgeFg: p.soft.navy[950],
};
/** night-gold, indigo-silver, graphite-gold: sidebar and band (themes/<name>.css, theme level). */
const nightSidebar: SidebarTokens = {
  bg: p.night[950],
  fg: "#b3c0d0", // color-mix(in srgb, night-200 50%, night-300)
  fgActive: p.paper,
  muted: "#94a2b7", // color-mix(in srgb, night-300 50%, night-400)
  hoverBg: p.night[760],
  activeBg: p.night[600],
  indicator: p.gold[200],
  border: p.night[800],
  focus: p.gold[400],
  badgeBg: p.gold[400],
  badgeFg: p.night[950],
  bgTop: p.night[930],
  glow: "rgba(214, 180, 106, 0.07)",
};
const nightBand: BandTokens = {
  from: p.navy[800],
  via: p.navy[700],
  to: p.night[760],
  glow: "rgba(214, 180, 106, 0.14)",
  glowAccent: "rgba(214, 180, 106, 0.16)",
};
const indigoSidebar: SidebarTokens = {
  bg: p.midnight[950],
  fg: "#d3d7df", // color-mix(in srgb, silver-200 50%, silver-300)
  fgActive: p.paper,
  muted: "#b5bcc7", // color-mix(in srgb, silver-300 40%, silver-400)
  hoverBg: p.midnight[700],
  activeBg: "#454e9e", // color-mix(in srgb, midnight-500 30%, midnight-600)
  indicator: p.silver[200],
  border: p.midnight[700],
  focus: p.silver[200],
  badgeBg: p.silver[200],
  badgeFg: p.midnight[900],
  bgTop: p.midnight[900],
  glow: "rgba(222, 225, 231, 0.07)",
};
const indigoBand: BandTokens = {
  from: p.midnight[900],
  via: p.midnight[800],
  to: p.midnight[700],
  glow: "rgba(222, 225, 231, 0.12)",
  glowAccent: "rgba(124, 133, 199, 0.28)",
};
const graphiteSidebar: SidebarTokens = {
  bg: p.graphite[950],
  fg: "#c8ccd1", // color-mix(in srgb, graphite-200 50%, graphite-300)
  fgActive: p.paper,
  muted: "#a8aeb6", // color-mix(in srgb, graphite-300 40%, graphite-350)
  hoverBg: p.graphite[700],
  activeBg: p.graphite[600],
  indicator: p.gold[200],
  border: p.graphite[700],
  focus: p.gold[400],
  badgeBg: p.gold[400],
  badgeFg: p.graphite[900],
  bgTop: p.graphite[900],
  glow: "rgba(214, 180, 106, 0.07)",
};
const graphiteBand: BandTokens = {
  from: p.graphite[900],
  via: p.graphite[800],
  to: p.graphite[700],
  glow: "rgba(214, 180, 106, 0.12)",
  glowAccent: "rgba(214, 180, 106, 0.16)",
};
const softBand: BandTokens = { from: p.soft.navy[900], via: p.soft.navy[800], to: p.soft.navy[700], glow: "rgba(53, 103, 176, 0.38)", glowAccent: "rgba(201, 162, 39, 0.18)" };

/** Token-audit groups of navy-gold (themes/navy-gold.css, theme level; roles in the README "Audit tokens"). */
const ngAudit = {
  onDark: {
    fg: p.paper,
    body: p.app["blue-130"],
    muted: p.app["blue-210"],
    soft: p.app["blue-180"],
    code: p.app["teal-110"],
    ok: p.app["green-290"],
    warn: p.app["amber-350"],
    bad: p.app["red-210"],
    now: p.app["amber-350"],
    nowInk: p.app["amber-870"],
  },
  auth: {
    muted: p.app["blue-215"],
    body: p.app["blue-185"],
    faint: p.app["blue-290"],
    icon: p.app["amber-355"],
    iconTint: p.app["orange-440"],
    page: p.app["blue-30"],
    logoShadow: p.app["blue-940"],
  },
  score: {
    fullSoft: p.app["green-110"],
    fullInk: p.app["green-780"],
    fullMark: p.app["green-470"],
    highSoft: p.app["lime-90"],
    highInk: p.app["lime-750"],
    highMark: p.app["lime-400"],
    highBar: p.app["lime-520"],
    midSoft: p.app["amber-90"],
    midInk: p.app["amber-760"],
    midMark: p.app["amber-370"],
    lowSoft: p.app["red-70"],
    lowInk: p.app["red-640"],
    lowMark: p.app["red-350"],
  },
  tier: {
    "1": p.app["green-700"],
    "2": p.app["green-580"],
    "3": p.app["amber-480"],
    "4": p.app["orange-510"],
    "5": p.app["red-550"],
    ink: p.paper,
  },
  heat: {
    "1": p.app["red-75"],
    "2": p.app["amber-80"],
    "3": p.app["green-115"],
    "4": p.app["green-240"],
    "4Ink": p.app["green-820"],
    none: p.app["blue-55"],
  },
  neutral: {
    mark: p.app["slate-410"],
    axis: p.soft.neutral[600],
    faint: p.app["slate-350"],
    off: p.app["blue-270"],
    na: p.app["slate-250"],
    stopped: p.app["slate-355"],
    lead: p.app["slate-320"],
    base: p.app["slate-470"],
    actual: p.app["slate-590"],
    arrow: p.app["blue-200"],
    dot: p.app["blue-250"],
  },
  unit: {
    university: p.app["blue-750"],
    universityBg: p.app["blue-60"],
    sector: p.app["purple-530"],
    sectorBg: p.app["purple-40"],
    administration: p.app["teal-630"],
    center: p.app["orange-570"],
    college: p.app["blue-550"],
    collegeBg: p.app["blue-45"],
    institute: p.app["blue-505"],
    department: p.app["slate-400"],
  },
  tag: {
    pendingFg: p.app["amber-760"],
    pendingBg: p.app["amber-50"],
    pendingBorder: p.app["amber-220"],
    positiveFg: p.app["lime-750"],
    positiveBg: p.app["lime-40"],
    positiveBorder: p.app["lime-260"],
    negativeFg: p.app["red-560"],
    negativeBg: p.app["red-35"],
    negativeBorder: p.app["red-190"],
    cautionFg: p.app["orange-720"],
    cautionBg: p.app["amber-55"],
    cautionBorder: p.app["orange-220"],
    infoFg: p.app["blue-560"],
    infoBg: p.app["blue-65"],
    infoBorder: p.app["blue-220"],
    specialFg: p.app["purple-610"],
    specialBg: p.app["purple-30"],
    specialBorder: p.app["purple-180"],
    highlightFg: p.ink["teal-700"],
    highlightBg: p.app["teal-50"],
    highlightBorder: p.app["teal-280"],
    categoryFg: p.app["indigo-560"],
    categoryBg: p.app["blue-35"],
    categoryBorder: p.app["blue-160"],
  },
  app: {
    mutedBg: p.soft.neutral[25],
    faintBg: p.app["blue-20"],
    rowHover: p.app["blue-95"],
    rowSelected: p.app["teal-40"],
    tableGroup: p.app["blue-100"],
    topbar: p.app["blue-105"],
    noteBg: p.app["teal-60"],
    partnerBg: p.app["amber-65"],
    codeBg: p.app["blue-880"],
    codeFg: p.app["blue-110"],
    printDesk: p.app["blue-115"],
    printInk: p.app["grey-930"],
    pdfIcon: p.app["red-540"],
    pdfFrame: p.app["grey-660"],
  },
} as const;
/** The audit fields of `chart`, `status` and `shadow` in navy-gold. */
const ngAuditExtra = {
  chart: {
    accent: p.app["amber-520"],
    value: p.app["blue-810"],
    track: p.app["blue-70"],
    ringTrack: p.app["blue-75"],
    gridSoft: p.app["blue-90"],
    gridFaint: p.app["blue-80"],
    off: p.app["blue-40"],
    offKey: p.app["blue-85"],
    targetLine: p.app["amber-510"],
  },
  status: {
    ok: p.app["green-600"],
    okInk: p.app["green-740"],
    okSoft: p.app["green-80"],
    okBorder: p.app["green-250"],
    okTint: p.app["green-40"],
    doneBorder: p.app["green-270"],
    near: p.app["amber-500"],
    nearInk: p.app["orange-720"],
    nearSoft: p.app["amber-60"],
    nearBorder: p.app["amber-250"],
    nearTint: p.app["amber-40"],
    nearMark: p.app["amber-590"],
    nearBar: p.app["amber-510"],
    late: p.app["red-530"],
    lateInk: p.app["red-600"],
    lateSoft: p.app["red-50"],
    lateBorder: p.app["red-200"],
    lateTint: p.app["red-30"],
    lateBar: p.app["red-470"],
    flagBg: p.app["red-20"],
    vacantBorder: p.app["red-230"],
    gap: p.app["orange-510"],
    plan: p.app["blue-330"],
    none: p.app["blue-190"],
    noneInk: p.app["slate-390"],
    draft: p.app["blue-280"],
    progress: p.app["blue-430"],
    info: p.app["blue-620"],
    share: p.app["blue-500"],
    caution: p.app["amber-505"],
    zeroSoft: p.app["blue-50"],
    zeroInk: p.app["blue-660"],
    exemptSoft: p.app["indigo-40"],
    exemptInk: p.app["purple-560"],
  },
  shadow: {
    ink: p.app["blue-890"],
    navy: p.app["blue-810"],
    band: p.app["blue-830"],
    mark: p.app["grey-990"],
  },
} as const;

/** Token-audit groups of app-soft (themes/app-soft.css, theme level; roles in the README "Audit tokens"). */
const softAudit = {
  onDark: {
    fg: p.paper,
    body: p.soft.navy[100],
    muted: p.soft.neutral[200],
    soft: p.soft.neutral[200],
    code: p.soft.navy[100],
    ok: p.app["green-290"],
    warn: p.app["amber-350"],
    bad: p.app["red-210"],
    now: p.soft.gold[300],
    nowInk: p.soft.navy[950],
  },
  auth: {
    muted: p.soft.neutral[200],
    body: p.soft.navy[100],
    faint: p.soft.neutral[200],
    icon: p.soft.gold[300],
    iconTint: p.soft.gold[500],
    page: p.soft.neutral[50],
    logoShadow: p.app["blue-940"],
  },
  score: {
    fullSoft: p.app["green-110"],
    fullInk: p.app["green-780"],
    fullMark: p.app["green-470"],
    highSoft: p.app["lime-90"],
    highInk: p.app["lime-750"],
    highMark: p.app["lime-400"],
    highBar: p.app["lime-520"],
    midSoft: p.app["amber-90"],
    midInk: p.app["amber-760"],
    midMark: p.app["amber-370"],
    lowSoft: p.app["red-70"],
    lowInk: p.app["red-640"],
    lowMark: p.app["red-350"],
  },
  tier: {
    "1": p.app["green-700"],
    "2": p.app["green-580"],
    "3": p.app["amber-480"],
    "4": p.app["orange-510"],
    "5": p.app["red-550"],
    ink: p.paper,
  },
  heat: {
    "1": p.app["red-75"],
    "2": p.app["amber-80"],
    "3": p.app["green-115"],
    "4": p.app["green-240"],
    "4Ink": p.app["green-820"],
    none: p.soft.neutral[75],
  },
  neutral: {
    mark: p.soft.neutral[500],
    axis: p.soft.neutral[600],
    faint: p.soft.neutral[400],
    off: p.soft.neutral[300],
    na: p.soft.neutral[300],
    stopped: p.soft.neutral[400],
    lead: p.soft.neutral[300],
    base: p.soft.neutral[500],
    actual: p.soft.neutral[600],
    arrow: p.soft.neutral[200],
    dot: p.soft.neutral[300],
  },
  unit: {
    university: p.app["blue-750"],
    universityBg: p.app["blue-60"],
    sector: p.app["purple-530"],
    sectorBg: p.app["purple-40"],
    administration: p.app["teal-630"],
    center: p.app["orange-570"],
    college: p.app["blue-550"],
    collegeBg: p.app["blue-45"],
    institute: p.app["blue-505"],
    department: p.app["slate-400"],
  },
  tag: {
    pendingFg: p.app["amber-760"],
    pendingBg: p.app["amber-50"],
    pendingBorder: p.app["amber-220"],
    positiveFg: p.app["lime-750"],
    positiveBg: p.app["lime-40"],
    positiveBorder: p.app["lime-260"],
    negativeFg: p.app["red-560"],
    negativeBg: p.app["red-35"],
    negativeBorder: p.app["red-190"],
    cautionFg: p.app["orange-720"],
    cautionBg: p.app["amber-55"],
    cautionBorder: p.app["orange-220"],
    infoFg: p.app["blue-560"],
    infoBg: p.app["blue-65"],
    infoBorder: p.app["blue-220"],
    specialFg: p.app["purple-610"],
    specialBg: p.app["purple-30"],
    specialBorder: p.app["purple-180"],
    highlightFg: p.ink["teal-700"],
    highlightBg: p.app["teal-50"],
    highlightBorder: p.app["teal-280"],
    categoryFg: p.app["indigo-560"],
    categoryBg: p.app["blue-35"],
    categoryBorder: p.app["blue-160"],
  },
  app: {
    mutedBg: p.soft.neutral[25],
    faintBg: p.soft.neutral[25],
    rowHover: p.soft.neutral[25],
    rowSelected: p.soft.neutral[50],
    tableGroup: p.soft.neutral[100],
    topbar: p.soft.neutral[50],
    noteBg: p.soft.neutral[50],
    partnerBg: p.soft.gold[50],
    codeBg: p.soft.navy[950],
    codeFg: p.soft.navy[100],
    printDesk: p.soft.neutral[75],
    printInk: p.app["grey-930"],
    pdfIcon: p.app["red-540"],
    pdfFrame: p.app["grey-660"],
  },
} as const;
/** The audit fields of `chart`, `status` and `shadow` in app-soft. */
const softAuditExtra = {
  chart: {
    accent: p.soft.gold[500],
    value: p.soft.navy[950],
    track: p.soft.neutral[75],
    ringTrack: p.soft.neutral[75],
    gridSoft: p.soft.neutral[150],
    gridFaint: p.soft.neutral[150],
    off: p.soft.neutral[50],
    offKey: p.soft.neutral[100],
    targetLine: p.app["amber-510"],
  },
  status: {
    ok: p.app["green-600"],
    okInk: p.app["green-740"],
    okSoft: p.app["green-80"],
    okBorder: p.app["green-250"],
    okTint: p.app["green-40"],
    doneBorder: p.app["green-270"],
    near: p.app["amber-500"],
    nearInk: p.app["orange-720"],
    nearSoft: p.app["amber-60"],
    nearBorder: p.app["amber-250"],
    nearTint: p.app["amber-40"],
    nearMark: p.app["amber-590"],
    nearBar: p.app["amber-510"],
    late: p.app["red-530"],
    lateInk: p.app["red-600"],
    lateSoft: p.app["red-50"],
    lateBorder: p.app["red-200"],
    lateTint: p.app["red-30"],
    lateBar: p.app["red-470"],
    flagBg: p.app["red-20"],
    vacantBorder: p.app["red-230"],
    gap: p.app["orange-510"],
    plan: p.app["blue-330"],
    none: p.app["blue-190"],
    noneInk: p.app["slate-390"],
    draft: p.app["blue-280"],
    progress: p.app["blue-430"],
    info: p.app["blue-620"],
    share: p.app["blue-500"],
    caution: p.app["amber-505"],
    zeroSoft: p.app["blue-50"],
    zeroInk: p.app["blue-660"],
    exemptSoft: p.app["indigo-40"],
    exemptInk: p.app["purple-560"],
  },
  shadow: {
    ink: p.app["blue-890"],
    navy: p.soft.navy[800],
    band: p.soft.navy[950],
    mark: p.app["grey-990"],
  },
} as const;

export const themes = {
  "navy-gold": {
    scheme: "light",
    statusToAntd: false,
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
      ...ngAuditExtra.chart,
    },
    status: { review: p.gold[600], ...ngAuditExtra.status },
    shadow: { ...shadow, ...ngAuditExtra.shadow },
    focus: "rgba(201, 162, 39, 0.5)",
    selection: { bg: p.soft.gold[500], fg: p.soft.navy[950] },
    sidebar: ngSidebar,
    band: ngBand,
    shade: p.navy[950],
    accentOnDark: p.gold[400],
    inkFill: p.navy[900],
    gradient: gradients(ngBand, ngSidebar, p.navy[950], {
      avatar: [p.navy[700], p.navy[900]],
      progress: [p.navy[900], p.navy[700]],
      callout: [p.sand[50], p.ivory[50]],
      surfaceSubtle: [p.cloud, p.paper],
    }),
    hatch: { light: hatch(p.ash[100], p.paper), muted: hatch(p.steel[400], p.mist[300]) },
    ...ngAudit,
    pillar: { keep: "100%", lift: p.paper, tintKeep: "100%", tintMix: "0%", fillMix: "0%" },
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
    scheme: "light",
    statusToAntd: false,
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
      ...softAuditExtra.chart,
    },
    status: { review: p.soft.gold[700], ...softAuditExtra.status },
    shadow: { ...shadow, ...softAuditExtra.shadow },
    focus: "rgba(201, 162, 39, 0.5)",
    selection: { bg: p.soft.gold[500], fg: p.soft.navy[950] },
    sidebar: softSidebar,
    band: softBand,
    shade: p.soft.navy[950],
    accentOnDark: p.soft.gold[300],
    inkFill: p.soft.navy[950],
    gradient: gradients(softBand, softSidebar, p.soft.navy[950], {
      avatar: [p.soft.navy[700], p.soft.navy[950]],
      progress: [p.soft.navy[800], p.soft.navy[500]],
      callout: [p.soft.gold[50], p.paper],
      surfaceSubtle: [p.soft.neutral[25], p.paper],
    }),
    hatch: { light: hatch(p.soft.neutral[75], p.paper), muted: hatch(p.soft.neutral[500], p.soft.neutral[300]) },
    ...softAudit,
    pillar: { keep: "100%", lift: p.paper, tintKeep: "100%", tintMix: "0%", fillMix: "0%" },
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
  "night-gold": {
    scheme: "dark",
    statusToAntd: true,
    fontFamily: cairo,
    chrome: p.night[950],
    gridHero: p.night[700],
    gridDark: p.night[500],
    chart: {
      series: [p.night["blue-500"], p.gold[400], p.night["steel-400"], p.night["cyan-400"], p.gold[200]],
      actual: p.gold[400],
      target: p.night["steel-400"],
      area: p.gold[400],
      current: p.gold[200],
      bg: p.night[900],
      grid: p.night[650],
      axis: p.night[400],
      up: p.night["green-400"],
      down: p.night["red-400"],
      college: p.night["blue-300"],
      accent: p.gold[400],
      value: p.night[50],
      track: p.night[800],
      ringTrack: p.night[800],
      gridSoft: p.night[800],
      gridFaint: p.night[800],
      off: p.night[875],
      offKey: p.night[800],
      targetLine: p.night["amber-400"],
    },
    status: {
      review: p.gold[500],
      ok: p.night["green-400"],
      okInk: p.night["green-300"],
      okSoft: "#19373b",
      okBorder: "#276351",
      okTint: "#142a34",
      doneBorder: "#2d745a",
      near: p.night["amber-400"],
      nearInk: p.night["amber-300"],
      nearSoft: "#363630",
      nearBorder: "#716036",
      nearTint: "#252a2e",
      nearMark: p.night["amber-700"],
      nearBar: p.night["amber-400"],
      late: p.night["red-400"],
      lateInk: p.night["red-300"],
      lateSoft: "#372635",
      lateBorder: "#743943",
      lateTint: "#252131",
      lateBar: p.night["red-400"],
      flagBg: "#2a2232",
      vacantBorder: "#8a4048",
      gap: p.night["orange-400"],
      plan: p.night["steel-400"],
      none: p.night[600],
      noneInk: p.night[400],
      draft: p.night[400],
      progress: p.night["cyan-400"],
      info: p.night["blue-400"],
      share: p.night["blue-400"],
      caution: p.night["orange-300"],
      zeroSoft: "#1e314e",
      zeroInk: p.night["blue-200"],
      exemptSoft: "#2d3150",
      exemptInk: p.night["purple-300"],
    },
    shadow: { sm: "0 8px 24px rgba(3, 8, 15, 0.35)", md: "0 16px 40px rgba(3, 8, 15, 0.5)", ink: p.app["grey-990"], navy: p.night[990], band: p.night[990], mark: p.app["grey-990"] },
    focus: "rgba(214, 180, 106, 0.45)",
    selection: { bg: p.gold[400], fg: p.night[950] },
    sidebar: nightSidebar,
    band: nightBand,
    shade: p.night[990],
    accentOnDark: p.gold[400],
    inkFill: p.night[760],
    gradient: gradients(nightBand, nightSidebar, p.night[990], {
      avatar: [p.navy[700], p.navy[900]],
      progress: [p.night["blue-500"], p.night["blue-300"]],
      callout: [p.night[800], p.night[900]],
      surfaceSubtle: [p.night[875], p.night[900]],
    }),
    hatch: { light: hatch(p.night[850], p.night[900]), muted: hatch(p.night[500], p.night[600]) },
    onDark: {
      fg: p.paper,
      body: p.app["blue-130"],
      muted: p.app["blue-210"],
      soft: p.app["blue-180"],
      code: p.app["teal-110"],
      ok: p.app["green-290"],
      warn: p.app["amber-350"],
      bad: p.app["red-210"],
      now: p.app["amber-350"],
      nowInk: p.app["amber-870"],
    },
    auth: {
      muted: p.app["blue-215"],
      body: p.app["blue-185"],
      faint: p.app["blue-210"],
      icon: p.app["amber-355"],
      iconTint: p.app["orange-440"],
      page: p.night[925],
      logoShadow: p.night[990],
    },
    score: {
      fullSoft: "#1b3e3e",
      fullInk: p.night["green-300"],
      fullMark: p.night["green-400"],
      highSoft: "#2b3d36",
      highInk: p.night["lime-300"],
      highMark: p.night["lime-400"],
      highBar: p.night["lime-400"],
      midSoft: "#3a3930",
      midInk: p.night["amber-300"],
      midMark: p.night["amber-400"],
      lowSoft: "#3c2836",
      lowInk: p.night["red-300"],
      lowMark: p.night["red-400"],
    },
    tier: {
      "1": p.night["green-400"],
      "2": p.night["lime-400"],
      "3": p.night["amber-400"],
      "4": p.night["orange-400"],
      "5": p.night["red-400"],
      ink: p.night[950],
    },
    heat: {
      "1": "#402937",
      "2": "#3f3c31",
      "3": "#19373b",
      "4": "#24584c",
      "4Ink": p.night[50],
      none: p.night[800],
    },
    neutral: {
      mark: p.night[400],
      axis: p.night[400],
      faint: p.night[400],
      off: p.night[650],
      na: p.night[600],
      stopped: p.night[500],
      lead: p.night[500],
      base: p.night[400],
      actual: p.night[200],
      arrow: p.night[600],
      dot: p.night[500],
    },
    unit: {
      university: p.night["blue-200"],
      universityBg: "#1b2c47",
      sector: p.night["purple-300"],
      sectorBg: "#272c49",
      administration: p.night["cyan-400"],
      center: p.night["orange-400"],
      college: p.night["blue-400"],
      collegeBg: "#182840",
      institute: p.night["cyan-300"],
      department: p.night[300],
    },
    tag: {
      pendingFg: p.night["amber-300"],
      pendingBg: "#2d302f",
      pendingBorder: "#716036",
      positiveFg: p.night["lime-300"],
      positiveBg: "#233233",
      positiveBorder: "#4e6942",
      negativeFg: p.night["red-300"],
      negativeBg: "#2e2433",
      negativeBorder: "#743943",
      cautionFg: p.night["orange-300"],
      cautionBg: "#2d292e",
      cautionBorder: "#714c32",
      infoFg: p.night["blue-200"],
      infoBg: "#1b2c47",
      infoBorder: "#355281",
      specialFg: p.night["purple-300"],
      specialBg: "#242a45",
      specialBorder: "#534d7e",
      highlightFg: p.night["cyan-300"],
      highlightBg: "#162e3f",
      highlightBorder: "#275e6c",
      categoryFg: p.night["geek-300"],
      categoryBg: "#1f2b47",
      categoryBorder: "#415084",
    },
    app: {
      mutedBg: p.night[875],
      faintBg: p.night[875],
      rowHover: p.night[850],
      rowSelected: "#1f2631",
      tableGroup: p.night[800],
      topbar: p.night[925],
      noteBg: "#1f2631",
      partnerBg: "#272c33",
      codeBg: p.night[990],
      codeFg: p.app["blue-110"],
      printDesk: p.night[950],
      printInk: p.night[100],
      pdfIcon: p.night["red-400"],
      pdfFrame: p.night[950],
    },
    pillar: {
      keep: "80%",
      lift: p.paper,
      tintKeep: "0%",
      tintMix: "10%",
      fillMix: "60%",
    },
    sections: {
      light: {
        bg: p.night[900],
        bgCool: p.night[925],
        bgWarm: "#1d2530",
        fg: p.night[50],
        surface: p.night[900],
        surfaceHover: p.night[850],
        surfaceHoverSoft: p.night[875],
        surfaceWarm: p.night[875],
        callout: p.night[850],
        border: p.night[650],
        lineSoft: p.paper,
        track: p.night[800],
        accent: p.gold[400],
        accentStrong: p.gold[400],
        accentBorder: "rgba(214, 180, 106, 0.35)",
        heading: p.night[50],
        highlight: p.gold[400],
        highlightDisplay: p.gold[200],
        body: p.night[300],
        bodyStrong: p.night[100],
        label: p.night[200],
        faint: p.night[400],
        index: p.night[500],
        eyebrow: p.gold[400],
        eyebrowAa: p.gold[400],
        icon: p.gold[400],
        tile: "#232932",
        tileActive: p.gold[400],
        tileActiveFg: p.night[950],
        tileSolid: p.night[760],
        tileSolidFg: p.gold[400],
        live: p.night["green-400"],
        focusRing: p.gold[400],
        link: p.gold[400],
        linkHover: p.night[50],
        destructive: "#f6ada9",
        btnPrimary: { bg: p.night["blue-300"], fg: p.night[950], border: p.night["blue-300"], hoverBg: p.gold[400], hoverFg: p.night[950], hoverBorder: p.gold[400] },
        btnGhost: { bg: "transparent", fg: p.night[50], border: "rgba(255, 255, 255, 0.22)", hoverBg: p.gold[400], hoverFg: p.night[950], hoverBorder: p.gold[400] },
      },
      dark: {
        bg: p.night[760],
        bgCool: p.night[760],
        bgWarm: p.night[760],
        fg: p.paper,
        surface: p.night[700],
        surfaceHover: p.navy[500],
        surfaceHoverSoft: p.navy[500],
        surfaceWarm: p.night[700],
        callout: p.night[700],
        border: p.night[600],
        lineSoft: p.paper,
        track: p.night[700],
        accent: p.gold[400],
        accentStrong: p.gold[400],
        accentBorder: "rgba(214, 180, 106, 0.3)",
        heading: p.paper,
        highlight: p.gold[400],
        highlightDisplay: p.gold[200],
        body: p.mist[300],
        bodyStrong: p.mist[100],
        label: p.mist[100],
        faint: p.night["steel-400"],
        index: "rgba(255, 255, 255, 0.2)",
        eyebrow: p.gold[400],
        eyebrowAa: p.gold[400],
        icon: p.gold[400],
        tile: "rgba(214, 180, 106, 0.1)",
        tileActive: p.gold[400],
        tileActiveFg: p.night[950],
        tileSolid: p.night[950],
        tileSolidFg: p.gold[400],
        live: p.night["green-400"],
        focusRing: p.gold[400],
        link: p.gold[400],
        linkHover: p.paper,
        destructive: "#f9c3c1",
        btnPrimary: { bg: p.gold[400], fg: p.night[950], border: p.gold[400], hoverBg: "transparent", hoverFg: p.gold[400], hoverBorder: p.gold[400] },
        btnGhost: { bg: "transparent", fg: p.paper, border: "rgba(255, 255, 255, 0.2)", hoverBg: p.gold[400], hoverFg: p.night[950], hoverBorder: p.gold[400] },
      },
    },
  },
  "indigo-silver": {
    scheme: "light",
    statusToAntd: true,
    fontFamily: cairo,
    chrome: p.midnight[950],
    gridHero: p.midnight[800],
    gridDark: p.silver[500],
    chart: {
      series: [p.midnight[700], p.silver[500], p.midnight[400], p.silver[300], p.midnight[500]],
      actual: p.midnight[500],
      target: p.silver[500],
      area: p.midnight[400],
      current: p.midnight[500],
      bg: p.paper,
      grid: p.silver[200],
      axis: p.silver[600],
      up: p.signal.green,
      down: p.signal.red,
      college: p.midnight[900],
      accent: p.midnight[500],
      value: p.midnight[900],
      track: p.silver[100],
      ringTrack: p.silver[100],
      gridSoft: p.silver[200],
      gridFaint: p.silver[200],
      off: p.silver[50],
      offKey: p.silver[100],
      targetLine: p.silver[600],
    },
    status: {
      review: p.midnight[500],
      ok: p.app["green-600"],
      okInk: p.app["green-740"],
      okSoft: p.app["green-80"],
      okBorder: p.app["green-250"],
      okTint: p.app["green-40"],
      doneBorder: p.app["green-270"],
      near: p.app["amber-500"],
      nearInk: p.app["orange-720"],
      nearSoft: p.app["amber-60"],
      nearBorder: p.app["amber-250"],
      nearTint: p.app["amber-40"],
      nearMark: p.app["amber-590"],
      nearBar: p.app["amber-510"],
      late: p.app["red-530"],
      lateInk: p.app["red-600"],
      lateSoft: p.app["red-50"],
      lateBorder: p.app["red-200"],
      lateTint: p.app["red-30"],
      lateBar: p.app["red-470"],
      flagBg: p.app["red-20"],
      vacantBorder: p.app["red-230"],
      gap: p.app["orange-510"],
      plan: p.app["blue-330"],
      none: p.app["blue-190"],
      noneInk: p.app["slate-390"],
      draft: p.app["blue-280"],
      progress: p.app["blue-430"],
      info: p.app["blue-620"],
      share: p.app["blue-500"],
      caution: p.app["amber-505"],
      zeroSoft: p.app["blue-50"],
      zeroInk: p.app["blue-660"],
      exemptSoft: p.app["indigo-40"],
      exemptInk: p.app["purple-560"],
    },
    shadow: { sm: "0 8px 24px rgba(27, 31, 82, 0.08)", md: "0 16px 40px rgba(27, 31, 82, 0.14)", ink: p.app["blue-890"], navy: p.midnight[900], band: p.midnight[950], mark: p.app["grey-990"] },
    focus: "rgba(85, 96, 176, 0.4)",
    selection: { bg: p.silver[300], fg: p.midnight[950] },
    sidebar: indigoSidebar,
    band: indigoBand,
    shade: p.midnight[990],
    accentOnDark: p.silver[300],
    inkFill: p.midnight[900],
    gradient: gradients(indigoBand, indigoSidebar, p.midnight[990], {
      avatar: [p.midnight[700], p.midnight[900]],
      progress: [p.midnight[900], p.midnight[600]],
      callout: [p.midnight[50], p.paper],
      surfaceSubtle: [p.midnight[25], p.paper],
    }),
    hatch: { light: hatch(p.silver[100], p.paper), muted: hatch(p.silver[500], p.silver[300]) },
    onDark: {
      fg: p.paper,
      body: p.silver[200],
      muted: p.silver[300],
      soft: p.silver[300],
      code: p.silver[100],
      ok: p.app["green-290"],
      warn: p.app["amber-350"],
      bad: p.app["red-210"],
      now: p.silver[200],
      nowInk: p.midnight[950],
    },
    auth: {
      muted: p.silver[300],
      body: p.silver[200],
      faint: p.silver[300],
      icon: p.silver[200],
      iconTint: p.silver[300],
      page: p.midnight[50],
      logoShadow: p.midnight[990],
    },
    score: {
      fullSoft: p.app["green-110"],
      fullInk: p.app["green-780"],
      fullMark: p.app["green-470"],
      highSoft: p.app["lime-90"],
      highInk: p.app["lime-750"],
      highMark: p.app["lime-400"],
      highBar: p.app["lime-520"],
      midSoft: p.app["amber-90"],
      midInk: p.app["amber-760"],
      midMark: p.app["amber-370"],
      lowSoft: p.app["red-70"],
      lowInk: p.app["red-640"],
      lowMark: p.app["red-350"],
    },
    tier: {
      "1": p.app["green-700"],
      "2": p.app["green-580"],
      "3": p.app["amber-480"],
      "4": p.app["orange-510"],
      "5": p.app["red-550"],
      ink: p.paper,
    },
    heat: {
      "1": p.app["red-75"],
      "2": p.app["amber-80"],
      "3": p.app["green-115"],
      "4": p.app["green-240"],
      "4Ink": p.app["green-820"],
      none: p.silver[100],
    },
    neutral: {
      mark: p.app["slate-410"],
      axis: p.soft.neutral[600],
      faint: p.app["slate-350"],
      off: p.app["blue-270"],
      na: p.app["slate-250"],
      stopped: p.app["slate-355"],
      lead: p.app["slate-320"],
      base: p.app["slate-470"],
      actual: p.app["slate-590"],
      arrow: p.app["blue-200"],
      dot: p.app["blue-250"],
    },
    unit: {
      university: p.app["blue-750"],
      universityBg: p.app["blue-60"],
      sector: p.app["purple-530"],
      sectorBg: p.app["purple-40"],
      administration: p.app["teal-630"],
      center: p.app["orange-570"],
      college: p.app["blue-550"],
      collegeBg: p.app["blue-45"],
      institute: p.app["blue-505"],
      department: p.app["slate-400"],
    },
    tag: {
      pendingFg: p.app["amber-760"],
      pendingBg: p.app["amber-50"],
      pendingBorder: p.app["amber-220"],
      positiveFg: p.app["lime-750"],
      positiveBg: p.app["lime-40"],
      positiveBorder: p.app["lime-260"],
      negativeFg: p.app["red-560"],
      negativeBg: p.app["red-35"],
      negativeBorder: p.app["red-190"],
      cautionFg: p.app["orange-720"],
      cautionBg: p.app["amber-55"],
      cautionBorder: p.app["orange-220"],
      infoFg: p.app["blue-560"],
      infoBg: p.app["blue-65"],
      infoBorder: p.app["blue-220"],
      specialFg: p.app["purple-610"],
      specialBg: p.app["purple-30"],
      specialBorder: p.app["purple-180"],
      highlightFg: p.ink["teal-700"],
      highlightBg: p.app["teal-50"],
      highlightBorder: p.app["teal-280"],
      categoryFg: p.app["indigo-560"],
      categoryBg: p.app["blue-35"],
      categoryBorder: p.app["blue-160"],
    },
    app: {
      mutedBg: p.silver[50],
      faintBg: p.midnight[25],
      rowHover: p.midnight[25],
      rowSelected: p.midnight[50],
      tableGroup: p.silver[100],
      topbar: p.midnight[50],
      noteBg: p.midnight[50],
      partnerBg: p.silver[50],
      codeBg: p.midnight[990],
      codeFg: p.silver[100],
      printDesk: p.silver[100],
      printInk: p.app["grey-930"],
      pdfIcon: p.app["red-540"],
      pdfFrame: p.app["grey-660"],
    },
    pillar: {
      keep: "100%",
      lift: p.paper,
      tintKeep: "100%",
      tintMix: "0%",
      fillMix: "0%",
    },
    sections: {
      light: {
        bg: p.paper,
        bgCool: p.midnight[50],
        bgWarm: p.silver[100],
        fg: p.midnight[900],
        surface: p.paper,
        surfaceHover: p.midnight[25],
        surfaceHoverSoft: p.midnight[25],
        surfaceWarm: p.silver[50],
        callout: p.silver[50],
        border: p.silver[200],
        lineSoft: p.midnight[900],
        track: p.silver[100],
        accent: p.silver[400],
        accentStrong: p.silver[500],
        accentBorder: "rgba(169, 176, 189, 0.5)",
        heading: p.midnight[900],
        highlight: p.midnight[600],
        highlightDisplay: p.midnight[600],
        body: p.silver[700],
        bodyStrong: p.silver[800],
        label: p.silver[700],
        faint: p.silver[500],
        index: p.silver[300],
        eyebrow: p.midnight[600],
        eyebrowAa: p.midnight[600],
        icon: p.midnight[500],
        tile: p.midnight[100],
        tileActive: p.silver[300],
        tileActiveFg: p.midnight[900],
        tileSolid: p.midnight[900],
        tileSolidFg: p.silver[200],
        live: p.signal.green,
        focusRing: p.midnight[900],
        link: p.midnight[600],
        linkHover: p.midnight[900],
        destructive: p.soft.danger[700],
        btnPrimary: { bg: p.midnight[900], fg: p.paper, border: p.midnight[900], hoverBg: p.midnight[700], hoverFg: p.paper, hoverBorder: p.midnight[700] },
        btnGhost: { bg: "transparent", fg: p.midnight[900], border: "rgba(27, 31, 82, 0.2)", hoverBg: p.midnight[100], hoverFg: p.midnight[900], hoverBorder: p.midnight[400] },
      },
      dark: {
        bg: p.midnight[900],
        bgCool: p.midnight[900],
        bgWarm: p.midnight[900],
        fg: p.paper,
        surface: p.midnight[800],
        surfaceHover: p.midnight[700],
        surfaceHoverSoft: p.midnight[700],
        surfaceWarm: p.midnight[800],
        callout: p.midnight[800],
        border: p.midnight[600],
        lineSoft: p.paper,
        track: p.midnight[700],
        accent: p.silver[300],
        accentStrong: p.silver[200],
        accentBorder: "rgba(200, 205, 215, 0.3)",
        heading: p.paper,
        highlight: p.silver[200],
        highlightDisplay: p.silver[100],
        body: p.silver[300],
        bodyStrong: p.silver[100],
        label: p.silver[100],
        faint: p.silver[400],
        index: "rgba(255, 255, 255, 0.2)",
        eyebrow: p.silver[300],
        eyebrowAa: p.silver[300],
        icon: p.silver[300],
        tile: "rgba(200, 205, 215, 0.12)",
        tileActive: p.silver[300],
        tileActiveFg: p.midnight[900],
        tileSolid: p.midnight[950],
        tileSolidFg: p.silver[200],
        live: p.signal.green,
        focusRing: p.silver[200],
        link: p.silver[200],
        linkHover: p.paper,
        destructive: "#f6ada9",
        btnPrimary: { bg: p.silver[200], fg: p.midnight[900], border: p.silver[200], hoverBg: "transparent", hoverFg: p.silver[200], hoverBorder: p.silver[200] },
        btnGhost: { bg: "transparent", fg: p.paper, border: "rgba(255, 255, 255, 0.2)", hoverBg: p.silver[200], hoverFg: p.midnight[900], hoverBorder: p.silver[200] },
      },
    },
  },
  "graphite-gold": {
    scheme: "light",
    statusToAntd: true,
    fontFamily: cairo,
    chrome: p.graphite[950],
    gridHero: p.graphite[700],
    gridDark: p.graphite[400],
    chart: {
      series: [p.graphite[700], p.gold[400], p.graphite[400], p.graphite[300], p.gold[500]],
      actual: p.gold[500],
      target: p.graphite[400],
      area: p.gold[400],
      current: p.gold[400],
      bg: p.paper,
      grid: p.graphite[200],
      axis: p.graphite[400],
      up: p.signal.green,
      down: p.signal.red,
      college: p.graphite[900],
      accent: p.app["amber-520"],
      value: p.graphite[900],
      track: p.graphite[100],
      ringTrack: p.graphite[100],
      gridSoft: p.graphite[200],
      gridFaint: p.graphite[200],
      off: p.graphite[50],
      offKey: p.graphite[100],
      targetLine: p.app["amber-510"],
    },
    status: {
      review: p.gold[600],
      ok: p.app["green-600"],
      okInk: p.app["green-740"],
      okSoft: p.app["green-80"],
      okBorder: p.app["green-250"],
      okTint: p.app["green-40"],
      doneBorder: p.app["green-270"],
      near: p.app["amber-500"],
      nearInk: p.app["orange-720"],
      nearSoft: p.app["amber-60"],
      nearBorder: p.app["amber-250"],
      nearTint: p.app["amber-40"],
      nearMark: p.app["amber-590"],
      nearBar: p.app["amber-510"],
      late: p.app["red-530"],
      lateInk: p.app["red-600"],
      lateSoft: p.app["red-50"],
      lateBorder: p.app["red-200"],
      lateTint: p.app["red-30"],
      lateBar: p.app["red-470"],
      flagBg: p.app["red-20"],
      vacantBorder: p.app["red-230"],
      gap: p.app["orange-510"],
      plan: p.app["blue-330"],
      none: p.app["blue-190"],
      noneInk: p.app["slate-390"],
      draft: p.app["blue-280"],
      progress: p.app["blue-430"],
      info: p.app["blue-620"],
      share: p.app["blue-500"],
      caution: p.app["amber-505"],
      zeroSoft: p.app["blue-50"],
      zeroInk: p.app["blue-660"],
      exemptSoft: p.app["indigo-40"],
      exemptInk: p.app["purple-560"],
    },
    shadow: { sm: "0 8px 24px rgba(29, 33, 38, 0.08)", md: "0 16px 40px rgba(29, 33, 38, 0.14)", ink: p.graphite[990], navy: p.graphite[900], band: p.graphite[950], mark: p.app["grey-990"] },
    focus: "rgba(185, 148, 77, 0.5)",
    selection: { bg: p.gold[400], fg: p.graphite[950] },
    sidebar: graphiteSidebar,
    band: graphiteBand,
    shade: p.graphite[990],
    accentOnDark: p.gold[400],
    inkFill: p.graphite[900],
    gradient: gradients(graphiteBand, graphiteSidebar, p.graphite[990], {
      avatar: [p.graphite[700], p.graphite[900]],
      progress: [p.graphite[900], p.graphite[600]],
      callout: [p.sand[50], p.ivory[50]],
      surfaceSubtle: [p.graphite[25], p.paper],
    }),
    hatch: { light: hatch(p.graphite[100], p.paper), muted: hatch(p.graphite[400], p.graphite[300]) },
    onDark: {
      fg: p.paper,
      body: p.graphite[200],
      muted: p.graphite[300],
      soft: p.graphite[300],
      code: p.graphite[100],
      ok: p.app["green-290"],
      warn: p.app["amber-350"],
      bad: p.app["red-210"],
      now: p.app["amber-350"],
      nowInk: p.app["amber-870"],
    },
    auth: {
      muted: p.graphite[300],
      body: p.graphite[200],
      faint: p.graphite[300],
      icon: p.app["amber-355"],
      iconTint: p.app["orange-440"],
      page: p.graphite[50],
      logoShadow: p.graphite[990],
    },
    score: {
      fullSoft: p.app["green-110"],
      fullInk: p.app["green-780"],
      fullMark: p.app["green-470"],
      highSoft: p.app["lime-90"],
      highInk: p.app["lime-750"],
      highMark: p.app["lime-400"],
      highBar: p.app["lime-520"],
      midSoft: p.app["amber-90"],
      midInk: p.app["amber-760"],
      midMark: p.app["amber-370"],
      lowSoft: p.app["red-70"],
      lowInk: p.app["red-640"],
      lowMark: p.app["red-350"],
    },
    tier: {
      "1": p.app["green-700"],
      "2": p.app["green-580"],
      "3": p.app["amber-480"],
      "4": p.app["orange-510"],
      "5": p.app["red-550"],
      ink: p.paper,
    },
    heat: {
      "1": p.app["red-75"],
      "2": p.app["amber-80"],
      "3": p.app["green-115"],
      "4": p.app["green-240"],
      "4Ink": p.app["green-820"],
      none: p.graphite[100],
    },
    neutral: {
      mark: p.app["slate-410"],
      axis: p.soft.neutral[600],
      faint: p.app["slate-350"],
      off: p.app["blue-270"],
      na: p.app["slate-250"],
      stopped: p.app["slate-355"],
      lead: p.app["slate-320"],
      base: p.app["slate-470"],
      actual: p.app["slate-590"],
      arrow: p.app["blue-200"],
      dot: p.app["blue-250"],
    },
    unit: {
      university: p.app["blue-750"],
      universityBg: p.app["blue-60"],
      sector: p.app["purple-530"],
      sectorBg: p.app["purple-40"],
      administration: p.app["teal-630"],
      center: p.app["orange-570"],
      college: p.app["blue-550"],
      collegeBg: p.app["blue-45"],
      institute: p.app["blue-505"],
      department: p.app["slate-400"],
    },
    tag: {
      pendingFg: p.app["amber-760"],
      pendingBg: p.app["amber-50"],
      pendingBorder: p.app["amber-220"],
      positiveFg: p.app["lime-750"],
      positiveBg: p.app["lime-40"],
      positiveBorder: p.app["lime-260"],
      negativeFg: p.app["red-560"],
      negativeBg: p.app["red-35"],
      negativeBorder: p.app["red-190"],
      cautionFg: p.app["orange-720"],
      cautionBg: p.app["amber-55"],
      cautionBorder: p.app["orange-220"],
      infoFg: p.app["blue-560"],
      infoBg: p.app["blue-65"],
      infoBorder: p.app["blue-220"],
      specialFg: p.app["purple-610"],
      specialBg: p.app["purple-30"],
      specialBorder: p.app["purple-180"],
      highlightFg: p.ink["teal-700"],
      highlightBg: p.app["teal-50"],
      highlightBorder: p.app["teal-280"],
      categoryFg: p.app["indigo-560"],
      categoryBg: p.app["blue-35"],
      categoryBorder: p.app["blue-160"],
    },
    app: {
      mutedBg: p.graphite[25],
      faintBg: p.graphite[25],
      rowHover: p.graphite[25],
      rowSelected: p.sand[50],
      tableGroup: p.graphite[100],
      topbar: p.graphite[50],
      noteBg: p.sand[50],
      partnerBg: p.app["amber-65"],
      codeBg: p.graphite[990],
      codeFg: p.graphite[100],
      printDesk: p.graphite[100],
      printInk: p.app["grey-930"],
      pdfIcon: p.app["red-540"],
      pdfFrame: p.app["grey-660"],
    },
    pillar: {
      keep: "100%",
      lift: p.paper,
      tintKeep: "100%",
      tintMix: "0%",
      fillMix: "0%",
    },
    sections: {
      light: {
        bg: p.paper,
        bgCool: p.graphite[50],
        bgWarm: p.sand[200],
        fg: p.graphite[900],
        surface: p.paper,
        surfaceHover: p.ivory.DEFAULT,
        surfaceHoverSoft: p.ivory[50],
        surfaceWarm: p.sand[100],
        callout: p.ivory.DEFAULT,
        border: p.graphite[200],
        lineSoft: p.graphite[900],
        track: p.graphite[100],
        accent: p.gold[400],
        accentStrong: p.gold[500],
        accentBorder: "rgba(214, 180, 106, 0.4)",
        heading: p.graphite[900],
        highlight: p.gold[500],
        highlightDisplay: p.gold[500],
        body: p.graphite[500],
        bodyStrong: p.graphite[600],
        label: p.graphite[600],
        faint: p.graphite[400],
        index: p.graphite[300],
        eyebrow: p.gold[600],
        eyebrowAa: p.gold[700],
        icon: p.gold[600],
        tile: p.sand[50],
        tileActive: p.gold[400],
        tileActiveFg: p.graphite[900],
        tileSolid: p.graphite[900],
        tileSolidFg: p.gold[400],
        live: p.signal.green,
        focusRing: p.graphite[900],
        link: p.gold[700],
        linkHover: p.graphite[900],
        destructive: p.soft.danger[700],
        btnPrimary: { bg: p.graphite[900], fg: p.paper, border: p.graphite[900], hoverBg: p.gold[400], hoverFg: p.graphite[900], hoverBorder: p.gold[400] },
        btnGhost: { bg: "transparent", fg: p.graphite[900], border: "rgba(29, 33, 38, 0.2)", hoverBg: p.gold[400], hoverFg: p.graphite[900], hoverBorder: p.gold[400] },
      },
      dark: {
        bg: p.graphite[900],
        bgCool: p.graphite[900],
        bgWarm: p.graphite[900],
        fg: p.paper,
        surface: p.graphite[800],
        surfaceHover: p.graphite[700],
        surfaceHoverSoft: p.graphite[700],
        surfaceWarm: p.graphite[800],
        callout: p.graphite[800],
        border: p.graphite[600],
        lineSoft: p.paper,
        track: p.graphite[700],
        accent: p.gold[400],
        accentStrong: p.gold[400],
        accentBorder: "rgba(214, 180, 106, 0.3)",
        heading: p.paper,
        highlight: p.gold[400],
        highlightDisplay: p.gold[200],
        body: p.graphite[300],
        bodyStrong: p.graphite[200],
        label: p.graphite[200],
        faint: p.graphite[400],
        index: "rgba(255, 255, 255, 0.2)",
        eyebrow: p.gold[400],
        eyebrowAa: p.gold[400],
        icon: p.gold[400],
        tile: "rgba(214, 180, 106, 0.1)",
        tileActive: p.gold[400],
        tileActiveFg: p.graphite[900],
        tileSolid: p.graphite[950],
        tileSolidFg: p.gold[400],
        live: p.signal.green,
        focusRing: p.gold[400],
        link: p.gold[400],
        linkHover: p.paper,
        destructive: "#f6ada9",
        btnPrimary: { bg: p.gold[400], fg: p.graphite[900], border: p.gold[400], hoverBg: "transparent", hoverFg: p.gold[400], hoverBorder: p.gold[400] },
        btnGhost: { bg: "transparent", fg: p.paper, border: "rgba(255, 255, 255, 0.2)", hoverBg: p.gold[400], hoverFg: p.graphite[900], hoverBorder: p.gold[400] },
      },
    },
  },
} as const satisfies Record<ThemeName, ThemeTokens>;

/** The value to put on data-theme. */
export const themeNames = Object.keys(themes) as ThemeName[];

export function isThemeName(value: unknown): value is ThemeName {
  return typeof value === "string" && Object.prototype.hasOwnProperty.call(themes, value);
}

/** A display name in both UI languages (the Appearance panel). */
export interface UiLabel {
  ar: string;
  en: string;
}

/**
 * Palette names in the Appearance panel (gear in the top bar). The panel lists every entry of `themes`, in this order, with
 * a live preview; a new theme needs its name here (the satisfies check fails without one) and nothing in the panel.
 */
export const themeLabels = {
  "navy-gold": { ar: "كحلي وذهبي", en: "Navy & gold" },
  "app-soft": { ar: "كحلي هادئ", en: "Soft navy" },
  "night-gold": { ar: "كحلي ليلي وذهبي (داكن)", en: "Night navy & gold (dark)" },
  "indigo-silver": { ar: "نيلي وفضي", en: "Midnight indigo & silver" },
  "graphite-gold": { ar: "فحمي وذهبي", en: "Graphite & gold" },
} as const satisfies Record<ThemeName, UiLabel>;

// ---------- Corners: a second axis, independent of the theme (corners.css, default CORNERS in config.ts) ----------

/** The value to put on data-corners. */
export type CornerStyle = "square" | "rounded" | "smooth";
export type CornerStep = "xs" | "sm" | "md" | "lg" | "xl" | "pill";

/**
 * --ds-radius-<step> in px, per corner style (corners.css). --ds-radius-round is 50% in every style (real circles)
 * and --ds-radius is an alias of md, so neither is listed here.
 */
export const corners = {
  square: { xs: 0, sm: 0, md: 0, lg: 0, xl: 0, pill: 0 },
  rounded: { xs: 4, sm: 6, md: 8, lg: 12, xl: 16, pill: 999 },
  smooth: { xs: 8, sm: 12, md: 16, lg: 20, xl: 24, pill: 999 },
} as const satisfies Record<CornerStyle, Record<CornerStep, number>>;

export type CornerScale = Readonly<Record<CornerStep, number>>;

export const cornerStyles = Object.keys(corners) as CornerStyle[];

export function isCornerStyle(value: unknown): value is CornerStyle {
  return typeof value === "string" && Object.prototype.hasOwnProperty.call(corners, value);
}

/** Buttons and tags take the pill step in this style (antd Button and Tag in antd-theme.ts); elsewhere they keep md / sm. */
export const pillButtons = { square: false, rounded: false, smooth: true } as const satisfies Record<CornerStyle, boolean>;

/** Corner style names in the Appearance panel, in `corners` order. */
export const cornerLabels = {
  square: { ar: "حاد", en: "Square" },
  rounded: { ar: "مستدير", en: "Rounded" },
  smooth: { ar: "ناعم", en: "Smooth" },
} as const satisfies Record<CornerStyle, UiLabel>;
