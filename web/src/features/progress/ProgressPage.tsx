import { useEffect, useMemo, useState } from "react";
import { Button, Input, Select, Tag, Tooltip } from "antd";
import { FormOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { api } from "../../api/client";
import type { Level, PlanQuarter, Progress, ProgressIndicator, ProgressPillar, ProgressStandard } from "../../api/scorecard";
import { L } from "../../shared/i18n";
import { num } from "../../shared/labels";
import { stdLabel, tint } from "../../shared/pillars";
import { AccordionPanel } from "../../shared/AccordionPanel";
import { PillarCarousel, type CarouselSlide } from "../strategy/PillarCarousel";
import { QueryState } from "../../shared/QueryState";
import { EmptyNote, FilterMeta, PageHeader } from "../../shared/ui";
import "./progress.css";

/**
 * تقدم الكلية: قيمها المعتمدة لكل مؤشر مقارنة بمستهدف الجامعة لذلك العام، وكم تبعد عنه، وبمستهدفها الخاص إن وضع،
 * وبالقيمة الرسمية للجامعة التي نقلها مسئول ملف Tier 1. ودرجتها في كل ركيزة بجوار درجة الجامعة.
 */
const fmt = (v: number | null | undefined, unit: string) => (v === null || v === undefined ? "—" : unit.trim() === "%" ? `${num(v)}%` : num(v));
const LEVEL: Record<Level, { ar: string; en: string; color: string }> = {
  InLevel: { ar: "في المستوى", en: "In level", color: "success" },
  Below: { ar: "دون المستهدف", en: "Below target", color: "error" },
  NoData: { ar: "لا بيانات", en: "No data", color: "default" },
};
type Filter = "all" | Level;

export default function ProgressPage() {
  const [params, setParams] = useSearchParams();
  const navigate = useNavigate();
  const unitId = params.get("unit") ?? undefined;
  const progress = useQuery({ queryKey: ["progress", unitId], queryFn: () => api.get<Progress | null>(`/api/progress${unitId ? `?unitId=${unitId}` : ""}`) });
  const [filter, setFilter] = useState<Filter>("all");
  const [q, setQ] = useState("");
  const p = progress.data;
  const rawPillar = params.get("pillar");
  const selectedIndex = p ? Math.max(0, p.pillars.findIndex((x) => x.id === rawPillar)) : 0;
  const selectedPillar = p?.pillars[selectedIndex];
  const selectPillar = (id: string) => setParams((prev) => {
    const next = new URLSearchParams(prev);
    next.set("pillar", id);
    return next;
  }, { replace: true });
  useEffect(() => {
    if (selectedPillar && rawPillar !== selectedPillar.id) selectPillar(selectedPillar.id);
  }, [selectedPillar?.id, rawPillar]);
  const everyone = p ? p.pillars.flatMap((x) => x.standards).flatMap((s) => s.indicators) : [];
  const below = everyone.filter((i) => i.level === "Below").length;
  const diff = p && p.universityScore !== null ? p.score - p.universityScore : null;

  return (
    <>
      <PageHeader title={p ? L(`تقدم ${p.unit.name}`, `${p.unit.name} progress`) : L("تقدم الكلية", "College progress")}
        subtitle={L("قيم الكلية المعتمدة في كل مؤشر مقارنة بمستهدف الجامعة للعام، وكم تبعد عنه، وبقيمة الجامعة الرسمية.", "College values vs the university target.")}
        stats={p ? [
          { label: L("درجة الكلية", "College score"), value: <span className="ltr">{num(p.score)}%</span> },
          ...(p.universityScore !== null ? [{ label: L("درجة الجامعة", "University score"), value: <span className="ltr">{num(p.universityScore)}%</span> }] : []),
          ...(diff !== null ? [{ label: L("الفرق عن الجامعة", "vs university"), value: <span className="ltr">{diff > 0 ? "+" : ""}{num(Math.round(diff * 10) / 10)}</span>, tone: diff >= 0 ? "ok" as const : "bad" as const }] : []),
          { label: L("في المستوى", "In level"), value: <span className="ltr">{p.inLevel}/{p.indicators}</span>, tone: "ok" },
          { label: L("دون المستهدف", "Below target"), value: below, tone: below > 0 ? "bad" : undefined },
        ] : undefined}
        extra={p && (
          <>
            {p.units.length > 1 && (
              <Select value={p.unit.id} showSearch optionFilterProp="label" style={{ minWidth: 240 }} aria-label={L("الكلية", "College")}
                onChange={(v) => setParams((prev) => { const next = new URLSearchParams(prev); next.set("unit", v); return next; }, { replace: true })} options={p.units.map((u) => ({ value: u.id, label: u.name }))} />
            )}
            <Button type="primary" icon={<FormOutlined />} onClick={() => navigate("/forms")}>{L("إدخال بيانات المؤشرات", "Enter indicator data")}</Button>
          </>
        )} />

      <QueryState query={progress}>
        {() => !p ? (
          <EmptyNote>{L("لا كليات في نطاق صلاحيتك.", "No colleges in your scope.")}</EmptyNote>
        ) : (
          <>
            <section className="t1-panel pg-pillars" aria-label={L("الركائز مقارنة بالجامعة", "Pillars vs university")}>
              <header className="pg-sec-head">
                <h2>{L("الكلية مقابل الجامعة في كل ركيزة", "College vs university by pillar")}</h2>
                <span className="pg-key"><span><i className="me" aria-hidden="true" />{p.unit.name}</span><span><i className="uni" aria-hidden="true" />{L("الجامعة (القيم المنقولة)", "University (transferred)")}</span></span>
              </header>
              <div className="pg-pillar-grid">
                {p.pillars.map((x) => (
                  <div key={x.id} className="pg-pillar" style={tint(x.index)}>
                    <b><span className="pg-pillar-index ltr">{String(x.index + 1).padStart(2, "0")}</span>{x.name}</b>
                    <Bar value={x.score} cls="me" label={p.unit.name} />
                    {x.universityScore !== null && <Bar value={x.universityScore} cls="uni" label={L("الجامعة", "University")} />}
                  </div>
                ))}
              </div>
              {p.lastReported && <p className="muted pg-note">{L(`آخر ربع فيه قيم معتمدة: ${p.lastReported.label}. الدرجة نسبة الإنجاز من المستهدف النهائي للخطة بقواعد بطاقة الجامعة نفسها.`, `Last reported: ${p.lastReported.label}.`)}</p>}
            </section>

            <section className="pg-road" aria-label={L("تفاصيل تقدم الركيزة", "Pillar progress details")}>
              <PillarCarousel single label={L("الركيزة المعروضة", "Pillar shown")}
                selected={selectedIndex} onSelect={(index) => p.pillars[index] && selectPillar(p.pillars[index].id)}
                slides={p.pillars.map((x): CarouselSlide => ({
                  key: x.id, name: x.name,
                  node: <div className="pg-pick" style={tint(x.index)}>
                    <span className="pg-pick-index ltr">{String(x.index + 1).padStart(2, "0")}</span>
                    <strong>{x.name}</strong>
                    <span className="pg-pick-stats">
                      <span><small>{L("درجة الكلية", "College score")}</small><b className="ltr">{num(x.score)}%</b></span>
                      <span><small>{L("درجة الجامعة", "University score")}</small><b className="ltr">{x.universityScore === null ? "—" : `${num(x.universityScore)}%`}</b></span>
                      <span><small>{L("المعايير", "Standards")}</small><b className="ltr">{x.standards.length}</b></span>
                      <span><small>{L("المؤشرات", "Indicators")}</small><b className="ltr">{x.standards.reduce((n, s) => n + s.indicators.length, 0)}</b></span>
                    </span>
                  </div>,
                }))} />
            <div className="rv-filter pg-filter">
              <Input.Search allowClear value={q} placeholder={L("كود المؤشر أو اسمه", "Code or name")} aria-label={L("كود المؤشر أو اسمه", "Code or name")} onChange={(e) => setQ(e.target.value)} style={{ maxWidth: 280 }} />
              {(["all", "Below", "InLevel", "NoData"] as Filter[]).map((f) => (
                <button key={f} type="button" className={filter === f ? "on" : ""} onClick={() => setFilter(f)}>
                  {f === "all" ? L("الكل", "All") : L(LEVEL[f].ar, LEVEL[f].en)}
                </button>
              ))}
              <FilterMeta shown={selectedPillar?.standards.flatMap((s) => s.indicators).filter((i) => (filter === "all" || i.level === filter) && (!q || i.name.toLocaleLowerCase().includes(q.toLocaleLowerCase()) || i.code.toLocaleLowerCase().startsWith(q.toLocaleLowerCase()))).length ?? 0}
                total={selectedPillar?.standards.reduce((n, s) => n + s.indicators.length, 0) ?? 0} noun={L("مؤشر", "indicators")} active={filter !== "all" || !!q} onReset={() => { setFilter("all"); setQ(""); }} />
            </div>
            {selectedPillar && <Indicators key={selectedPillar.id} pillar={selectedPillar} quarters={p.quarters} filter={filter} q={q} />}
            </section>
          </>
        )}
      </QueryState>
    </>
  );
}

function Bar({ value, cls, label }: { value: number; cls: string; label: string }) {
  return (
    <div className={`pg-bar ${cls}`} title={`${label}: ${num(value)}%`}>
      <span className="pg-bar-track"><i style={{ width: `${Math.min(100, value)}%` }} /></span>
      <small className="pg-bar-label">{label}</small>
      <b className="ltr">{num(value)}%</b>
    </div>
  );
}

function Indicators({ pillar, quarters, filter, q }: { pillar: ProgressPillar; quarters: PlanQuarter[]; filter: Filter; q: string }) {
  const standards = useMemo(() => pillar.standards.map((s) => ({
    ...s,
    indicators: s.indicators.filter((i) => (filter === "all" || i.level === filter)
      && (!q || i.name.toLocaleLowerCase().includes(q.toLocaleLowerCase()) || i.code.toLocaleLowerCase().startsWith(q.toLocaleLowerCase()))),
  })).filter((s) => s.indicators.length > 0), [pillar, filter, q]);
  if (standards.length === 0) return <EmptyNote>{L("لا مؤشرات بهذه المرشحات في الركيزة المختارة.", "Nothing matches in the selected pillar.")}</EmptyNote>;
  return (
    <section className="pg-group" style={tint(pillar.index)} aria-label={pillar.name}>
      <div className="pg-table-scroll">
        <div className="pg-table" role="table" aria-label={L(`مؤشرات ${pillar.name}`, `Indicators: ${pillar.name}`)}>
            <div className="pg-tr head" role="row">
              <span role="columnheader">{L("المؤشر", "Indicator")}</span>
              <span role="columnheader">{L("قيمة الكلية", "College value")}</span>
              <span role="columnheader">{L("مستهدف الجامعة للعام", "University target")}</span>
              <span role="columnheader">{L("البعد عن المستهدف", "Distance to target")}</span>
              {/* للمجموع والعدد: البعد عن مستهدف الكلية نفسها إن وضع، وإلا حصتها من رقم الجامعة. */}
              <span role="columnheader">{L("الإنجاز", "Achievement")}</span>
              <span role="columnheader">{L("مستهدف الكلية", "Own target")}</span>
              <span role="columnheader">{L("الأرباع", "Quarters")}</span>
            </div>
            <StandardPanels standards={standards} quarters={quarters} pillarId={pillar.id} />
        </div>
      </div>
    </section>
  );
}

function StandardPanels({ standards, quarters, pillarId }: { standards: ProgressStandard[]; quarters: PlanQuarter[]; pillarId: string }) {
  const [open, setOpen] = useState<Set<string>>(() => new Set(standards.length === 1 ? [standards[0].code] : []));
  return <div className="pg-standards" role="rowgroup">
    {standards.map((s) => <AccordionPanel key={s.code} id={`pg-${pillarId}-${s.code}`} className="pg-std"
      open={open.has(s.code)} onToggle={() => setOpen((previous) => {
        const next = new Set(previous);
        if (next.has(s.code)) next.delete(s.code); else next.add(s.code);
        return next;
      })}
      header={<><code className="pill-code ltr">{stdLabel(s.code)}</code><b>{s.name}</b><small>{L(`${s.indicators.length} مؤشر`, `${s.indicators.length} indicators`)}</small><span className="pg-std-score ltr">{num(s.score)}%</span></>}>
      <div className="pg-std-rows">{s.indicators.map((i) => <Row key={i.id} i={i} quarters={quarters} />)}</div>
    </AccordionPanel>)}
  </div>;
}

function Row({ i, quarters }: { i: ProgressIndicator; quarters: PlanQuarter[] }) {
  const lv = !i.comparable && i.yearTarget === null && i.latest !== null ? { ar: "مساهمة في رقم الجامعة", en: "Contribution", color: "blue" } : LEVEL[i.level];
  const good = i.gap === null ? null : i.direction === "LowerIsBetter" ? i.gap <= 0 : i.gap >= 0;
  const ach = i.yearAchievement;
  return (
    <div className="pg-tr" role="row">
      <span role="cell" className="pg-name">
        <Link to={`/indicators/${i.code}`} className="pill-code ltr">{i.code}</Link>
        <span>{i.name}<br /><Tag color={lv.color}>{L(lv.ar, lv.en)}</Tag>{i.direction === "LowerIsBetter" && <small className="muted">{L("الأقل أفضل", "Lower is better")}</small>}</span>
      </span>
      <span role="cell" className="ltr pg-num">
        <b>{fmt(i.latest, i.unit)}</b>
        {i.latestIndex >= 0 && <small className="muted">{quarters[i.latestIndex]?.label}</small>}
      </span>
      <span role="cell" className="ltr pg-num">
        {i.comparable ? <b>{fmt(i.yearTarget, i.unit)}</b> : (
          // مجموع أو عدد: مستهدف الجامعة للجامعة كلها، فلا يقارن به رقم كلية واحدة.
          <Tooltip title={L("مؤشر مجموع أو عدد: مستهدف الجامعة لمجموع الكليات كلها، فتقارن الكلية بمستهدفها هي إن وضع، وتظهر حصتها من رقم الجامعة.", "A sum: the target is for all colleges together.")}>
            <span className="muted">{fmt(i.universityYearTarget, i.unit)}</span><small className="muted">{L("للجامعة كلها", "whole university")}</small>
          </Tooltip>
        )}
      </span>
      <span role="cell" className="ltr pg-num">
        {i.gap === null ? (i.share !== null ? <><b className="pg-share">{num(i.share)}%</b><small className="muted">{L("حصتها من رقم الجامعة", "share of university")}</small></> : <span className="muted">—</span>) : (
          <b className={good ? "rv-up" : "rv-down"}>{good ? "✓ " : ""}{i.gap > 0 ? "+" : ""}{num(Math.round(i.gap * 100) / 100)}</b>
        )}
        {i.gap !== null && !good && <small className="muted">{L("تحتاج للوصول", "to reach")}</small>}
      </span>
      <span role="cell" className="pg-ach">
        {ach === null ? <span className="muted">—</span> : (
          <>
            <span className="sc-bar"><i className={ach >= 100 ? "c100" : ach >= 90 ? "c90" : ach >= 70 ? "c70" : "clow"} style={{ width: `${Math.min(100, ach)}%`, background: "currentColor" }} /></span>
            <b className="ltr">{num(ach)}%</b>
          </>
        )}
      </span>
      <span role="cell" className="ltr pg-num">
        {i.ownTarget === null ? <span className="muted">—</span> : <><b>{fmt(i.ownTarget, i.unit)}</b>{i.ownAchievement !== null && <small className="muted">{num(i.ownAchievement)}%</small>}</>}
      </span>
      <span role="cell"><Spark i={i} quarters={quarters} /></span>
    </div>
  );
}

/** خط قيم الكلية في الأرباع حتى آخر ربع فيه قيمة، ونقاط قيمة الجامعة الرسمية، ودرجات مستهدف الجامعة لكل عام. */
function Spark({ i, quarters }: { i: ProgressIndicator; quarters: PlanQuarter[] }) {
  // مجموع أو عدد: رقم الجامعة مجموع الكليات فلا يرسم على مقياس رقم كلية واحدة.
  const uni = i.comparable ? i.universityQuarters : i.universityQuarters.map(() => null);
  const lastUni = uni.reduce<number>((m, v, k) => (v !== null ? k : m), -1);
  const end = Math.max(i.latestIndex, lastUni, 3);
  const n = Math.min(20, end + 1);
  const targets = Array.from({ length: n }, (_, k) => (i.comparable ? i.universityTargets : i.collegeTargets)[Math.floor(k / 4)]);
  const all = [...i.quarters.slice(0, n), ...uni.slice(0, n), ...targets].filter((v): v is number => v !== null);
  if (all.length === 0) return <span className="muted">—</span>;
  const lo = Math.min(...all), hi = Math.max(...all), span = hi - lo || 1;
  const W = 140, H = 34, x = (k: number) => 4 + (k * (W - 8)) / Math.max(1, n - 1), y = (v: number) => H - 4 - ((v - lo) * (H - 8)) / span;
  const line = (vals: (number | null)[]) => vals.slice(0, n).map((v, k) => (v === null ? null : `${x(k)},${y(v)}`)).filter(Boolean).join(" ");
  const tip = i.quarters.slice(0, n).map((v, k) => `${quarters[k]?.label}: ${fmt(v, i.unit)}${uni[k] !== null ? ` / ${L("الجامعة", "Univ.")} ${fmt(uni[k], i.unit)}` : ""}`).filter((_, k) => i.quarters[k] !== null || uni[k] !== null).join("\n");
  return (
    <Tooltip title={<span style={{ whiteSpace: "pre-line" }}>{tip || L("لا قيم بعد", "No values yet")}</span>}>
      <svg className="pg-spark" viewBox={`0 0 ${W} ${H}`} width={W} height={H} role="img" aria-label={L("قيم الكلية في الأرباع", "College quarterly values")}>
        <polyline className="t" points={targets.map((v, k) => (v === null ? null : `${x(k)},${y(v)}`)).filter(Boolean).join(" ")} />
        <polyline className="me" points={line(i.quarters)} />
        {i.quarters.slice(0, n).map((v, k) => v !== null && <circle key={k} className="me" cx={x(k)} cy={y(v)} r={2.4} />)}
        {uni.slice(0, n).map((v, k) => v !== null && v !== undefined && <rect key={`u${k}`} className="uni" x={x(k) - 2.5} y={y(v) - 2.5} width={5} height={5} />)}
      </svg>
    </Tooltip>
  );
}
