import type { Performance } from "../../api/collection";
import { L, useLang } from "../../shared/i18n";
import { num } from "../../shared/labels";

/**
 * رسوم الخطة بلا مكتبة: خط صغير لمسار المستهدف في القوائم، ورسم مسار المستهدف الكامل للمؤشر،
 * وشريط السنوات لمدة مبادرة أو مؤشر. السنوات تتبع اتجاه الصفحة: الأقدم في بداية السطر.
 */

/** خط صغير لمسار المستهدفات: خط الأساس ثم قيم السنوات، والنقطة الأخيرة هدف سنة التحقق. */
export function Sparkline({ values, label }: { values: (number | null)[]; label: string }) {
  const rtl = useLang() === "ar";
  const W = 88, H = 28, P = 4;
  const known = values.map((v, i) => ({ v, i })).filter((p): p is { v: number; i: number } => p.v !== null);
  if (known.length < 2) return <span className="sp-spark-empty" aria-label={label}>—</span>;
  const lo = Math.min(...known.map((p) => p.v));
  const hi = Math.max(...known.map((p) => p.v));
  const x = (i: number) => {
    const f = P + ((W - 2 * P) * i) / Math.max(1, values.length - 1);
    return rtl ? W - f : f;
  };
  const y = (v: number) => (hi === lo ? H / 2 : H - P - ((H - 2 * P) * (v - lo)) / (hi - lo));
  const d = known.map((p, k) => `${k === 0 ? "M" : "L"}${x(p.i).toFixed(1)},${y(p.v).toFixed(1)}`).join(" ");
  const end = known[known.length - 1];
  return (
    <svg className="sp-spark" viewBox={`0 0 ${W} ${H}`} width={W} height={H} role="img" aria-label={label}>
      <path d={d} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      <circle cx={x(known[0].i)} cy={y(known[0].v)} r="2.5" className="sp-spark-start" />
      <circle cx={x(end.i)} cy={y(end.v)} r="3.5" fill="currentColor" />
    </svg>
  );
}

/** شريط سنوات الخطة بالفترة المحددة ملونة: لمدة مبادرة أو أفق مستهدف. */
export function YearBar({ years, start, end, label, showYears = true }: { years: number[]; start: number; end: number; label: string; showYears?: boolean }) {
  return (
    <div className="sp-yearbar" role="img" aria-label={label}>
      <div className="sp-yearbar-track" style={{ gridTemplateColumns: `repeat(${years.length}, minmax(0, 1fr))` }}>
        {years.map((y) => {
          const inside = y >= start && y <= end;
          return <span key={y} className={`${inside ? "on" : ""}${y === start && inside ? " first" : ""}${y === end && inside ? " last" : ""}`} />;
        })}
      </div>
      {showYears && (
        <div className="sp-yearbar-years" style={{ gridTemplateColumns: `repeat(${years.length}, minmax(0, 1fr))` }}>
          {years.map((y) => <small key={y} className={y >= start && y <= end ? "on" : ""}>{y}</small>)}
        </div>
      )}
    </div>
  );
}

export interface ActualPoint { year: number; value: number; performance: Performance; label: string }

/**
 * مسار مستهدف المؤشر: خط الأساس، ثم مستهدف كل سنة من سنة البدء حتى سنة التحقق، ثم استمراره بعدها.
 * السنوات قبل البدء مظللة («لم يبدأ»)، وسنة التحقق مميزة، والقيمة الفعلية لآخر قياس معتمد إن وجدت.
 */
