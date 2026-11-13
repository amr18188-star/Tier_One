import type { ThemeName } from "../../styles/design-system/tokens";
import { AnalyticsSection } from "./AnalyticsSection";
import { CtaSection } from "./CtaSection";
import { DataManagementSection } from "./DataManagementSection";
import { HeroSection } from "./HeroSection";
import { HierarchySection } from "./HierarchySection";
import { IntegrationSection } from "./IntegrationSection";
import { LandingFooter, LandingHeader } from "./LandingChrome";
import { OverviewSection } from "./OverviewSection";
import { PillarsSection } from "./PillarsSection";
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

interface LandingPageProps {
  /** Design-system theme of the whole page (styles/design-system). Switching it re-colours every section. */
  theme?: ThemeName;
}

/**
 * Public landing page for signed-out visitors at "/", ported from the old React app (Layout/Home.jsx).
 * The outer element scopes the page: Tailwind utilities, base reset and body styles in landing.css apply only
 * inside [data-landing], so nothing here leaks into the login screen or the antd pages.
 */
export default function LandingPage({ theme = "navy-gold" }: LandingPageProps) {
  return (
    <div data-landing="" data-ds-page="" data-theme={theme} dir="rtl" lang="ar">
      <div dir="rtl" className="min-h-screen w-full bg-ds-bg text-ds-fg">
        <LandingHeader />

        <main>
          <HeroSection />

          <OverviewSection />

          <PillarsSection />

          <AnalyticsSection />

          <HierarchySection />

          <DataManagementSection />

          <IntegrationSection />

          <CtaSection />
        </main>

        <LandingFooter />
      </div>
    </div>
  );
}
