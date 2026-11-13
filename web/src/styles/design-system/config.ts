/**
 * Site-wide design switches. theme.ts puts them on <html> before the first render.
 *
 * CORNERS: the corner style of the whole site, separate from the colour theme (ACTIVE_THEME in theme.ts), so any
 * theme works with either style. It sets <html data-corners>, and everything follows: antd (ConfigProvider radius
 * tokens), shadcn --radius, Tailwind rounded-ds*, the landing, the scrollbar thumb and the legacy app CSS.
 * Scale: corners.css (CSS) and `corners` in tokens.ts (antd). Real circles (--ds-radius-round) stay round in both.
 * CSS-only override, no TypeScript needed: :root[data-corners="rounded"] { … } or set the attribute in devtools.
 */
import type { CornerStyle } from "./tokens";

/** Change this one line: "square" (navy-gold, sharp corners everywhere) or "rounded" (4–16px corners, pill chips). */
export const CORNERS: CornerStyle = "square";