export function TargetChart({ baselineYear, baseline, years, values, start, end, unit, actual }: {
  baselineYear: number; baseline: number | null; years: number[]; values: Record<number, number | null>;
  start: number; end: number; unit: string; actual?: ActualPoint | null;
}) {
  const rtl = useLang() === "ar";
  const all = [baselineYear, ...years];
  const W = 680, H = 260, P = { t: 34, b: 44, s: 40 };
  const pct = unit.trim() === "%";
  const fmt = (v: number) => (pct ? `${num(v)}%` : num(v));

  const active = years.filter((y) => y >= start && y <= end && values[y] !== null && values[y] !== undefined).map((y) => ({ year: y, v: values[y] as number }));
  const lastKnown = active.length > 0 ? active[active.length - 1] : null;
  const after = years.filter((y) => y > end);
  const pool = [...active.map((p) => p.v), ...(baseline !== null ? [baseline] : []), ...(actual ? [actual.value] : [])];

  if (pool.length === 0)
    return <p className="sp-chart-empty">{L("لم تحدد قيم المستهدف بعد؛ أدخلها في الخانات أعلاه فيرسم مسارها هنا.", "No target values yet.")}</p>;

  const lo = Math.min(...pool), hi = Math.max(...pool);
  const pad = (hi - lo) * 0.2 || Math.abs(hi) * 0.2 || 1;
  let yMin = lo - pad, yMax = hi + pad;
  if (lo >= 0 && yMin < 0) yMin = 0;
  if (pct && yMax > 100 && hi <= 100) yMax = 100;
  const step = (W - 2 * P.s) / (all.length - 1);
  const x = (year: number) => {
    const f = P.s + step * all.indexOf(year);
    return rtl ? W - f : f;
  };
  const y = (v: number) => P.t + (H - P.t - P.b) * (1 - (v - yMin) / (yMax - yMin || 1));
  const line = (pts: { year: number; v: number }[]) => pts.map((p, k) => `${k === 0 ? "M" : "L"}${x(p.year).toFixed(1)},${y(p.v).toFixed(1)}`).join(" ");

  // التظليل قبل سنة البدء: من منتصف المسافة بين سنة الأساس والسنة الأولى حتى منتصف ما قبل سنة البدء.
  const offYears = years.filter((yr) => yr < start);
  const half = step / 2;
  const shade = offYears.length > 0
    ? (() => {
      const a = x(offYears[0]) + (rtl ? half : -half);
      const b = x(offYears[offYears.length - 1]) + (rtl ? -half : half);
      return { x: Math.min(a, b), w: Math.abs(b - a) };
    })()
    : null;
  const ticks = [0, 0.5, 1].map((f) => yMin + (yMax - yMin) * f);
  const lead = baseline !== null && active.length > 0 ? [{ year: baselineYear, v: baseline }, active[0]] : [];
  const tail = lastKnown && after.length > 0 ? [lastKnown, { year: after[after.length - 1], v: lastKnown.v }] : [];

  const summary = L(
    `مسار المستهدف: خط الأساس ${baseline === null ? "غير محدد" : fmt(baseline)} في ${baselineYear}، ${active.map((p) => `${p.year}: ${fmt(p.v)}`).join("، ")}.${start > years[0] ? ` يبدأ في ${start}.` : ""} يتحقق في ${end}.${actual ? ` الفعلي في ${actual.year}: ${fmt(actual.value)}.` : ""}`,
    `Target path to ${end}.`);

  return (
    <figure className="sp-chart">
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={summary}>
        {shade && (
          <g className="sp-chart-off">
            <rect x={shade.x} y={P.t - 18} width={shade.w} height={H - P.t - P.b + 18} rx="10" />
            <text x={shade.x + shade.w / 2} y={P.t - 4} textAnchor="middle">{L("لم يبدأ المستهدف", "Not started")}</text>
          </g>
        )}
        {ticks.map((t) => (
          <g key={t} className="sp-chart-grid">
            <line x1={P.s - 14} x2={W - P.s + 14} y1={y(t)} y2={y(t)} />
            <text x={rtl ? W - 6 : 6} y={y(t) + 4} textAnchor={rtl ? "end" : "start"}>{fmt(Math.round(t * 10) / 10)}</text>
          </g>
        ))}
        {all.map((yr) => (
          <text key={yr} x={x(yr)} y={H - 16} textAnchor="middle" className={`sp-chart-year${yr === end ? " goal" : ""}${yr === baselineYear ? " base" : ""}`}>{yr}</text>
        ))}
        <text x={x(baselineYear)} y={H - 2} textAnchor="middle" className="sp-chart-yearnote">{L("الأساس", "Baseline")}</text>
        <text x={x(end)} y={H - 2} textAnchor="middle" className="sp-chart-yearnote goal">{L("سنة التحقق", "Target year")}</text>

        {lead.length > 0 && <path d={line(lead)} className="sp-chart-lead" />}
        {active.length > 1 && <path d={line(active)} className="sp-chart-line" />}
        {tail.length > 0 && <path d={line(tail)} className="sp-chart-tail" />}
        {tail.length > 0 && <text x={x(after[after.length - 1])} y={y(tail[1].v) - 12} textAnchor="middle" className="sp-chart-note">{L("يستمر", "Sustained")}</text>}

        {baseline !== null && (
          <g className="sp-chart-base">
            <circle cx={x(baselineYear)} cy={y(baseline)} r="6" />
            <text x={x(baselineYear)} y={y(baseline) - 12} textAnchor="middle">{fmt(baseline)}</text>
          </g>
        )}
        {active.map((p) => (
          <g key={p.year} className={`sp-chart-pt${p.year === end ? " goal" : ""}`}>
            <circle cx={x(p.year)} cy={y(p.v)} r={p.year === end ? 9 : 6} />
            <text x={x(p.year)} y={y(p.v) - (p.year === end ? 16 : 12)} textAnchor="middle">{fmt(p.v)}</text>
          </g>
        ))}
        {actual && all.includes(actual.year) && (
          <g className={`sp-chart-actual perf-${actual.performance}`}>
            <rect x={x(actual.year) - 7} y={y(actual.value) - 7} width="14" height="14" rx="2" transform={`rotate(45 ${x(actual.year)} ${y(actual.value)})`} />
            <text x={x(actual.year)} y={y(actual.value) + 24} textAnchor="middle">{L(`الفعلي ${fmt(actual.value)}`, `Actual ${fmt(actual.value)}`)}</text>
          </g>
        )}
      </svg>
      <figcaption className="sp-chart-legend">
        <span><i className="k-base" />{L("خط الأساس", "Baseline")}</span>
        <span><i className="k-target" />{L("مستهدف السنة", "Yearly target")}</span>
        <span><i className="k-goal" />{L("مستهدف سنة التحقق", "Final target")}</span>
        {actual && <span><i className="k-actual" />{actual.label}</span>}
        {shade && <span><i className="k-off" />{L("قبل سنة البدء", "Before start")}</span>}
      </figcaption>
    </figure>
  );
}
