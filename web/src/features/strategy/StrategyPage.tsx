import { useMemo, useState, type ReactNode } from "react";
import { App, Button, Input, Tag, Tooltip, Tree } from "antd";
import type { DataNode } from "antd/es/tree";
import {
  AimOutlined, ApartmentOutlined, BulbOutlined, DeleteOutlined, EditOutlined, FileTextOutlined, FlagOutlined, PlusOutlined, RocketOutlined, SearchOutlined,
} from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../../api/client";
import type { IndicatorNode, InitiativeNode, PillarNode, StandardNode, StrategyTree } from "../../api/tier1";
import { howCalculated } from "../../shared/calc";
import { DecisionModal } from "../../shared/DecisionModal";
import { L } from "../../shared/i18n";
import { aggregation, definitionStatus, direction, horizon, initiativeStatus, level, num } from "../../shared/labels";
import { QueryState } from "../../shared/QueryState";
import { EmptyNote, PageHeader, Panel } from "../../shared/ui";
import { NewIndicatorModal } from "../indicators/NewIndicatorModal";
import { IndicatorPlan } from "./IndicatorPlan";
import { InitiativeDrawer, PillarDrawer, StandardDrawer, useRefreshPlan } from "./StrategyForms";

type Selection =
  | { kind: "root" }
  | { kind: "pillar"; pillar: PillarNode }
  | { kind: "standard"; standard: StandardNode }
  | { kind: "initiative"; initiative: InitiativeNode; standard: StandardNode }
  | { kind: "indicator"; indicator: IndicatorNode; standard: StandardNode };

type Editing =
  | { kind: "pillar"; pillar?: PillarNode }
  | { kind: "standard"; standard?: StandardNode; pillarId?: string }
  | { kind: "initiative"; initiative?: InitiativeNode; standardId: string }
  | { kind: "indicator"; standardCode: string }
  | { kind: "delete"; title: string; explain: string; url: string; after: string };

/**
 * شجرة الخطة الاستراتيجية: الجامعة ← الركائز ← المعايير ← المبادرات والمؤشرات. النقر على أي عنصر يعرض شرحه وتفاصيله بجانب الشجرة،
 * وللمؤشر خطة مستهدفه على سنوات الخطة الخمس وكيف يحسب على مستوى الكلية والجامعة. الإضافة والتعديل والحذف من مكانها بحسب الصلاحية.
 */
export default function StrategyPage() {
  const tree = useQuery({ queryKey: ["strategy"], queryFn: () => api.get<StrategyTree>("/api/strategy/tree") });
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<Editing | null>(null);
  const key = params.get("node") ?? "root";
  const select = (k: string) => setParams(k === "root" ? {} : { node: k }, { replace: true });
  const t = tree.data;
  const canAddPillar = t?.actions.canAdd;

  return (
    <>
      <PageHeader title={L("الخطة الاستراتيجية", "Strategic plan")}
        subtitle={L("الركائز ← المعايير ← المبادرات والمؤشرات ← مستهدف كل سنة. اختر أي عنصر لترى شرحه وتفاصيله.",
          "Pillars → standards → initiatives and indicators → yearly targets. Pick any item to see its details.")}
        extra={canAddPillar && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing({ kind: "pillar" })}>{L("ركيزة جديدة", "New pillar")}</Button>} />
      <QueryState query={tree}>
        {() => {
          const sel = find(t!, key);
          return (
            <div className="t1-plan">
              <aside className="lcp-panel t1-plan-tree" aria-label={L("شجرة الخطة", "Plan tree")}>
                <Input allowClear prefix={<SearchOutlined aria-hidden />} value={search} onChange={(e) => setSearch(e.target.value)}
                  placeholder={L("ابحث في الشجرة: ركيزة أو معيار أو مؤشر أو مبادرة", "Search the tree")} aria-label={L("بحث في الشجرة", "Search the tree")} />
                <PlanTree tree={t!} search={search} selected={key} onSelect={select} />
              </aside>
              <section className="t1-plan-detail" aria-live="polite">
                {sel.kind === "root" && <RootDetail tree={t!} onSelect={select} />}
                {sel.kind === "pillar" && <PillarDetail tree={t!} pillar={sel.pillar} onSelect={select} onEdit={setEditing} />}
                {sel.kind === "standard" && <StandardDetail tree={t!} standard={sel.standard} onSelect={select} onEdit={setEditing} />}
                {sel.kind === "initiative" && <InitiativeDetail tree={t!} initiative={sel.initiative} standard={sel.standard} onSelect={select} onEdit={setEditing} />}
                {sel.kind === "indicator" && <IndicatorDetail tree={t!} indicator={sel.indicator} standard={sel.standard} onSelect={select} onEdit={setEditing} />}
              </section>
              <Editors tree={t!} editing={editing} onClose={() => setEditing(null)} onSelect={select} />
            </div>
          );
        }}
      </QueryState>
    </>
  );
}

