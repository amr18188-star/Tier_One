import { Lottie } from "lottie-react";
import "@fontsource/cairo/600.css";
import loadingAnimation from "../assets/animations/loading.json";
import { L } from "./i18n";
import "./app-loading.css";

/** Shared page and session fallback. The animation is decorative; the status text announces the wait. */
export function AppLoading({ fullScreen = false }: { fullScreen?: boolean }) {
  return (
    <div className={`app-loading${fullScreen ? " app-loading--screen" : ""}`} role="status" aria-live="polite" aria-atomic="true">
      {/* The supplied JSON is artwork only; never evaluate embedded Lottie expressions. */}
      <Lottie className="app-loading__animation" src={loadingAnimation} rendererSettings={{ runExpressions: false }} autoplay loop aria-hidden="true" />
      <span className="app-loading__label">{L("جاري التحميل", "Loading…")}</span>
    </div>
  );
}
