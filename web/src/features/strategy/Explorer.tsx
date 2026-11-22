import { useEffect, useRef, type ReactNode } from "react";
import { Button, Tag, Tooltip } from "antd";
import {
  AimOutlined, ApartmentOutlined, BulbOutlined, CalendarOutlined, DeleteOutlined, EditOutlined, FileTextOutlined, FlagFilled,
  FlagOutlined, LeftOutlined, PlusOutlined, RightOutlined, RocketOutlined, TrophyOutlined, UnorderedListOutlined,
} from "@ant-design/icons";
import { Link, Navigate, useSearchParams } from "react-router-dom";
import type { IndicatorNode, InitiativeNode, PillarNode, StandardNode } from "../../api/tier1";
import { howCalculated } from "../../shared/calc";
import { L, useLang } from "../../shared/i18n";
import { UniTarget } from "../../shared/CollegeTarget";
import { aggregation, definitionStatus, direction, horizon, initiativeStatus, level, num, performance, withUnit } from "../../shared/labels";
import { EmptyNote } from "../../shared/ui";
import { IndicatorPlan } from "./IndicatorPlan";
import { InitiativeActivities } from "./InitiativeActivities";
import {
  allIndicators, allInitiatives, boardSearch, detailSearch, dotState, dotText, finalTarget, find, pillarIndexOf, pillarNum, span, targetOf,
  targetsComplete, tint, usePlan, type Selection,
} from "./model";
import { DotLegend, Dots } from "./PlanMap";
import { Sparkline, YearBar } from "./PlanCharts";
import { stdLabel } from "../../shared/pillars";

/**
 * صفحة تفاصيل عنصر واحد (?view=explore&node=P:|S:|M:|I:): ركيزة أو معيار أو مبادرة أو مؤشر بلون ركيزته،
 * وفوقها سطر المسار من «الهيكل والعلاقات» إلى العنصر، وكل عنصر مرتبط فيها يفتح صفحته بإضافة إلى السجل،
 * والإضافة والتعديل والحذف من مكانها بحسب الصلاحية. لا شجرة ولا «الخطة كاملة»: بلا عنصر صالح تحول إلى اللوحة بالاستبدال.
 */
export function Explorer({ selected }: { selected: string }) {
  const { tree } = usePlan();
  const [params] = useSearchParams();
  const rootRef = useRef<HTMLDivElement>(null);
  const sel = find(tree, selected);
  const missing = sel.kind === "root";

  // كل عنصر جديد صفحة جديدة: تبدأ من أولها، والتركيز على عنوانها فيقرؤه قارئ الشاشة.
  useEffect(() => {
    if (missing) return;
    window.scrollTo({ top: 0 });
    rootRef.current?.querySelector<HTMLElement>("h1")?.focus({ preventScroll: true });
  }, [selected, missing]);

  if (sel.kind === "root") return <Navigate replace to={{ search: boardSearch(tree, null, params.get("pillar")) }} />;
  const index = sel.kind === "pillar" ? tree.pillars.findIndex((p) => p.id === sel.pillar.id) : pillarIndexOf(tree, sel.standard);

  return (
    <div ref={rootRef} className="sp-detail" style={tint(index)}>
      <PathLine sel={sel} node={selected} />
      {sel.kind === "pillar" && <PillarDetail pillar={sel.pillar} />}
      {sel.kind === "standard" && <StandardDetail standard={sel.standard} />}
      {sel.kind === "initiative" && <InitiativeDetail initiative={sel.initiative} standard={sel.standard} />}
      {sel.kind === "indicator" && <IndicatorDetail indicator={sel.indicator} standard={sel.standard} />}
    </div>
  );
}

// ----------------------------------------------------------------- سطر المسار

/** «م1» في العربية و«S1» في الإنجليزية للمعايير المرقمة؛ وغيرها برمزه. */
function stdShort(code: string): string {
  return L(stdLabel(code), /^\d+$/.test(code) ? `S${code}` : code);
}

/**
 * «الهيكل والعلاقات ‹ الركيزة 01 ‹ م1 ‹ 1.01»: الأول يفتح اللوحة على ركيزة العنصر (والمؤشر أو المبادرة محددًا فيها)،
 * والركيزة والمعيار صفحتا تفاصيلهما، والأخير الصفحة الحالية (aria-current). الفاصل يتجه مع اللغة كما في مسار التنقل السابق.
 */
