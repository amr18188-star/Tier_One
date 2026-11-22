import { useEffect, useId, useState, type CSSProperties } from "react";
import { Segmented } from "antd";
import { AimOutlined, DownOutlined, FlagFilled, RocketOutlined } from "@ant-design/icons";
import { useSearchParams } from "react-router-dom";
import type { IndicatorNode, InitiativeNode, StandardNode } from "../../api/tier1";
import { L } from "../../shared/i18n";
import { UniTarget } from "../../shared/CollegeTarget";
import { stdLabel } from "../../shared/pillars";
import { horizon, initiativeStatus, num } from "../../shared/labels";
import { EmptyNote, FilterMeta } from "../../shared/ui";
import { span, targetOf, tint, usePlan } from "./model";
import { PillarCarousel, type CarouselSlide } from "./PillarCarousel";

/**
 * الخط الزمني للخطة، ركيزة واحدة في كل مرة: شريط الركائز أعلاه هو مرشح الركيزة (بطاقة واحدة في العرض)،
 * ثم المؤشرات أو المبادرات (أحدهما دائمًا). صف لكل مبادرة بمدتها، وصف لكل مؤشر بمستهدف كل سنة من البدء حتى التحقق.
 * الرابط يحمل الاختيارين: ?view=roadmap&pillar=<معرف الركيزة أو none>&kind=indicators|initiatives؛
 * الناقص أو غير الصالح يرجع إلى الركيزة الأولى والمؤشرات ويصحح الرابط، والتنقل يستبدل الرابط فلا يزيد السجل.
 */

type Kind = "indicators" | "initiatives";

