import { useMemo } from "react";

/** Actual vs. target over the plan's quarterly cycles (illustrative data, one point per quarter). */
export function PerformanceLineChart() {
  const chart = useMemo(() => {
    const actual = [22, 29, 27, 36, 34, 43, 40, 49, 46, 58, 62, 72];
    const target = [25, 30, 35, 38, 42, 45, 49, 52, 56, 60, 65, 70];

    const width = 700;
    const height = 260;
    const padding = 20;

    const getX = (index: number) => padding + (index / (actual.length - 1)) * (width - padding * 2);

    const getY = (value: number) => height - padding - (value / 100) * (height - padding * 2);

    const actualPoints = actual.map((value, index) => `${getX(index)},${getY(value)}`);

    const targetPoints = target.map((value, index) => `${getX(index)},${getY(value)}`);

    const areaPath = [
      `M ${getX(0)} ${getY(actual[0])}`,
      ...actual.slice(1).map((value, index) => `L ${getX(index + 1)} ${getY(value)}`),
      `L ${getX(actual.length - 1)} ${height - padding}`,
      `L ${getX(0)} ${height - padding}`,
      "Z",
    ].join(" ");

    return {
      actual,
      width,
      actualPoints: actualPoints.join(" "),
      targetPoints: targetPoints.join(" "),
      areaPath,
      getX,
      getY,
    };
  }, []);

  /** Labelled quarters (1, 3 … 11) and the index of their point. */
  const xLabels = [1, 3, 5, 7, 9, 11].map((quarter) => ({ quarter, index: quarter - 1 }));

  return (
    <div className="w-full">
      {/* Legend */}
      <div className="mb-7 flex flex-wrap items-center gap-6">
        <div className="flex items-center gap-2">
          <span className="h-2.5 w-2.5 bg-ds-chart-actual" />
          <span className="text-xs font-medium text-ds-label">الأداء الفعلي</span>
        </div>

        <div className="flex items-center gap-2">
          <span className="h-0.5 w-5 bg-ds-chart-target" />
          <span className="text-xs font-medium text-ds-label">المستهدف</span>
        </div>
      </div>

      {/* Chart */}
      <div className="relative w-full overflow-hidden">
        <svg viewBox="0 0 700 260" className="h-auto w-full overflow-visible" fill="none">
          <defs>
            <linearGradient id="performanceArea" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" style={{ stopColor: "var(--ds-chart-area)" }} stopOpacity="0.3" />
              <stop offset="100%" style={{ stopColor: "var(--ds-chart-area)" }} stopOpacity="0.01" />
            </linearGradient>
          </defs>

          {/* Plot, drawn left to right. In RTL the group is mirrored around the middle of the view box, so the
              first quarter sits on the right and the trend rises toward the left, as Arabic reads. */}
          <g className="origin-center rtl:-scale-x-100">
            {/* Grid */}
            {[0, 25, 50, 75, 100].map((value) => {
              const y = chart.getY(value);

              return (
                <line key={value} x1="20" y1={y} x2="680" y2={y} className="stroke-ds-chart-grid" strokeWidth="1" strokeDasharray="4 6" />
              );
            })}

            {/* Area */}
            <path d={chart.areaPath} fill="url(#performanceArea)" />

            {/* Target Line */}
            <polyline
              points={chart.targetPoints}
              className="stroke-ds-chart-target"
              strokeWidth="2"
              strokeDasharray="6 7"
              strokeLinecap="round"
              strokeLinejoin="round"
            />

            {/* Actual Line */}
            <polyline
              points={chart.actualPoints}
              className="stroke-ds-chart-actual"
              strokeWidth="3"
              strokeLinecap="round"
              strokeLinejoin="round"
            />

            {/* Points */}
            {chart.actual.map((value, index) => (
              <circle
                key={index}
                cx={chart.getX(index)}
                cy={chart.getY(value)}
                r="4"
                className="fill-ds-chart-bg stroke-ds-chart-actual"
                strokeWidth="2.5"
              />
            ))}

            {/* Current Point */}
            <circle
              cx={chart.getX(11)}
              cy={chart.getY(chart.actual[11])}
              r="7"
              className="fill-ds-chart-current stroke-ds-chart-bg"
              strokeWidth="3"
            />
          </g>

          {/* Y axis values, never mirrored: in the start-side gutter (text-anchor start follows the direction). */}
          {[0, 25, 50, 75, 100].map((value) => (
            <text key={value} x="0" y={chart.getY(value) + 4} className="fill-ds-chart-axis rtl:translate-x-[700px]" fontSize="10">
              {value}
            </text>
          ))}
        </svg>
      </div>

      {/* X Axis: each label centred under its point, in the same order as the plot in both directions */}
      <div className="relative mt-3 h-5 text-[10px] text-ds-faint sm:text-xs">
        {xLabels.map(({ quarter, index }) => (
          <span
            key={quarter}
            className="absolute top-0 whitespace-nowrap leading-5 ltr:-translate-x-1/2 rtl:translate-x-1/2"
            style={{ insetInlineStart: `${(chart.getX(index) / chart.width) * 100}%` }}
          >
            الربع {quarter}
          </span>
        ))}
      </div>
    </div>
  );
}

const bars = [
  { label: "الاستراتيجي", value: 82 },
  { label: "المستفيدون", value: 91 },
  { label: "المالي", value: 68 },
  { label: "التشغيلي", value: 76 },
  { label: "الابتكار", value: 88 },
];

/** Score per pillar; the second and fifth bars are highlighted. */
export function PerformanceBarChart() {
  return (
    <div className="flex flex-1 flex-col justify-center gap-6">
      {bars.map((item, index) => (
        <div key={item.label}>
          <div className="mb-2 flex items-center justify-between gap-3">
            <span className="text-xs font-medium text-ds-label">{item.label}</span>

            <span className="text-xs font-bold text-ds-heading">{item.value}%</span>
          </div>

          <div className="h-2 w-full bg-ds-track">
            <div
              className="h-full transition-all duration-700"
              style={{
                width: `${item.value}%`,
                backgroundColor: index === 1 || index === 4 ? "var(--ds-chart-2)" : "var(--ds-chart-1)",
              }}
            />
          </div>
        </div>
      ))}
    </div>
  );
}

const segments = [
  { value: 35, color: "var(--ds-chart-1)" },
  { value: 25, color: "var(--ds-chart-2)" },
  { value: 20, color: "var(--ds-chart-3)" },
  { value: 20, color: "var(--ds-chart-4)" },
];

/** Indicator mix as a ring of four segments around the total. */
export function DonutChart() {
  const circumference = 2 * Math.PI * 75;
  let offset = 0;

  return (
    <div className="relative mx-auto flex h-48 w-48 items-center justify-center">
      <svg viewBox="0 0 200 200" className="h-full w-full -rotate-90">
        {segments.map((segment, index) => {
          const dash = (segment.value / 100) * circumference;
          const currentOffset = offset;

          offset += dash;

          return (
            <circle
              key={index}
              cx="100"
              cy="100"
              r="75"
              fill="none"
              style={{ stroke: segment.color }}
              strokeWidth="22"
              strokeDasharray={`${dash} ${circumference - dash}`}
              strokeDashoffset={-currentOffset}
            />
          );
        })}
      </svg>

      <div className="absolute inset-0 flex flex-col items-center justify-center">
        <span className="text-4xl font-bold text-ds-heading">92</span>

        <span className="mt-1 text-xs text-ds-body">مؤشرًا</span>
      </div>
    </div>
  );
}
