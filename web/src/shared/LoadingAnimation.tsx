import { Lottie } from "lottie-react";
import loadingAnimation from "../assets/animations/loading.json";

/** FE-9: the Lottie runtime and artwork load in their own chunk, after the page's first paint. */
export default function LoadingAnimation() {
  // The supplied JSON is artwork only; never evaluate embedded Lottie expressions.
  return <Lottie className="app-loading__animation" src={loadingAnimation} rendererSettings={{ runExpressions: false }} autoplay loop aria-hidden="true" />;
}
