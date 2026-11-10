import { useState, type ReactNode } from "react";
import { Button, Table, Tooltip } from "antd";
import { BarChartOutlined, TableOutlined } from "@ant-design/icons";
import { L, useLang } from "./i18n";
import { num } from "./labels";

/**
 * رسوم خفيفة بلا مكتبة: القيم الفعلية خط متصل، والمستهدفات خط متقطع، وخط الأساس نقطة مميزة.
 * كل رسم له ملخص مكتوب وعرض جدولي بديل (متطلب 4-10-1)، والأعمدة تتبع اتجاه الصفحة تلقائيًا.
 */

/** شريط تقدم نحو مستهدف، بعلامة للمستهدف ونص يقرأ دون اللون. */
export function ProgressToTarget({ value, target, max, label, suffix = "" }: { value: number; target?: number; max: number; label?: ReactNode; suffix?: string }) {
  const pct = (v: number) => `${Math.max(0, Math.min(100, (v / (max || 1)) * 100))}%`;
  return (
    <div className="ch-prog">
      {label && <div className="ch-prog-label">{label}</div>}
      <div className="ch-prog-track" role="img" aria-label={`${num(value)}${suffix} ${L("من", "of")} ${num(max)}${suffix}`}>
        <div className="ch-prog-fill" style={{ width: pct(value) }} />
        {target !== undefined && <span className="ch-prog-target" style={{ insetInlineStart: pct(target) }} title={`${L("المستهدف", "Target")} ${num(target)}${suffix}`} />}
      </div>
    </div>
  );
}

export interface TrendPoint { year: number; value: number | null; kind: "baseline" | "target" | "actual" }

/** تطور المؤشر عبر السنوات: خط الأساس ثم المستهدفات (وفيما بعد القيم الفعلية). */
export function TrendChart({ points, unit, title }: { points: TrendPoint[]; unit: string; title: string }) {
  const lang = useLang();
  const [asTable, setAsTable] = useState(false);
  const known = points.filter((p) => p.value !== null) as (TrendPoint & { value: number })[];
  const years = [...new Set(points.map((p) => p.year))].sort((a, b) => a - b);
  const toggle = (
    <Button size="small" type="text" icon={asTable ? <BarChartOutlined /> : <TableOutlined />} onClick={() => setAsTable(!asTable)}>
      {asTable ? L("عرض كرسم", "Show chart") : L("عرض كجدول", "Show table")}
    </Button>
  );
  if (known.length === 0) return <p className="ch-empty">{L("لا توجد قيم لرسمها بعد.", "No values to chart yet.")}</p>;

  if (asTable)
    return (
      <figure className="ch">
        <figcaption className="ch-cap"><b>{title}</b>{toggle}</figcaption>
        <Table size="small" pagination={false} rowKey="year" dataSource={points.map((p) => ({ ...p }))}
          columns={[
            { title: L("السنة", "Year"), dataIndex: "year" },
            { title: L("النوع", "Kind"), dataIndex: "kind", render: (k: TrendPoint["kind"]) => kindLabel(k) },
            { title: `${L("القيمة", "Value")} (${unit})`, dataIndex: "value", render: (v: number | null) => <span className="ltr">{num(v)}</span> },
          ]} />
      </figure>
    );

  const W = 560, H = 220, P = { t: 18, r: 40, b: 30, l: 40 };
  const values = known.map((p) => p.value);
  const lo = Math.min(...values), hi = Math.max(...values);
  const pad = (hi - lo) * 0.15 || Math.abs(hi) * 0.15 || 1;
  const yMin = Math.max(0, lo - pad), yMax = hi + pad;
  const rtl = lang === "ar";
  const xOf = (year: number) => {
    const i = years.indexOf(year);
    const f = years.length === 1 ? 0.5 : i / (years.length - 1);
    return P.l + (rtl ? 1 - f : f) * (W - P.l - P.r);
  };
  const yOf = (v: number) => P.t + (1 - (v - yMin) / (yMax - yMin)) * (H - P.t - P.b);
  const planned = known.filter((p) => p.kind !== "actual").sort((a, b) => a.year - b.year);
  const actual = known.filter((p) => p.kind === "actual").sort((a, b) => a.year - b.year);
  const path = (ps: { year: number; value: number }[]) => ps.map((p, i) => `${i ? "L" : "M"}${xOf(p.year)},${yOf(p.value)}`).join(" ");
  const ticks = [yMin, (yMin + yMax) / 2, yMax];

  return (
    <figure className="ch">
      <figcaption className="ch-cap"><b>{title}</b><span className="ch-unit">{L("الوحدة:", "Unit:")} {unit}</span>{toggle}</figcaption>
      <svg viewBox={`0 0 ${W} ${H}`} className="ch-svg" role="img" aria-label={title}>
        {ticks.map((t) => (
          <g key={t}>
            <line x1={P.l} x2={W - P.r} y1={yOf(t)} y2={yOf(t)} className="ch-grid" />
            <text x={rtl ? W - P.r + 6 : P.l - 6} y={yOf(t) + 4} className="ch-axis" textAnchor={rtl ? "start" : "end"}>{num(Math.round(t * 100) / 100)}</text>
          </g>
        ))}
        {years.map((y) => <text key={y} x={xOf(y)} y={H - 8} className="ch-axis" textAnchor="middle">{y}</text>)}
        {planned.length > 1 && <path d={path(planned)} className="ch-line-target" />}
        {actual.length > 1 && <path d={path(actual)} className="ch-line-actual" />}
        {known.map((p) => (
          <g key={`${p.kind}-${p.year}`}>
            <circle cx={xOf(p.year)} cy={yOf(p.value)} r={p.kind === "baseline" ? 6 : 4.5} className={`ch-dot ch-dot-${p.kind}`}>
              <title>{`${kindLabel(p.kind)} ${p.year}: ${num(p.value)}`}</title>
            </circle>
            <text x={xOf(p.year)} y={yOf(p.value) - 10} className="ch-val" textAnchor="middle">{num(p.value)}</text>
          </g>
        ))}
      </svg>
      <div className="ch-legend">
        <span><i className="ch-key ch-key-baseline" />{L("خط الأساس", "Baseline")}</span>
        <span><i className="ch-key ch-key-target" />{L("المستهدف", "Target")}</span>
        <span><i className="ch-key ch-key-actual" />{L("الفعلي", "Actual")}</span>
      </div>
    </figure>
  );
}