// ----------------------------------------------------------------- الشجرة

function allStandards(t: StrategyTree): StandardNode[] {
  return [...t.pillars.flatMap((p) => p.standards), ...t.unassigned];
}

function find(t: StrategyTree, key: string): Selection {
  const [kind, id] = [key.slice(0, 2), key.slice(2)];
  if (kind === "P:") { const pillar = t.pillars.find((p) => p.id === id); if (pillar) return { kind: "pillar", pillar }; }
  for (const s of allStandards(t)) {
    if (kind === "S:" && s.id === id) return { kind: "standard", standard: s };
    const m = kind === "M:" ? s.initiatives.find((i) => i.id === id) : undefined;
    if (m) return { kind: "initiative", initiative: m, standard: s };
    const i = kind === "I:" ? s.indicators.find((x) => x.code === id) : undefined;
    if (i) return { kind: "indicator", indicator: i, standard: s };
  }
  return { kind: "root" };
}

function Label({ code, name, extra }: { code: string; name: string; extra?: ReactNode }) {
  return <span className="t1-node"><code>{code}</code><span>{name}</span>{extra}</span>;
}

function PlanTree({ tree, search, selected, onSelect }: { tree: StrategyTree; search: string; selected: string; onSelect: (k: string) => void }) {
  const q = search.trim();
  const hit = (...texts: (string | null | undefined)[]) => !q || texts.some((x) => x?.toLowerCase().includes(q.toLowerCase()));
  const first = tree.years[0];
  const last = tree.years[tree.years.length - 1];

  const standardNode = (s: StandardNode): DataNode | null => {
    const own = hit(s.code, s.nameAr);
    const initiatives = s.initiatives.filter((m) => own || hit(m.code, m.nameAr));
    const indicators = s.indicators.filter((i) => own || hit(i.code, i.nameAr));
    if (!own && initiatives.length === 0 && indicators.length === 0) return null;
    const children: DataNode[] = [];
    if (initiatives.length > 0)
      children.push({
        key: `GM${s.id}`, selectable: false, title: <span className="t1-node-group"><RocketOutlined aria-hidden /> {L(`المبادرات (${initiatives.length})`, `Initiatives (${initiatives.length})`)}</span>,
        children: initiatives.map((m) => ({ key: `M:${m.id}`, isLeaf: true, title: <Label code={m.code} name={m.nameAr} extra={<small className="t1-node-tag">{initiativeStatus[m.status].text}</small>} /> })),
      });
    if (indicators.length > 0)
      children.push({
        key: `GI${s.id}`, selectable: false, title: <span className="t1-node-group"><AimOutlined aria-hidden /> {L(`المؤشرات (${indicators.length})`, `Indicators (${indicators.length})`)}</span>,
        children: indicators.map((i) => ({
          key: `I:${i.code}`, isLeaf: true,
          title: <Label code={i.code} name={i.nameAr} extra={(i.planStartYear ?? first) > first || (i.planEndYear ?? last) < last
            ? <small className="t1-node-tag">{(i.planStartYear ?? first) > first ? L(`يبدأ ${i.planStartYear}`, `from ${i.planStartYear}`) : L(`حتى ${i.planEndYear}`, `by ${i.planEndYear}`)}</small> : undefined} />,
        })),
      });
    return { key: `S:${s.id}`, title: <Label code={s.code} name={s.nameAr} />, children };
  };

  const data: DataNode[] = [{
    key: "root", title: <span className="t1-node t1-node-root"><ApartmentOutlined aria-hidden /> {L(`جامعة العاصمة — خطة ${first}–${last}`, `Capital University — plan ${first}–${last}`)}</span>,
    children: [
      ...tree.pillars.map((p) => {
        const standards = p.standards.map(standardNode).filter((x): x is DataNode => !!x);
        if (q && standards.length === 0 && !hit(p.code, p.nameAr)) return null;
        return { key: `P:${p.id}`, title: <Label code={p.code} name={p.nameAr} />, children: standards };
      }).filter((x): x is NonNullable<typeof x> => !!x),
      ...(tree.unassigned.length > 0 ? [{
        key: "GU", selectable: false, title: <span className="t1-node-group">{L("معايير بلا ركيزة", "Standards without a pillar")}</span>,
        children: tree.unassigned.map(standardNode).filter((x): x is DataNode => !!x),
      }] : []),
    ],
  }];

  // البحث يفتح كل ما وجد فيه، وبدونه تفتح الركائز ومسار العنصر المختار.
  const expanded = useMemo(() => {
    const keys = new Set<string>(["root", ...tree.pillars.map((p) => `P:${p.id}`), "GU"]);
    const walk = (nodes: DataNode[] | undefined) => nodes?.forEach((n) => { if (q) keys.add(String(n.key)); walk(n.children); });
    walk(data);
    for (const s of allStandards(tree)) {
      if (selected === `S:${s.id}` || s.initiatives.some((m) => selected === `M:${m.id}`) || s.indicators.some((i) => selected === `I:${i.code}`)) {
        keys.add(`S:${s.id}`);
        keys.add(`GM${s.id}`);
        keys.add(`GI${s.id}`);
      }
    }
    return [...keys];
  }, [q, tree, selected]); // eslint-disable-line react-hooks/exhaustive-deps -- data مشتق من tree وq
  const [open, setOpen] = useState<string[] | null>(null);

  return (
    <Tree className="t1-tree" blockNode showLine={{ showLeafIcon: false }} treeData={data} selectedKeys={[selected]}
      expandedKeys={q ? expanded : open ?? expanded} onExpand={(k) => setOpen(k.map(String))}
      onSelect={(k) => k.length > 0 && onSelect(String(k[0]))} />
  );
}

