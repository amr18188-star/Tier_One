import { Button, Drawer, Tag } from "antd";
import { AimOutlined } from "@ant-design/icons";
import { Link } from "react-router-dom";
import type { IndicatorScore, Scorecard } from "../../api/scorecard";
import { L } from "../../shared/i18n";
import { num } from "../../shared/labels";
import { RAG_TEXT, cellTone, fmt, pct, ragOf } from "./scoreParts";

/** One indicator's scorecard detail, opened from any view. */
export function IndicatorDrawer({ c, i, onClose }: { c: Scorecard; i: IndicatorScore; onClose: () => void }) {
  return (
    <Drawer open width={760} onClose={onClose} title={<span className="t1-code-title"><code className="ltr">{i.code}</code>{i.name}</span>}>
      <dl className="sc-facts">
        <div><dt>{L("أحدث قيمة", "Latest")}</dt><dd className="ltr">{fmt(i.latest, i.unit)}</dd><small>{i.latestIndex >= 0 ? c.quarters[i.latestIndex]?.label : L("لا قيمة معتمدة", "None")}</small></div>
        <div><dt>{L("المستهدف النهائي (السنة 5)", "Final target")}</dt><dd className="ltr">{fmt(i.finalTarget, i.unit)}</dd><small>{i.direction === "LowerIsBetter" ? L("تنازلي: الأقل أفضل", "Lower is better") : L("تصاعدي: الأعلى أفضل", "Higher is better")}</small></div>
        <div><dt>{L("الإنجاز", "Achievement")}</dt><dd className="ltr">{i.latest === null ? "—" : pct(i.score)}</dd><small>{L("الفرق عن المستهدف:", "Gap:")} <span className="ltr">{i.gap === null ? "—" : num(i.gap)}</span></small></div>
        <div><dt>{L("نوع المؤشر", "Type")}</dt><dd className="small">{i.accumulation === "Cumulative" ? L("تراكمي", "Cumulative") : L("غير تراكمي (لحظي)", "Snapshot")}</dd>
          <small>{i.accumulation === "Cumulative" ? L("قيمة السنة آخر ربع فيها", "Year = last quarter") : L("قيمة السنة متوسط أرباعها", "Year = average of quarters")}</small></div>
      </dl>

      <h3 className="sc-h3">{L("القيم الربعية ونسبة إنجازها من المستهدف النهائي", "Quarterly values")}</h3>
      <div className="sc-qgrid" role="table">
        <div className="sc-qrow head" role="row"><span role="columnheader">{L("السنة", "Year")}</span>{[1, 2, 3, 4].map((q) => <span key={q} role="columnheader">{L(`الربع ${q}`, `Q${q}`)}</span>)}
          <span role="columnheader">{L("قيمة السنة", "Year value")}</span><span role="columnheader">{L("مستهدف السنة", "Year target")}</span><span role="columnheader">{L("الإنجاز", "Achieved")}</span></div>
        {c.years.map((y, k) => (
          <div key={y} className="sc-qrow" role="row">
            <span role="cell">{L(`السنة ${k + 1}`, `Year ${k + 1}`)} <small className="muted ltr">{y}</small></span>
            {[0, 1, 2, 3].map((q) => {
              const idx = k * 4 + q;
              return <span key={q} role="cell" className={`ltr ${cellTone(i.quarterScores[idx])}`}>{fmt(i.quarters[idx], i.unit)}</span>;
            })}
            <span role="cell" className="ltr"><b>{fmt(i.yearValues[k], i.unit)}</b></span>
            <span role="cell" className="ltr">{fmt(i.yearTargets[k], i.unit)}</span>
            <span role="cell" className={`ltr ${cellTone(i.yearScoresVsYearly[k])}`}>{pct(i.yearScoresVsYearly[k])}</span>
          </div>
        ))}
      </div>
      <p className="t1-help">{L("ألوان الخلايا كما في الملف: أخضر 100% فأكثر من المستهدف، وأخضر فاتح 90%، وأصفر 70%، وأحمر دونها.", "Colours as in the workbook.")}</p>
      <Link to={`/indicators/${i.code}`}><Button icon={<AimOutlined />}>{L("بطاقة المؤشر وتعريفه", "Indicator card")}</Button></Link>
      {i.score >= 0 && <span className="sc-rag-note">{L("حالة الأداء:", "Status:")} <Tag color={ragOf(i.score) === "Strong" ? "green" : ragOf(i.score) === "Developing" ? "gold" : "red"}>{L(RAG_TEXT[ragOf(i.score)].ar, RAG_TEXT[ragOf(i.score)].en)}</Tag></span>}
    </Drawer>
  );
}
