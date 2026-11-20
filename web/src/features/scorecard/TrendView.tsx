import { useState } from "react";
import { LineChartOutlined } from "@ant-design/icons";
import type { Scorecard } from "../../api/scorecard";
import { L } from "../../shared/i18n";
import { num } from "../../shared/labels";
import { tint } from "../../shared/pillars";
import { EmptyNote, Panel } from "../../shared/ui";
import { cellTone } from "./scoreParts";

/** The trend view: each pillar's score over the plan years. */
export function TrendView({ c }: { c: Scorecard }) {
  const [mode, setMode] = useState<"yearly" | "final">("yearly");
  const rows = c.pillars.map((p, index) => ({ p, index, values: mode === "yearly" ? p.yearScoresVsYearly : p.yearScores }));
  const overall = mode === "yearly" ? c.overall.yearScoresVsYearly : c.overall.yearScores;
  const last = (v: number[]) => { for (let k = v.length - 1; k >= 0; k--) if (v[k] !== 0) return k; return -1; };
  // آخر سنة وصلتها القيم المعتمدة: ما بعدها لم يأت بعد، فلا يرسم صفرًا ولا يعرض كأنه تراجع.
  const reached = c.lastReported ? c.lastReported.yearIndex - 1 : -1;
  return (
    <>
      <Panel title={L("ملخص أداء الركائز عبر السنوات", "Pillar performance over the years")} icon={<LineChartOutlined aria-hidden />}
        extra={(
          <span className="sc-mode" role="radiogroup" aria-label={L("المقارنة", "Compared with")}>
            <button type="button" role="radio" aria-checked={mode === "yearly"} className={mode === "yearly" ? "on" : ""} onClick={() => setMode("yearly")}>{L("مقابل مستهدف كل سنة", "vs yearly target")}</button>
            <button type="button" role="radio" aria-checked={mode === "final"} className={mode === "final" ? "on" : ""} onClick={() => setMode("final")}>{L("مقابل المستهدف النهائي", "vs final target")}</button>
          </span>
        )}>
        {reached < 0 ? <EmptyNote>{L("لا قيم معتمدة بعد لرسم الاتجاه.", "No approved values yet.")}</EmptyNote> : (
          <TrendChart series={rows.map((r) => ({ key: r.p.id, name: r.p.name, index: r.index, values: r.values.slice(0, reached + 1) }))}
            overall={overall.slice(0, reached + 1)} years={c.years} />
        )}
        <div className="sc-trend" role="table">
          <div className="sc-trow head" role="row">
            <span role="columnheader">{L("الركيزة", "Pillar")}</span>
            {c.years.map((y, k) => <span key={y} role="columnheader">{L(`السنة ${k + 1}`, `Year ${k + 1}`)}<small className="ltr">{y}</small></span>)}
            <span role="columnheader">{L("التغير السنوي", "Annual change")}</span>
          </div>
          {[...rows, { p: null, index: -1, values: overall }].map((r) => {
            const k = last(r.values);
            const change = k > 0 ? Math.round((r.values[k] - r.values[k - 1]) * 10) / 10 : null;
            return (
              <div key={r.p?.id ?? "all"} className={`sc-trow${r.p ? "" : " total"}`} role="row" style={r.p ? tint(r.index) : undefined}>
                <span role="cell">{r.p ? r.p.name : L("الدرجة الكلية", "Overall")}</span>
                {r.values.map((v, j) => j > reached
                  ? <span key={j} role="cell" className="ltr zero" title={L("لم تصلها البيانات بعد", "Not reached yet")}>—</span>
                  : <span key={j} role="cell" className={`ltr ${v === 0 ? "zero" : cellTone(v)}`}>{num(v)}</span>)}
                <span role="cell" className="ltr">
                  {change === null ? <span className="muted">{L("بيانات غير كافية", "Insufficient data")}</span>
                    : change > 0 ? <span className="up">▲ {num(change)}</span> : change < 0 ? <span className="down">▼ {num(Math.abs(change))}</span> : L("مستقر", "Stable")}
                </span>
              </div>
            );
          })}
        </div>
        <p className="t1-help">{L("قيمة السنة للمؤشر التراكمي آخر ربع فيها، وللحظي متوسط أرباعها. «مقابل المستهدف النهائي» يحسب المؤشر بلا قيمة صفرًا كما في الملف؛ «مقابل مستهدف كل سنة» يستبعده.",
          "Yearly value: last quarter (cumulative) or average (snapshot).")}</p>
      </Panel>
    </>
  );
}

export function TrendChart({ series, overall, years }: { series: { key: string; name: string; index: number; values: number[] }[]; overall: number[]; years: number[] }) {
  const W = 720, H = 240, P = { l: 36, r: 40, t: 14, b: 30 };
  const x = (k: number) => P.l + ((W - P.l - P.r) * k) / (years.length - 1);
  const dots = (vals: number[]) => vals.length === 1;
  const y = (v: number) => P.t + (H - P.t - P.b) * (1 - v / 100);
  const line = (vals: number[]) => vals.map((v, k) => `${k === 0 ? "M" : "L"}${x(k).toFixed(1)},${y(v).toFixed(1)}`).join(" ");
  return (
    <figure className="sc-chart ltr">
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={L("الدرجة الكلية ودرجات الركائز عبر السنوات", "Scores over the years")}>
        {[0, 25, 50, 75, 100].map((v) => (
          <g key={v}><line x1={P.l} x2={W - P.r} y1={y(v)} y2={y(v)} className="grid" /><text x={P.l - 6} y={y(v) + 4} textAnchor="end" className="tick">{v}</text></g>
        ))}
        {years.map((yr, k) => <text key={yr} x={x(k)} y={H - 8} textAnchor="middle" className="tick">{L(`س${k + 1}`, `Y${k + 1}`)} · {yr}</text>)}
        {series.map((s) => dots(s.values)
          ? <circle key={s.key} cx={x(0)} cy={y(s.values[0])} r="4" style={tint(s.index)} className="pillar-pt" />
          : <path key={s.key} d={line(s.values)} style={tint(s.index)} className="pillar" />)}
        <path d={line(overall)} className="overall" />
        {overall.map((v, k) => <circle key={k} cx={x(k)} cy={y(v)} r="5" className="overall-pt" />)}
      </svg>
      <figcaption>
        <span><i className="k-overall" />{L("الدرجة الكلية", "Overall")}</span>
        {series.map((s) => <span key={s.key} style={tint(s.index)}><i className="k-pillar" />{s.name}</span>)}
      </figcaption>
    </figure>
  );
}

// ----------------------------------------------------------------- المؤشر
