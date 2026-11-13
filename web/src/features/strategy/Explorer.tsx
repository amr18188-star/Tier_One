import { useEffect, useState, type ReactNode } from "react";
import { Button, Grid, Input, Tag, Tooltip } from "antd";
import {
  AimOutlined, ApartmentOutlined, BankOutlined, BulbOutlined, CalendarOutlined, DeleteOutlined, DownOutlined, EditOutlined, FileTextOutlined, FlagFilled,
  FlagOutlined, LeftOutlined, PlusOutlined, RightOutlined, RocketOutlined, SearchOutlined, TrophyOutlined, UnorderedListOutlined,
} from "@ant-design/icons";
import { Link } from "react-router-dom";
import type { IndicatorNode, InitiativeNode, PillarNode, StandardNode } from "../../api/tier1";
import { howCalculated } from "../../shared/calc";
import { L, useLang } from "../../shared/i18n";
import { aggregation, definitionStatus, direction, horizon, initiativeStatus, level, num, performance, withUnit } from "../../shared/labels";
import { EmptyNote } from "../../shared/ui";
import { IndicatorPlan } from "./IndicatorPlan";
import {
  allIndicators, allInitiatives, dotState, dotText, finalTarget, find, pillarIndexOf, span, targetOf, targetsComplete, tint, usePlan,
} from "./model";
import { Dots, DotLegend, StateBar } from "./PlanMap";
import { Sparkline, YearBar } from "./PlanCharts";
import { stdLabel } from "../../shared/pillars";

/**
 * المستكشف: شجرة الخطة على جانب، وتفاصيل العنصر المختار على الجانب الآخر بلون ركيزته،
 * مع مسار التنقل (الخطة › الركيزة › المعيار) والإضافة والتعديل والحذف من مكانها بحسب الصلاحية.
 */
export function Explorer({ selected }: { selected: string }) {
  const { tree } = usePlan();
  const screens = Grid.useBreakpoint();
  const narrow = screens.lg === false;
  const [navOpen, setNavOpen] = useState(false);
  const sel = find(tree, selected);
  // لون التفاصيل لون ركيزة العنصر المختار؛ الخطة كاملة بالكحلي.
  const index = sel.kind === "pillar" ? tree.pillars.findIndex((p) => p.id === sel.pillar.id)
    : sel.kind === "root" ? -2 : pillarIndexOf(tree, sel.standard);
  // على الشاشة الضيقة تطوى الشجرة بعد كل اختيار فتظهر التفاصيل مباشرة.
  useEffect(() => setNavOpen(false), [selected]);

  return (
    <div className="sp-explore">
      {narrow ? (
        <div className="sp-nav-mobile">
          <Button block icon={<UnorderedListOutlined aria-hidden />} onClick={() => setNavOpen(!navOpen)} aria-expanded={navOpen}>
            {navOpen ? L("إخفاء شجرة الخطة", "Hide plan tree") : L("تصفح شجرة الخطة", "Browse the plan tree")}
          </Button>
          {navOpen && <Navigator selected={selected} />}
        </div>
      ) : <Navigator selected={selected} />}
      <section className="sp-detail" aria-live="polite" style={index === -2 ? undefined : tint(index)}>
        {sel.kind === "root" && <RootDetail />}
        {sel.kind === "pillar" && <PillarDetail pillar={sel.pillar} />}
        {sel.kind === "standard" && <StandardDetail standard={sel.standard} />}
        {sel.kind === "initiative" && <InitiativeDetail initiative={sel.initiative} standard={sel.standard} />}
        {sel.kind === "indicator" && <IndicatorDetail indicator={sel.indicator} standard={sel.standard} />}
      </section>
    </div>
  );
}

// ----------------------------------------------------------------- الشجرة