// ----------------------------------------------------------------- التفاصيل

interface DetailProps { tree: StrategyTree; onSelect: (k: string) => void; onEdit?: (e: Editing) => void }

/** القيمة في جدول السنوات: الرقم وحده، والنسبة بعلامتها؛ الوحدة مكتوبة بجانب اسم المؤشر. */
function short(v: number | null, unit: string) {
  return v === null ? "—" : unit.trim() === "%" ? `${num(v)}%` : num(v);
}

function Description({ text, empty }: { text: string | null; empty: string }) {
  return text ? <p className="t1-desc">{text}</p> : <p className="t1-help">{empty}</p>;
}

function Counts({ items }: { items: { label: string; value: number }[] }) {
  return <div className="t1-plan-counts">{items.map((i) => <div key={i.label}><b>{i.value}</b><span>{i.label}</span></div>)}</div>;
}

function RootDetail({ tree, onSelect }: DetailProps) {
  const standards = allStandards(tree);
  const indicators = standards.flatMap((s) => s.indicators);
  const first = tree.years[0];
  const last = tree.years[tree.years.length - 1];
  const late = indicators.filter((i) => (i.planStartYear ?? first) > first).length;
  const short = indicators.filter((i) => (i.planEndYear ?? last) - (i.planStartYear ?? first) + 1 <= 3).length;
  return (
    <>
      <Panel title={L("الخطة في سطور", "The plan at a glance")} icon={<ApartmentOutlined aria-hidden />}>
        <Counts items={[
          { label: L("ركائز", "Pillars"), value: tree.pillars.length },
          { label: L("معايير", "Standards"), value: standards.length },
          { label: L("مبادرات", "Initiatives"), value: standards.reduce((n, s) => n + s.initiatives.length, 0) },
          { label: L("مؤشرات", "Indicators"), value: indicators.length },
          { label: L("مستهدفها قصير أو متوسط المدى", "Short/medium-term"), value: short },
          { label: L("يبدأ مستهدفها متأخرًا", "Start later"), value: late },
        ]} />
        <h3 className="t1-form-sec">{L("كيف تقرأ الشجرة", "How to read the tree")}</h3>
        <ul className="t1-howto">
          <li>{L("الركيزة محور كبير في الخطة، وتحتها معاييرها.", "A pillar is a major axis; its standards sit under it.")}</li>
          <li>{L("لكل معيار مبادرات (ما ستنفذه الجامعة) ومؤشرات (ما يقاس به التقدم).", "Each standard has initiatives (what will be done) and indicators (how progress is measured).")}</li>
          <li>{L(`لكل مؤشر مستهدف في كل سنة من ${first} إلى ${last}: منها ما يتحقق بعد سنتين أو ثلاث، ومنها ما يمتد خمس سنوات، ومنها ما يبدأ متأخرًا.`, `Each indicator has a target per year from ${first} to ${last}.`)}</li>
          <li>{L("وفي صفحة المؤشر شرح كيف تحسب قيمته على مستوى الكلية وعلى مستوى الجامعة.", "The indicator page explains how its college and university values are computed.")}</li>
        </ul>
      </Panel>
      <Panel title={L("الركائز", "Pillars")} icon={<FlagOutlined aria-hidden />}>
        <div className="t1-cards">
          {tree.pillars.map((p) => (
            <button key={p.id} type="button" className="t1-card" onClick={() => onSelect(`P:${p.id}`)}>
              <code>{p.code}</code><b>{p.nameAr}</b>
              <small>{L(`${p.standards.length} معيار · ${p.standards.reduce((n, s) => n + s.indicators.length, 0)} مؤشر`, `${p.standards.length} standards`)}</small>
            </button>
          ))}
        </div>
      </Panel>
    </>
  );
}