function PathLine({ sel, node }: { sel: Exclude<Selection, { kind: "root" }>; node: string }) {
  const { tree } = usePlan();
  const Sep = useLang() === "ar" ? LeftOutlined : RightOutlined;
  const standard = sel.kind === "pillar" ? null : sel.standard;
  const pillar = sel.kind === "pillar" ? sel.pillar : tree.pillars[pillarIndexOf(tree, sel.standard)] ?? null;
  const steps: { key: string; to?: string; label: ReactNode; title?: string }[] = [
    { key: "board", to: boardSearch(tree, node), label: L("الهيكل والعلاقات", "Structure & links") },
  ];
  if (pillar) {
    const n = pillarNum(tree.pillars.indexOf(pillar));
    steps.push({ key: `P:${pillar.id}`, to: detailSearch(`P:${pillar.id}`), label: <>{L("الركيزة", "Pillar")} <span className="ltr-nums">{n}</span></>, title: pillar.nameAr });
  }
  if (standard) steps.push({ key: `S:${standard.id}`, to: detailSearch(`S:${standard.id}`), label: stdShort(standard.code), title: standard.nameAr });
  if (sel.kind === "initiative") steps.push({ key: node, label: <span className="ltr-nums">{sel.initiative.code}</span>, title: sel.initiative.nameAr });
  if (sel.kind === "indicator") steps.push({ key: node, label: <span className="ltr-nums">{sel.indicator.code}</span>, title: sel.indicator.nameAr });

  return (
    <nav className="sp-path" aria-label={L("موقعك في الخطة", "Where you are")}>
      <ol>
        {steps.map((s, k) => {
          const current = k === steps.length - 1;
          return (
            <li key={s.key}>
              {k > 0 && <Sep aria-hidden className="sp-path-sep" />}
              {current ? <span aria-current="page" title={s.title}>{s.label}</span> : <Link to={{ search: s.to }} title={s.title}>{s.label}</Link>}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

// ----------------------------------------------------------------- رأس التفاصيل

function DetailHead({ index, kind, icon, code, title, sub, chips, actions, children }: {
  index: number; kind?: string; icon: ReactNode; code?: string; title: string; sub?: string | null; chips?: ReactNode; actions?: ReactNode;
  children?: ReactNode;
}) {
  // بلا kind وcode لا يظهر سطر النوع والرمز فوق العنوان (الركيزة: يكفي اسمها، كما في الخريطة).
  // العنوان h1 الصفحة (اسم الخطة في الشريط العلوي نص هنا)، وtabIndex=-1 ليستقبل التركيز بعد كل انتقال.
  return (
    <header className="sp-dhead" style={tint(index)}>
      <div className="sp-dhead-row">
        <span className="sp-dhead-icon" aria-hidden>{icon}</span>
        <div className="sp-dhead-text">
          {kind && code && <small>{kind} <code>{code}</code></small>}
          <h1 tabIndex={-1}>{title}</h1>
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

/** صف مؤشر: الاسم، وأفق المستهدف، ومسار المستهدف المصغر، والمستهدف النهائي، وحالته. النقر يفتح صفحة المؤشر. */
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
          <b className="ltr-nums"><UniTarget>{fmt(i, goal)}</UniTarget></b>
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

// ----------------------------------------------------------------- الركيزة

/** اسم طويل في عنوان نافذة: يقطع بنقاط بعد 40 حرفًا. */
function clip(text: string, max = 40): string {
  return text.length > max ? `${text.slice(0, max).trimEnd()}…` : text;
}

function PillarDetail({ pillar }: { pillar: PillarNode }) {
  const { tree, edit } = usePlan();
  const a = tree.actions;
  const index = tree.pillars.findIndex((p) => p.id === pillar.id);
  const indicators = pillar.standards.flatMap((s) => s.indicators);
  const initiatives = pillar.standards.flatMap((s) => s.initiatives);
  return (
    <>
      <DetailHead index={index} icon={<FlagOutlined />} title={pillar.nameAr} sub={pillar.nameEn}
        actions={<>
          {a.canEdit && <Button icon={<EditOutlined />} onClick={() => edit({ kind: "pillar", pillar })}>{L("تعديل", "Edit")}</Button>}
          {a.canAdd && <Button icon={<PlusOutlined />} onClick={() => edit({ kind: "standard", pillarId: pillar.id })}>{L("معيار جديد", "New standard")}</Button>}
          {a.canDelete && <Tooltip title={L("حذف الركيزة", "Delete pillar")}><Button danger icon={<DeleteOutlined />} aria-label={L("حذف الركيزة", "Delete pillar")}
            onClick={() => edit({ kind: "delete", title: L(`حذف الركيزة ${pillarNum(index)} — ${clip(pillar.nameAr)}`, `Delete pillar ${pillarNum(index)} — ${clip(pillar.nameEn ?? pillar.nameAr)}`), url: `/api/strategy/pillars/${pillar.id}/delete`, after: "board",
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
        actions={<>
          {a.canEdit && <Button icon={<EditOutlined />} onClick={() => edit({ kind: "standard", standard })}>{L("تعديل", "Edit")}</Button>}
          {a.canDelete && <Tooltip title={L("حذف المعيار", "Delete standard")}><Button danger icon={<DeleteOutlined />} aria-label={L("حذف المعيار", "Delete standard")}
            onClick={() => edit({ kind: "delete", title: L(`حذف المعيار ${standard.code}`, `Delete ${standard.code}`), url: `/api/strategy/standards/${standard.id}/delete`,
              after: pillar ? `P:${pillar.id}` : "board",
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
  const indicators = allIndicators(tree).filter((i) => m.indicatorCodes.includes(i.code));
  const years = m.endYear - m.startYear + 1;
  return (
    <>
      <DetailHead index={index} kind={L("المبادرة", "Initiative")} icon={<RocketOutlined />} code={m.code} title={m.nameAr}
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
          { label: L("الأنشطة", "Activities"), value: m.activityCount, hint: m.activityProgress !== null ? L(`إنجاز ${m.activityProgress}%`, `${m.activityProgress}% done`) : undefined },
        ]} />
        <div className="sp-dhead-bar"><YearBar years={tree.years} start={m.startYear} end={m.endYear} label={L(`تنفذ من ${m.startYear} إلى ${m.endYear}`, `${m.startYear}–${m.endYear}`)} /></div>
      </DetailHead>

      <Section title={L(`أنشطة المبادرة (${m.activityCount})`, `Activities (${m.activityCount})`)} icon={<UnorderedListOutlined aria-hidden />}>
        <InitiativeActivities initiativeId={m.id} canAdd={a.canAdd} canEdit={a.canEdit} canDelete={a.canDelete} />
      </Section>

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
          { label: L(`المستهدف في ${end}`, `Target in ${end}`), value: <span className="ltr-nums"><FlagFilled aria-hidden className="sp-goal-flag" /> <UniTarget>{fmt(i, goal)}</UniTarget></span>,
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
