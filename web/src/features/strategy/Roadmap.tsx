import { useState, type CSSProperties } from "react";
import { Segmented, Select } from "antd";
import { AimOutlined, FlagFilled, RocketOutlined } from "@ant-design/icons";
import type { IndicatorNode, InitiativeNode, StandardNode } from "../../api/tier1";
import { L } from "../../shared/i18n";
import { stdLabel } from "../../shared/pillars";
import { horizon, initiativeStatus, num } from "../../shared/labels";
import { EmptyNote, FilterMeta } from "../../shared/ui";
import { horizonKind, span, targetOf, tint, usePlan, type HorizonKind } from "./model";

/**
 * الخط الزمني للخطة: صف لكل مبادرة بمدتها، وصف لكل مؤشر بمستهدف كل سنة من سنة البدء حتى سنة التحقق.
 * يجيب مباشرة عن: ما الذي يتحقق بعد سنتين أو ثلاث، وما يمتد خمس سنوات، وما يبدأ متأخرًا.
 */

type Kind = "all" | "initiatives" | "indicators";
type Horizon = "all" | HorizonKind | "late";

export function Roadmap() {
  const { tree } = usePlan();
  const years = tree.years;
  const first = years[0];
  const [kind, setKind] = useState<Kind>("all");
  const [pillar, setPillar] = useState<string>("all");
  const [hz, setHz] = useState<Horizon>("all");

  const indicatorOk = (i: IndicatorNode) =>
    kind !== "initiatives" && (hz === "all" || (hz === "late" ? (i.planStartYear ?? first) > first : horizonKind(i, years) === hz));
  const initiativeOk = (m: InitiativeNode) => {
    if (kind === "indicators") return false;
    if (hz === "all") return true;
    if (hz === "late") return m.startYear > first;
    const n = m.endYear - m.startYear + 1;
    return hz === "short" ? n <= 2 : hz === "medium" ? n === 3 : n > 3;
  };

  const groups = [
    ...tree.pillars.map((p, index) => ({ id: p.id, code: p.code, name: p.nameAr, index, standards: p.standards })),
    ...(tree.unassigned.length > 0 ? [{ id: "none", code: "—", name: L("معايير بلا ركيزة", "No pillar"), index: -1, standards: tree.unassigned }] : []),
  ].filter((g) => pillar === "all" || g.id === pillar);

  const rows = groups.map((g) => ({
    ...g,
    standards: g.standards
      .map((s) => ({ s, initiatives: s.initiatives.filter(initiativeOk), indicators: s.indicators.filter(indicatorOk) }))
      .filter((x) => x.initiatives.length + x.indicators.length > 0),
  })).filter((g) => g.standards.length > 0);

  const shown = rows.reduce((n, g) => n + g.standards.reduce((k, x) => k + x.initiatives.length + x.indicators.length, 0), 0);
  const total = tree.pillars.flatMap((p) => p.standards).concat(tree.unassigned).reduce((n, s) => n + s.initiatives.length + s.indicators.length, 0);
  const active = kind !== "all" || pillar !== "all" || hz !== "all";
  const cols = { "--cols": `var(--label-col, minmax(240px, 2.2fr)) repeat(${years.length + 1}, minmax(66px, 1fr))` } as CSSProperties;

  return (
    <section className="lcp-panel sp-road" aria-label={L("الخط الزمني للخطة", "Plan roadmap")}>
      <div className="sp-road-bar">
        <Segmented<Kind> value={kind} onChange={setKind} options={[
          { value: "all", label: L("الكل", "All") },
          { value: "indicators", label: L("المؤشرات", "Indicators") },
          { value: "initiatives", label: L("المبادرات", "Initiatives") },
        ]} />
        <Segmented<Horizon> value={hz} onChange={setHz} options={[
          { value: "all", label: L("كل المدد", "Any span") },
          { value: "short", label: L("قصير (سنتان أو أقل)", "Short") },
          { value: "medium", label: L("متوسط (3 سنوات)", "Medium") },
          { value: "long", label: L("طويل (4–5 سنوات)", "Long") },
          { value: "late", label: L("يبدأ متأخرًا", "Starts later") },
        ]} />
        <Select value={pillar} onChange={setPillar} style={{ minWidth: 220 }} aria-label={L("الركيزة", "Pillar")}
          options={[{ value: "all", label: L("كل الركائز", "All pillars") }, ...tree.pillars.map((p) => ({ value: p.id, label: `${p.code} — ${p.nameAr}` }))]} />
        <FilterMeta shown={shown} total={total} noun={L("عنصرًا", "items")} active={active} onReset={() => { setKind("all"); setPillar("all"); setHz("all"); }} />
      </div>

      {rows.length === 0 ? <EmptyNote>{L("لا عناصر بهذه المرشحات.", "Nothing matches these filters.")}</EmptyNote> : (
        <div className="sp-road-scroll">
          <div className="sp-road-grid" style={cols} role="table" aria-label={L("الخط الزمني: المبادرات والمستهدفات بالسنوات", "Initiatives and targets by year")}>
            <div className="sp-road-row head" role="row">
              <span role="columnheader">{L("العنصر", "Item")}</span>
              <span role="columnheader" className="ltr-nums base">{tree.baselineYear}<small>{L("الأساس", "Baseline")}</small></span>
              {years.map((y) => <span key={y} role="columnheader" className="ltr-nums">{y}</span>)}
            </div>
            {rows.map((g) => (
              <div key={g.id} role="rowgroup" style={tint(g.index)} className="sp-road-group">
                <div className="sp-road-row pillar" role="row">
                  <span role="cell"><code>{g.code}</code><b>{g.name}</b></span>
                </div>
                {g.standards.map((x) => (
                  <StandardRows key={x.s.id} standard={x.s} initiatives={x.initiatives} indicators={x.indicators} />
                ))}
              </div>
            ))}
          </div>
        </div>
      )}
      <p className="t1-help sp-road-note">
        <FlagFilled aria-hidden /> {L("سنة التحقق، ويستمر مستهدفها بعدها. القيم الرمادية في سنة الأساس، والشرطة قبل سنة البدء تعني أن المؤشر لا يحكم عليه بعد.",
          "Flag = target year. Grey = baseline. A dash before the start year means the indicator is not judged yet.")}
      </p>
    </section>
  );
}