function PillarDetail({ tree, pillar, onSelect, onEdit }: DetailProps & { pillar: PillarNode }) {
  const a = tree.actions;
  return (
    <>
      <Panel title={<span className="t1-code-title"><code>{pillar.code}</code>{pillar.nameAr}</span>} icon={<FlagOutlined aria-hidden />}
        extra={<span className="t1-sticky-actions">
          {a.canEdit && <Button icon={<EditOutlined />} onClick={() => onEdit!({ kind: "pillar", pillar })}>{L("تعديل", "Edit")}</Button>}
          {a.canAdd && <Button icon={<PlusOutlined />} onClick={() => onEdit!({ kind: "standard", pillarId: pillar.id })}>{L("معيار جديد", "New standard")}</Button>}
          {a.canDelete && <Button danger icon={<DeleteOutlined />} aria-label={L("حذف الركيزة", "Delete pillar")}
            onClick={() => onEdit!({ kind: "delete", title: L(`حذف الركيزة ${pillar.code}`, `Delete ${pillar.code}`), url: `/api/strategy/pillars/${pillar.id}/delete`, after: "root",
              explain: pillar.standards.length > 0 ? L("لا تحذف ركيزة تحتها معايير؛ انقل معاييرها إلى ركيزة أخرى أولًا.", "Move its standards first.") : L("تحذف الركيزة نهائيًا ويبقى حذفها في السجل.", "The pillar is deleted; the log keeps the record.") })} />}
        </span>}>
        {pillar.nameEn && <p className="t1-help ltr">{pillar.nameEn}</p>}
        <Description text={pillar.description} empty={L("لا شرح لهذه الركيزة بعد. اضغط «تعديل» لكتابته.", "No description yet.")} />
      </Panel>
      <Panel title={L(`معايير الركيزة (${pillar.standards.length})`, `Standards (${pillar.standards.length})`)} icon={<ApartmentOutlined aria-hidden />}>
        {pillar.standards.length === 0 ? <EmptyNote>{L("لا معايير تحت هذه الركيزة بعد.", "No standards yet.")}</EmptyNote> : (
          <div className="t1-cards">
            {pillar.standards.map((s) => (
              <button key={s.id} type="button" className="t1-card" onClick={() => onSelect(`S:${s.id}`)}>
                <code>{s.code}</code><b>{s.nameAr}</b>
                <small>{L(`${s.initiatives.length} مبادرة · ${s.indicators.length} مؤشر`, `${s.initiatives.length} initiatives · ${s.indicators.length} indicators`)}</small>
              </button>
            ))}
          </div>
        )}
      </Panel>
    </>
  );
}

