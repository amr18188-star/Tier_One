/**
 * Design-system theme + corner style → antd ThemeConfig (ConfigProvider `theme`). Colours and radius only:
 * fonts, sizes and spacing come from the caller's `base` (App.tsx) and stay antd's otherwise.
 *
 * Every value comes from tokens.ts (real colours: antd derives hover, active and tint shades in JS,
 * so it cannot use var()). A new theme re-themes antd as soon as it has its entry in `themes`.
 * Token → antd table: README.md ("antd").
 */
import { theme as antd, type ThemeConfig } from "antd";
import { CORNERS } from "./config";
import { corners as cornerScales, pillButtons, themes, type CornerScale, type CornerStyle, type SectionTone, type ThemeName } from "./tokens";

type Components = NonNullable<ThemeConfig["components"]>;

/**
 * antd global radius tokens from the corner scale (corners.css / `corners` in tokens.ts):
 * borderRadius = md (= --ds-radius), LG = lg, SM = sm, XS = xs, Outer = xs. Square: all 0.
 */
export function antdRadii(c: CornerScale) {
  return { borderRadius: c.md, borderRadiusLG: c.lg, borderRadiusSM: c.sm, borderRadiusXS: c.xs, borderRadiusOuter: c.xs };
}

export interface AntdThemeOptions {
  /** Section scope to read: "light" for app surfaces (default), "dark" for a nested ConfigProvider inside a navy block. */
  tone?: SectionTone;
  /** Corner style (AppearanceProvider passes the one on <html data-corners>, so a live change re-themes antd). Default: CORNERS. */
  corners?: CornerStyle;
}