export function Roadmap() {
  const { tree } = usePlan();
  const years = tree.years;
  const [params, setParams] = useSearchParams();

  const groups = [
    ...tree.pillars.map((p, index) => ({ id: p.id, name: p.nameAr, index, standards: p.standards })),
    ...(tree.unassigned.length > 0 ? [{ id: "none", name: L("معايير بلا ركيزة", "No pillar"), index: -1, standards: tree.unassigned }] : []),
  ];
  const rawPillar = params.get("pillar");
  const rawKind = params.get("kind");
  const found = groups.findIndex((g) => g.id === rawPillar);
  const at = found < 0 ? 0 : found;
  const group = groups[at];
  const kind: Kind = rawKind === "initiatives" ? "initiatives" : "indicators";

  const update = (next: { pillar?: string; kind?: Kind }) =>
    setParams((prev) => {
      const p = new URLSearchParams(prev);
      if (next.pillar) p.set("pillar", next.pillar);
      if (next.kind) p.set("kind", next.kind);
      return p;
    }, { replace: true });
  useEffect(() => {
    if (group && (rawPillar !== group.id || rawKind !== kind)) update({ pillar: group.id, kind });
  }, [group?.id, rawPillar, rawKind, kind]); // update يتجدد مع كل رسم ولا يغير شيئًا هنا

  const count = (s: StandardNode) => (kind === "indicators" ? s.indicators.length : s.initiatives.length);
  const standards = (group?.standards ?? []).filter((s) => count(s) > 0);
  const shown = standards.reduce((n, s) => n + count(s), 0);
  const total = groups.reduce((n, g) => n + g.standards.reduce((k, s) => k + count(s), 0), 0);
  const cols = { "--cols": `var(--label-col, minmax(240px, 2.2fr)) repeat(${years.length + 1}, minmax(66px, 1fr))` } as CSSProperties;

  const slides: CarouselSlide[] = groups.map((g) => {
    const indicators = g.standards.reduce((n, s) => n + s.indicators.length, 0);
    const initiatives = g.standards.reduce((n, s) => n + s.initiatives.length, 0);
    return {
      key: g.id,
      name: g.name,
      node: (
        <article className={`sp-pillar sp-road-pick${g.index < 0 ? " loose" : ""}`} style={tint(g.index)}>
          <header>
            <div className="sp-pillar-head">
              {g.index >= 0 && <span className="sp-pillar-num ltr-nums">{String(g.index + 1).padStart(2, "0")}</span>}
              <span className="sp-pillar-name"><b title={g.name}>{g.name}</b></span>
            </div>
            <dl className="sp-pillar-stats">
              <div><dt>{L("معيار", "standards")}</dt><dd>{g.standards.length}</dd></div>
              <div><dt>{L("مؤشر", "indicators")}</dt><dd>{indicators}</dd></div>
              <div><dt>{L("مبادرة", "initiatives")}</dt><dd>{initiatives}</dd></div>
            </dl>
          </header>
        </article>
      ),
    };
  });

  return (
    <section className="lcp-panel sp-road" aria-label={L("الخط الزمني للخطة", "Plan roadmap")}>
      {groups.length > 0 && (
        <PillarCarousel single label={L("الركيزة المعروضة", "Pillar shown")} slides={slides} selected={at}
          onSelect={(i) => groups[i] && update({ pillar: groups[i].id })} />
      )}
      <div className="sp-road-bar">
        <Segmented<Kind> value={kind} onChange={(k) => update({ kind: k })} aria-label={L("نوع العناصر", "Item type")} options={[
          { value: "indicators", label: L("المؤشرات", "Indicators") },
          { value: "initiatives", label: L("المبادرات", "Initiatives") },
        ]} />
        <FilterMeta shown={shown} total={total} noun={kind === "indicators" ? L("مؤشرًا", "indicators") : L("مبادرة", "initiatives")} active={false} onReset={() => undefined} />
      </div>

      {!group || standards.length === 0 ? (
        <EmptyNote>{kind === "indicators" ? L("لا مؤشرات في هذه الركيزة.", "No indicators in this pillar.") : L("لا مبادرات في هذه الركيزة.", "No initiatives in this pillar.")}</EmptyNote>
      ) : (
        <div className="sp-road-scroll">
          <div className="sp-road-grid" style={cols} role="table"
            aria-label={kind === "indicators" ? L(`مستهدفات مؤشرات ${group.name} بالسنوات`, `Indicator targets by year: ${group.name}`) : L(`مبادرات ${group.name} بالسنوات`, `Initiatives by year: ${group.name}`)}>
            <div className="sp-road-row head" role="row">
              <span role="columnheader">{kind === "indicators" ? L("المؤشر", "Indicator") : L("المبادرة", "Initiative")}</span>
              <span role="columnheader" className="ltr-nums base">{tree.baselineYear}<small>{L("الأساس", "Baseline")}</small></span>
              {years.map((y) => <span key={y} role="columnheader" className="ltr-nums">{y}</span>)}
            </div>
            <StandardPanels key={`${group.id}:${kind}`} standards={group.standards} kind={kind} index={group.index} />
          </div>
        </div>
      )}
      {kind === "indicators" && (
        <p className="t1-help sp-road-note">
          <FlagFilled aria-hidden /> {L("سنة التحقق، ويستمر مستهدفها بعدها. القيم الرمادية في سنة الأساس، والشرطة قبل سنة البدء تعني أن المؤشر لا يحكم عليه بعد.",
            "Flag = target year. Grey = baseline. A dash before the start year means the indicator is not judged yet.")}
        </p>
      )}
    </section>
  );
}

function countText(kind: Kind, n: number): string {
  if (kind === "indicators")
    return L(n === 1 ? "مؤشر واحد" : n === 2 ? "مؤشران" : n <= 10 ? `${n} مؤشرات` : `${n} مؤشرًا`, n === 1 ? "1 indicator" : `${n} indicators`);
  return L(n === 1 ? "مبادرة واحدة" : n === 2 ? "مبادرتان" : n <= 10 ? `${n} مبادرات` : `${n} مبادرة`, n === 1 ? "1 initiative" : `${n} initiatives`);
}

