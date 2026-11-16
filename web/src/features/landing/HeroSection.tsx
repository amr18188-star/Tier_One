import { Link } from "react-router-dom";
import { ArrowLeft, ArrowRight, Languages } from "lucide-react";
import { L, setLang, useLang } from "../../shared/i18n";
import universityLogo from "../../assets/brand/capital-university-logo-dark.png";
import tierLogo from "../../assets/brand/sidebarLogo.png";
import strategyLogo from "../../assets/brand/strategy-performance-dark.png";
import { HeroChart } from "./HeroChart";

export function HeroSection() {
  const lang = useLang();
  const isAr = lang === "ar";
  const Arrow = isAr ? ArrowLeft : ArrowRight;

  return (
    <section data-section="dark" className="landing-hero relative isolate flex flex-col overflow-hidden text-ds-fg">
      {/* Grid texture, vignette, gold orbit */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 opacity-40"
        style={{ backgroundImage: "var(--ds-texture-hero)", backgroundSize: "40px 40px" }}
      />
      <div aria-hidden="true" className="pointer-events-none absolute inset-0" style={{ background: "var(--ds-hero-vignette)" }} />
      <div aria-hidden="true" className="landing-hero-orbit pointer-events-none absolute" />

      {/* Chart along the bottom edge */}
      <div className="landing-hero-chart pointer-events-none absolute inset-x-0 bottom-0">
        <HeroChart />
      </div>

      {/* Header: Tier1 logo and the language switch only */}
      <header className="relative z-10 mx-auto flex w-full max-w-7xl items-center justify-between gap-4 px-5 py-4 sm:px-10">
        <img src={tierLogo} alt="Tier1" className="landing-hero-tier h-10 w-auto sm:h-12" />

        <button
          type="button"
          onClick={() => setLang(isAr ? "en" : "ar")}
          lang={isAr ? "en" : "ar"}
          className="inline-flex min-h-11 items-center gap-2 rounded-ds-md border border-ds-btn-ghost-border px-4 text-sm font-semibold text-ds-btn-ghost-fg transition-colors hover:border-ds-btn-ghost-hover-border hover:bg-ds-btn-ghost-hover-bg hover:text-ds-btn-ghost-hover-fg"
        >
          <Languages size={18} aria-hidden="true" />
          <span>{isAr ? "English" : "عربي"}</span>
        </button>
      </header>

      {/* Content */}
      <div className="relative z-10 mx-auto grid w-full max-w-7xl flex-1 items-center gap-8 px-5 py-6 sm:px-10 lg:grid-cols-[1.15fr_.85fr] lg:gap-12">
        <div className="landing-rise flex min-w-0 flex-col items-start">
          <div className="mb-5 flex items-center gap-3">
            <span aria-hidden="true" className="h-0.5 w-10 bg-ds-accent sm:w-14" />
            <span className="text-sm font-bold tracking-wide text-ds-eyebrow sm:text-base">
              {L("جامعة العاصمة · الخطة الاستراتيجية", "Capital University · Strategic Plan")}{" "}
              <span className="inline-block whitespace-nowrap">2027–2031</span>
            </span>
          </div>

          <h1 className="font-ds-heading text-4xl font-bold leading-[1.4] text-ds-heading sm:text-5xl xl:text-6xl">
            {L("من الرؤية إلى", "From vision to")}
            <span className="block text-ds-highlight-display">{L("أثر قابل للقياس", "measurable impact")}</span>
          </h1>

          <p className="mt-5 max-w-xl text-base leading-8 text-ds-body-strong sm:text-lg">
            {L(
              "ستة محاور تقود الخطة، ومؤشرات تُقاس كل ربع سنة، لتصل إلى بطاقة أداء الجامعة وترتيبها.",
              "Six pillars drive the plan. Indicators are measured every quarter, feeding the university scorecard and ranking.",
            )}
          </p>

          <Link
            to="/login"
            className="group mt-7 inline-flex min-h-12 items-center gap-3 rounded-ds-md border border-ds-btn-primary-border bg-ds-btn-primary-bg px-8 py-3 text-base font-bold text-ds-btn-primary-fg transition-colors hover:border-ds-btn-primary-hover-border hover:bg-ds-btn-primary-hover-bg hover:text-ds-btn-primary-hover-fg"
          >
            <span>{L("الدخول إلى المنظومة", "Enter the platform")}</span>
            <Arrow
              size={19}
              aria-hidden="true"
              className="transition-transform motion-reduce:transition-none rtl:group-hover:-translate-x-1 ltr:group-hover:translate-x-1"
            />
          </Link>
        </div>

        {/* Logos */}
        <div className="landing-rise landing-rise-2 flex items-center justify-center gap-5 sm:gap-8 xl:gap-10">
          <img src={universityLogo} alt={L("جامعة العاصمة", "Capital University")} className="landing-hero-logo w-auto shrink-0 object-contain" />
          <span aria-hidden="true" className="landing-hero-divider w-px shrink-0 bg-gradient-to-b from-transparent via-ds-accent to-transparent" />
          <img
            src={strategyLogo}
            alt={L("الخطة الاستراتيجية وتقييم الأداء", "Strategic plan and performance evaluation")}
            width={1536}
            height={1024}
            fetchPriority="high"
            className="landing-hero-logo-wide shrink-0 object-contain"
          />
        </div>
      </div>

      {/* Footer line */}
      <footer className="relative z-10 mx-auto w-full max-w-7xl px-5 pb-3 pt-1 text-center text-xs leading-6 text-ds-body sm:px-10">
        {L(
          `© ${new Date().getFullYear()} جامعة العاصمة · مركز الاتصالات وتكنولوجيا المعلومات`,
          `© ${new Date().getFullYear()} Capital University · Center for Communication and Information Technology`,
        )}
      </footer>
    </section>
  );
}