export function antdTheme(name: ThemeName, base: ThemeConfig = {}, { tone = "light", corners = CORNERS }: AntdThemeOptions = {}): ThemeConfig {
  const t = themes[name];
  const s = t.sections[tone];
  const dark = t.sections.dark;
  const c = cornerScales[corners];
  const r = antdRadii(c);
  // smooth: buttons (every size) and tags are pills. The other styles keep the global radii, as before.
  const pill = pillButtons[corners] ? c.pill : undefined;
  // A dark palette (scheme "dark"): the page scope itself is dark, so antd derives fills, masks and disabled shades for dark.
  const darkPage = t.scheme === "dark";

  // Input, InputNumber, DatePicker and Select share the field border states.
  const field = { hoverBorderColor: s.accentStrong, activeBorderColor: s.focusRing };

  const components: Components = {
    Button: {
      primaryColor: s.btnPrimary.fg,
      defaultColor: s.btnGhost.fg,
      defaultBg: s.surface,
      defaultBorderColor: s.btnGhost.border,
      defaultHoverColor: s.btnGhost.hoverFg,
      defaultHoverBg: s.btnGhost.hoverBg,
      defaultHoverBorderColor: s.btnGhost.hoverBorder,
      defaultActiveColor: s.btnGhost.hoverFg,
      defaultActiveBg: s.btnGhost.hoverBg,
      defaultActiveBorderColor: s.btnGhost.hoverBorder,
      ...(pill !== undefined ? { borderRadius: pill, borderRadiusLG: pill, borderRadiusSM: pill } : {}),
    },
    Input: { ...field, activeShadow: `0 0 0 2px ${t.focus}` },
    InputNumber: { ...field, activeShadow: `0 0 0 2px ${t.focus}` },
    DatePicker: { ...field, activeShadow: `0 0 0 2px ${t.focus}` },
    Select: {
      ...field,
      activeOutlineColor: t.focus,
      optionSelectedBg: s.tile,
      optionSelectedColor: s.heading,
      optionActiveBg: s.surfaceHover,
    },
    // The header is a dark block on a light page: dark-section tokens whatever the tone.
    Table: {
      headerBg: dark.bg,
      headerColor: dark.heading,
      headerSortActiveBg: dark.surface,
      headerSortHoverBg: dark.surfaceHover,
      fixedHeaderSortActiveBg: dark.surface,
      headerFilterHoverBg: dark.surfaceHover,
      headerSplitColor: dark.border,
      headerBorderRadius: c.lg,
      stickyScrollBarBorderRadius: c.pill,
      rowHoverBg: s.surfaceHover,
      bodySortBg: s.bgCool,
      borderColor: s.border,
    },
    Tag: { defaultBg: s.bgCool, defaultColor: s.fg, ...(pill !== undefined ? { borderRadiusSM: pill } : {}) },
    Modal: { contentBg: s.surface, headerBg: s.surface, titleColor: s.heading },
    Drawer: { colorBgElevated: s.surface },
    Tabs: {
      itemColor: s.label,
      itemHoverColor: s.heading,
      itemSelectedColor: s.heading,
      itemActiveColor: s.heading,
      inkBarColor: s.icon, // ≥ 3:1 (a state indicator); s.accent is decorative only
    },
    Segmented: {
      trackBg: s.track,
      itemColor: s.label,
      itemHoverColor: s.heading,
      itemSelectedBg: s.btnPrimary.bg,
      itemSelectedColor: s.btnPrimary.fg,
      // Selection is instant, no thumb glide. After a change antd draws the selection on a "thumb" that slides from
      // the previous option for one motion cycle; this shortens the cycle to a frame or two, and shared/theme.css hides
      // the thumb meanwhile. Not "0s": the cycle ends on transitionend, which a 0s transition never fires (the thumb
      // would stay and no option would show as selected).
      motionDurationSlow: "0.01s",
    },
    // antd's own default is a 100px pill; bars follow the corner style like the app's CSS bars.
    Progress: { lineBorderRadius: c.pill },
    // A dark palette sets colorTextLightSolid to its primary text colour (below); tooltips sit on the navy spotlight and keep it light.
    ...(darkPage ? { Tooltip: { colorTextLightSolid: dark.heading } } : {}),
  };

  return {
    ...base,
    ...(tone === "dark" || darkPage ? { algorithm: antd.darkAlgorithm } : {}),
    token: {
      colorPrimary: s.btnPrimary.bg,
      colorPrimaryBorder: s.focusRing, // antd draws :focus-visible outlines in this colour
      colorLink: s.link,
      colorLinkHover: s.linkHover,
      colorLinkActive: s.linkHover,
      colorInfo: s.heading,
      colorInfoBg: s.bgCool,
      colorInfoBorder: s.border,
      // Success and warning: antd's defaults in navy-gold and app-soft (their pixels stay); a theme with statusToAntd seeds them
      // from its status tokens, so antd's success / warning (Tag, Alert, Badge, Result, Timeline green) match the app's colours.
      // The ink (text) tone of each state: antd writes Tag and Alert text in colorSuccess / colorWarning, so AA needs the ink.
      ...(t.statusToAntd ? { colorSuccess: t.status.okInk, colorWarning: t.status.nearInk } : {}),
      // Text antd puts on solid primary, danger and badge fills: a dark palette has a light primary, so its dark ink.
      // and hints (form extras, "optional", descriptions): antd's dark default (white 45%) is just under 4.5:1 on the cards.
      ...(darkPage ? { colorTextLightSolid: s.btnPrimary.fg, colorTextDescription: s.body, colorTextTertiary: s.body } : {}),
      colorError: s.destructive,
      colorText: s.fg,
      colorTextHeading: s.heading,
      colorTextSecondary: s.label,
      colorBorder: s.border,
      colorBorderSecondary: s.border,
      colorBgLayout: s.bgCool,
      colorBgContainer: s.surface,
      colorBgElevated: s.surface,
      colorBgSpotlight: dark.bg, // tooltips
      ...r,
      ...base.token,
    },
    components: mergeComponents(components, base.components),
  };
}

function mergeComponents(a: Components, b: Components | undefined): Components {
  if (!b) return a;
  const out: Record<string, object> = { ...a };
  for (const [k, v] of Object.entries(b)) out[k] = { ...(out[k] ?? {}), ...(v as object) };
  return out as Components;
}

/**
 * Nested ConfigProvider for a navy block inside a light antd page (page band, home hero): Button colours from the
 * dark-section tokens, so a primary button is gold on navy, not navy on navy. Buttons only, colours only: fields keep
 * the page theme, and radius (corners) comes from the parent provider.
 */
export function antdDarkBlockTheme(name: ThemeName): ThemeConfig {
  const d = themes[name].sections.dark;
  return {
    components: {
      Button: {
        algorithm: true, // derive hover, active and focus-outline shades from these seeds
        colorPrimary: d.btnPrimary.bg,
        primaryColor: d.btnPrimary.fg,
        colorLink: d.link,
        colorLinkHover: d.linkHover,
        colorLinkActive: d.linkHover,
        colorText: d.fg,
        primaryShadow: "none",
      },
    },
  };
}