/**
 * معايير الركيزة لوحات تطوى: رأس كل معيار زر يفتح صفوفه ويطويها، وتفتح عدة لوحات معًا. تبدأ مطوية إلا معيارًا وحيدًا،
 * وتعود كذلك مع كل ركيزة أو نوع (يعاد تركيب المكون بمفتاحهما). المعيار بلا عناصر من النوع المختار يكتب «لا عناصر» بلا زر.
 * الصفوف داخل اللوحة شبكات بقالب الأعمدة نفسه، فتبقى السنوات والأشرطة على أعمدة الرأس مفتوحة كانت أو تفتح.
 */
function StandardPanels({ standards, kind, index }: { standards: StandardNode[]; kind: Kind; index: number }) {
  const { select } = usePlan();
  const count = (s: StandardNode) => (kind === "indicators" ? s.indicators.length : s.initiatives.length);
  const [open, setOpen] = useState<Set<string>>(() => new Set(standards.length === 1 && count(standards[0]) > 0 ? [standards[0].id] : []));
  const baseId = useId();
  const toggle = (id: string) => setOpen((was) => {
    const next = new Set(was);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });

  return (
    <>
      {standards.map((s, k) => {
        const n = count(s);
        const isOpen = n > 0 && open.has(s.id);
        const bodyId = `${baseId}-${k}`;
        const head = <><code>{stdLabel(s.code)}</code><b>{s.nameAr}</b></>;
        return (
          <div key={s.id} role="rowgroup" className="sp-road-std" data-open={isOpen} style={tint(index)}>
            <div className="sp-road-row std" role="row">
              <span role="cell" className="sp-road-std-cell">
                <span className="sp-road-std-head">
                  {n > 0 ? (
                    <button type="button" className="sp-road-toggle" aria-expanded={isOpen} aria-controls={bodyId} onClick={() => toggle(s.id)}>
                      <DownOutlined aria-hidden className="sp-road-chev" />{head}<small>{countText(kind, n)}</small>
                    </button>
                  ) : (
                    <span className="sp-road-toggle empty">{head}<small>{L("لا عناصر", "No items")}</small></span>
                  )}
                  <button type="button" className="t1-link sp-road-std-open" onClick={() => select(`S:${s.id}`)}>{L("شرح المعيار", "Standard details")}</button>
                </span>
              </span>
            </div>
            {n > 0 && (
              <div className="sp-road-body" id={bodyId} role="none">
                <div className="sp-road-inner" role="none">
                  <StandardRows initiatives={kind === "initiatives" ? s.initiatives : []} indicators={kind === "indicators" ? s.indicators : []} />
                </div>
              </div>
            )}
          </div>
        );
      })}
    </>
  );
}

function StandardRows({ initiatives, indicators }: { initiatives: InitiativeNode[]; indicators: IndicatorNode[] }) {
  const { tree, select } = usePlan();
  const years = tree.years;
  const first = years[0];
  const last = years[years.length - 1];
  const col = (y: number) => 3 + years.indexOf(Math.min(last, Math.max(first, y)));
  const pct = (i: IndicatorNode) => i.unit.trim() === "%";
  const val = (i: IndicatorNode, v: number | null) => (v === null ? "—" : pct(i) ? `${num(v)}%` : num(v));

  return (
    <>
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
                  ? <span key={y} role="cell" style={at} className="sp-road-cell ltr-nums"><UniTarget>{val(i, v)}</UniTarget></span>
                  : <span key={y} role="cell" style={at} className="sp-road-cell after ltr-nums" title={L("يستمر آخر مستهدف", "Latest target carries on")}><UniTarget>{val(i, carried)}</UniTarget></span>;
              return (
                <span key={y} role="cell" style={at} className={`sp-road-cell ltr-nums${y === end ? " goal" : ""}${v === null ? " missing" : ""}`}>
                  {y === end && <FlagFilled aria-label={L("سنة التحقق", "Target year")} />}{v === null ? L("ناقص", "missing") : <UniTarget>{val(i, v)}</UniTarget>}
                </span>
              );
            })}
          </div>
        );
      })}
    </>
  );
}
