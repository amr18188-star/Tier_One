import { FundOutlined } from "@ant-design/icons";
import type { Scorecard } from "../../api/scorecard";
import { L } from "../../shared/i18n";
import { num } from "../../shared/labels";
import { Panel } from "../../shared/ui";
import { Ring, cellTone, pct } from "./scoreParts";

/** The PSS view: the indicators grouped as in the framework file, by year. */
export const PSS_GROUPS = [
  { ar: "المخرجات والأثر", en: "Outcomes & impact", numbers: [1, 2, 3] },
  { ar: "العمليات التشغيلية", en: "Processes", numbers: [4] },
  { ar: "القدرات والممكّنات", en: "Capabilities & enablers", numbers: [5, 6, 7] },
];

export function PssView({ c }: { c: Scorecard }) {
  return (
    <>
      {PSS_GROUPS.map((g) => (
        <Panel key={g.ar} title={L(g.ar, g.en)} icon={<FundOutlined aria-hidden />}>
          <div className="sc-pss">
            {c.perspectives.filter((p) => g.numbers.includes(p.number)).map((p) => (
              <article key={p.number} className="sc-pss-card">
                <header><span className="sc-pss-n">{p.number}</span><b>{p.nameAr}</b><small className="ltr">{p.nameEn}</small></header>
                <div className="sc-pss-body">
                  <Ring value={p.inLevelPercent ?? 0} color={p.inLevelPercent === null ? "var(--ds-status-none)" : p.inLevelPercent >= 85 ? "var(--ds-status-ok)" : p.inLevelPercent >= 70 ? "var(--ds-status-near)" : "var(--ds-status-late)"}
                    label={L(`${pct(p.inLevelPercent)} في المستوى`, "In level")} />
                  <dl>
                    <div><dt>{L("مؤشرات مرتبطة", "Linked")}</dt><dd>{p.indicators}</dd></div>
                    <div><dt>{L("في المستوى", "On target")}</dt><dd>{p.inLevel}</dd></div>
                    <div><dt>{L("دون المستوى", "Below")}</dt><dd className="bad">{p.below}</dd></div>
                  </dl>
                </div>
                <YearStrip label={L("نسبة المؤشرات المحققة لمستهدف السنة", "Share meeting the yearly target")} values={p.yearPercentVsYearly} />
              </article>
            ))}
          </div>
        </Panel>
      ))}
      <p className="t1-help">{L("المناظير السبعة لبطاقة أداء القطاع العام (Max Moullin). النسبة في الحلقة: المؤشرات التي بلغت أحدث قيمها المستهدف النهائي من كل مؤشرات المنظور.",
        "Seven PSS perspectives (Moullin).")}</p>
    </>
  );
}

export function YearStrip({ label, values }: { label: string; values: (number | null)[] }) {
  return (
    <div className="sc-ystrip" aria-label={label}>
      <small>{label}</small>
      <div>
        {values.map((v, k) => (
          <span key={k} className={cellTone(v)}><b className="ltr">{v === null ? "—" : `${num(v)}%`}</b><small>{L(`س${k + 1}`, `Y${k + 1}`)}</small></span>
        ))}
      </div>
    </div>
  );
}

// ----------------------------------------------------------------- أهداف التنمية المستدامة
