import type { ReactNode } from "react";
import { Button, Input, Segmented, Select, Tag } from "antd";
import { CalendarOutlined, CheckCircleOutlined, ClockCircleOutlined, ExclamationCircleOutlined, WarningOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router-dom";
import { api, query } from "../../api/client";
import type { Conflict, ConflictList, ConflictSeverity, ConflictStatus } from "../../api/tier1";
import { L } from "../../shared/i18n";
import { conflictSeverity, conflictStatus } from "../../shared/labels";
import { DataTable, FilterMeta, oneOf, PageHeader, Panel } from "../../shared/ui";
import { ConflictDrawer } from "./ConflictDrawer";

const STATUSES: ConflictStatus[] = ["Open", "Ready", "Deferred", "Resolved"];
const SEVERITIES: ConflictSeverity[] = ["High", "Medium", "Low"];
const PIPE: { status: ConflictStatus; tone: string; icon: ReactNode; ar: string; en: string }[] = [
  { status: "Open", tone: "bad", icon: <ExclamationCircleOutlined aria-hidden />, ar: "مفتوح يحتاج تجهيزًا", en: "Open" },
  { status: "Ready", tone: "review", icon: <CalendarOutlined aria-hidden />, ar: "جاهز للجنة", en: "Ready" },
  { status: "Deferred", tone: "work", icon: <ClockCircleOutlined aria-hidden />, ar: "مؤجل", en: "Deferred" },
  { status: "Resolved", tone: "ok", icon: <CheckCircleOutlined aria-hidden />, ar: "محسوم برقم المحضر", en: "Resolved" },
];

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
        stats={all.data ? [
          ...SEVERITIES.map((s) => ({
            label: L(`مفتوح — ${conflictSeverity[s].text}`, `Open — ${s}`),
            value: all.data!.items.filter((c) => c.severity === s && c.status !== "Resolved").length,
            tone: s === "High" ? "bad" as const : s === "Medium" ? "warn" as const : undefined,
          })),
          { label: L("نسبة المحسوم", "Resolved"), value: <span className="ltr">{all.data.items.length ? Math.round((100 * count("Resolved")) / all.data.items.length) : 0}%</span>, tone: "ok" },
        ] : undefined}
        extra={<Link to="/conflicts/agenda"><Button icon={<CalendarOutlined />}>{L("جدول أعمال اللجنة", "Committee agenda")}</Button></Link>} />

      {/* مسار التعارض: يجهز، ثم يعرض على اللجنة، فيحسم أو يؤجل. النقر يعرض ما في المرحلة. */}
      <nav className="f-pipe cf-pipe" aria-label={L("التعارضات بحسب مرحلتها", "Conflicts by stage")}>
        {PIPE.map((p, k) => {
          const n = count(p.status);
          const on = status === p.status;
          return (
            <button key={p.status} type="button" className={`f-pipe-step tone-${p.tone}${on ? " on" : ""}`} aria-pressed={on} onClick={() => set("status", on ? undefined : p.status)}>
              <span className="f-pipe-n ltr">{all.isLoading ? "…" : n}</span>
              <span className="f-pipe-label">{p.icon}{L(p.ar, p.en)}</span>
              <span className="f-pipe-bar"><i style={{ width: `${(100 * n) / (all.data?.items.length || 1)}%` }} /></span>
              {k < PIPE.length - 1 && <span className="f-pipe-arrow" aria-hidden />}
            </button>
          );
        })}
      </nav>

      <Panel>
        <div className="lcp-filters">
          <Segmented value={status ?? "all"} onChange={(v) => set("status", v === "all" ? undefined : String(v))}
            options={[{ value: "all", label: L("الكل", "All") }, ...STATUSES.map((s) => ({ value: s, label: conflictStatus[s].text }))]} />
          <Select allowClear placeholder={L("كل درجات الخطورة", "All severities")} aria-label={L("كل درجات الخطورة", "All severities")} value={severity} onChange={(v) => set("severity", v)} style={{ minWidth: 170 }}
            options={SEVERITIES.map((s) => ({ value: s, label: conflictSeverity[s].text }))} />
          <Input.Search key={q} allowClear defaultValue={q} placeholder={L("رقم التعارض أو كلمة من وصفه", "Code or a word from the description")} aria-label={L("رقم التعارض أو كلمة من وصفه", "Code or a word from the description")} onSearch={(v) => set("q", v || undefined)} />
          <FilterMeta shown={list.data?.items.length ?? 0} total={all.data?.items.length} noun={L("تعارض", "conflicts")} active={!!(status || severity || q)}
            onReset={() => setParams({}, { replace: true })} />
        </div>
        <DataTable<Conflict>
          rowKey={(c) => c.code}
          data={list.data?.items ?? []}
          loading={list.isLoading}
          resetKey={`${status}|${severity}|${q}`}
          onRowClick={(c) => set("open", c.code)}
          rowLabel={(c) => L(`فتح التعارض ${c.code}`, `Open conflict ${c.code}`)}
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
