import { useMemo } from "react";
import { Link } from "react-router-dom";
import { Icon } from "./Icon";
import universityLogo from "../../assets/brand/capital-university-logo-dark.png";
import strategyLogoDark from "../../assets/brand/strategy-performance-dark.png";

/** Two decorative signal lines (rising and falling) behind the hero text. */
function HeroChart() {
  const { greenPoints, redPoints } = useMemo(() => {
    const green: string[] = [];
    const red: string[] = [];

    for (let i = 0; i < 120; i++) {
      const x = (i / 119) * 1000;
      const greenY = 180 + Math.sin(i * 0.18) * 45 + Math.cos(i * 0.43) * 20;
      const redY = 210 + Math.cos(i * 0.16) * 40 + Math.sin(i * 0.38) * 25;

      green.push(`${x},${greenY}`);
      red.push(`${x},${redY}`);
    }

    return { greenPoints: green, redPoints: red };
  }, []);

  return (
    <svg
      className="pointer-events-none absolute inset-0 h-full w-full opacity-60"
      viewBox="0 0 1000 400"
      preserveAspectRatio="none"
      fill="none"
    >
      <defs>
        <linearGradient id="heroGreenGradient" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" style={{ stopColor: "var(--ds-chart-up)" }} stopOpacity="0.15" />
          <stop offset="100%" style={{ stopColor: "var(--ds-chart-up)" }} stopOpacity="0" />
        </linearGradient>

        <linearGradient id="heroRedGradient" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" style={{ stopColor: "var(--ds-chart-down)" }} stopOpacity="0.12" />
          <stop offset="100%" style={{ stopColor: "var(--ds-chart-down)" }} stopOpacity="0" />
        </linearGradient>

        <filter id="heroGreenGlow">
          <feGaussianBlur stdDeviation="3" result="blur" />
          <feMerge>
            <feMergeNode in="blur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>

        <filter id="heroRedGlow">
          <feGaussianBlur stdDeviation="3" result="blur" />
          <feMerge>
            <feMergeNode in="blur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
      </defs>

      <polygon points={`0,400 ${greenPoints.join(" ")} 1000,400`} fill="url(#heroGreenGradient)" />

      <polygon points={`0,400 ${redPoints.join(" ")} 1000,400`} fill="url(#heroRedGradient)" />

      <polyline
        points={greenPoints.join(" ")}
        className="stroke-ds-chart-up"
        strokeWidth="2"
        strokeLinejoin="round"
        strokeLinecap="round"
        filter="url(#heroGreenGlow)"
      />

      <polyline
        points={redPoints.join(" ")}
        className="stroke-ds-chart-down"
        strokeWidth="2"
        strokeLinejoin="round"
        strokeLinecap="round"
        filter="url(#heroRedGlow)"
      />
    </svg>
  );
}

export function HeroSection() {
  return (
    <section
      data-section="dark"
      className="landing-hero relative isolate overflow-hidden px-5 text-ds-fg sm:px-10"
    >
      {/* Grid */}
      <div
        className="pointer-events-none absolute inset-0 opacity-40"
        style={{ backgroundImage: "var(--ds-texture-hero)", backgroundSize: "40px 40px" }}
      />

      <HeroChart />

      <div aria-hidden="true" className="landing-hero-orbit pointer-events-none absolute" />

      {/* Overlay */}
      <div className="pointer-events-none absolute inset-0" style={{ background: "var(--ds-hero-vignette)" }} />

      <div className="landing-hero-content relative z-10 mx-auto w-full max-w-7xl">
        <div className="landing-hero-university">
          <img src={universityLogo} alt="جامعة العاصمة" />
        </div>

        <div className="landing-hero-main">
          <div className="landing-hero-brand">
            <img
              src={strategyLogoDark}
              alt="الخطة الاستراتيجية وتقييم الأداء، جامعة العاصمة"
              width={1536}
              height={1024}
              fetchPriority="high"
            />
          </div>

          <h1 className="landing-hero-title text-ds-heading">
            من الرؤية إلى <span className="text-ds-highlight-display">أثر قابل للقياس</span>
          </h1>

          <p className="landing-hero-description text-ds-body-strong">
            نربط أهداف جامعة العاصمة بمؤشرات الأداء والمبادرات، لنرى التقدم بوضوح ونطوّر أثره باستمرار.
          </p>

          <Link
            to="/login"
            className="landing-hero-cta group flex items-center gap-4 rounded-ds-md border border-ds-btn-primary-border bg-ds-btn-primary-bg px-8 py-3 text-sm font-bold text-ds-btn-primary-fg transition-all hover:border-ds-btn-primary-hover-border hover:bg-ds-btn-primary-hover-bg hover:text-ds-btn-primary-hover-fg"
          >
            <span>الدخول إلى المنظومة</span>
            <Icon name="arrow" size={19} className="transition-transform rtl:group-hover:-translate-x-1 ltr:group-hover:translate-x-1" />
          </Link>
        </div>
      </div>

      {/* Bottom Indicator */}
      <div className="absolute bottom-0 left-0 right-0 h-1 bg-ds-accent/40" />
    </section>
  );
}
