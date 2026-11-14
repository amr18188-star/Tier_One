/**
 * Runtime appearance for React: the viewer's { theme, corners }, its setters, and antd fed from the look on <html>.
 * The store itself (localStorage "t1.appearance", <html data-theme / data-corners>, page pins) is in theme.ts.
 *
 *   <AppearanceProvider base={antdBase}>…</AppearanceProvider>     App.tsx: wraps the app once
 *   const { theme, corners, setTheme, setCorners, reset } = useAppearance();   the Appearance panel
 *   usePinnedAppearance(BRAND_APPEARANCE)                           a page with its own fixed look (the landing)
 */
import { createContext, useContext, useLayoutEffect, useMemo, useSyncExternalStore, type ReactNode } from "react";
import { ConfigProvider, type ThemeConfig } from "antd";
import { antdTheme } from "./antd-theme";
import {
  DEFAULT_APPEARANCE, getAppearance, pinAppearance, resetAppearance, setAppearance, subscribeAppearance, useDsCorners, useDsTheme,
  type Appearance,
} from "./theme";
import type { CornerStyle, ThemeName } from "./tokens";

export interface AppearanceValue extends Appearance {
  /** What <html> shows now: the choice above, or a page pin (the landing). antd is built from this. */
  shown: Appearance;
  /** The choice equals ACTIVE_THEME + CORNERS. */
  isDefault: boolean;
  setTheme: (name: ThemeName) => void;
  setCorners: (style: CornerStyle) => void;
  /** Back to ACTIVE_THEME + CORNERS (the saved choice is removed). */
  reset: () => void;
}

const AppearanceContext = createContext<AppearanceValue | null>(null);

const setTheme = (theme: ThemeName) => setAppearance({ theme });
const setCorners = (corners: CornerStyle) => setAppearance({ corners });

/**
 * Holds the viewer's appearance and wraps the children in an antd ConfigProvider whose theme is
 * antdTheme(shown theme, base, { corners }): a theme or corner change re-themes antd, CSS tokens, shadcn and the
 * scrollbar together, with no reload. Nested providers (DarkBlock in shared/ui.tsx) inherit it and follow.
 */
export function AppearanceProvider({ base, children }: { base?: ThemeConfig; children: ReactNode }) {
  const choice = useSyncExternalStore(subscribeAppearance, getAppearance, () => DEFAULT_APPEARANCE);
  const shownTheme = useDsTheme();
  const shownCorners = useDsCorners();
  const value = useMemo<AppearanceValue>(
    () => ({
      theme: choice.theme,
      corners: choice.corners,
      shown: { theme: shownTheme, corners: shownCorners },
      isDefault: choice.theme === DEFAULT_APPEARANCE.theme && choice.corners === DEFAULT_APPEARANCE.corners,
      setTheme,
      setCorners,
      reset: resetAppearance,
    }),
    [choice, shownTheme, shownCorners],
  );
  const antd = useMemo(() => antdTheme(shownTheme, base, { corners: shownCorners }), [shownTheme, shownCorners, base]);
  return (
    <AppearanceContext.Provider value={value}>
      <ConfigProvider theme={antd}>{children}</ConfigProvider>
    </AppearanceContext.Provider>
  );
}

export function useAppearance(): AppearanceValue {
  const value = useContext(AppearanceContext);
  if (!value) throw new Error("useAppearance() needs an <AppearanceProvider> above it (App.tsx).");
  return value;
}

/**
 * While the calling component is mounted, <html> shows `look` instead of the viewer's choice (set before paint,
 * restored on unmount). One theme on every ancestor, so no other theme's section rules reach the page.
 */
export function usePinnedAppearance(look: Appearance) {
  const { theme, corners } = look;
  useLayoutEffect(() => pinAppearance({ theme, corners }), [theme, corners]);
}

/** Renders its children with `look` pinned on <html> (a route element wrapper). */
export function PinnedAppearance({ look, children }: { look: Appearance; children: ReactNode }) {
  usePinnedAppearance(look);
  return <>{children}</>;
}