function StandardDetail({ tree, standard, onSelect, onEdit }: DetailProps & { standard: StandardNode }) {
  const a = tree.actions;
  const first = tree.years[0];
  const last = tree.years[tree.years.length - 1];
  const pillar = tree.pillars.find((p) => p.id === standard.pillarId);
  return (
    <>
      <Panel title={<span className="t1-code-title"><code>{standard.code}</code>{standard.nameAr}</span>} icon={<ApartmentOutlined aria-hidden />}
        extra={<span className="t1-sticky-actions">
          {a.canEdit && <Button icon={<EditOutlined />} onClick={() => onEdit!({ kind: "standard", standard })}>{L("تعديل", "Edit")}</Button>}
          {a.canDelete && <Button danger icon={<DeleteOutlined />} aria-label={L("حذف المعيار", "Delete standard")}
            onClick={() => onEdit!({ kind: "delete", title: L(`حذف المعيار ${standard.code}`, `Delete ${standard.code}`), url: `/api/strategy/standards/${standard.id}/delete`,
              after: pillar ? `P:${pillar.id}` : "root",
              explain: standard.indicators.length + standard.initiatives.length > 0
                ? L("لا يحذف معيار تحته مؤشرات أو مبادرات؛ احذفها أو انقلها أولًا.", "Delete or move its indicators and initiatives first.")
                : L("يحذف المعيار نهائيًا ويبقى حذفه في السجل.", "The standard is deleted; the log keeps the record.") })} />}
        </span>}>
        {pillar && <p className="t1-help">{L("الركيزة:", "Pillar:")} <button type="button" className="t1-link" onClick={() => onSelect(`P:${pillar.id}`)}>{pillar.code} — {pillar.nameAr}</button></p>}
        <Description text={standard.description} empty={L("لا شرح لهذا المعيار بعد. اضغط «تعديل» لكتابته.", "No description yet.")} />
      </Panel>

      <Panel title={L(`المبادرات (${standard.initiatives.length})`, `Initiatives (${standard.initiatives.length})`)} icon={<RocketOutlined aria-hidden />}
        extra={a.canAdd && <Button size="small" icon={<PlusOutlined />} onClick={() => onEdit!({ kind: "initiative", standardId: standard.id })}>{L("مبادرة جديدة", "New initiative")}</Button>}>
        {standard.initiatives.length === 0 ? <EmptyNote>{L("لا مبادرات لهذا المعيار بعد.", "No initiatives yet.")}</EmptyNote> : (
          <ul className="t1-rows">
            {standard.initiatives.map((m) => (
              <li key={m.id}>
                <button type="button" onClick={() => onSelect(`M:${m.id}`)}>
                  <code>{m.code}</code><b>{m.nameAr}</b>
                  <Tag color={initiativeStatus[m.status].color}>{initiativeStatus[m.status].text}</Tag>
                  <small className="ltr">{m.startYear}–{m.endYear}</small>
                </button>
              </li>
            ))}
          </ul>
        )}
      </Panel>

      <Panel title={L(`المؤشرات (${standard.indicators.length})`, `Indicators (${standard.indicators.length})`)} icon={<AimOutlined aria-hidden />}
        extra={a.canAddIndicator && <Button size="small" icon={<PlusOutlined />} onClick={() => onEdit!({ kind: "indicator", standardCode: standard.code })}>{L("مؤشر جديد", "New indicator")}</Button>}>
        {standard.indicators.length === 0 ? <EmptyNote>{L("لا مؤشرات لهذا المعيار بعد.", "No indicators yet.")}</EmptyNote> : (
          <div className="t1-plan-table" role="table" aria-label={L("مستهدفات المؤشرات بالسنوات", "Indicator targets by year")}>
            <div role="row" className="head">
              <span role="columnheader">{L("المؤشر", "Indicator")}</span>
              <span role="columnheader">{L("المدة", "Span")}</span>
              <span role="columnheader" className="ltr">{tree.baselineYear}</span>
              {tree.years.map((y) => <span key={y} role="columnheader" className="ltr">{y}</span>)}
            </div>
            {standard.indicators.map((i) => (
              <div role="row" key={i.code} onClick={() => onSelect(`I:${i.code}`)} className="click">
                <span role="cell"><code>{i.code}</code> {i.nameAr} <small className="muted">({i.unit})</small></span>
                <Tooltip title={horizon(i.planStartYear, i.planEndYear, first, last)}>
                  <span role="cell" className="t1-help ltr">{(i.planStartYear ?? first) > first ? `${i.planStartYear}–${i.planEndYear ?? last}` : `→ ${i.planEndYear ?? last}`}</span>
                </Tooltip>
                <span role="cell" className="ltr muted">{short(i.baseline, i.unit)}</span>
                {tree.years.map((y) => {
                  const off = y < (i.planStartYear ?? first);
                  const v = i.targets.find((x) => x.year === y)?.value;
                  return <span key={y} role="cell" className={`ltr${off ? " muted" : ""}${y === (i.planEndYear ?? last) ? " goal" : ""}`}>{off ? "—" : short(v ?? null, i.unit)}</span>;
                })}
              </div>
            ))}
          </div>
        )}
      </Panel>
    </>
  );
}

