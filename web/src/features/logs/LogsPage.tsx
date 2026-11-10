import { useMemo, useState } from "react";
import { Alert, App, Button, DatePicker, Descriptions, Input, Select, Space, Table, Tabs, Tag } from "antd";
import { DownloadOutlined, ReloadOutlined, SafetyCertificateOutlined } from "@ant-design/icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { Dayjs } from "dayjs";
import { api, ApiError, query } from "../../api/client";
import type { ChainDay, LogItem, LogPage, LogsStatus } from "../../api/types";
import { errorText, formatDate, formatDateTime } from "../../shared/format";
import { getLang, L } from "../../shared/i18n";
import { usePermissions } from "../../shared/session";
import { DataTable, EmptyNote, PageHeader, Panel } from "../../shared/ui";

const COLLECTIONS = [
  { key: "audit_events", feature: "log.audit", label: () => L("سجل العمليات", "Operations") },
  { key: "security_events", feature: "log.security", label: () => L("السجل الأمني", "Security") },
  { key: "access_events", feature: "log.access", label: () => L("الاطلاع والطباعة والتصدير", "Access and export") },
  { key: "integration_events", feature: "log.tech", label: () => L("التكاملات", "Integrations") },
  { key: "http_requests", feature: "log.tech", label: () => L("الطلبات التقنية", "HTTP requests") },
  { key: "app_errors", feature: "log.tech", label: () => L("الأخطاء", "Errors") },
  { key: "job_runs", feature: "log.tech", label: () => L("المهام الخلفية", "Background jobs") },
];

interface Filters {
  range: [Dayjs | null, Dayjs | null] | null;
  action: string;
  outcome: string;
  ref: string;
  ip: string;
  correlationId: string;
}

const EMPTY: Filters = { range: null, action: "", outcome: "", ref: "", ip: "", correlationId: "" };

export default function LogsPage() {
  const perms = usePermissions();
  const tabs = COLLECTIONS.filter((c) => perms.can(c.feature, "V"));
  // في مرحلة الأساس أحداث الصلاحيات والدخول في السجل الأمني، فيبدأ به من يملكه.
  const [tab, setTab] = useState(tabs.find((t) => t.key === "security_events")?.key ?? tabs[0]?.key ?? "security_events");
  const showIntegrity = perms.scopeOf("log.audit", "V") === "A";
  return (
    <>
      <PageHeader title={L("السجلات", "Logs")}
        subtitle={L("السجلات في MongoDB منفصلة، تضاف ولا تعدل ولا تحذف من المنظومة لأي دور، وكل حدث مربوط ببصمة سابقه.",
          "Logs live in a separate MongoDB. They are appended only; no role can edit or delete them, and every event is chained to the previous one by hash.")} />
      {showIntegrity && <IntegrityPanel />}
      <Tabs className="lcp-tabs" activeKey={tab} onChange={setTab} items={tabs.map((c) => ({ key: c.key, label: c.label(), children: <LogTable collection={c.key} feature={c.feature} /> }))} />
    </>
  );
}

