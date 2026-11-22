import { Tag, Tooltip } from "antd";
import { DownOutlined } from "@ant-design/icons";
import type { ReviewRow } from "../../api/scorecard";
import { Reveal } from "../../shared/AccordionPanel";
import { L } from "../../shared/i18n";
import { aggregation, num } from "../../shared/labels";
import { stdLabel } from "../../shared/pillars";
import { pillarNum } from "../strategy/model";
import { Detail } from "./TransferDetail";
import { PillarGroup, SUMMARY_ORDER, fmt, indicatorCount, levelText, stateOf } from "./reviewParts";

/** A pillar's header and one indicator's row in the review of college indicators. */
/** رأس الركيزة (داخل زر اللوحة، فعناصر سطرية فقط): الرقم 01…06 والاسم، وعدد المؤشرات وحالاتها، والمنقول منها بشريط. */
export function PillarHeader({ group, filtering, loading }: { group: PillarGroup; filtering: boolean; loading?: boolean }) {
  const { index, info, all, shown } = group;
  const total = all.length;
  const moved = all.filter((x) => x.state === "Transferred").length;
  const pct = total > 0 ? Math.round((moved / total) * 100) : 0;
  const name = info?.name ?? (index >= 0 ? L(`الركيزة ${pillarNum(index)}`, `Pillar ${pillarNum(index)}`) : L("معايير بلا ركيزة", "Standards without a pillar"));
  return (
    <>
      <span className="rv-p-num ltr" aria-hidden>{index >= 0 ? pillarNum(index) : "—"}</span>
      <span className="rv-p-main">
        <b className="rv-p-name">{name}</b>
        <span className="rv-p-sum">
          {loading ? null : filtering
            ? <span>{L("المطابقة", "Matching")} <span className="ltr">{shown.length}/{total}</span></span>
            : <span>{indicatorCount(total)}</span>}
          {!filtering && !loading && SUMMARY_ORDER.map((s) => {
            const n = all.filter((x) => x.state === s).length;
            return n === 0 ? null : <span key={s} className="rv-p-st" data-st={s}><i aria-hidden />{L(stateOf(s).ar, stateOf(s).en)} <span className="ltr">{n}</span></span>;
          })}
        </span>
      </span>
      {total > 0 && !loading && (
        <span className="rv-p-state">
          <span className="rv-p-state-label">{L("منقول", "Transferred")} <b className="ltr">{moved}/{total}</b></span>
          <span className="rv-p-bar" aria-hidden><i style={{ width: `${pct}%` }} /></span>
        </span>
      )}
    </>
  );
}

export function IndicatorRow({ row, cycleId, canTransfer, open, onToggle }: { row: ReviewRow; cycleId: string; canTransfer: boolean; open: boolean; onToggle: () => void }) {
  const st = stateOf(row.state);
  // DEC-5: ارتفاع العمود إنجاز الكلية من مستهدفها ولونه حكمها. بلا مستهدف لها: حصتها من رقم الجامعة (للمجموع والعدد) بلون محايد.
  const measure = (u: ReviewRow["units"][number]) => u.achievement ?? (row.comparable ? 0 : u.share ?? 0);
  const max = Math.max(1, ...row.units.map(measure));
  return (
    <article className={`rv-row${open ? " open" : ""}`}>
      <button type="button" className="rv-head" onClick={onToggle} aria-expanded={open} aria-controls={`rv-d-${row.indicatorId}`}>
        <span className="rv-name">
          <code className="pill-code ltr">{row.code}</code>
          <b>{row.name}</b>
          <small className="muted">{stdLabel(row.standardCode)} · {L(levelText[row.level], row.level)} · {aggregation[row.aggregation]}</small>
        </span>
        {/* تقدم الكليات: إنجاز كل كلية من مستهدفها. */}
        <span className="rv-colleges" aria-label={L("تقدم الكليات", "College progress")}>
          {row.units.length === 0 ? <small className="muted">—</small> : row.units.slice(0, 8).map((u) => (
            <Tooltip key={u.unitId} title={`${u.name}: ${fmt(u.value, row.unit)}${u.achievement !== null
              ? ` (${num(u.achievement)}% ${L("من مستهدفها", "of its target")})`
              : !row.comparable && u.share !== null ? ` (${num(u.share)}% ${L("من رقم الجامعة", "of the university total")})` : ""}`}>
              <span className="rv-col">
                <i style={{ height: `${u.value === null ? 4 : Math.max(6, (measure(u) / max) * 36)}px` }}
                  className={u.value === null ? "none" : u.achievement === null ? "share" : measure(u) >= 100 ? "ok" : measure(u) >= 90 ? "near" : "low"} />
              </span>
            </Tooltip>
          ))}
        </span>
        <span className="rv-uni">
          <small>{L("المحسوبة", "Computed")} <span className="ltr">({row.reported}/{row.expected})</span></small>
          <b className="ltr">{fmt(row.computed, row.unit)}</b>
        </span>
        <span className="rv-uni">
          <small>{L("المنقولة للجامعة", "Transferred")}</small>
          <b className="ltr">{fmt(row.official?.value, row.unit)}</b>
        </span>
        <span className="rv-state"><Tag color={st.color}>{L(st.ar, st.en)}</Tag><DownOutlined className="rv-caret" aria-hidden /></span>
      </button>
      <Reveal open={open} id={`rv-d-${row.indicatorId}`}><Detail row={row} cycleId={cycleId} canTransfer={canTransfer} /></Reveal>
    </article>
  );
}
