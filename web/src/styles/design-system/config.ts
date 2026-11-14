/**
 * Site-wide design defaults. theme.ts puts the viewer's appearance on <html> before the first render.
 *
 * CORNERS: the default corner style of the whole site, separate from the colour theme (ACTIVE_THEME in theme.ts), so
 * any theme works with any style. A viewer can pick another one in the Appearance panel (gear in the top bar; saved in
 * localStorage "t1.appearance"); "Restore default" comes back here. It sets <html data-corners>, and everything
 * follows: antd (ConfigProvider radius tokens), shadcn --radius, Tailwind rounded-ds*, the scrollbar thumb and the
 * legacy app CSS. The public landing follows the viewer's choice too.
 * Scale: corners.css (CSS) and `corners` in tokens.ts (antd). Real circles (--ds-radius-round) stay round in all.
 * CSS-only override, no TypeScript needed: :root[data-corners="rounded"] { … } or set the attribute in devtools.
 */
import type { CornerStyle } from "./tokens";

/**
 * The default: "rounded" (4–16px corners, pill chips). Other choices are "square" (sharp corners everywhere)
 * and "smooth" (8–24px corners, pill buttons and chips).
 */
export const CORNERS: CornerStyle = "rounded";