function InitiativeDetail({ tree, initiative: m, standard, onSelect, onEdit }: DetailProps & { initiative: InitiativeNode; standard: StandardNode }) {
  const a = tree.actions;
  const indicators = allStandards(tree).flatMap((s) => s.indicators).filter((i) => m.indicatorCodes.includes(i.code));
  return (
    <Panel title={<span className="t1-code-title"><code>{m.code}</code>{m.nameAr}</span>} icon={<RocketOutlined aria-hidden />}
      extra={<span className="t1-sticky-actions">
        {a.canEdit && <Button icon={<EditOutlined />} onClick={() => onEdit!({ kind: "initiative", initiative: m, standardId: standard.id })}>{L("تعديل", "Edit")}</Button>}
        {a.canDelete && <Button danger icon={<DeleteOutlined />} aria-label={L("حذف المبادرة", "Delete initiative")}
          onClick={() => onEdit!({ kind: "delete", title: L(`حذف المبادرة ${m.code}`, `Delete ${m.code}`), url: `/api/strategy/initiatives/${m.id}/delete`, after: `S:${standard.id}`,
            explain: L("تحذف المبادرة نهائيًا ويبقى حذفها وسببه في السجل.", "The initiative is deleted; the log keeps the reason.") })} />}
      </span>}>
      <dl className="t1-kv">
        <dt>{L("المعيار", "Standard")}</dt><dd><button type="button" className="t1-link" onClick={() => onSelect(`S:${standard.id}`)}>{standard.code} — {standard.nameAr}</button></dd>
        <dt>{L("الحالة", "Status")}</dt><dd><Tag color={initiativeStatus[m.status].color}>{initiativeStatus[m.status].text}</Tag></dd>
        <dt>{L("المدة", "Duration")}</dt><dd className="ltr">{m.startYear} – {m.endYear}</dd>
        <dt>{L("الجهة المسؤولة", "Responsible unit")}</dt><dd>{m.ownerUnitName ?? "—"}</dd>
        <dt>{L("الشرح", "Description")}</dt><dd>{m.description ?? "—"}</dd>
        <dt>{L("المخرجات المتوقعة", "Expected outcome")}</dt><dd>{m.expectedOutcome ?? "—"}</dd>
        <dt>{L("المؤشرات التي تسهم فيها", "Indicators")}</dt>
        <dd>{indicators.length === 0 ? "—" : indicators.map((i) => <div key={i.code}><button type="button" className="t1-link" onClick={() => onSelect(`I:${i.code}`)}><code>{i.code}</code> {i.nameAr}</button></div>)}</dd>
      </dl>
    </Panel>
  );
}