function LogTable({ collection, feature }: { collection: string; feature: string }) {
  const perms = usePermissions();
  const { message } = App.useApp();
  const [draft, setDraft] = useState<Filters>(EMPTY);
  const [filters, setFilters] = useState<Filters>(EMPTY);
  const [page, setPage] = useState(1);
  const tech = ["http_requests", "app_errors", "job_runs", "integration_events"].includes(collection);
  const params = useMemo(
    () => ({
      from: filters.range?.[0]?.startOf("day").toISOString(),
      to: filters.range?.[1]?.endOf("day").toISOString(),
      action: filters.action,
      outcome: filters.outcome,
      ref: filters.ref,
      ip: filters.ip,
      correlationId: filters.correlationId,
    }),
    [filters],
  );
  const data = useQuery({
    queryKey: ["logs", collection, params, page],
    queryFn: () => api.get<LogPage>(`/api/logs/${collection}${query({ ...params, page, pageSize: 25 })}`),
  });

  async function exportCsv() {
    try {
      const r = await fetch(`/api/logs/${collection}/export${query(params)}`, { credentials: "same-origin", headers: { "Accept-Language": getLang() } });
      if (!r.ok) {
        const body = await r.json().catch(() => null);
        throw new ApiError(r.status, body?.code ?? "error", body?.title ?? `HTTP ${r.status}`);
      }
      const blob = await r.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `${collection}.csv`;
      a.click();
      URL.revokeObjectURL(url);
      message.success(L("صُدّر الملف، وسُجل التصدير في سجل الاطلاع.", "Exported. The export itself is recorded in the access log."));
    } catch (e) {
      message.error(errorText(e));
    }
  }

  return (
    <div className="stack">
      <div className="lcp-filters">
        <DatePicker.RangePicker value={draft.range as never} onChange={(v) => setDraft({ ...draft, range: v as Filters["range"] })} />
        <Input placeholder={tech ? L("المسار أو المهمة", "Path or job") : L("رمز الإجراء، مثل iam.change_approve", "Action code, e.g. iam.change_approve")} value={draft.action} onChange={(e) => setDraft({ ...draft, action: e.target.value })} style={{ width: 240 }} dir="ltr" />
        {!tech && (
          <>
            <Select
              allowClear
              placeholder={L("النتيجة", "Outcome")}
              value={draft.outcome || undefined}
              onChange={(v) => setDraft({ ...draft, outcome: v ?? "" })}
              style={{ width: 140 }}
              options={[{ value: "success", label: L("نجاح", "Success") }, { value: "denied", label: L("مرفوض", "Denied") }, { value: "failure", label: L("فشل", "Failure") }]}
            />
            <Input placeholder={L("المرجع أو البريد", "Reference or email")} value={draft.ref} onChange={(e) => setDraft({ ...draft, ref: e.target.value })} style={{ width: 180 }} />
            <Input placeholder="IP" value={draft.ip} onChange={(e) => setDraft({ ...draft, ip: e.target.value })} style={{ width: 130 }} dir="ltr" />
          </>
        )}
        <Input placeholder={L("معرّف الارتباط", "Correlation ID")} value={draft.correlationId} onChange={(e) => setDraft({ ...draft, correlationId: e.target.value })} style={{ width: 200 }} dir="ltr" />
        <Button type="primary" onClick={() => { setFilters(draft); setPage(1); }}>{L("ابحث", "Search")}</Button>
        <Button onClick={() => { setDraft(EMPTY); setFilters(EMPTY); setPage(1); }}>{L("مسح", "Clear")}</Button>
        {perms.can(feature, "export") && <Button icon={<DownloadOutlined />} onClick={exportCsv}>{L("تصدير CSV", "Export CSV")}</Button>}
      </div>
      {data.error && <Alert type="error" showIcon message={errorText(data.error)} />}
      <DataTable<LogItem>
        rowKey={(i) => i.id}
        loading={data.isLoading}
        data={data.data?.items ?? []}
        minWidth={900}
        paging={{ page, size: 25, total: data.data?.total ?? 0, onPage: setPage }}
        expandable={{ expandedRowRender: (i) => <LogDetails item={i} /> }}
        empty={<EmptyNote>{L("لا أحداث بهذه المرشحات.", "No events match these filters.")}</EmptyNote>}
        columns={[
          { title: L("الوقت (القاهرة)", "Time (Cairo)"), dataIndex: "ts", width: 170, render: (v) => <span className="nowrap">{formatDateTime(v)}</span> },
          { title: L("الفاعل", "Actor"), render: (_, i) => <Space direction="vertical" size={0}><span>{i.actor?.name ?? (i.actor?.type === "system" ? L("النظام", "System") : "—")}</span><span>{i.actor?.roles.map((r) => <Tag key={r}>{r}</Tag>)}</span></Space> },
          { title: L("الحدث", "Event"), render: (_, i) => <Space direction="vertical" size={0}><span>{i.message}</span><code className="feature-code">{i.action}</code></Space> },
          { title: L("النتيجة", "Outcome"), dataIndex: "outcome", width: 100, render: (v) => <OutcomeTag value={v} /> },
          { title: L("الهدف", "Target"), render: (_, i) => (i.target ? <span className="ltr">{i.target.type} {i.target.ref ?? ""}</span> : "—") },
          // رقم الحدث في سلسلته؛ عمود الترقيم «#» من الجدول نفسه.
          ...(tech ? [] : [{ title: L("التسلسل", "Seq."), dataIndex: "seq", width: 90, render: (v: number | null) => <code>{v ?? "—"}</code> }]),
        ]}
      />
    </div>
  );
}

function OutcomeTag({ value }: { value: string | null }) {
  if (!value) return <span>—</span>;
  const color = value === "success" || value.startsWith("2") ? "green" : value === "denied" || value.startsWith("4") ? "orange" : "red";
  const label = value === "success" ? L("نجاح", "Success") : value === "denied" ? L("مرفوض", "Denied") : value === "failure" ? L("فشل", "Failure") : value;
  return <Tag color={color}>{label}</Tag>;
}

