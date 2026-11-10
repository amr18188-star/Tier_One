import type { Results } from "../../api/collection";
import { formatDateTime } from "../../shared/format";
import { L } from "../../shared/i18n";
import { performance as perfText, withUnit } from "../../shared/labels";
import { PrintView } from "../../shared/PrintView";

/**
 * تقرير الدورة للطباعة أو الحفظ PDF من المتصفح: الملخص، ثم كل معيار بمؤشراته (الفعلي والمستهدف والإنجاز والحالة)،
 * ثم خريطة الكليات. كل رقم من القيم المعتمدة نهائيًا وقت الطباعة.
 */
export function ReportView({ r, onClose }: { r: Results; onClose: () => void }) {
  const who = r.unit.kind === "University" ? L("جامعة العاصمة", "Capital University") : r.unit.name;
  const count = (p: string) => r.indicators.filter((i) => i.performance === p).length;
  const standards = [...new Set(r.heat.map((h) => h.standardCode))];
  return (
    <PrintView title={L("تقرير نتائج الدورة", "Cycle results report")} onClose={onClose}>
      <article className="rep">
        <h1>{L(`تقرير مؤشرات Tier 1 — ${who}`, `Tier 1 indicators report — ${who}`)}</h1>
        <div className="rep-meta">
          {r.cycle.name} · {L("الفترة", "Period")} {r.cycle.periodLabel} · {L("مقارنة بمستهدف", "Target year")} {r.cycle.targetYear}
          {r.cycle.trial && ` · ${L("بروفة داخلية — لا تعتمد نتائجها", "Internal rehearsal")}`} · {L("طبع في", "Printed")} {formatDateTime(new Date().toISOString())}
        </div>

        <h2>{L("الملخص", "Summary")}</h2>
        <div className="rep-stats">
          <div><b>{count("Achieved")}</b>{perfText.Achieved.text}</div>
          <div><b>{count("Near")}</b>{perfText.Near.text}</div>
          <div><b>{count("Behind")}</b>{perfText.Behind.text}</div>
          <div><b>{count("NoData") + count("NoTarget")}</b>{L("بلا بيانات أو مستهدف", "No data or target")}</div>
          <div><b className="ltr">{r.formsApproved}/{r.formsTotal}</b>{L("نماذج معتمدة", "Approved forms")}</div>
        </div>
        <p className="t1-help">{L("النتائج من القيم المعتمدة نهائيًا من الجودة المركزية فقط. «بلا بيانات» لا يعني ضعف الأداء.", "Results use final approved values only.")}</p>

        {r.standards.map((s) => (
          <section key={s.code}>
            <h2>{s.code} — {s.name}</h2>
            <table>
              <thead>
                <tr>
                  <th style={{ width: 70 }}>{L("الرمز", "Code")}</th><th>{L("المؤشر", "Indicator")}</th><th>{L("الفعلي", "Actual")}</th><th>{L("المستهدف", "Target")}</th>
                  <th>{L("الإنجاز", "Achv.")}</th><th>{L("السابقة", "Previous")}</th><th>{L("الحالة", "Status")}</th>
                </tr>
              </thead>
              <tbody>
                {r.indicators.filter((i) => i.standardCode === s.code).map((i) => (
                  <tr key={i.indicatorId}>
                    <td className="ltr">{i.code}</td><td>{i.name}</td><td className="ltr">{withUnit(i.actual, i.unit)}</td><td className="ltr">{withUnit(i.target, i.unit)}</td>
                    <td className="ltr">{i.achievement === null ? "—" : `${Math.round(i.achievement * 100)}%`}</td><td className="ltr">{withUnit(i.previous, i.unit)}</td>
                    <td>{perfText[i.performance].text}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        ))}

        {r.heatUnits.length > 1 && (
          <section>
            <h2>{L("الكليات × المعايير (المحقق من المحكوم عليه)", "Colleges × standards")}</h2>
            <table>
              <thead><tr><th>{L("الكلية", "College")}</th>{standards.map((s) => <th key={s} className="ltr">{s}</th>)}</tr></thead>
              <tbody>
                {r.heatUnits.map((u) => (
                  <tr key={u.id}>
                    <td>{u.name}</td>
                    {standards.map((s) => {
                      const c = r.heat.find((h) => h.unitId === u.id && h.standardCode === s);
                      const judged = c ? c.achieved + c.near + c.behind : 0;
                      return <td key={s} className="ltr">{judged ? `${c!.achieved}/${judged}` : "—"}</td>;
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        )}
      </article>
    </PrintView>
  );
}