function StandardRows({ standard: s, initiatives, indicators }: { standard: StandardNode; initiatives: InitiativeNode[]; indicators: IndicatorNode[] }) {
  const { tree, select } = usePlan();
  const years = tree.years;
  const first = years[0];
  const last = years[years.length - 1];
  const col = (y: number) => 3 + years.indexOf(Math.min(last, Math.max(first, y)));
  const pct = (i: IndicatorNode) => i.unit.trim() === "%";
  const val = (i: IndicatorNode, v: number | null) => (v === null ? "—" : pct(i) ? `${num(v)}%` : num(v));

  return (
    <>
      <div className="sp-road-row std" role="row">
        <span role="cell"><button type="button" className="t1-link" onClick={() => select(`S:${s.id}`)}><code>{stdLabel(s.code)}</code> {s.nameAr}</button></span>
      </div>
      {initiatives.map((m) => (
        <div key={m.id} className={`sp-road-row item init st-${m.status}`} role="row" onClick={() => select(`M:${m.id}`)}>
          <span role="cell" className="sp-road-label">
            <RocketOutlined aria-hidden className="sp-road-ico" />
            <button type="button" onClick={(e) => { e.stopPropagation(); select(`M:${m.id}`); }}><code>{m.code}</code> {m.nameAr}</button>
          </span>
          <span className="sp-road-bg" style={{ gridColumn: `2 / span ${years.length + 1}` }} aria-hidden />
          <span role="cell" className="sp-road-initbar" style={{ gridColumn: `${col(m.startYear)} / ${col(m.endYear) + 1}` }}>
            <b>{initiativeStatus[m.status].text}</b><small className="ltr-nums">{m.startYear}–{m.endYear}</small>
          </span>
        </div>
      ))}
      {indicators.map((i) => {
        const { start, end } = span(i, years);
        let carried: number | null = null;
        return (
          <div key={i.code} className="sp-road-row item ind" role="row" onClick={() => select(`I:${i.code}`)}>
            <span role="cell" className="sp-road-label" title={horizon(i.planStartYear, i.planEndYear, first, last)}>
              <AimOutlined aria-hidden className="sp-road-ico" />
              <button type="button" onClick={(e) => { e.stopPropagation(); select(`I:${i.code}`); }}><code>{i.code}</code> {i.nameAr} <small className="muted">({i.unit})</small></button>
            </span>
            <span className="sp-road-span" style={{ gridColumn: `${col(start)} / ${col(end) + 1}` }} aria-hidden />
            <span role="cell" className="sp-road-cell base ltr-nums" style={{ gridColumn: 2 }}>{val(i, i.baseline)}</span>
            {years.map((y) => {
              const v = targetOf(i, y);
              if (y >= start && v !== null) carried = v;
              const at = { gridColumn: col(y) };
              if (y < start) return <span key={y} role="cell" style={at} className="sp-road-cell off" aria-label={L(`${y}: لم يبدأ`, `${y}: not started`)}>—</span>;
              if (y > end)
                return v !== null
                  ? <span key={y} role="cell" style={at} className="sp-road-cell ltr-nums">{val(i, v)}</span>
                  : <span key={y} role="cell" style={at} className="sp-road-cell after ltr-nums" title={L("يستمر آخر مستهدف", "Latest target carries on")}>{val(i, carried)}</span>;
              return (
                <span key={y} role="cell" style={at} className={`sp-road-cell ltr-nums${y === end ? " goal" : ""}${v === null ? " missing" : ""}`}>
                  {y === end && <FlagFilled aria-label={L("سنة التحقق", "Target year")} />}{v === null ? L("ناقص", "missing") : val(i, v)}
                </span>
              );
            })}
          </div>
        );
      })}
    </>
  );
}
