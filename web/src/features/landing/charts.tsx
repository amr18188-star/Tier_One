import { useMemo } from "react";

/** Actual vs. target over the follow-up cycles (illustrative data). */
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
      actualPoints: actualPoints.join(" "),
      targetPoints: targetPoints.join(" "),
      areaPath,
      getX,
      getY,
    };
  }, []);

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

          {/* Grid */}
          {[0, 25, 50, 75, 100].map((value) => {
            const y = chart.getY(value);

            return (
              <g key={value}>
                <line x1="20" y1={y} x2="680" y2={y} className="stroke-ds-chart-grid" strokeWidth="1" strokeDasharray="4 6" />

                <text x="0" y={y + 4} className="fill-ds-chart-axis" fontSize="10">
                  {value}
                </text>
              </g>
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
        </svg>
      </div>

      {/* X Axis */}
      <div className="mt-3 flex justify-between pr-5 text-[10px] text-ds-faint sm:text-xs">
        {["الموسم 01", "الموسم 03", "الموسم 05", "الموسم 07", "الموسم 09", "الموسم 11"].map((label) => (
          <span key={label}>{label}</span>
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
  { label: "القيادة", value: 73 },
];

/** Score per pillar; the second and fifth bars are highlighted. */
export function PerformanceBarChart() {
  return (
    <div className="flex h-full flex-col justify-center gap-6">
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
