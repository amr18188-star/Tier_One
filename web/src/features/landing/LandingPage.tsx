import { useLang } from "../../shared/i18n";
import { HeroSection } from "./HeroSection";
// Cairo, self-hosted (no Google Fonts on the university servers): the five weights and two subsets the page uses.
import "@fontsource/cairo/arabic-300.css";
import "@fontsource/cairo/latin-300.css";
import "@fontsource/cairo/arabic-400.css";
import "@fontsource/cairo/latin-400.css";
import "@fontsource/cairo/arabic-500.css";
import "@fontsource/cairo/latin-500.css";
import "@fontsource/cairo/arabic-600.css";
import "@fontsource/cairo/latin-600.css";
import "@fontsource/cairo/arabic-700.css";
import "@fontsource/cairo/latin-700.css";
import "./landing.css";

/**
 * Public landing for signed-out visitors at "/": a single full-screen hero (Arabic RTL by default, English on toggle).
 * The outer element scopes the page: Tailwind utilities, base reset and body styles in landing.css apply only
 * inside [data-landing], so nothing here leaks into the login screen or the antd pages.
 */
export default function LandingPage() {
  const lang = useLang();
  return (
    <div data-landing="" data-ds-page="" dir={lang === "ar" ? "rtl" : "ltr"} lang={lang}>
      <div className="w-full bg-ds-bg text-ds-fg">
        <main>
          <HeroSection />
        </main>
      </div>
    </div>
  );
}
