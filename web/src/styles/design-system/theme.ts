/**
 * The site's design switches, on <html> when this module loads (before the first render):
 * - ACTIVE_THEME (below) → <html data-theme>: colours.
 * - CORNERS (config.ts) → <html data-corners>: square or rounded corners, independent of the theme.
 * Every --ds-* token, the legacy aliases in app.css / shared/ui.css and antd portals (modals, drawers, dropdowns,
 * messages under <body>) follow them. useDsTheme() / useDsCorners() feed the antd ConfigProvider (App.tsx) and
 * re-render when the attribute changes, also from devtools.
 */
import { useSyncExternalStore } from "react";
import { CORNERS } from "./config";
import { isCornerStyle, isThemeName, type CornerStyle, type ThemeName } from "./tokens";
import "./index.css";

/** Change this one value to re-theme everything: app, login screens, landing and antd. */
export const ACTIVE_THEME: ThemeName = "navy-gold";

const root = typeof document === "undefined" ? null : document.documentElement;
if (root) {
  root.dataset.theme = ACTIVE_THEME;
  root.dataset.corners = CORNERS;
}

function read(): ThemeName {
  const value = root?.dataset.theme;
  return isThemeName(value) ? value : ACTIVE_THEME;
}

/** Same rule as corners.css: no (or an unknown) data-corners value means square. */
function readCorners(): CornerStyle {
  const value = root?.dataset.corners;
  return isCornerStyle(value) ? value : "square";
}

function watch(attribute: string) {
  return (onChange: () => void) => {
    if (!root) return () => undefined;
    const observer = new MutationObserver(onChange);
    observer.observe(root, { attributes: true, attributeFilter: [attribute] });
    return () => observer.disconnect();
  };
}
const subscribe = watch("data-theme");
const subscribeCorners = watch("data-corners");

/** The theme on <html data-theme> (ACTIVE_THEME unless something changed it). */
export function useDsTheme(): ThemeName {
  return useSyncExternalStore(subscribe, read, () => ACTIVE_THEME);
}

/** Switch the theme at run time (CSS and antd follow). */
export function setDsTheme(name: ThemeName) {
  if (root) root.dataset.theme = name;
}

/** The corner style on <html data-corners> (CORNERS unless something changed it). */
export function useDsCorners(): CornerStyle {
  return useSyncExternalStore(subscribeCorners, readCorners, () => CORNERS);
}

/** Switch the corner style at run time (CSS and antd follow). */
export function setDsCorners(style: CornerStyle) {
  if (root) root.dataset.corners = style;
}
