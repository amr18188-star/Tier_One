import { useLayoutEffect, useRef, useState } from "react";
import { Alert, Button, Drawer, Input, Segmented, Select, Space, Tag, Tooltip } from "antd";
import { DownloadOutlined, PrinterOutlined, QuestionCircleOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { api, query } from "../../api/client";
import type { HeatCell, IndicatorResult, IndicatorResults, Performance, Results, StandardResult } from "../../api/collection";
import { PillarBand, StdCode, stdLabel, tint, usePillarMap, type PillarInfo } from "../../shared/pillars";
import { AccordionPanel } from "../../shared/AccordionPanel";
import { HBarChart, ProgressToTarget, SegmentBar } from "../../shared/charts";
import { L } from "../../shared/i18n";
import { PeriodNav } from "../../shared/PeriodNav";
import { num, performance as perfText, withUnit } from "../../shared/labels";
import { PendingTarget } from "../../shared/PendingTarget";
import { CollegeTarget, shownOf, UniTarget } from "../../shared/CollegeTarget";
import { DataTable, EmptyNote, FilterMeta, oneOf, PageHeader, Panel } from "../../shared/ui";
import { usePeriodParams, useSearchState } from "../../shared/usePeriodParams";
import { ReportView } from "./ReportView";
import { groupResultIndicators } from "./indicatorGroups";
import "./results.css";
import { useCycles, queryKeys } from "../../api/queries";

const PERF: Performance[] = ["Achieved", "Near", "Behind", "NoData", "NoTarget", "Incomplete", "NotStarted"];

/** حصة من مستهدف الجامعة (0–1) نسبة مئوية بخانة عشرية واحدة؛ الحصة الموجبة الأصغر من 0.1% لا تعرض صفرًا. */
const pct = (x: number) => (x > 0 && x < 0.0005 ? "<0.1%" : `${Math.round(x * 1000) / 10}%`);
const PERF_COLORS: Record<string, string> = { Achieved: "var(--ds-status-ok)", Near: "var(--ds-status-near)", Behind: "var(--ds-status-late-ink)", NoData: "var(--ds-status-none)", NoTarget: "var(--ds-status-none-ink)" };

/**
 * النتائج: القيم المعتمدة نهائيًا مقابل المستهدف للجامعة أو لكلية، بملخص لكل معيار، وخريطة الكليات × المعايير،
 * وجدول المؤشرات بحالة الأداء والتغير عن الدورة السابقة. النقر على مؤشر يفتح مقارنة الكليات واتجاهه عبر الدورات.
 */
export default function ResultsPage() {
  // الرابط هو الحالة (cycle وyear وunit وperf وstd وq) في نسخة واحدة يكتب منها شريط السنة والربع والمرشحات.
  const search = useSearchState();
  const { params, update } = search;
  const cycleParam = params.get("cycle") ?? undefined;
  const unitParam = params.get("unit") ?? undefined;
  const perf = oneOf(params.get("perf"), PERF);
  const std = params.get("std") ?? undefined;
  const q = params.get("q") ?? "";
  const [open, setOpen] = useState<IndicatorResult | null>(null);
  const [openStandard, setOpenStandard] = useState<string | null>(null);
  const [printing, setPrinting] = useState(false);
  const pickerRef = useRef<HTMLDivElement>(null);
  const pendingUnitRef = useRef<{ id: string; focusPicker: boolean } | null>(null);
  const pillarMap = usePillarMap();

  const set = (key: string, value: string | undefined) => update({ [key]: value });

  const cycles = useCycles();
  const res = useQuery({
    queryKey: queryKeys.results(cycleParam, unitParam),
    queryFn: ({ signal }) => api.get<Results | null>(`/api/results${query({ cycleId: cycleParam, unitId: unitParam })}`, { signal }),
  });
  const r = res.data;
  useLayoutEffect(() => {
    const pending = pendingUnitRef.current;
    if (!r || r.unit.id !== pending?.id) return;
    pendingUnitRef.current = null;

    // The shell normally scrolls the document, but use a local scroll container if one owns this page.
    let scroller: Element | null = pickerRef.current?.parentElement ?? null;
    while (scroller) {
      const overflow = window.getComputedStyle(scroller).overflowY;
      if (/(auto|scroll)/.test(overflow) && scroller.scrollHeight > scroller.clientHeight) break;
      scroller = scroller.parentElement;
    }
    (scroller ?? document.scrollingElement ?? document.documentElement).scrollTo({ top: 0, behavior: "auto" });
    if (pending.focusPicker) pickerRef.current?.querySelector<HTMLElement>("[role=combobox]")?.focus({ preventScroll: true });
  }, [r]);

  const pickUnit = (unitId: string, focusPicker = false) => {
    if (unitId === r?.unit.id) return;
    pendingUnitRef.current = { id: unitId, focusPicker };
    set("unit", unitId);
  };
  // الدورات التي لها نتائج كما كانت في القائمة: غير المسودة؛ الربع بلا دورة منها «لا دورة».
  const live = cycles.data?.filter((c) => c.status !== "Draft");
  // السنوات التي فيها دورة منها فقط: نقر سنة يفتح دورة لها نتائج، لا سنة فارغة يعود فيها الخادم إلى دورته الافتراضية.
  const years = live && [...new Set(live.map((c) => c.targetYear))];
  // الافتراضي كما كان من الخادم (أحدث دورة فيها اعتماد، وإلا أحدثها)؛ الطلب يبقى بـ cycle= من الرابط.
  const period = usePeriodParams({ mode: "cycle", cycles: live, years, search, defaults: { cycleId: r?.cycle.id } });

  if (res.isSuccess && !r)
    return (
      <>
        <PageHeader title={L("النتائج والمقارنات", "Results")} />
        <Panel><EmptyNote>{L("لا توجد دورة بعد. تظهر النتائج بعد اعتماد أول نماذج في دورة.", "No cycle yet.")}</EmptyNote></Panel>
      </>
    );

  const rows = (r?.indicators ?? []).filter((i) => (!perf || i.performance === perf) && (!std || i.standardCode === std)
    && (!q || i.name.includes(q) || i.code.toLowerCase().includes(q.toLowerCase())));
  const indicatorsFiltered = !!(perf || std || q);
  const indicatorGroups = r ? groupResultIndicators(r.standards, r.indicators) : [];
  const count = (p: Performance) => r?.indicators.filter((i) => i.performance === p).length ?? 0;
  const who = r?.unit.kind === "University" ? L("الجامعة", "University") : r?.unit.name;

  const renderIndicatorsTable = (data: IndicatorResult[], resetKey: string) => (
    <DataTable<IndicatorResult>
      rowKey={(i) => i.indicatorId}
      data={data}
      loading={res.isLoading}
      resetKey={resetKey}
      onRowClick={setOpen}
      rowLabel={(i) => L(`تفاصيل نتيجة ${i.code} ${i.name}`, `Result details ${i.code}`)}
      pageSizes={[20, 50, 100]}
      columns={[
        { title: L("الرمز", "Code"), dataIndex: "code", width: 96, render: (c: string, i) => <StdCode code={c} standard={i.standardCode} map={pillarMap.map} /> },
        { title: L("المؤشر", "Indicator"), dataIndex: "name", ellipsis: true },
        { title: L("الفعلي", "Actual"), key: "a", width: 110, render: (_, i) => <b className="ltr">{withUnit(i.actual, i.unit)}</b> },
        {
          // DEC-5: the college's target right after its value; without one, the university's target underlined (display only).
          title: r?.unit.kind === "University" ? L("المستهدف", "Target") : L("مستهدف الكلية", "College target"), key: "t", width: 140, render: (_, i) => (
            <span>
              <CollegeTarget target={shownOf(i)} unit={i.unit} university={r?.unit.kind === "University"} />
              <PendingTarget value={i.pendingTarget} year={i.pendingTargetYear} unit={i.unit} />
            </span>
          ),
        },
        { title: L("الإنجاز", "Achievement"), key: "p", width: 190, render: (_, i) => <Achievement i={i} /> },
        ...(r && r.unit.kind !== "University" ? [{
          title: L("مستهدف الجامعة (للاطلاع)", "University target (reference)"), key: "u", width: 170,
          render: (_: unknown, i: IndicatorResult) => i.universityTotalTarget != null ? (
            // مؤشر مجموع (عدد): مساهمة الكلية في رقم الجامعة المرجو، مستهدفًا وإنجازًا.
            <Tooltip title={L(`مستهدف الجامعة كلها ${withUnit(i.universityTotalTarget, i.unit)}${i.targetShare != null ? `؛ مستهدف كليتك ${pct(i.targetShare)} منه` : ""}${i.actualShare != null ? `، وما حققته ${pct(i.actualShare)} منه` : ""}.`,
              `University total ${withUnit(i.universityTotalTarget, i.unit)}.`)}>
              <span className="t1-share">
                <small className="muted">{L("الحصة", "Share")}</small>{" "}
                <span className="ltr">{i.targetShare != null ? pct(i.targetShare) : "—"}</span>
                {i.actualShare != null && <> · <small className="muted">{L("حققت", "Done")}</small> <b className="ltr">{pct(i.actualShare)}</b></>}
              </span>
            </Tooltip>
          ) : i.universityTarget === null ? <span className="muted">—</span> : (
            <Tooltip title={L(`مستهدف الجامعة ${withUnit(i.universityTarget, i.unit)}`, `University target ${withUnit(i.universityTarget, i.unit)}`)}>
              <span className="ltr"><UniTarget>{withUnit(i.universityTarget, i.unit)}</UniTarget></span>
              {i.universityAchievement !== null && <b className="ltr ms">{Math.round(i.universityAchievement * 100)}%</b>}
            </Tooltip>
          ),
        }] : []),
        { title: L("عن السابقة", "vs previous"), key: "d", width: 110, render: (_, i) => <Delta i={i} /> },
        { title: L("الحالة", "Status"), dataIndex: "performance", width: 150, render: (p: Performance) => <><i className={`perf-dot perf-${perfText[p].tone}`} />{perfText[p].text}</> },
      ]}
      renderCard={(i) => (
        <>
          <div className="dt-card-row"><StdCode code={i.code} standard={i.standardCode} map={pillarMap.map} /><span><i className={`perf-dot perf-${perfText[i.performance].tone}`} />{perfText[i.performance].text}</span></div>
          <b>{i.name}</b>
          <small>{i.summary}</small>
        </>
      )}
      empty={L("لا مؤشرات بهذه المرشحات.", "No indicators match.")}
    />
  );

  return (
    <>
      <PageHeader title={L("النتائج والمقارنات", "Results and comparisons")}
        subtitle={r && <>{r.cycle.name} · {L("مقارنة بمستهدف", "Against the target of")} <b className="ltr">{r.cycle.targetYear}</b>{r.cycle.trial && <Tag color="purple" className="ms">{L("بروفة", "Rehearsal")}</Tag>}</>}
        stats={r ? [
          { label: L("محقق", "Achieved"), value: count("Achieved"), tone: "ok" },
          { label: L("قريب من المستهدف", "Near"), value: count("Near"), tone: "warn" },
          { label: L("متأخر", "Behind"), value: count("Behind"), tone: "bad" },
          { label: L("بلا بيانات معتمدة", "No approved data"), value: count("NoData") },
          { label: L("النماذج المعتمدة", "Approved forms"), value: <span className="ltr">{r.formsApproved}/{r.formsTotal}</span> },
        ] : undefined}
        extra={r && (
          <Space wrap>
            <Button icon={<PrinterOutlined />} onClick={() => setPrinting(true)}>{L("تقرير للطباعة", "Printable report")}</Button>
            {r.canExport && <Button icon={<DownloadOutlined />} href={`/api/results/${r.cycle.id}/export.xlsx${query({ unitId: r.unit.id })}`}>{L("تصدير Excel", "Export Excel")}</Button>}
          </Space>
        )} />

      {period.ready && !!live?.length && (cycleParam || r) && (
        <PeriodNav mode="cycle" cycles={live} years={years} value={period.value} onChange={period.onChange} ariaLabel={L("دورة القياس: السنة والربع", "Measured cycle: year and quarter")} />
      )}

      <div className="r-pickers" ref={pickerRef}>
        {r && r.units.length > 1 && (
          <label className="r-picker"><span>{L("الجهة المعروضة", "Unit")}</span>
            <Select value={r.unit.id} onChange={(v) => pickUnit(v)} style={{ minWidth: 240 }} showSearch optionFilterProp="label" aria-label={L("الجهة", "Unit")}
              options={r.units.map((u) => ({ value: u.id, label: u.kind === "University" ? L("الجامعة كلها", "Whole university") : u.name }))} />
          </label>
        )}
        {r && <OverallRing r={r} />}
      </div>

      {r && !!r.formsUnderCorrection && (
        <Alert type="warning" showIcon className="mb" message={L(`${r.formsUnderCorrection} نموذج أعيد فتحه بقرار اللجنة العليا وهو قيد التصحيح؛ يحتسب بقيمه المعتمدة السابقة حتى يعاد اعتماده.`,
          `${r.formsUnderCorrection} form(s) reopened for correction count with their previously approved values until re-approved.`)} />
      )}
      {r && r.formsApproved + (r.formsUnderCorrection ?? 0) === 0 && (
        <Panel><EmptyNote>{L("لم يعتمد أي نموذج في هذه الدورة بعد؛ النتائج تحسب من القيم المعتمدة نهائيًا فقط.", "No approved forms yet.")}</EmptyNote></Panel>
      )}
      {r && r.formsApproved > 0 && r.formsApproved < r.formsTotal && (
        <Alert type="info" showIcon message={L(`النتائج مؤقتة: محسوبة من ${r.formsApproved} نموذجًا معتمدًا نهائيًا من ${r.formsTotal}. المجموع الذي لم تكتمل جهاته يظهر «بيانات جزئية» ولا يحكم عليه.`,
          `Provisional: from ${r.formsApproved} of ${r.formsTotal} approved forms.`)} />
      )}

      <Panel title={L(`الأداء بحسب المعيار — ${who ?? ""}`, "By standard")} query={res}
        extra={<span className="r-legend">{(["Achieved", "Near", "Behind", "NoData"] as const).map((p) => <span key={p}><i style={{ background: PERF_COLORS[p] }} />{p === "NoData" ? L("بلا بيانات", "No data") : perfText[p].text}</span>)}</span>}>
        {() => (
          <div className="r-standards-grid">
            {groupByPillar(r!.standards, pillarMap.map).map((g) => (
          <div key={g.key} className="r-pgroup">
            {pillarMap.pillars.length > 0 && (
              <PillarBand info={g.info}>
                <span className="ltr">{g.standards.reduce((n, s) => n + s.achieved, 0)}/{g.standards.reduce((n, s) => n + s.indicators, 0)}</span> {L("محقق", "achieved")}
              </PillarBand>
            )}
            {g.standards.map((s) => (
          <button type="button" key={s.code} className="r-std" style={{ width: "100%", background: "none", border: 0, textAlign: "start", cursor: "pointer", font: "inherit", color: "inherit" }}
            onClick={() => set("std", std === s.code ? undefined : s.code)} aria-pressed={std === s.code}>
            <StdCode code={s.code} map={pillarMap.map} />
            <span>{s.name}{std === s.code && <Tag color="cyan" className="ms">{L("مرشح", "Filtered")}</Tag>}<br />
              <small className="muted">{L(`${s.achieved} محقق · ${s.near} قريب · ${s.behind} متأخر · ${s.noData + s.noTarget} بلا بيانات من ${s.indicators}`, `${s.achieved} achieved of ${s.indicators}`)}</small></span>
            <SegmentBar compact label={s.name} segments={[
              { key: "a", label: perfText.Achieved.text, count: s.achieved, color: PERF_COLORS.Achieved },
              { key: "n", label: perfText.Near.text, count: s.near, color: PERF_COLORS.Near },
              { key: "b", label: perfText.Behind.text, count: s.behind, color: PERF_COLORS.Behind },
              { key: "d", label: L("بلا بيانات", "No data"), count: s.noData + s.noTarget, color: PERF_COLORS.NoData },
            ]} />
          </button>
            ))}
          </div>
            ))}
          </div>
        )}
      </Panel>

      {r && r.heatUnits.length > 1 && (
        <Panel title={L("خريطة الكليات × المعايير", "Colleges × standards")} extra={<Tooltip title={L("نسبة مؤشرات المعيار المحققة في الكلية مقابل مستهدفها. الرمادي: لا بيانات معتمدة.", "Share achieved per standard.")}><QuestionCircleOutlined /></Tooltip>}>
          <Heatmap r={r} map={pillarMap.map} onPick={(unitId) => pickUnit(unitId, true)} />
        </Panel>
      )}

      <Panel title={L("المؤشرات", "Indicators")}>
        <div className="lcp-filters">
          <Segmented value={perf ?? "all"} onChange={(v) => set("perf", v === "all" ? undefined : String(v))}
            options={[{ value: "all", label: L("الكل", "All") }, ...PERF.map((p) => ({ value: p, label: perfText[p].text }))]} />
          <Input.Search key={q} allowClear defaultValue={q} placeholder={L("رمز المؤشر أو اسمه", "Code or name")} aria-label={L("رمز المؤشر أو اسمه", "Code or name")} onSearch={(v) => set("q", v || undefined)} />
          {std && <Tag closable onClose={() => set("std", undefined)}>{L("المعيار", "Standard")} {std}</Tag>}
          <FilterMeta shown={rows.length} total={r?.indicators.length} noun={L("مؤشر", "indicators")} active={!!(perf || std || q)}
            onReset={() => update({ perf: undefined, std: undefined, q: undefined })} />
        </div>
        {indicatorsFiltered || !r ? renderIndicatorsTable(rows, `${perf}|${std}|${q}|${r?.unit.id}`) : (
          <section className="r-indicator-groups" aria-label={L("المعايير ومؤشراتها", "Standards and their indicators")}>
            {indicatorGroups.map((group) => (
              <AccordionPanel key={group.code} id={`r-indicator-standard-${group.code}`}
                className="r-indicator-group" style={tint(pillarMap.map.get(group.code)?.index ?? -1)}
                open={openStandard === group.code}
                onToggle={() => setOpenStandard((selected) => selected === group.code ? null : group.code)}
                header={<><code className="pill-code ltr">{stdLabel(group.code)}</code><b>{group.name}</b><small>{L(`${group.indicators.length} مؤشر`, `${group.indicators.length} indicators`)}</small></>}>
                {renderIndicatorsTable(group.indicators, `${group.code}|${r.unit.id}|${r.cycle.id}`)}
              </AccordionPanel>
            ))}
            {indicatorGroups.length === 0 && <EmptyNote>{L("لا مؤشرات في هذه الدورة.", "No indicators in this cycle.")}</EmptyNote>}
          </section>
        )}
      </Panel>

      {open && r && <IndicatorDrawer cycleId={r.cycle.id} unitId={r.unit.id} university={r.unit.kind === "University"} item={open} onClose={() => setOpen(null)} />}
      {printing && r && <ReportView r={r} onClose={() => setPrinting(false)} />}
    </>
  );
}

/** المعايير مجمعة تحت ركائزها بترتيب الخطة؛ المعيار بلا ركيزة (أو بلا صلاحية على الخطة) في مجموعة أخيرة. */
function groupByPillar(standards: StandardResult[], map: Map<string, PillarInfo>) {
  const groups = new Map<string, { key: string; info: PillarInfo | undefined; order: number; standards: StandardResult[] }>();
  for (const s of standards) {
    const info = map.get(s.code);
    const key = info ? info.id : "none";
    if (!groups.has(key)) groups.set(key, { key, info, order: info ? info.index : 999, standards: [] });
    groups.get(key)!.standards.push(s);
  }
  return [...groups.values()].sort((a, b) => a.order - b.order);
}

/** نسبة المؤشرات المحققة مما حكم عليه، في حلقة: صورة الأداء الكلي للجهة في سطر. */
function OverallRing({ r }: { r: Results }) {
  const a = r.indicators.filter((i) => i.performance === "Achieved").length;
  const judged = r.indicators.filter((i) => ["Achieved", "Near", "Behind"].includes(i.performance)).length;
  const pct = judged === 0 ? 0 : a / judged;
  const R = 22, C = 2 * Math.PI * R;
  return (
    <div className="r-ring" title={L("نسبة المؤشرات المحققة من المؤشرات التي حكم عليها", "Share achieved of judged indicators")}>
      <svg viewBox="0 0 56 56" width="56" height="56" aria-hidden>
        <circle cx="28" cy="28" r={R} fill="none" stroke="var(--ds-chart-track)" strokeWidth="6" />
        <circle cx="28" cy="28" r={R} fill="none" stroke="var(--ds-status-ok)" strokeWidth="6" strokeLinecap="round" strokeDasharray={`${(C * pct).toFixed(1)} ${C.toFixed(1)}`} transform="rotate(-90 28 28)" />
        <text x="28" y="32" textAnchor="middle" fontSize="12" fontWeight="700" fill="var(--ds-chart-value)">{Math.round(pct * 100)}%</text>
      </svg>
      <span><b>{L("نسبة التحقق", "Achieved share")}</b><small>{L(`${a} محقق من ${judged} حكم عليه`, `${a} of ${judged} judged`)}</small></span>
    </div>
  );
}

/** نسبة الإنجاز وتحتها الفجوة عن المستهدف بوحدة المؤشر، فيعرف القارئ كم بقي لا النسبة وحدها. */
function Achievement({ i }: { i: IndicatorResult }) {
  if (i.achievement === null) return <span className="muted">—</span>;
  const pct = Math.round(i.achievement * 100);
  const gap = i.actual !== null && i.target !== null ? i.actual - i.target : null;
  // الفجوة «مطلوبة» إن كانت في غير صالح الاتجاه، وإلا «فائض».
  const short = gap === null ? false : i.direction === "LowerIsBetter" ? gap > 0 : i.direction === "Range" ? Math.abs(gap) > 0 : gap < 0;
  return (
    <span className="r-ach-wrap">
      <span className="r-ach">
        <ProgressToTarget value={Math.min(pct, 150)} target={100} max={150} />
        <b className="ltr">{pct}%</b>
      </span>
      {gap !== null && gap !== 0 && (
        <small className={short ? "r-gap short" : "r-gap"}>
          {short ? L("الفجوة", "Gap") : L("فوق المستهدف", "Above target")} <bdi className="ltr">{num(Math.abs(gap))}</bdi> {i.unit.trim() === "%" ? L("نقطة", "pts") : i.unit}
        </small>
      )}
    </span>
  );
}

function Delta({ i }: { i: IndicatorResult }) {
  if (i.actual === null || i.previous === null) return <span className="muted">—</span>;
  const d = i.actual - i.previous;
  if (d === 0) return <span className="muted">{L("ثابت", "Same")}</span>;
  // للوصول إلى قيمة: التحسن اقتراب منها، لا زيادة أو نقص.
  const better = i.direction === "LowerIsBetter" ? d < 0
    : i.direction === "Range" && i.target !== null ? Math.abs(i.actual - i.target) < Math.abs(i.previous - i.target)
    : d > 0;
  return <span className={`r-delta ${better ? "up" : "down"} ltr`}>{d > 0 ? "▲" : "▼"} {num(Math.abs(d))}{i.unit.trim() === "%" ? L(" نقطة", " pts") : ""}</span>;
}

function heatClass(c: HeatCell | undefined) {
  if (!c || c.indicators === c.noData) return "h0";
  const judged = c.achieved + c.near + c.behind;
  if (judged === 0) return "h0";
  const share = c.achieved / judged;
  return share >= 0.75 ? "h4" : share >= 0.5 ? "h3" : share >= 0.25 ? "h2" : "h1";
}

function Heatmap({ r, onPick, map }: { r: Results; onPick: (unitId: string) => void; map: Map<string, PillarInfo> }) {
  const standards = [...new Set(r.heat.map((h) => h.standardCode))];
  const cell = (u: string, s: string) => r.heat.find((h) => h.unitId === u && h.standardCode === s);
  return (
    <div className="r-heat">
      <table>
        <thead>
          <tr><th className="unit">{L("الكلية", "College")}</th>{standards.map((s) => <th key={s}><StdCode code={s} map={map} /></th>)}</tr>
        </thead>
        <tbody>
          {r.heatUnits.map((u) => (
            <tr key={u.id}>
              <th className="unit"><Button type="link" size="small" onClick={() => onPick(u.id)} style={{ padding: 0 }}>{u.name}</Button></th>
              {standards.map((s) => {
                const c = cell(u.id, s);
                const judged = c ? c.achieved + c.near + c.behind : 0;
                return (
                  <Tooltip key={s} title={c ? L(`${c.achieved} محقق، ${c.near} قريب، ${c.behind} متأخر، ${c.noData} بلا بيانات من ${c.indicators}`, `${c.achieved}/${c.indicators}`) : undefined}>
                    <td className={heatClass(c)}>{judged ? <span className="ltr">{c!.achieved}/{judged}</span> : "—"}</td>
                  </Tooltip>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
      <div className="r-heat-legend">
        <span><i className="h4" style={{ background: "var(--ds-heat-4)" }} />{L("75% فأكثر محقق", "≥75% achieved")}</span>
        <span><i style={{ background: "var(--ds-heat-3)" }} />50–74%</span>
        <span><i style={{ background: "var(--ds-heat-2)" }} />25–49%</span>
        <span><i style={{ background: "var(--ds-heat-1)" }} />{L("أقل من 25%", "<25%")}</span>
        <span><i style={{ background: "var(--ds-heat-none)" }} />{L("لا بيانات معتمدة", "No approved data")}</span>
      </div>
    </div>
  );
}

function IndicatorDrawer({ cycleId, unitId, university, item, onClose }: { cycleId: string; unitId: string; university: boolean; item: IndicatorResult; onClose: () => void }) {
  // خط مستهدف الجامعة على مقارنة الكليات: مستهدف الجهة المعروضة إن كانت الجامعة، وإلا مستهدف الجامعة المحسوب معها.
  const uniTarget = university ? item.target : item.universityTarget;
  const d = useQuery({
    queryKey: queryKeys.resultsIndicator(cycleId, item.indicatorId, unitId),
    queryFn: ({ signal }) => api.get<IndicatorResults>(`/api/results/${cycleId}/indicators/${item.indicatorId}${query({ unitId })}`, { signal }),
  });
  return (
    <Drawer open width={640} onClose={onClose} title={<span className="t1-code-title"><code className="ltr">{item.code}</code> {item.name}</span>}>
      <p className="t1-summary">{item.summary}</p>
      {item.note && <p className="t1-help">{item.note}</p>}
      <p className="t1-help">{L(`اعتمدت أرقام ${item.reported} من ${item.expected} جهة مطلوب منها رقم («غير منطبق» لا يحسب، و«غير متاح» نقص لا صفر).`,
        `${item.reported} of ${item.expected} units with a number due ("not applicable" excluded; "not available" is missing, not zero).`)}</p>
      <Panel query={d}>
        {() => (
          <>
            {d.data!.colleges.length > 0 && (
              <HBarChart title={L("مقارنة الكليات", "Colleges compared")} unit={item.unit}
                rows={d.data!.colleges.map((c) => ({
                  key: c.unitId, label: c.unitName, value: c.actual,
                  tone: c.performance === "Achieved" ? "teal" : c.performance === "Near" ? "gold" : c.performance === "Behind" ? "rose" : "muted",
                  note: `${perfText[c.performance].text}${c.target !== null ? ` · ${L("المستهدف", "Target")} ${withUnit(c.target, item.unit)}` : ""}`
                    + (c.numerator !== null && c.denominator !== null ? ` · ${num(c.numerator)} ÷ ${num(c.denominator)}` : ""),
                }))}
                reference={uniTarget !== null && d.data!.colleges.some((c) => c.targetSource === "University" || c.numerator !== null)
                  ? { value: uniTarget, label: L(`مستهدف الجامعة ${withUnit(uniTarget, item.unit)}`, "University target") } : undefined} />
            )}
            {d.data!.trend.length > 0 && (
              <HBarChart title={L("عبر الدورات", "Across cycles")} unit={item.unit} sort={false}
                rows={d.data!.trend.map((t) => ({ key: t.cycleId, label: t.label, value: t.actual, note: t.target !== null ? `${L("المستهدف", "Target")} ${withUnit(t.target, item.unit)}` : undefined }))} />
            )}
          </>
        )}
      </Panel>
    </Drawer>
  );
}
