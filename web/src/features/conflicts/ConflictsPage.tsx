import { Button, Input, Segmented, Select, Tag } from "antd";
import { CalendarOutlined, CheckCircleOutlined, ClockCircleOutlined, ExclamationCircleOutlined, WarningOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router-dom";
import { api, query } from "../../api/client";
import type { Conflict, ConflictList, ConflictSeverity, ConflictStatus } from "../../api/tier1";
import { L } from "../../shared/i18n";
import { conflictSeverity, conflictStatus } from "../../shared/labels";
import { DataTable, FilterMeta, oneOf, PageHeader, Panel, StatTile } from "../../shared/ui";
import { ConflictDrawer } from "./ConflictDrawer";

const STATUSES: ConflictStatus[] = ["Open", "Ready", "Deferred", "Resolved"];
const SEVERITIES: ConflictSeverity[] = ["High", "Medium", "Low"];

/** سجل التعارضات: الأخطر والمفتوح أولًا، والمرشحات في الرابط، والنقر يفتح التفاصيل في درج دون مغادرة القائمة. */
export default function ConflictsPage() {
  const [params, setParams] = useSearchParams();
  const status = oneOf(params.get("status"), STATUSES);
  const severity = oneOf(params.get("severity"), SEVERITIES);
  const q = params.get("q") ?? "";
  const open = params.get("open");

  const set = (key: string, value: string | undefined) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next, { replace: true });
  };

  const all = useQuery({ queryKey: ["conflicts", "all"], queryFn: () => api.get<ConflictList>("/api/kpi/conflicts") });
  const list = useQuery({ queryKey: ["conflicts", status, severity, q], queryFn: () => api.get<ConflictList>(`/api/kpi/conflicts${query({ status, severity, q })}`) });
  const count = (s: ConflictStatus) => all.data?.items.filter((c) => c.status === s).length ?? 0;
  const selected = all.data?.items.find((c) => c.code === open);
  const actions = list.data?.actions ?? all.data?.actions;

  return (
    <>
      <PageHeader title={L("سجل التعارضات", "Conflict register")}
        subtitle={L("تعارضات الملفين المرجعيين تحسمها اللجنة العليا لـ Tier 1 برقم المحضر. القرار لا يغير الأرقام تلقائيًا.",
          "Conflicts between the two reference files are decided by the Tier 1 higher committee. A decision never changes numbers automatically.")}
        extra={<Link to="/conflicts/agenda"><Button icon={<CalendarOutlined />}>{L("جدول أعمال اللجنة", "Committee agenda")}</Button></Link>} />

      <div className="lcp-stats">
        <StatTile icon={<ExclamationCircleOutlined />} tone="rose" label={L("مفتوح يحتاج تجهيزًا", "Open, needs preparation")} value={count("Open")} to="/conflicts?status=Open" loading={all.isLoading} />
        <StatTile icon={<CalendarOutlined />} label={L("جاهز للجنة", "Ready for the committee")} value={count("Ready")} to="/conflicts?status=Ready" loading={all.isLoading} />
        <StatTile icon={<ClockCircleOutlined />} tone="gold" label={L("مؤجل", "Deferred")} value={count("Deferred")} to="/conflicts?status=Deferred" loading={all.isLoading} />
        <StatTile icon={<CheckCircleOutlined />} tone="green" label={L("محسوم", "Resolved")} value={count("Resolved")} to="/conflicts?status=Resolved" loading={all.isLoading} />
      </div>

      <Panel>
        <div className="lcp-filters">
          <Segmented value={status ?? "all"} onChange={(v) => set("status", v === "all" ? undefined : String(v))}
            options={[{ value: "all", label: L("الكل", "All") }, ...STATUSES.map((s) => ({ value: s, label: conflictStatus[s].text }))]} />
          <Select allowClear placeholder={L("كل درجات الخطورة", "All severities")} value={severity} onChange={(v) => set("severity", v)} style={{ minWidth: 170 }}
            options={SEVERITIES.map((s) => ({ value: s, label: conflictSeverity[s].text }))} />
          <Input.Search key={q} allowClear defaultValue={q} placeholder={L("رقم التعارض أو كلمة من وصفه", "Code or a word from the description")} onSearch={(v) => set("q", v || undefined)} />
          <FilterMeta shown={list.data?.items.length ?? 0} total={all.data?.items.length} noun={L("تعارض", "conflicts")} active={!!(status || severity || q)}
            onReset={() => setParams({}, { replace: true })} />
        </div>
        <DataTable<Conflict>
          rowKey={(c) => c.code}
          data={list.data?.items ?? []}
          loading={list.isLoading}
          resetKey={`${status}|${severity}|${q}`}
          onRowClick={(c) => set("open", c.code)}
          pageSizes={[20, 50]}
          columns={[
            { title: L("الرقم", "No."), dataIndex: "code", width: 80, render: (c: string) => <code>{c}</code> },
            { title: L("الخطورة", "Severity"), dataIndex: "severity", width: 100, render: (s: ConflictSeverity) => <Tag color={conflictSeverity[s].color}>{conflictSeverity[s].text}</Tag> },
            { title: L("النوع", "Type"), dataIndex: "type", width: 170 },
            { title: L("الوصف", "Description"), dataIndex: "description", ellipsis: true },
            { title: L("المتأثر", "Affected"), dataIndex: "scope", width: 170, render: (s: string[]) => <span className="ltr">{s.join(" · ")}</span> },
            { title: L("الحالة", "Status"), dataIndex: "status", width: 110, render: (s: ConflictStatus) => <Tag color={conflictStatus[s].color}>{conflictStatus[s].text}</Tag> },
          ]}
          renderCard={(c) => (
            <>
              <div className="dt-card-row"><code>{c.code}</code><span><Tag color={conflictSeverity[c.severity].color}>{conflictSeverity[c.severity].text}</Tag><Tag color={conflictStatus[c.status].color}>{conflictStatus[c.status].text}</Tag></span></div>
              <b>{c.description}</b>
              <small className="muted ltr">{c.scope.join(" · ")}</small>
            </>
          )}
          empty={<span><WarningOutlined /> {L("لا تعارضات بهذه المرشحات.", "No conflicts match these filters.")}</span>}
        />
      </Panel>

      {selected && actions && (
        <ConflictDrawer conflict={selected} canPrepare={actions.canPrepare} canDecide={actions.canDecide} onClose={() => set("open", undefined)} />
      )}
    </>
  );
}