function IndicatorDetail({ tree, indicator: i, standard, onSelect, onEdit }: DetailProps & { indicator: IndicatorNode; standard: StandardNode }) {
  const a = tree.actions;
  const calc = howCalculated(i);
  const initiatives = allStandards(tree).flatMap((s) => s.initiatives).filter((m) => m.indicatorCodes.includes(i.code));
  return (
    <>
      <Panel title={<span className="t1-code-title"><code>{i.code}</code>{i.nameAr}</span>} icon={<AimOutlined aria-hidden />}
        extra={<span className="t1-sticky-actions">
          <Link to={`/indicators/${i.code}`}><Button icon={<FileTextOutlined />}>{a.canEditIndicator ? L("التعريف وتعديله", "Definition") : L("بطاقة المؤشر", "Indicator card")}</Button></Link>
          {a.canDeleteIndicator && <Button danger icon={<DeleteOutlined />} aria-label={L("حذف المؤشر", "Delete indicator")}
            onClick={() => onEdit!({ kind: "delete", title: L(`حذف المؤشر ${i.code}`, `Delete ${i.code}`), url: `/api/kpi/indicators/${i.code}/delete`, after: `S:${standard.id}`,
              explain: L("يحذف المؤشر بتعريفاته ومستهدفاته إن لم يدخل أي دورة قياس. المؤشر الذي قيس لا يحذف؛ يوقف من بطاقته.",
                "Deleted with its definitions and targets if never measured; a measured indicator is suspended instead.") })} />}
        </span>}>
        <p className="t1-help">
          <button type="button" className="t1-link" onClick={() => onSelect(`S:${standard.id}`)}>{standard.code} — {standard.nameAr}</button>
          {" · "}<Tag color={definitionStatus[i.status].color}>{definitionStatus[i.status].text}</Tag>
          {i.suspended && <Tag color="error">{L("موقوف", "Suspended")}</Tag>}
        </p>
        <Description text={i.definition} empty={L("لم يكتب تعريف المؤشر بعد.", "No definition yet.")} />
        <dl className="t1-kv">
          <dt>{L("وحدة القياس", "Unit")}</dt><dd>{i.unit}</dd>
          <dt>{L("الاتجاه المطلوب", "Direction")}</dt><dd>{direction[i.direction]}</dd>
          <dt>{L("يجمع على مستوى", "Collected at")}</dt><dd>{level[i.level]}</dd>
          <dt>{L("قاعدة التجميع", "Aggregation")}</dt><dd>{aggregation[i.aggregation]}</dd>
        </dl>
      </Panel>

      <Panel title={L("كيف يحسب المؤشر", "How it is calculated")} icon={<BulbOutlined aria-hidden />}>
        <dl className="t1-kv">
          <dt>{L("ما تدخله الجهة", "What is entered")}</dt><dd>{calc.entry}</dd>
          <dt>{L("قيمة الكلية", "College value")}</dt><dd>{calc.college}</dd>
          <dt>{L("قيمة الجامعة", "University value")}</dt><dd>{calc.university}</dd>
          <dt>{L("الحكم على الأداء", "Judgement")}</dt><dd>{calc.judge}</dd>
        </dl>
      </Panel>

      <IndicatorPlan node={i} tree={tree} />

      {initiatives.length > 0 && (
        <Panel title={L(`مبادرات تسهم فيه (${initiatives.length})`, `Contributing initiatives (${initiatives.length})`)} icon={<RocketOutlined aria-hidden />}>
          <ul className="t1-rows">
            {initiatives.map((m) => (
              <li key={m.id}><button type="button" onClick={() => onSelect(`M:${m.id}`)}><code>{m.code}</code><b>{m.nameAr}</b><small className="ltr">{m.startYear}–{m.endYear}</small></button></li>
            ))}
          </ul>
        </Panel>
      )}
    </>
  );
}

// ----------------------------------------------------------------- النوافذ

function Editors({ tree, editing, onClose, onSelect }: { tree: StrategyTree; editing: Editing | null; onClose: () => void; onSelect: (k: string) => void }) {
  const { message } = App.useApp();
  const refresh = useRefreshPlan();
  if (!editing) return null;
  switch (editing.kind) {
    case "pillar": return <PillarDrawer pillar={editing.pillar} onClose={onClose} />;
    case "standard": return <StandardDrawer standard={editing.standard} pillars={tree.pillars} pillarId={editing.pillarId} onClose={onClose} />;
    case "initiative": return <InitiativeDrawer initiative={editing.initiative} standardId={editing.standardId} tree={tree} onClose={onClose} />;
    case "indicator": return <NewIndicatorModal standards={allStandards(tree)} standard={editing.standardCode} onClose={onClose} />;
    case "delete":
      return (
        <DecisionModal title={editing.title} explain={editing.explain} field="reason" label={L("سبب الحذف", "Reason")} okText={L("حذف", "Delete")} danger onClose={onClose}
          onSubmit={async (reason) => {
            await api.post(editing.url, { reason });
            await refresh();
            onSelect(editing.after);
            message.success(L("تم الحذف.", "Deleted."));
          }} />
      );
  }
}
