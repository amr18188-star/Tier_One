import { useMemo } from "react";
import { Link } from "react-router-dom";
import { Icon } from "./Icon";
// White + gold artwork for the navy hero (the footer uses the same file).
import logo from "../../assets/brand/capital-university-logo-dark.png";

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
      className="relative isolate flex h-screen items-center overflow-hidden bg-ds-bg px-5 pb-24 pt-24 text-ds-fg supports-[height:100svh]:h-svh sm:px-10 sm:pt-32 lg:pt-36 short:pb-12 short:pt-24 tiny:pb-6 tiny:pt-16"
    >
      {/* Grid */}
      <div
        className="pointer-events-none absolute inset-0 opacity-40"
        style={{ backgroundImage: "var(--ds-texture-hero)", backgroundSize: "40px 40px" }}
      />

      <HeroChart />

      {/* Overlay */}
      <div className="pointer-events-none absolute inset-0" style={{ background: "var(--ds-hero-vignette)" }} />

      {/* Logo in the top start corner (right in RTL), on the content edge. It ends above the section's top padding
          in every tier (80 < 96, 112 < 128, 128 < 144; short screens 80 < 96; tiny 56 < 64), so it never reaches the title. */}
      <div className="pointer-events-none absolute inset-x-0 top-6 z-10 px-5 sm:top-8 sm:px-10 short:top-6 tiny:top-4">
        <div className="mx-auto max-w-7xl">
          <img src={logo} alt="جامعة العاصمة" className="h-14 w-auto object-contain sm:h-20 lg:h-24 short:h-14 tiny:h-10" />
        </div>
      </div>

      <div className="relative z-10 mx-auto w-full max-w-7xl text-center">
        {/* Title */}
        <h1 className="mx-auto max-w-5xl text-4xl font-bold leading-[1.5] tracking-tight sm:text-5xl md:text-6xl lg:text-7xl short:text-3xl short:sm:text-4xl short:md:text-5xl short:lg:text-6xl tiny:text-2xl tiny:md:text-3xl tiny:lg:text-4xl">
          <span className="block text-ds-heading">
            نحو أداء جامعي
          </span>

          <span className="block text-ds-highlight-display">
            أكثر تميزًا واستدامة
          </span>
        </h1>

        {/* Subtitle */}
        <h2 className="mx-auto mt-5 max-w-3xl text-lg font-semibold leading-8 text-ds-body-strong sm:text-xl short:mt-3 tiny:mt-2 tiny:text-base tiny:leading-7">
          منظومة متكاملة لقياس وتطوير الأداء المؤسسي في جامعة العاصمة
        </h2>

        {/* Description */}
        <p className="mx-auto mt-4 max-w-3xl text-sm leading-8 text-ds-body sm:text-base short:mt-3 short:leading-7 tiny:mt-2 tiny:text-sm tiny:leading-6">
          إطار استراتيجي متكامل لتخطيط وقياس ومتابعة الأداء الجامعي،
          يربط الأهداف المؤسسية بمؤشرات الأداء والمبادرات عبر مختلف
          الركائز والمعايير، ويوفر رؤية موحدة لمتابعة الإنجاز وتعزيز
          كفاءة الأداء، بما يتماشى مع أطر التميز المؤسسي وأهداف
          التنمية المستدامة.
        </p>

        {/* CTA */}
        <div className="mt-8 flex justify-center short:mt-6 tiny:mt-4">
          <Link
            to="/login"
            className="group flex items-center gap-4 rounded-ds-md border border-ds-btn-primary-border bg-ds-btn-primary-bg px-8 py-4 text-sm font-bold text-ds-btn-primary-fg transition-all hover:border-ds-btn-primary-hover-border hover:bg-ds-btn-primary-hover-bg hover:text-ds-btn-primary-hover-fg tiny:py-3"
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