function Navigator({ selected }: { selected: string }) {
  const { tree, select } = usePlan();
  const [search, setSearch] = useState("");
  const [closed, setClosed] = useState<Set<string>>(new Set());
  const q = search.trim().toLowerCase();
  const hit = (...texts: (string | null | undefined)[]) => !q || texts.some((x) => x?.toLowerCase().includes(q));
  const toggle = (id: string) => setClosed((s) => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n; });
  const inside = (s: StandardNode) => selected === `S:${s.id}` || s.initiatives.some((m) => selected === `M:${m.id}`) || s.indicators.some((i) => selected === `I:${i.code}`);

  const groups = [
    ...tree.pillars.map((p, index) => ({ key: `P:${p.id}`, pillar: p as PillarNode | null, code: p.code, name: p.nameAr, index, standards: p.standards })),
    ...(tree.unassigned.length > 0 ? [{ key: "GU", pillar: null, code: "—", name: L("معايير بلا ركيزة", "No pillar"), index: -1, standards: tree.unassigned }] : []),
  ];

  let matches = 0;
  const body = groups.map((g) => {
    const stds = g.standards.map((s) => {
      const own = hit(s.code, s.nameAr) || hit(g.code, g.name);
      const ms = s.initiatives.filter((m) => own || hit(m.code, m.nameAr));
      const is = s.indicators.filter((i) => own || hit(i.code, i.nameAr));
      return { s, ms, is, show: own || ms.length + is.length > 0 };
    }).filter((x) => x.show);
    if (q && stds.length === 0) return null;
    matches += stds.length;
    const open = !!q || !closed.has(g.key);
    return (
      <li key={g.key} className="sp-nav-group" style={tint(g.index)}>
        <div className={`sp-nav-p${selected === g.key ? " on" : ""}`}>
          <button type="button" className="sp-nav-caret" onClick={() => toggle(g.key)} aria-expanded={open} aria-label={open ? L("طي", "Collapse") : L("فتح", "Expand")}>
            <DownOutlined aria-hidden className={open ? "" : "shut"} />
          </button>
          <button type="button" className="sp-nav-btn" onClick={() => (g.pillar ? select(g.key) : toggle(g.key))} aria-current={selected === g.key ? "true" : undefined}>
            <code>{g.code}</code><span>{g.name}</span>
          </button>
        </div>
        {open && (
          <ul>
            {stds.map(({ s, ms, is }) => {
              const deep = !!q || inside(s);
              return (
                <li key={s.id}>
                  <button type="button" className={`sp-nav-s${selected === `S:${s.id}` ? " on" : ""}`} onClick={() => select(`S:${s.id}`)} aria-current={selected === `S:${s.id}` ? "true" : undefined}>
                    <code>{stdLabel(s.code)}</code><span>{s.nameAr}</span><small className="ltr-nums">{s.indicators.length}</small>
                  </button>
                  {deep && (ms.length > 0 || is.length > 0) && (
                    <ul className="sp-nav-leaves">
                      {ms.map((m) => (
                        <li key={m.id}>
                          <button type="button" className={selected === `M:${m.id}` ? "on" : ""} onClick={() => select(`M:${m.id}`)} aria-current={selected === `M:${m.id}` ? "true" : undefined}>
                            <RocketOutlined aria-hidden /><code>{m.code}</code><span>{m.nameAr}</span>
                          </button>
                        </li>
                      ))}
                      {is.map((i) => (
                        <li key={i.code}>
                          <button type="button" className={selected === `I:${i.code}` ? "on" : ""} onClick={() => select(`I:${i.code}`)} aria-current={selected === `I:${i.code}` ? "true" : undefined}>
                            <AimOutlined aria-hidden /><code>{i.code}</code><span>{i.nameAr}</span>
                          </button>
                        </li>
                      ))}
                    </ul>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </li>
    );
  });

  return (
    <nav className="lcp-panel sp-nav" aria-label={L("شجرة الخطة", "Plan tree")}>
      <Input allowClear prefix={<SearchOutlined aria-hidden />} value={search} onChange={(e) => setSearch(e.target.value)}
        placeholder={L("ابحث: ركيزة أو معيار أو مؤشر أو مبادرة", "Search the plan")} aria-label={L("بحث في شجرة الخطة", "Search the plan")} />
      <button type="button" className={`sp-nav-root${selected === "root" ? " on" : ""}`} onClick={() => select("root")}>
        <BankOutlined aria-hidden /> {L(`الخطة كاملة ${tree.years[0]}–${tree.years[tree.years.length - 1]}`, "The whole plan")}
      </button>
      <ul className="sp-nav-list">{body}</ul>
      {q && matches === 0 && <p className="t1-help">{L("لا نتائج لهذا البحث.", "No matches.")}</p>}
    </nav>
  );
}

// ----------------------------------------------------------------- رأس التفاصيل

function Crumbs({ items }: { items: { key: string; label: string }[] }) {
  const { select } = usePlan();
  const Sep = useLang() === "ar" ? LeftOutlined : RightOutlined;
  return (
    <nav className="sp-crumbs" aria-label={L("موقعك في الخطة", "Where you are")}>
      <button type="button" onClick={() => select("root")}>{L("الخطة", "Plan")}</button>
      {items.map((c) => <span key={c.key}><Sep aria-hidden /><button type="button" onClick={() => select(c.key)}>{c.label}</button></span>)}
    </nav>
  );
}

function DetailHead({ index, kind, icon, code, title, sub, chips, actions, crumbs, children }: {
  index: number; kind?: string; icon: ReactNode; code?: string; title: string; sub?: string | null; chips?: ReactNode; actions?: ReactNode;
  crumbs?: { key: string; label: string }[]; children?: ReactNode;
}) {
  // بلا kind وcode لا يظهر سطر النوع والرمز فوق العنوان (الركيزة: يكفي اسمها، كما في الخريطة).
  return (
    <header className="sp-dhead" style={tint(index)}>
      {crumbs && <Crumbs items={crumbs} />}
      <div className="sp-dhead-row">
        <span className="sp-dhead-icon" aria-hidden>{icon}</span>
        <div className="sp-dhead-text">
          {kind && code && <small>{kind} <code>{code}</code></small>}
          <h2>{title}</h2>
          {sub && <p className="ltr sp-dhead-en">{sub}</p>}
          {chips && <div className="sp-dhead-chips">{chips}</div>}
        </div>
        {actions && <div className="sp-dhead-actions">{actions}</div>}
      </div>
      {children}
    </header>
  );
}

function Desc({ text, empty }: { text: string | null; empty: string }) {
  return text ? <p className="sp-desc">{text}</p> : <p className="sp-desc empty">{empty}</p>;
}

function Section({ title, icon, extra, children }: { title: string; icon: ReactNode; extra?: ReactNode; children: ReactNode }) {
  return (
    <section className="lcp-panel sp-sec">
      <div className="lcp-panel-head"><h2>{icon}{title}</h2>{extra}</div>
      {children}
    </section>
  );
}

function Facts({ items }: { items: { label: string; value: ReactNode; hint?: ReactNode; tone?: string }[] }) {
  return (
    <dl className="sp-facts">
      {items.map((f) => (
        <div key={f.label} className={f.tone ? `tone-${f.tone}` : undefined}>
          <dt>{f.label}</dt><dd>{f.value}</dd>{f.hint && <small>{f.hint}</small>}
        </div>
      ))}
    </dl>
  );
}

// ----------------------------------------------------------------- عناصر القوائم

function fmt(i: IndicatorNode, v: number | null) {
  return v === null ? "—" : i.unit.trim() === "%" ? `${num(v)}%` : withUnit(v, i.unit);
}

/** صف مؤشر: الاسم، وأفق المستهدف، ومسار المستهدف المصغر، والمستهدف النهائي، وحالته. */
function IndicatorRow({ i }: { i: IndicatorNode }) {
  const { tree, results, select } = usePlan();
  const years = tree.years;
  const { start, end } = span(i, years);
  const r = results.get(i.code);
  const state = dotState(i, years, r);
  const goal = finalTarget(i, years);
  return (
    <li>
      <button type="button" className="sp-irow" onClick={() => select(`I:${i.code}`)}>
        <i className={`sp-dot d-${state}`} title={dotText[state]} aria-hidden />
        <span className="sp-irow-name"><code>{i.code}</code><b>{i.nameAr}</b><small>{horizon(i.planStartYear, i.planEndYear, years[0], years[years.length - 1])}</small></span>
        <Sparkline values={[i.baseline, ...years.map((y) => (y < start ? null : targetOf(i, y)))]} label={L(`مسار مستهدف ${i.code}`, `Target path of ${i.code}`)} />
        <span className="sp-irow-goal">
          <small>{L(`مستهدف ${end}`, `Target ${end}`)}</small>
          <b className="ltr-nums">{fmt(i, goal)}</b>
        </span>
        <span className="sp-irow-state">
          {r && r.actual !== null ? (
            <><small>{L("آخر قياس", "Latest")}</small><b className="ltr-nums">{fmt(i, r.actual)}</b><Tag color={performance[r.performance].color}>{performance[r.performance].text}</Tag></>
          ) : !targetsComplete(i, years) ? <Tag color="warning">{L("خطة ناقصة", "Plan incomplete")}</Tag> : <Tag>{definitionStatus[i.status].text}</Tag>}
        </span>
      </button>
    </li>
  );
}

function InitiativeCard({ m }: { m: InitiativeNode }) {
  const { tree, select } = usePlan();
  return (
    <button type="button" className={`sp-init st-${m.status}`} onClick={() => select(`M:${m.id}`)}>
      <span className="sp-init-top">
        <code>{m.code}</code>
        <Tag color={initiativeStatus[m.status].color}>{initiativeStatus[m.status].text}</Tag>
      </span>
      <b>{m.nameAr}</b>
      <small className="muted">{m.ownerUnitName ?? L("لم تحدد الجهة المسؤولة", "No owner yet")} · {L(`${m.indicatorCodes.length} مؤشر مرتبط`, `${m.indicatorCodes.length} linked indicators`)}</small>
      <YearBar years={tree.years} start={m.startYear} end={m.endYear} label={L(`من ${m.startYear} إلى ${m.endYear}`, `${m.startYear}–${m.endYear}`)} />
    </button>
  );
}

function StandardCard({ s }: { s: StandardNode }) {
  const { select } = usePlan();
  return (
    <button type="button" className="sp-scard" onClick={() => select(`S:${s.id}`)}>
      <span className="sp-scard-head"><code>{stdLabel(s.code)}</code><b>{s.nameAr}</b></span>
      {s.description && <span className="sp-scard-desc">{s.description}</span>}
      <span className="sp-std-meta">
        <span><AimOutlined aria-hidden /> {L(`${s.indicators.length} مؤشر`, `${s.indicators.length} indicators`)}</span>
        <span><RocketOutlined aria-hidden /> {L(`${s.initiatives.length} مبادرة`, `${s.initiatives.length} initiatives`)}</span>
      </span>
      <Dots list={s.indicators} />
    </button>
  );
}

// ----------------------------------------------------------------- الخطة كاملة

function RootDetail() {
  const { tree, select } = usePlan();
  const indicators = allIndicators(tree);
  return (
    <>
      <header className="sp-dhead sp-dhead-root">
        <div className="sp-dhead-row">
          <span className="sp-dhead-icon" aria-hidden><BankOutlined /></span>
          <div className="sp-dhead-text">
            <small>{L("الخطة كاملة", "Whole plan")}</small>
            <h2>{L("كيف تقرأ الخطة", "How to read the plan")}</h2>
          </div>
        </div>
        <ol className="sp-howto">
          <li><FlagOutlined aria-hidden /><span><b>{L("الركيزة", "Pillar")}</b>{L("محور كبير من محاور الخطة، وتحته معاييره.", "A major axis of the plan.")}</span></li>
          <li><ApartmentOutlined aria-hidden /><span><b>{L("المعيار", "Standard")}</b>{L("ما تقيمه Tier 1، ولكل معيار مبادرات ومؤشرات.", "What Tier 1 assesses.")}</span></li>
          <li><RocketOutlined aria-hidden /><span><b>{L("المبادرة", "Initiative")}</b>{L("ما ستنفذه الجامعة، بجهة مسؤولة ومدة.", "What the university will do.")}</span></li>
          <li><AimOutlined aria-hidden /><span><b>{L("المؤشر", "Indicator")}</b>{L("ما يقاس به التقدم، بمستهدف في كل سنة وشرح لحسابه.", "How progress is measured.")}</span></li>
        </ol>
      </header>
      <Section title={L("الركائز", "Pillars")} icon={<FlagOutlined aria-hidden />}>
        <DotLegend list={indicators} />
        <div className="sp-pgrid">
          {tree.pillars.map((p, index) => {
            const list = p.standards.flatMap((s) => s.indicators);
            return (
              <button key={p.id} type="button" className="sp-pmini" style={tint(index)} onClick={() => select(`P:${p.id}`)}>
                <span className="sp-pillar-num ltr-nums">{String(index + 1).padStart(2, "0")}</span>
                <span className="sp-pmini-text">
                  <b>{p.nameAr}</b>
                  <small>{L(`${p.standards.length} معيار · ${p.standards.reduce((n, s) => n + s.initiatives.length, 0)} مبادرة · ${list.length} مؤشر`, `${p.standards.length} standards · ${list.length} indicators`)}</small>
                  <StateBar list={list} />
                </span>
              </button>
            );
          })}
        </div>
      </Section>
    </>
  );
}

// ----------------------------------------------------------------- الركيزة

function PillarDetail({ pillar }: { pillar: PillarNode }) {
  const { tree, edit } = usePlan();
  const a = tree.actions;
  const index = tree.pillars.findIndex((p) => p.id === pillar.id);
  const indicators = pillar.standards.flatMap((s) => s.indicators);
  const initiatives = pillar.standards.flatMap((s) => s.initiatives);
  return (
    <>
      <DetailHead index={index} icon={<FlagOutlined />} title={pillar.nameAr} sub={pillar.nameEn}
        crumbs={[]}
        actions={<>
          {a.canEdit && <Button icon={<EditOutlined />} onClick={() => edit({ kind: "pillar", pillar })}>{L("تعديل", "Edit")}</Button>}
          {a.canAdd && <Button icon={<PlusOutlined />} onClick={() => edit({ kind: "standard", pillarId: pillar.id })}>{L("معيار جديد", "New standard")}</Button>}
          {a.canDelete && <Tooltip title={L("حذف الركيزة", "Delete pillar")}><Button danger icon={<DeleteOutlined />} aria-label={L("حذف الركيزة", "Delete pillar")}
            onClick={() => edit({ kind: "delete", title: L(`حذف الركيزة ${pillar.code}`, `Delete ${pillar.code}`), url: `/api/strategy/pillars/${pillar.id}/delete`, after: "root",
              explain: pillar.standards.length > 0 ? L("لا تحذف ركيزة تحتها معايير؛ انقل معاييرها إلى ركيزة أخرى أولًا.", "Move its standards first.") : L("تحذف الركيزة نهائيًا ويبقى حذفها في السجل.", "The pillar is deleted; the log keeps the record.") })} /></Tooltip>}
        </>}>
        <Desc text={pillar.description} empty={L("لم يكتب شرح هذه الركيزة بعد.", "No description yet.")} />
        <Facts items={[
          { label: L("معايير", "Standards"), value: pillar.standards.length },
          { label: L("مبادرات", "Initiatives"), value: initiatives.length },
          { label: L("مؤشرات", "Indicators"), value: indicators.length },
          { label: L("خطط مستهدف مكتملة", "Complete target plans"), value: <span className="ltr-nums">{indicators.filter((i) => targetsComplete(i, tree.years)).length}/{indicators.length}</span> },
        ]} />
      </DetailHead>

      <Section title={L(`معايير الركيزة (${pillar.standards.length})`, `Standards (${pillar.standards.length})`)} icon={<ApartmentOutlined aria-hidden />}>
        {pillar.standards.length === 0 ? <EmptyNote>{L("لا معايير تحت هذه الركيزة بعد.", "No standards yet.")}</EmptyNote> : (
          <div className="sp-sgrid" style={tint(index)}>{pillar.standards.map((s) => <StandardCard key={s.id} s={s} />)}</div>
        )}
      </Section>

      {initiatives.length > 0 && (
        <Section title={L(`مبادرات الركيزة على سنوات الخطة (${initiatives.length})`, `Initiatives over the plan (${initiatives.length})`)} icon={<RocketOutlined aria-hidden />}>
          <div className="sp-igrid" style={tint(index)}>{initiatives.map((m) => <InitiativeCard key={m.id} m={m} />)}</div>
        </Section>
      )}
    </>
  );
}

// ----------------------------------------------------------------- المعيار

function StandardDetail({ standard }: { standard: StandardNode }) {
  const { tree, edit } = usePlan();
  const a = tree.actions;
  const index = pillarIndexOf(tree, standard);
  const pillar = tree.pillars[index];
  return (
    <>
      <DetailHead index={index} kind={L("المعيار", "Standard")} icon={<ApartmentOutlined />} code={stdLabel(standard.code)} title={standard.nameAr} sub={standard.nameEn}
        crumbs={pillar ? [{ key: `P:${pillar.id}`, label: `${pillar.code} ${pillar.nameAr}` }] : []}
        actions={<>
          {a.canEdit && <Button icon={<EditOutlined />} onClick={() => edit({ kind: "standard", standard })}>{L("تعديل", "Edit")}</Button>}
          {a.canDelete && <Tooltip title={L("حذف المعيار", "Delete standard")}><Button danger icon={<DeleteOutlined />} aria-label={L("حذف المعيار", "Delete standard")}
            onClick={() => edit({ kind: "delete", title: L(`حذف المعيار ${standard.code}`, `Delete ${standard.code}`), url: `/api/strategy/standards/${standard.id}/delete`,
              after: pillar ? `P:${pillar.id}` : "root",
              explain: standard.indicators.length + standard.initiatives.length > 0
                ? L("لا يحذف معيار تحته مؤشرات أو مبادرات؛ احذفها أو انقلها أولًا.", "Delete or move its indicators and initiatives first.")
                : L("يحذف المعيار نهائيًا ويبقى حذفه في السجل.", "The standard is deleted; the log keeps the record.") })} /></Tooltip>}
        </>}>
        <Desc text={standard.description} empty={L("لم يكتب شرح هذا المعيار بعد.", "No description yet.")} />
        <DotLegend list={standard.indicators} />
      </DetailHead>

      <Section title={L(`المبادرات (${standard.initiatives.length})`, `Initiatives (${standard.initiatives.length})`)} icon={<RocketOutlined aria-hidden />}
        extra={a.canAdd && <Button size="small" icon={<PlusOutlined />} onClick={() => edit({ kind: "initiative", standardId: standard.id })}>{L("مبادرة جديدة", "New initiative")}</Button>}>
        {standard.initiatives.length === 0 ? <EmptyNote>{L("لا مبادرات لهذا المعيار بعد.", "No initiatives yet.")}</EmptyNote> : (
          <div className="sp-igrid" style={tint(index)}>{standard.initiatives.map((m) => <InitiativeCard key={m.id} m={m} />)}</div>
        )}
      </Section>

      <Section title={L(`المؤشرات ومستهدفاتها (${standard.indicators.length})`, `Indicators and targets (${standard.indicators.length})`)} icon={<AimOutlined aria-hidden />}
        extra={a.canAddIndicator && <Button size="small" icon={<PlusOutlined />} onClick={() => edit({ kind: "indicator", standardCode: standard.code })}>{L("مؤشر جديد", "New indicator")}</Button>}>
        {standard.indicators.length === 0 ? <EmptyNote>{L("لا مؤشرات لهذا المعيار بعد.", "No indicators yet.")}</EmptyNote> : (
          <ul className="sp-irows" style={tint(index)}>{standard.indicators.map((i) => <IndicatorRow key={i.code} i={i} />)}</ul>
        )}
      </Section>
    </>
  );
}

// ----------------------------------------------------------------- المبادرة

function InitiativeDetail({ initiative: m, standard }: { initiative: InitiativeNode; standard: StandardNode }) {
  const { tree, edit } = usePlan();
  const a = tree.actions;
  const index = pillarIndexOf(tree, standard);
  const pillar = tree.pillars[index];
  const indicators = allIndicators(tree).filter((i) => m.indicatorCodes.includes(i.code));
  const years = m.endYear - m.startYear + 1;
  return (
    <>
      <DetailHead index={index} kind={L("المبادرة", "Initiative")} icon={<RocketOutlined />} code={m.code} title={m.nameAr}
        crumbs={[...(pillar ? [{ key: `P:${pillar.id}`, label: `${pillar.code} ${pillar.nameAr}` }] : []), { key: `S:${standard.id}`, label: `${stdLabel(standard.code)} ${standard.nameAr}` }]}
        chips={<Tag color={initiativeStatus[m.status].color}>{initiativeStatus[m.status].text}</Tag>}
        actions={<>
          {a.canEdit && <Button icon={<EditOutlined />} onClick={() => edit({ kind: "initiative", initiative: m, standardId: standard.id })}>{L("تعديل", "Edit")}</Button>}
          {a.canDelete && <Tooltip title={L("حذف المبادرة", "Delete initiative")}><Button danger icon={<DeleteOutlined />} aria-label={L("حذف المبادرة", "Delete initiative")}
            onClick={() => edit({ kind: "delete", title: L(`حذف المبادرة ${m.code}`, `Delete ${m.code}`), url: `/api/strategy/initiatives/${m.id}/delete`, after: `S:${standard.id}`,
              explain: L("تحذف المبادرة نهائيًا ويبقى حذفها وسببه في السجل.", "The initiative is deleted; the log keeps the reason.") })} /></Tooltip>}
        </>}>
        <Desc text={m.description} empty={L("لم يكتب شرح هذه المبادرة بعد.", "No description yet.")} />
        <Facts items={[
          { label: L("الجهة المسؤولة", "Responsible unit"), value: m.ownerUnitName ?? "—" },
          { label: L("المدة", "Duration"), value: <span className="ltr-nums">{m.startYear}–{m.endYear}</span>, hint: years === 1 ? L("سنة واحدة", "1 year") : years === 2 ? L("سنتان", "2 years") : L(`${years} سنوات`, `${years} years`) },
          { label: L("مؤشرات تسهم فيها", "Linked indicators"), value: indicators.length },
        ]} />
        <div className="sp-dhead-bar"><YearBar years={tree.years} start={m.startYear} end={m.endYear} label={L(`تنفذ من ${m.startYear} إلى ${m.endYear}`, `${m.startYear}–${m.endYear}`)} /></div>
      </DetailHead>

      <Section title={L("المخرجات المتوقعة", "Expected outcome")} icon={<TrophyOutlined aria-hidden />}>
        <Desc text={m.expectedOutcome} empty={L("لم تكتب المخرجات المتوقعة بعد.", "Not written yet.")} />
      </Section>

      <Section title={L(`المؤشرات التي تقيس أثرها (${indicators.length})`, `Indicators measuring it (${indicators.length})`)} icon={<AimOutlined aria-hidden />}>
        {indicators.length === 0 ? <EmptyNote>{L("لم تربط هذه المبادرة بمؤشرات بعد.", "No linked indicators yet.")}</EmptyNote> : (
          <ul className="sp-irows" style={tint(index)}>{indicators.map((i) => <IndicatorRow key={i.code} i={i} />)}</ul>
        )}
      </Section>
    </>
  );
}

// ----------------------------------------------------------------- المؤشر

function IndicatorDetail({ indicator: i, standard }: { indicator: IndicatorNode; standard: StandardNode }) {
  const { tree, results, resultsFrom, resultsYear, select, edit } = usePlan();
  const a = tree.actions;
  const index = pillarIndexOf(tree, standard);
  const pillar = tree.pillars[index];
  const years = tree.years;
  const { start, end } = span(i, years);
  const calc = howCalculated(i);
  const initiatives = allInitiatives(tree).filter((m) => m.indicatorCodes.includes(i.code));
  const r = results.get(i.code);
  const goal = finalTarget(i, years);
  const actual = r && r.actual !== null && resultsYear !== null
    ? { year: resultsYear, value: r.actual, performance: r.performance, label: L(`الفعلي — ${resultsFrom ?? ""}`, "Actual") }
    : null;

  return (
    <>
      <DetailHead index={index} kind={L("المؤشر", "Indicator")} icon={<AimOutlined />} code={i.code} title={i.nameAr}
        crumbs={[...(pillar ? [{ key: `P:${pillar.id}`, label: `${pillar.code} ${pillar.nameAr}` }] : []), { key: `S:${standard.id}`, label: `${stdLabel(standard.code)} ${standard.nameAr}` }]}
        chips={<>
          <Tag color={definitionStatus[i.status].color}>{L("التعريف:", "Definition:")} {definitionStatus[i.status].text}</Tag>
          {i.suspended && <Tag color="error">{L("موقوف", "Suspended")}</Tag>}
          <Tag color="cyan"><CalendarOutlined aria-hidden /> {horizon(i.planStartYear, i.planEndYear, years[0], years[years.length - 1])}</Tag>
          <Tag>{i.accumulation === "Cumulative" ? L("تراكمي: قيمة السنة آخر ربع", "Cumulative") : L("لحظي: قيمة السنة متوسط الأرباع", "Snapshot")}</Tag>
          {i.perspectives.length > 0 && <Tag color="geekblue">{L(`مناظير بطاقة الأداء: ${i.perspectives.join("، ")}`, `PSS: ${i.perspectives.join(", ")}`)}</Tag>}
          {i.sdgPrimary && <Tag color="blue">{L(`أهداف التنمية: ${[i.sdgPrimary, i.sdgSecondary, i.sdgInduced].filter(Boolean).join("، ")}`, "SDGs")}</Tag>}
        </>}
        actions={<>
          <Link to={`/indicators/${i.code}`}><Button icon={<FileTextOutlined />}>{a.canEditIndicator ? L("التعريف وتعديله", "Definition") : L("بطاقة المؤشر", "Indicator card")}</Button></Link>
          {a.canDeleteIndicator && <Tooltip title={L("حذف المؤشر", "Delete indicator")}><Button danger icon={<DeleteOutlined />} aria-label={L("حذف المؤشر", "Delete indicator")}
            onClick={() => edit({ kind: "delete", title: L(`حذف المؤشر ${i.code}`, `Delete ${i.code}`), url: `/api/kpi/indicators/${i.code}/delete`, after: `S:${standard.id}`,
              explain: L("يحذف المؤشر بتعريفاته ومستهدفاته إن لم يدخل أي دورة قياس. المؤشر الذي قيس لا يحذف؛ يوقف من بطاقته.",
                "Deleted with its definitions and targets if never measured; a measured indicator is suspended instead.") })} /></Tooltip>}
        </>}>
        <Desc text={i.definition} empty={L("لم يكتب تعريف المؤشر بعد.", "No definition yet.")} />
        <Facts items={[
          { label: L(`خط الأساس ${tree.baselineYear}`, `Baseline ${tree.baselineYear}`), value: <span className="ltr-nums">{fmt(i, i.baseline)}</span> },
          { label: L(`المستهدف في ${end}`, `Target in ${end}`), value: <span className="ltr-nums"><FlagFilled aria-hidden className="sp-goal-flag" /> {fmt(i, goal)}</span>,
            hint: start > years[0] ? L(`يبدأ في ${start}`, `Starts ${start}`) : L("من أول الخطة", "From plan start"), tone: "goal" },
          r && r.actual !== null
            ? { label: L("آخر قياس معتمد", "Latest approved"), value: <span className="ltr-nums">{fmt(i, r.actual)}</span>,
              hint: <>{resultsFrom} · <Tag color={performance[r.performance].color}>{performance[r.performance].text}</Tag>{r.achievement !== null && <span className="ltr-nums" title={L("نسبة الإنجاز", "Achievement")}>{L("إنجاز", "")} {Math.round(r.achievement * 100)}%</span>}</>,
              tone: performance[r.performance].tone }
            : { label: L("آخر قياس معتمد", "Latest approved"), value: "—", hint: resultsFrom ? L("لا قيمة معتمدة بعد", "No approved value yet") : L("النتائج خارج صلاحيتك", "Results not available to you") },
          { label: L("الاتجاه المطلوب", "Direction"), value: direction[i.direction], hint: i.unit },
        ]} />
      </DetailHead>

      <Section title={L("كيف يحسب المؤشر من الكلية إلى الجامعة", "How it is calculated")} icon={<BulbOutlined aria-hidden />}>
        <ol className="sp-flow" style={tint(index)}>
          <li><span className="sp-flow-n">1</span><b>{L("ما تدخله الجهة", "What is entered")}</b><p>{calc.entry}</p><small>{L("يجمع على مستوى:", "Collected at:")} {level[i.level]}</small></li>
          <li><span className="sp-flow-n">2</span><b>{L("قيمة الكلية", "College value")}</b><p>{calc.college}</p></li>
          <li><span className="sp-flow-n">3</span><b>{L("قيمة الجامعة", "University value")}</b><p>{calc.university}</p><small>{L("قاعدة التجميع:", "Rule:")} {aggregation[i.aggregation]}</small></li>
        </ol>
        <p className="sp-judge"><TrophyOutlined aria-hidden /> <b>{L("الحكم على الأداء:", "Judgement:")}</b> {calc.judge}</p>
      </Section>

      <IndicatorPlan node={i} tree={tree} actual={actual} />

      {initiatives.length > 0 && (
        <Section title={L(`مبادرات تسهم فيه (${initiatives.length})`, `Contributing initiatives (${initiatives.length})`)} icon={<RocketOutlined aria-hidden />}>
          <div className="sp-igrid" style={tint(index)}>{initiatives.map((m) => <InitiativeCard key={m.id} m={m} />)}</div>
        </Section>
      )}
      {initiatives.length === 0 && a.canEdit && (
        <p className="t1-help">{L("لا مبادرة مرتبطة بهذا المؤشر. اربطه من نافذة تعديل المبادرة في ", "No initiative is linked. Link it from an initiative in ")}
          <button type="button" className="t1-link" onClick={() => select(`S:${standard.id}`)}>{stdLabel(standard.code)}</button>.</p>
      )}
    </>
  );
}
