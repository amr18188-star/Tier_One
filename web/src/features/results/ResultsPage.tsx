import { useState } from "react";
import { Button, Drawer, Input, Segmented, Select, Space, Tag, Tooltip } from "antd";
import { CheckCircleOutlined, ClockCircleOutlined, DownloadOutlined, FileDoneOutlined, PrinterOutlined, QuestionCircleOutlined, WarningOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import { api, query } from "../../api/client";
import type { Cycle, HeatCell, IndicatorResult, IndicatorResults, Performance, Results } from "../../api/collection";
import { HBarChart, ProgressToTarget, SegmentBar } from "../../shared/charts";
import { L } from "../../shared/i18n";
import { num, performance as perfText, withUnit } from "../../shared/labels";
import { DataTable, EmptyNote, oneOf, PageHeader, Panel, StatTile } from "../../shared/ui";
import { ReportView } from "./ReportView";

const PERF: Performance[] = ["Achieved", "Near", "Behind", "NoData", "NoTarget"];
const PERF_COLORS: Record<string, string> = { Achieved: "#2e9e63", Near: "#d9a227", Behind: "#b42318", NoData: "#c4ccd8", NoTarget: "#8d99ab" };

/**
 * النتائج: القيم المعتمدة نهائيًا مقابل المستهدف للجامعة أو لكلية، بملخص لكل معيار، وخريطة الكليات × المعايير،
 * وجدول المؤشرات بحالة الأداء والتغير عن الدورة السابقة. النقر على مؤشر يفتح مقارنة الكليات واتجاهه عبر الدورات.
 */
export default function ResultsPage() {
  const [params, setParams] = useSearchParams();
  const cycleParam = params.get("cycle") ?? undefined;
  const unitParam = params.get("unit") ?? undefined;
  const perf = oneOf(params.get("perf"), PERF);
  const std = params.get("std") ?? undefined;
  const q = params.get("q") ?? "";
  const [open, setOpen] = useState<IndicatorResult | null>(null);
  const [printing, setPrinting] = useState(false);

  const set = (key: string, value: string | undefined) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next, { replace: true });
  };

  const cycles = useQuery({ queryKey: ["cycles"], queryFn: () => api.get<Cycle[]>("/api/cycles") });
  const res = useQuery({
    queryKey: ["results", cycleParam, unitParam],
    queryFn: () => api.get<Results | null>(`/api/results${query({ cycleId: cycleParam, unitId: unitParam })}`),
  });
  const r = res.data;
  const live = cycles.data?.filter((c) => c.status !== "Draft") ?? [];

  if (res.isSuccess && !r)
    return (
      <>
        <PageHeader title={L("النتائج والمقارنات", "Results")} />
        <Panel><EmptyNote>{L("لا توجد دورة بعد. تظهر النتائج بعد اعتماد أول نماذج في دورة.", "No cycle yet.")}</EmptyNote></Panel>
      </>
    );

  const rows = (r?.indicators ?? []).filter((i) => (!perf || i.performance === perf) && (!std || i.standardCode === std)
    && (!q || i.name.includes(q) || i.code.toLowerCase().includes(q.toLowerCase())));
  const count = (p: Performance) => r?.indicators.filter((i) => i.performance === p).length ?? 0;
  const who = r?.unit.kind === "University" ? L("الجامعة", "University") : r?.unit.name;

  return (
    <>
      <PageHeader title={L("النتائج والمقارنات", "Results and comparisons")}
        subtitle={r && <>{r.cycle.name} · {L("مقارنة بمستهدف", "Against the target of")} <b className="ltr">{r.cycle.targetYear}</b>{r.cycle.trial && <Tag color="purple" className="ms">{L("بروفة", "Rehearsal")}</Tag>}</>}
        extra={r && (
          <Space wrap>
            <Button icon={<PrinterOutlined />} onClick={() => setPrinting(true)}>{L("تقرير للطباعة", "Printable report")}</Button>
            {r.canExport && <Button icon={<DownloadOutlined />} href={`/api/results/${r.cycle.id}/export.xlsx${query({ unitId: r.unit.id })}`}>{L("تصدير Excel", "Export Excel")}</Button>}
          </Space>
        )} />

      <div className="r-pickers">
        {live.length > 0 && (
          <Select value={r?.cycle.id ?? cycleParam} onChange={(v) => set("cycle", v)} style={{ minWidth: 260 }} aria-label={L("الدورة", "Cycle")}
            options={live.map((c) => ({ value: c.id, label: c.name }))} />
        )}
        {r && r.units.length > 1 && (
          <Select value={r.unit.id} onChange={(v) => set("unit", v)} style={{ minWidth: 240 }} showSearch optionFilterProp="label" aria-label={L("الجهة", "Unit")}
            options={r.units.map((u) => ({ value: u.id, label: u.kind === "University" ? L("الجامعة كلها", "Whole university") : u.name }))} />
        )}
      </div>

      <div className="lcp-stats">
        <StatTile icon={<CheckCircleOutlined />} tone="green" label={L("محقق", "Achieved")} value={count("Achieved")} loading={res.isLoading} to={`/results${query({ cycle: cycleParam, unit: unitParam, perf: "Achieved" })}`} />
        <StatTile icon={<ClockCircleOutlined />} tone="gold" label={L("قريب من المستهدف", "Near target")} value={count("Near")} loading={res.isLoading} to={`/results${query({ cycle: cycleParam, unit: unitParam, perf: "Near" })}`} />
        <StatTile icon={<WarningOutlined />} tone="rose" label={L("متأخر", "Behind")} value={count("Behind")} loading={res.isLoading} to={`/results${query({ cycle: cycleParam, unit: unitParam, perf: "Behind" })}`} />
        <StatTile icon={<FileDoneOutlined />} tone="navy" label={L("النماذج المعتمدة", "Approved forms")} value={r ? `${r.formsApproved}/${r.formsTotal}` : "…"} loading={res.isLoading}
          hint={L(`${count("NoData")} مؤشر بلا بيانات معتمدة`, `${count("NoData")} without approved data`)} />
      </div>

      {r && r.formsApproved === 0 && (
        <Panel><EmptyNote>{L("لم يعتمد أي نموذج في هذه الدورة بعد؛ النتائج تحسب من القيم المعتمدة نهائيًا فقط.", "No approved forms yet.")}</EmptyNote></Panel>
      )}

      <Panel title={L(`الأداء بحسب المعيار — ${who ?? ""}`, "By standard")} query={res}
        extra={<span className="r-legend">{(["Achieved", "Near", "Behind", "NoData"] as const).map((p) => <span key={p}><i style={{ background: PERF_COLORS[p] }} />{p === "NoData" ? L("بلا بيانات", "No data") : perfText[p].text}</span>)}</span>}>
        {() => r!.standards.map((s) => (
          <button type="button" key={s.code} className="r-std" style={{ width: "100%", background: "none", border: 0, textAlign: "start", cursor: "pointer", font: "inherit", color: "inherit" }}
            onClick={() => set("std", std === s.code ? undefined : s.code)} aria-pressed={std === s.code}>
            <code className="ltr">{s.code}</code>
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
      </Panel>

      {r && r.heatUnits.length > 1 && (
        <Panel title={L("خريطة الكليات × المعايير", "Colleges × standards")} extra={<Tooltip title={L("نسبة مؤشرات المعيار المحققة في الكلية مقابل مستهدفها. الرمادي: لا بيانات معتمدة.", "Share achieved per standard.")}><QuestionCircleOutlined /></Tooltip>}>
          <Heatmap r={r} onPick={(unitId) => set("unit", unitId)} />
        </Panel>
      )}

      <Panel title={L("المؤشرات", "Indicators")}>
        <div className="lcp-filters">
          <Segmented value={perf ?? "all"} onChange={(v) => set("perf", v === "all" ? undefined : String(v))}
            options={[{ value: "all", label: L("الكل", "All") }, ...PERF.map((p) => ({ value: p, label: perfText[p].text }))]} />
          <Input.Search allowClear defaultValue={q} placeholder={L("رمز المؤشر أو اسمه", "Code or name")} onSearch={(v) => set("q", v || undefined)} />
          {std && <Tag closable onClose={() => set("std", undefined)}>{L("المعيار", "Standard")} {std}</Tag>}
        </div>
        <DataTable<IndicatorResult>
          rowKey={(i) => i.indicatorId}
          data={rows}
          loading={res.isLoading}
          resetKey={`${perf}|${std}|${q}|${r?.unit.id}`}
          onRowClick={setOpen}
          pageSizes={[20, 50, 100]}
          columns={[
            { title: L("الرمز", "Code"), dataIndex: "code", width: 90, render: (c: string) => <code className="ltr">{c}</code> },
            { title: L("المؤشر", "Indicator"), dataIndex: "name", ellipsis: true },
            { title: L("الفعلي", "Actual"), key: "a", width: 110, render: (_, i) => <b className="ltr">{withUnit(i.actual, i.unit)}</b> },
            { title: L("المستهدف", "Target"), key: "t", width: 110, render: (_, i) => <span className="ltr">{withUnit(i.target, i.unit)}</span> },
            { title: L("الإنجاز", "Achievement"), key: "p", width: 190, render: (_, i) => <Achievement i={i} /> },
            { title: L("عن السابقة", "vs previous"), key: "d", width: 110, render: (_, i) => <Delta i={i} /> },
            { title: L("الحالة", "Status"), dataIndex: "performance", width: 150, render: (p: Performance) => <><i className={`perf-dot perf-${perfText[p].tone}`} />{perfText[p].text}</> },
          ]}
          renderCard={(i) => (
            <>
              <div className="dt-card-row"><code className="ltr">{i.code}</code><span><i className={`perf-dot perf-${perfText[i.performance].tone}`} />{perfText[i.performance].text}</span></div>
              <b>{i.name}</b>
              <small>{i.summary}</small>
            </>
          )}
          empty={L("لا مؤشرات بهذه المرشحات.", "No indicators match.")}
        />
      </Panel>

      {open && r && <IndicatorDrawer cycleId={r.cycle.id} unitId={r.unit.id} item={open} onClose={() => setOpen(null)} />}
      {printing && r && <ReportView r={r} onClose={() => setPrinting(false)} />}
    </>
  );
}

function Achievement({ i }: { i: IndicatorResult }) {
  if (i.achievement === null) return <span className="muted">—</span>;
  const pct = Math.round(i.achievement * 100);
  return (
    <span className="r-ach">
      <ProgressToTarget value={Math.min(pct, 150)} target={100} max={150} />
      <b className="ltr">{pct}%</b>
    </span>
  );
}

function Delta({ i }: { i: IndicatorResult }) {
  if (i.actual === null || i.previous === null) return <span className="muted">—</span>;
  const d = i.actual - i.previous;
  if (d === 0) return <span className="muted">{L("ثابت", "Same")}</span>;
  const better = i.direction === "LowerIsBetter" ? d < 0 : d > 0;
  return <span className={`r-delta ${better ? "up" : "down"} ltr`}>{d > 0 ? "▲" : "▼"} {num(Math.abs(d))}{i.unit.trim() === "%" ? L(" نقطة", " pts") : ""}</span>;
}

function heatClass(c: HeatCell | undefined) {
  if (!c || c.indicators === c.noData) return "h0";
  const judged = c.achieved + c.near + c.behind;
  if (judged === 0) return "h0";
  const share = c.achieved / judged;
  return share >= 0.75 ? "h4" : share >= 0.5 ? "h3" : share >= 0.25 ? "h2" : "h1";
}

function Heatmap({ r, onPick }: { r: Results; onPick: (unitId: string) => void }) {
  const standards = [...new Set(r.heat.map((h) => h.standardCode))];
  const cell = (u: string, s: string) => r.heat.find((h) => h.unitId === u && h.standardCode === s);
  return (
    <div className="r-heat">
      <table>
        <thead>
          <tr><th className="unit">{L("الكلية", "College")}</th>{standards.map((s) => <th key={s} className="ltr">{s}</th>)}</tr>
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
        <span><i className="h4" style={{ background: "#a9dcc0" }} />{L("75% فأكثر محقق", "≥75% achieved")}</span>
        <span><i style={{ background: "#d9efe2" }} />50–74%</span>
        <span><i style={{ background: "#fff0d4" }} />25–49%</span>
        <span><i style={{ background: "#fde2df" }} />{L("أقل من 25%", "<25%")}</span>
        <span><i style={{ background: "#edf1f5" }} />{L("لا بيانات معتمدة", "No approved data")}</span>
      </div>
    </div>
  );
}

function IndicatorDrawer({ cycleId, unitId, item, onClose }: { cycleId: string; unitId: string; item: IndicatorResult; onClose: () => void }) {
  const d = useQuery({
    queryKey: ["results", cycleId, "indicator", item.indicatorId, unitId],
    queryFn: () => api.get<IndicatorResults>(`/api/results/${cycleId}/indicators/${item.indicatorId}${query({ unitId })}`),
  });
  return (
    <Drawer open width={640} onClose={onClose} title={<span className="t1-code-title"><code className="ltr">{item.code}</code> {item.name}</span>}>
      <p className="t1-summary">{item.summary}</p>
      {item.note && <p className="t1-help">{item.note}</p>}
      <p className="t1-help">{L(`أبلغ ${item.reported} من ${item.expected} جهة متوقعة.`, `${item.reported} of ${item.expected} units reported.`)}</p>
      <Panel query={d}>
        {() => (
          <>
            {d.data!.colleges.length > 0 && (
              <HBarChart title={L("مقارنة الكليات", "Colleges compared")} unit={item.unit}
                rows={d.data!.colleges.map((c) => ({
                  key: c.unitId, label: c.unitName, value: c.actual,
                  tone: c.performance === "Achieved" ? "teal" : c.performance === "Near" ? "gold" : c.performance === "Behind" ? "rose" : "muted",
                  note: `${perfText[c.performance].text}${c.target !== null ? ` · ${L("المستهدف", "Target")} ${withUnit(c.target, item.unit)}` : ""}`,
                }))}
                reference={item.target !== null && item.unit.trim() === "%" ? { value: item.target, label: L(`مستهدف الجامعة ${withUnit(item.target, item.unit)}`, "University target") } : undefined} />
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