function kindLabel(k: TrendPoint["kind"]) {
  return k === "baseline" ? L("خط الأساس", "Baseline") : k === "target" ? L("مستهدف", "Target") : L("فعلي", "Actual");
}

export interface BarRow { key: string; label: string; value: number | null; note?: string; tone?: "teal" | "gold" | "muted" | "rose" }

/** أعمدة أفقية مرتبة لمقارنة الكليات أو المعايير؛ القيمة الغائبة تظهر «بلا قيمة» ولا ترسم صفرًا. */
export function HBarChart({ rows, unit, title, sort = true, reference }: { rows: BarRow[]; unit: string; title?: string; sort?: boolean; reference?: { value: number; label: string } }) {
  const [asTable, setAsTable] = useState(false);
  const list = sort ? [...rows].sort((a, b) => (b.value ?? -Infinity) - (a.value ?? -Infinity)) : rows;
  const max = Math.max(1, reference?.value ?? 0, ...list.map((r) => r.value ?? 0));
  const pct = (v: number) => `${(v / max) * 100}%`;
  return (
    <figure className="ch">
      {title !== undefined && (
        <figcaption className="ch-cap">
          <b>{title}</b>
          <Button size="small" type="text" icon={asTable ? <BarChartOutlined /> : <TableOutlined />} onClick={() => setAsTable(!asTable)}>
            {asTable ? L("عرض كرسم", "Show chart") : L("عرض كجدول", "Show table")}
          </Button>
        </figcaption>
      )}
      {asTable ? (
        <Table size="small" pagination={false} rowKey="key" dataSource={list}
          columns={[
            { title: L("الجهة", "Unit"), dataIndex: "label" },
            { title: `${L("القيمة", "Value")} (${unit})`, dataIndex: "value", render: (v: number | null) => <span className="ltr">{num(v)}</span> },
            { title: L("ملاحظة", "Note"), dataIndex: "note" },
          ]} />
      ) : (
        <div className="ch-bars">
          {list.map((r) => (
            <div key={r.key} className="ch-bar-row">
              <span className="ch-bar-label" title={r.label}>{r.label}</span>
              <span className="ch-bar-track">
                {r.value !== null && <Tooltip title={r.note}><span className={`ch-bar-fill tone-${r.tone ?? "teal"}`} style={{ width: pct(r.value) }} /></Tooltip>}
                {reference && <span className="ch-bar-ref" style={{ insetInlineStart: pct(reference.value) }} title={reference.label} />}
              </span>
              <span className="ch-bar-value ltr">{r.value === null ? L("بلا قيمة", "none") : num(r.value)}</span>
            </div>
          ))}
          {reference && <div className="ch-ref-note"><i className="ch-key ch-key-ref" /> {reference.label}</div>}
        </div>
      )}
    </figure>
  );
}

export interface Segment { key: string; label: string; count: number; color: string }

/** شريط مكدس لتوزيع الحالات (المسودة والمعتمد...)، بعدد كل فئة ونسبتها مكتوبين. */
export function SegmentBar({ segments, label, compact }: { segments: Segment[]; label: string; compact?: boolean }) {
  const total = segments.reduce((s, x) => s + x.count, 0);
  return (
    <div className="ch-seg">
      <div className="ch-seg-track" role="img" aria-label={`${label}: ${segments.map((s) => `${s.label} ${s.count}`).join("، ")}`}>
        {segments.filter((s) => s.count > 0).map((s) => (
          <Tooltip key={s.key} title={`${s.label}: ${s.count}`}>
            <span style={{ flexGrow: s.count, background: s.color }} />
          </Tooltip>
        ))}
      </div>
      {!compact && <ul className="ch-seg-legend">
        {segments.map((s) => (
          <li key={s.key}><i style={{ background: s.color }} />{s.label} <b className="ltr">{s.count}</b>{total > 0 && <small className="ltr"> ({Math.round((100 * s.count) / total)}%)</small>}</li>
        ))}
      </ul>}
    </div>
  );
}
