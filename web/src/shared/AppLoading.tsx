import { lazy, Suspense } from "react";
import "@fontsource/cairo/600.css";
import { L } from "./i18n";
import "./app-loading.css";

// FE-9: the animation engine (about 300 KB) is not in the entry chunk; a CSS ring of the same size shows until it arrives.
const LoadingAnimation = lazy(() => import("./LoadingAnimation"));

/** Shared page and session fallback. The animation is decorative; the status text announces the wait. */
export function AppLoading({ fullScreen = false }: { fullScreen?: boolean }) {
  return (
    <div className={`app-loading${fullScreen ? " app-loading--screen" : ""}`} role="status" aria-live="polite" aria-atomic="true">
      <Suspense fallback={<span className="app-loading__animation app-loading__ring" aria-hidden="true" />}>
        <LoadingAnimation />
      </Suspense>
      <span className="app-loading__label">{L("جاري التحميل", "Loading…")}</span>
    </div>
  );
}