function LogDetails({ item }: { item: LogItem }) {
  return (
    <div className="log-details">
      {item.changes.length > 0 && (
        <table className="diff-table">
          <thead><tr><th>{L("الحقل", "Field")}</th><th>{L("قبل", "Before")}</th><th>{L("بعد", "After")}</th></tr></thead>
          <tbody>{item.changes.map((c, n) => <tr key={n}><td><code>{c.field}</code></td><td className="before">{c.old ?? "—"}</td><td className="after">{c.new ?? "—"}</td></tr>)}</tbody>
        </table>
      )}
      <Descriptions size="small" column={{ xs: 1, md: 2 }} className="mt">
        {item.reason && <Descriptions.Item label={L("السبب", "Reason")}>{item.reason}</Descriptions.Item>}
        {item.related.length > 0 && <Descriptions.Item label={L("مرتبط بـ", "Related")}>{item.related.map((r, n) => <Tag key={n}>{r.type} {r.ref ?? r.id}</Tag>)}</Descriptions.Item>}
        {Object.entries(item.context).filter(([, v]) => v).map(([k, v]) => <Descriptions.Item key={k} label={k}><span className="ltr break">{v}</span></Descriptions.Item>)}
        {item.hash && <Descriptions.Item label={L("البصمة", "Hash")}><code className="break">{item.hash}</code></Descriptions.Item>}
      </Descriptions>
      {item.details != null && <pre className="json">{JSON.stringify(item.details, null, 2)}</pre>}
    </div>
  );
}

function IntegrityPanel() {
  const qc = useQueryClient();
  const { message } = App.useApp();
  const status = useQuery({ queryKey: ["logs-status"], queryFn: () => api.get<LogsStatus>("/api/logs/status?days=14"), refetchInterval: 15_000 });
  const [busy, setBusy] = useState(false);
  const days = status.data?.days ?? [];
  const broken = days.filter((d) => d.intact === false);
  async function verify() {
    setBusy(true);
    try {
      const r = await api.post<ChainDay[]>("/api/logs/verify", {});
      const bad = r.filter((d) => d.intact === false).length;
      if (bad) message.error(L(`${bad} يوم مكسور`, `${bad} broken day(s)`));
      else message.success(L(`فُحص ${r.length} يومًا، كلها سليمة.`, `Checked ${r.length} day(s); all intact.`));
      await qc.invalidateQueries({ queryKey: ["logs-status"] });
    } catch (e) {
      message.error(errorText(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Panel
      title={L("سلامة السلاسل", "Chain integrity")}
      icon={<SafetyCertificateOutlined aria-hidden />}
      extra={<Button icon={<ReloadOutlined />} loading={busy} onClick={verify}>{L("افحص آخر 7 أيام الآن", "Check the last 7 days now")}</Button>}
    >
      <Space wrap className="mb">
        <Tag color={status.data?.pendingOutbox ? "orange" : "green"}>{L("بانتظار الإرسال إلى MongoDB:", "Waiting to send to MongoDB:")} {status.data?.pendingOutbox ?? "…"}</Tag>
        {status.data?.oldestPendingAt && <Tag color="orange">{L("أقدمها منذ", "Oldest since")} {formatDateTime(status.data.oldestPendingAt)}</Tag>}
        {broken.length > 0 ? <Tag color="red">{L(`${broken.length} يوم مكسور`, `${broken.length} broken day(s)`)}</Tag> : <Tag color="green">{L("لا كسر في الأيام المفحوصة", "No breaks in checked days")}</Tag>}
      </Space>
      <Table<ChainDay>
        rowKey={(d) => d.chain + d.day}
        size="small"
        dataSource={days}
        pagination={{ pageSize: 8, hideOnSinglePage: true }}
        scroll={{ x: 640 }}
        columns={[
          { title: L("اليوم", "Day"), dataIndex: "day", render: (v) => formatDate(v) },
          { title: L("السلسلة", "Chain"), dataIndex: "chain", render: (v) => <code>{v}</code> },
          { title: L("الأحداث", "Events"), dataIndex: "eventCount" },
          { title: L("الأرقام", "Sequence"), render: (_, d) => <code>{d.firstSeq}–{d.lastSeq}</code> },
          {
            title: L("الحالة", "Status"),
            render: (_, d) =>
              d.intact === true ? <Tag color="green">{L("سليم", "Intact")}</Tag>
                : d.intact === false ? <Tag color="red" title={d.note ?? ""}>{L("مكسور عند", "Broken at")} #{d.brokenAtSeq} · {d.note}</Tag>
                : <Tag>{L("لم يفحص بعد", "Not checked yet")}</Tag>,
          },
        ]}
      />
    </Panel>
  );
}
