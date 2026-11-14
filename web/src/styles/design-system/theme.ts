/**
 * The site's look, on <html> when this module loads (before the first render):
 * - <html data-theme>: colours. <html data-corners>: square, rounded or smooth corners, independent of the theme.
 * - Both come from the viewer's appearance: what they picked in the Appearance panel (gear in the top bar), saved in this
 *   browser under localStorage APPEARANCE_KEY, or the defaults ACTIVE_THEME (below) and CORNERS (config.ts).
 * - A page may pin its own look while mounted: pinAppearance(). The public landing follows the viewer's choice.
 *   The saved choice is untouched and comes back when the page unmounts.
 * Every --ds-* token, the legacy aliases in app.css / shared/ui.css, the scrollbar and antd portals (modals, drawers,
 * dropdowns, messages under <body>) follow the attributes. useDsTheme() / useDsCorners() read the look on <html> and
 * re-render when the attribute changes (also from devtools); AppearanceProvider (appearance.tsx) feeds antd from them.
 * Storage may be blocked (private mode, policies): every access is in try/catch, and the look then lasts for this tab.
 */
import { useSyncExternalStore } from "react";
import { CORNERS } from "./config";
import { isCornerStyle, isThemeName, type CornerStyle, type ThemeName } from "./tokens";
import "./index.css";

/** The default theme (a viewer without a saved choice, and "Restore default" in the Appearance panel). */
export const ACTIVE_THEME: ThemeName = "navy-gold";

/** localStorage key of the viewer's appearance: JSON { "theme": ThemeName, "corners": CornerStyle }. */
export const APPEARANCE_KEY = "t1.appearance";

export interface Appearance {
  theme: ThemeName;
  corners: CornerStyle;
}

/** The documented defaults: ACTIVE_THEME + CORNERS. */
export const DEFAULT_APPEARANCE: Readonly<Appearance> = Object.freeze({ theme: ACTIVE_THEME, corners: CORNERS });

/** A stored value → a valid appearance. Bad JSON, an unknown theme or corner style: that field takes its default. */
export function parseAppearance(raw: string | null | undefined): Appearance {
  let value: unknown = null;
  try {
    value = raw ? JSON.parse(raw) : null;
  } catch {
    value = null;
  }
  const o = value !== null && typeof value === "object" ? (value as Record<string, unknown>) : {};
  return {
    theme: isThemeName(o.theme) ? o.theme : DEFAULT_APPEARANCE.theme,
    corners: isCornerStyle(o.corners) ? o.corners : DEFAULT_APPEARANCE.corners,
  };
}

function load(): Appearance {
  try {
    return parseAppearance(window.localStorage.getItem(APPEARANCE_KEY));
  } catch {
    return { ...DEFAULT_APPEARANCE };
  }
}

/** null removes the key (back to the defaults, and to new defaults if they change later). */
function save(value: Appearance | null) {
  try {
    if (value) window.localStorage.setItem(APPEARANCE_KEY, JSON.stringify(value));
    else window.localStorage.removeItem(APPEARANCE_KEY);
  } catch {
    // Storage blocked: the look stays for this tab only.
  }
}

const root = typeof document === "undefined" ? null : document.documentElement;
let preference: Appearance = load();
const pins: { look: Appearance }[] = [];
const listeners = new Set<() => void>();

function apply() {
  if (!root) return;
  const look = pins.length ? pins[pins.length - 1].look : preference;
  if (root.dataset.theme !== look.theme) root.dataset.theme = look.theme;
  if (root.dataset.corners !== look.corners) root.dataset.corners = look.corners;
}
apply();

function changed() {
  apply();
  listeners.forEach((l) => l());
}

// Another tab changed (or cleared) the setting: follow it.
if (typeof window !== "undefined") {
  window.addEventListener("storage", (e) => {
    if (e.key !== null && e.key !== APPEARANCE_KEY) return;
    preference = load();
    changed();
  });
}

/** The viewer's saved choice (not a page pin). Stable between changes (useSyncExternalStore snapshot). */
export function getAppearance(): Appearance {
  return preference;
}

export function subscribeAppearance(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** Change the theme and/or corner style, save it for this viewer, and apply it now (CSS and antd follow, no reload). */
export function setAppearance(patch: Partial<Appearance>) {
  const next: Appearance = {
    theme: isThemeName(patch.theme) ? patch.theme : preference.theme,
    corners: isCornerStyle(patch.corners) ? patch.corners : preference.corners,
  };
  if (next.theme === preference.theme && next.corners === preference.corners) return;
  preference = next;
  save(next);
  changed();
}

/** Back to ACTIVE_THEME + CORNERS; the saved choice is removed. */
export function resetAppearance() {
  save(null);
  if (preference.theme === DEFAULT_APPEARANCE.theme && preference.corners === DEFAULT_APPEARANCE.corners) return;
  preference = { ...DEFAULT_APPEARANCE };
  changed();
}

/**
 * Show `look` on <html> until the returned function is called (for a page that needs its own look).
 * The latest pin wins; releasing it restores the previous pin or the viewer's choice. Nothing is saved.
 */
export function pinAppearance(look: Appearance): () => void {
  const entry = { look: { theme: look.theme, corners: look.corners } };
  pins.push(entry);
  apply();
  return () => {
    const i = pins.indexOf(entry);
    if (i >= 0) pins.splice(i, 1);
    apply();
  };
}

function read(): ThemeName {
  const value = root?.dataset.theme;
  return isThemeName(value) ? value : ACTIVE_THEME;
}

/** Same rule as corners.css: no (or an unknown) data-corners value means the default style. */
function readCorners(): CornerStyle {
  const value = root?.dataset.corners;
  return isCornerStyle(value) ? value : CORNERS;
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

/** The theme shown now, on <html data-theme> (the viewer's choice, or a page pin). */
export function useDsTheme(): ThemeName {
  return useSyncExternalStore(subscribe, read, () => ACTIVE_THEME);
}

/** Switch the theme at run time and save it for this viewer (CSS and antd follow). Same as setAppearance({ theme }). */
export function setDsTheme(name: ThemeName) {
  setAppearance({ theme: name });
}

/** The corner style shown now, on <html data-corners>. */
export function useDsCorners(): CornerStyle {
  return useSyncExternalStore(subscribeCorners, readCorners, () => CORNERS);
}

/** Switch the corner style at run time and save it for this viewer. Same as setAppearance({ corners }). */
export function setDsCorners(style: CornerStyle) {
  setAppearance({ corners: style });
}
