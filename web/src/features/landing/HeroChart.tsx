import { useMemo } from "react";

/** Quarterly plan trend: rising actual line (gold) against a flatter target line, drawn once on load. */
const W = 1000;
const H = 100;
const N = 120;

/** y for sample i: rises from ~78 to ~22 with a soft wave on top (smaller y = higher on screen). */
function actualY(i: number) {
  const t = i / (N - 1);
  return 78 - t * 54 + Math.sin(i * 0.3) * 6 + Math.cos(i * 0.71) * 2.5;
}
function targetY(i: number) {
  const t = i / (N - 1);
  return 70 - t * 30 + Math.cos(i * 0.2) * 3;
}

/** End of the actual line as a percentage of the chart height, to place the marker in HTML (the svg is stretched). */
const END_TOP = (actualY(N - 1) / H) * 100;

/**
 * Decorative chart behind the hero. Time runs with the reading direction: the svg is mirrored in RTL
 * (rtl:-scale-x-100), so the line starts on the right and climbs toward the left, and still reads as rising.
 * It carries no text, so the mirror never flips a label.
 */
export function HeroChart() {
  const { actual, target } = useMemo(() => {
    const a: string[] = [];
    const t: string[] = [];
    for (let i = 0; i < N; i++) {
      const x = (i / (N - 1)) * W;
      a.push(`${x.toFixed(1)},${actualY(i).toFixed(1)}`);
      t.push(`${x.toFixed(1)},${targetY(i).toFixed(1)}`);
    }
    return { actual: a.join(" "), target: t.join(" ") };
  }, []);

  return (
    <div aria-hidden="true" className="pointer-events-none absolute inset-0">
      <svg
        className="landing-chart absolute inset-0 h-full w-full rtl:-scale-x-100"
        viewBox={`0 0 ${W} ${H}`}
        preserveAspectRatio="none"
        fill="none"
      >
        <defs>
          <linearGradient id="landingChartArea" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" style={{ stopColor: "var(--ds-chart-area)" }} stopOpacity="0.28" />
            <stop offset="100%" style={{ stopColor: "var(--ds-chart-area)" }} stopOpacity="0" />
          </linearGradient>
        </defs>

        <polygon points={`0,${H} ${actual} ${W},${H}`} fill="url(#landingChartArea)" className="landing-chart-area" />

        <polyline
          points={target}
          pathLength={1}
          className="landing-chart-line stroke-ds-chart-target"
          strokeWidth="1.5"
          strokeDasharray="0.012 0.012"
          strokeLinecap="round"
          strokeLinejoin="round"
          vectorEffect="non-scaling-stroke"
          opacity="0.7"
        />
        <polyline
          points={actual}
          pathLength={1}
          className="landing-chart-line landing-chart-actual stroke-ds-chart-actual"
          strokeWidth="2.5"
          strokeLinecap="round"
          strokeLinejoin="round"
          vectorEffect="non-scaling-stroke"
        />
      </svg>

      <span
        className="landing-chart-dot absolute -translate-y-1/2 rounded-ds-round bg-ds-chart-current"
        style={{ top: `${END_TOP}%`, insetInlineEnd: "1.5%" }}
      />
    </div>
  );
}
