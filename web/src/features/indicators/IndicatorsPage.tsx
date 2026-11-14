import { useState } from "react";
import { Badge, Button, Input, Segmented, Select, Switch, Tag, Tooltip } from "antd";
import { PlusOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { useNavigate, useSearchParams } from "react-router-dom";
import { api, query } from "../../api/client";
import type { DefinitionStatus, IndicatorRow, Standard } from "../../api/tier1";
import { L } from "../../shared/i18n";
import { definitionStatus, num, withUnit } from "../../shared/labels";
import { StdCode, stdLabel, tint, usePillarMap } from "../../shared/pillars";
import { usePermissions } from "../../shared/session";
import { DataTable, FilterMeta, oneOf, PageHeader, Panel } from "../../shared/ui";
import { NewIndicatorModal } from "./NewIndicatorModal";

const STATUSES: DefinitionStatus[] = ["Draft", "UnderReview", "Approved"];

/** قاموس المؤشرات: كل المرشحات في الرابط، فيمكن مشاركة أي عرض أو العودة إليه، والنقر على صف يفتح صفحة المؤشر. */
export default function IndicatorsPage() {
  const [params, setParams] = useSearchParams();
  const navigate = useNavigate();
  const standard = params.get("standard") ?? undefined;
  const status = oneOf(params.get("status"), STATUSES);
  const q = params.get("q") ?? "";
  const withConflicts = params.get("conflicts") === "1";
  const canAdd = usePermissions().can("kpi.dictionary", "A");
  const [adding, setAdding] = useState(false);

  const set = (key: string, value: string | undefined) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next, { replace: true });
  };

  const standards = useQuery({ queryKey: ["standards"], queryFn: () => api.get<Standard[]>("/api/kpi/standards") });
  const rows = useQuery({
    queryKey: ["indicators", standard, status, q, withConflicts],
    queryFn: () => api.get<IndicatorRow[]>(`/api/kpi/indicators${query({ standard, status, q, withConflicts: withConflicts ? "true" : undefined })}`),
  });
  const selected = standards.data?.find((s) => s.code === standard);
  const pillarMap = usePillarMap();
  const pillar = params.get("pillar") ?? undefined;
  const total = standards.data?.reduce((n, s) => n + s.indicatorCount, 0) ?? 0;
  // الركيزة تصفية في الواجهة: الخادم يصفي بالمعيار، والركيزة مجموعة معاييرها.
  const list = (rows.data ?? []).filter((r) => !pillar || pillarMap.map.get(r.standardCode)?.id === pillar);
  const stdOptions = (standards.data ?? []).filter((s) => !pillar || pillarMap.map.get(s.code)?.id === pillar);

  return (
    <>
      <PageHeader title={L("قاموس المؤشرات", "Indicator dictionary")}
        subtitle={L("كل مؤشر بتعريفه ومستهدفاته وتعارضاته في صفحة واحدة. اختر معيارًا أو ابحث بالكود أو الاسم.",
          "Each indicator's definition, targets and conflicts on one page. Pick a standard or search by code or name.")}
        stats={standards.data ? [
          { label: L("مؤشرات", "Indicators"), value: total },
          { label: L("معتمد التعريف", "Approved"), value: standards.data.reduce((n, s) => n + s.approvedCount, 0), tone: "ok" },
          { label: L("معايير", "Standards"), value: standards.data.length },
          { label: L("تعارضات مفتوحة", "Open conflicts"), value: standards.data.reduce((n, s) => n + s.openConflicts, 0), tone: "bad" },
        ] : undefined}
        extra={canAdd && <Button type="primary" icon={<PlusOutlined />} onClick={() => setAdding(true)}>{L("مؤشر جديد", "New indicator")}</Button>} />
      {adding && standards.data && <NewIndicatorModal standards={standards.data} standard={standard} onClose={() => setAdding(false)} />}

      {pillarMap.pillars.length > 0 && standards.data && (
        <nav className="ix-pillars" aria-label={L("التصفية بالركيزة", "Filter by pillar")}>
          {pillarMap.pillars.map((p) => {
            const stds = standards.data!.filter((s) => pillarMap.map.get(s.code)?.id === p.id);
            const n = stds.reduce((k, s) => k + s.indicatorCount, 0);
            const ok = stds.reduce((k, s) => k + s.approvedCount, 0);
            const on = pillar === p.id;
            return (
              <button key={p.id} type="button" style={tint(p.index)} className={on ? "on" : ""} aria-pressed={on}
                onClick={() => { const next = new URLSearchParams(params); if (on) next.delete("pillar"); else next.set("pillar", p.id); next.delete("standard"); setParams(next, { replace: true }); }}>
                <span className="ix-num ltr">{String(p.index + 1).padStart(2, "0")}</span>
                <span className="ix-name"><b>{p.name}</b><small>{L(`${stds.length} معيار · ${n} مؤشر · ${ok} معتمد`, `${stds.length} standards · ${n} indicators`)}</small></span>
                <span className="ix-bar"><i style={{ width: `${n ? (100 * ok) / n : 0}%` }} /></span>
              </button>
            );
          })}
        </nav>
      )}

      <Panel>
        <div className="lcp-filters">
          <Select allowClear showSearch optionFilterProp="label" style={{ minWidth: 300 }} placeholder={L("كل المعايير", "All standards")}
            value={standard} onChange={(v) => set("standard", v)} loading={standards.isLoading} aria-label={L("المعيار", "Standard")}
            options={stdOptions.map((s) => ({ value: s.code, label: `${stdLabel(s.code)} — ${s.nameAr} (${s.indicatorCount})` }))} />
          <Segmented value={status ?? "all"} onChange={(v) => set("status", v === "all" ? undefined : String(v))}
            options={[{ value: "all", label: L("الكل", "All") }, ...STATUSES.map((s) => ({ value: s, label: definitionStatus[s].text }))]} />
          <Input.Search key={q} allowClear defaultValue={q} placeholder={L("كود المؤشر أو اسمه", "Code or name")} onSearch={(v) => set("q", v || undefined)} />
          <label className="lcp-switch">
            <Switch size="small" checked={withConflicts} onChange={(v) => set("conflicts", v ? "1" : undefined)} /> {L("بتعارضات مفتوحة فقط", "Open conflicts only")}
          </label>
          <FilterMeta shown={list.length} total={total || undefined} noun={L("مؤشر", "indicators")} active={!!(standard || status || q || withConflicts || pillar)} onReset={() => setParams({}, { replace: true })} />
        </div>
        {selected?.classificationNote && <p className="t1-summary">{L("ملاحظة تصنيف:", "Classification note:")} {selected.classificationNote}</p>}

        <DataTable<IndicatorRow>
          rowKey={(r) => r.code}
          data={list}
          loading={rows.isLoading}
          resetKey={`${standard}|${status}|${q}|${withConflicts}|${pillar}`}
          onRowClick={(r) => navigate(`/indicators/${r.code}`)}
          pageSizes={[20, 50, 120]}
          columns={[
            { title: L("الكود", "Code"), dataIndex: "code", width: 96, render: (c: string, r) => <StdCode code={c} standard={r.standardCode} map={pillarMap.map} /> },
            {
              title: L("المؤشر", "Indicator"), dataIndex: "nameAr",
              render: (name: string, r) => <>{name}{r.suspended && <Tag className="ms">{L("موقوف", "Suspended")}</Tag>}</>,
            },
            { title: L("الوحدة", "Unit"), dataIndex: "unit", width: 110 },
            {
              title: L("الحالة", "Status"), dataIndex: "status", width: 140,
              render: (s: DefinitionStatus, r) => (
                <Tooltip title={definitionStatus[s].hint}>
                  <Tag color={definitionStatus[s].color}>{definitionStatus[s].text}{r.versionNumber > 1 && ` · ${L("إصدار", "v")} ${r.versionNumber}`}</Tag>
                </Tooltip>
              ),
            },
            {
              title: L("ينقص التعريف", "Missing"), dataIndex: "missingCount", width: 110, align: "center",
              render: (n: number) => n === 0 ? <Tag color="success">{L("مكتمل", "Complete")}</Tag> : <Tag color="warning">{L(`${n} بند`, `${n} items`)}</Tag>,
            },
            { title: L("خط الأساس", "Baseline"), dataIndex: "baseline", width: 110, render: (v: number | null) => <span className="ltr">{num(v)}</span> },
            { title: L("مستهدف 2031", "2031 target"), dataIndex: "target2031", width: 120, render: (v: number | null, r) => <b className="ltr">{withUnit(v, r.unit)}</b> },
            {
              title: L("التعارضات", "Conflicts"), dataIndex: "openConflicts", width: 100, align: "center",
              render: (n: number) => n > 0 ? <Badge count={n} color="var(--ds-status-late-ink)" /> : <span className="muted">—</span>,
            },
          ]}
          renderCard={(r) => (
            <>
              <div className="dt-card-row"><StdCode code={r.code} standard={r.standardCode} map={pillarMap.map} /><Tag color={definitionStatus[r.status].color}>{definitionStatus[r.status].text}</Tag></div>
              <b>{r.nameAr}</b>
              <div className="dt-card-row"><span>{L("مستهدف 2031", "2031 target")}</span><b className="ltr">{withUnit(r.target2031, r.unit)}</b></div>
              {r.openConflicts > 0 && <div className="dt-card-row"><span>{L("تعارضات مفتوحة", "Open conflicts")}</span><Badge count={r.openConflicts} color="var(--ds-status-late-ink)" /></div>}
            </>
          )}
        />
      </Panel>
    </>
  );
}
