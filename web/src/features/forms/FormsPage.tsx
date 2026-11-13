import type { ReactNode } from "react";
import { Input, Progress, Segmented, Select, Tag } from "antd";
import { CheckCircleOutlined, ClockCircleOutlined, EditOutlined, InboxOutlined, RollbackOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { useNavigate, useSearchParams } from "react-router-dom";
import { api, query } from "../../api/client";
import type { Cycle, FormRow, SubmissionStatus } from "../../api/collection";
import { formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { dueText, submissionStatus, unitKind } from "../../shared/labels";
import { DataTable, EmptyNote, FilterMeta, oneOf, PageHeader, Panel } from "../../shared/ui";

const VIEWS = ["mine", "all", "NotStarted", "InProgress", "InReview", "Returned", "Approved"] as const;
type View = (typeof VIEWS)[number];

const PIPE: { status: SubmissionStatus; tone: string; icon: ReactNode }[] = [
  { status: "NotStarted", tone: "idle", icon: <InboxOutlined aria-hidden /> },
  { status: "InProgress", tone: "work", icon: <EditOutlined aria-hidden /> },
  { status: "Returned", tone: "bad", icon: <RollbackOutlined aria-hidden /> },
  { status: "InReview", tone: "review", icon: <ClockCircleOutlined aria-hidden /> },
  { status: "Approved", tone: "ok", icon: <CheckCircleOutlined aria-hidden /> },
];

/** نسبة المؤشرات المدخلة من المطلوب في كل نماذج الدورة. */
function entryPct(rows: FormRow[]) {
  const req = rows.reduce((n, r) => n + r.required, 0);
  return req === 0 ? 0 : Math.round((100 * rows.reduce((n, r) => n + r.completed, 0)) / req);
}

/**
 * النماذج: يفتح على «مطلوب مني» (ما ينتظر إدخالي أو مراجعتي الآن)، والأقدم موعدًا أولًا.
 * النقر على صف يفتح النموذج نفسه للإدخال أو المراجعة.
 */
export default function FormsPage() {
  const [params, setParams] = useSearchParams();
  const navigate = useNavigate();
  const view: View = oneOf(params.get("view"), VIEWS) ?? "mine";
  const q = params.get("q") ?? "";
  const cycles = useQuery({ queryKey: ["cycles"], queryFn: () => api.get<Cycle[]>("/api/cycles") });
  const live = cycles.data?.filter((c) => c.status !== "Draft") ?? [];
  const cycleId = params.get("cycle") ?? live.find((c) => c.status === "Open")?.id ?? live[0]?.id;

  const set = (key: string, value: string | undefined) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next, { replace: true });
  };

  const all = useQuery({
    queryKey: ["forms", cycleId],
    queryFn: () => api.get<FormRow[]>(`/api/forms${query({ cycleId })}`),
    enabled: !!cycleId,
  });
  // الأولوية: المعاد للتصحيح، ثم ما ينتظرني، ثم الأقرب موعدًا.
  const rows = (all.data ?? []).filter((r) =>
    (view === "mine" ? r.awaitingMe : view === "all" ? true : r.status === view)
    && (!q || r.unitName.includes(q) || (r.parentName ?? "").includes(q)))
    .sort((a, b) => Number(b.status === "Returned") - Number(a.status === "Returned") || Number(b.awaitingMe) - Number(a.awaitingMe) || a.dueOn.localeCompare(b.dueOn));
  const filtered = view !== "mine" || !!q;
  const count = (f: (r: FormRow) => boolean) => all.data?.filter(f).length ?? 0;

  return (
    <>
      <PageHeader title={L("نماذج الجهات", "Unit forms")}
        subtitle={L("كل جهة تدخل قيم مؤشراتها في نموذج واحد، ثم يمر على المراجعين حتى اعتماد الجودة المركزية.",
          "Each unit enters its values in one form, which then passes through reviewers up to central quality approval.")}
        stats={all.data ? [
          { label: L("مطلوب مني الآن", "Waiting for me"), value: count((r) => r.awaitingMe), tone: count((r) => r.awaitingMe) > 0 ? "warn" : undefined },
          { label: L("متأخر عن الموعد", "Overdue"), value: count((r) => r.overdue), tone: count((r) => r.overdue) > 0 ? "bad" : undefined },
          { label: L("معتمد نهائيًا", "Approved"), value: <span className="ltr">{count((r) => r.status === "Approved")}/{all.data.length}</span>, tone: "ok" },
          { label: L("اكتمال الإدخال", "Entry complete"), value: <span className="ltr">{entryPct(all.data)}%</span> },
        ] : undefined}
        extra={live.length > 1 && (
          <Select value={cycleId} onChange={(v) => set("cycle", v)} style={{ minWidth: 260 }} aria-label={L("الدورة", "Cycle")}
            options={live.map((c) => ({ value: c.id, label: `${c.name}${c.status === "Closed" ? L(" (مغلقة)", " (closed)") : ""}` }))} />
        )} />

      {cycles.isSuccess && live.length === 0 ? (
        <Panel><EmptyNote>{L("لا توجد دورة مفتوحة الآن. تفتح الدورات من صفحة «دورات جمع البيانات».", "No open cycle right now.")}</EmptyNote></Panel>
      ) : (
        <>
          {/* مسار النماذج في الدورة: كم نموذجًا في كل مرحلة، والنقر يعرضها. */}
          <nav className="f-pipe" aria-label={L("النماذج بحسب مرحلتها", "Forms by stage")}>
            {PIPE.map((p, k) => {
              const n = count((r) => r.status === p.status);
              const total = all.data?.length || 1;
              return (
                <button key={p.status} type="button" className={`f-pipe-step tone-${p.tone}${view === p.status ? " on" : ""}`} aria-pressed={view === p.status}
                  onClick={() => set("view", view === p.status ? undefined : p.status)}>
                  <span className="f-pipe-n ltr">{all.isLoading ? "…" : n}</span>
                  <span className="f-pipe-label">{p.icon}{submissionStatus[p.status].text}</span>
                  <span className="f-pipe-bar"><i style={{ width: `${(100 * n) / total}%` }} /></span>
                  {k < PIPE.length - 1 && <span className="f-pipe-arrow" aria-hidden />}
                </button>
              );
            })}
          </nav>

          <Panel>
            <div className="lcp-filters">
              <Segmented value={view} onChange={(v) => set("view", v === "mine" ? undefined : String(v))}
                options={[
                  { value: "mine", label: L("مطلوب مني", "Waiting for me") },
                  { value: "all", label: L("الكل", "All") },
                  ...(["NotStarted", "InProgress", "InReview", "Returned", "Approved"] as SubmissionStatus[]).map((s) => ({ value: s, label: submissionStatus[s].text })),
                ]} />
              <Input.Search key={q} allowClear defaultValue={q} placeholder={L("اسم الجهة أو الكلية", "Unit or college name")} onSearch={(v) => set("q", v || undefined)} />
              <FilterMeta shown={rows.length} total={all.data?.length} noun={L("نموذج", "forms")} active={filtered}
                onReset={() => setParams(params.get("cycle") ? { cycle: params.get("cycle")! } : {}, { replace: true })} />
            </div>
            <DataTable<FormRow>
              rowKey={(r) => r.id}
              data={rows}
              loading={all.isLoading}
              resetKey={`${cycleId}|${view}|${q}`}
              onRowClick={(r) => navigate(`/forms/${r.id}`)}
              pageSizes={[20, 50, 100]}
              columns={[
                {
                  title: L("الجهة", "Unit"), key: "unit", render: (_, r) => (
                    <span><b>{r.unitName}</b>{r.parentName && <small className="muted"> · {r.parentName}</small>}<br /><small className="muted">{unitKind[r.unitKind]}</small></span>
                  ),
                },
                { title: L("عند", "At"), dataIndex: "stepName", width: 170 },
                {
                  title: L("الحالة", "Status"), key: "status", width: 150, render: (_, r) => (
                    <span>
                      <Tag color={submissionStatus[r.status].color}>{submissionStatus[r.status].text}</Tag>
                      {r.awaitingMe && <Tag color="cyan">{L("ينتظرك", "Yours")}</Tag>}
                    </span>
                  ),
                },
                {
                  title: L("الإدخال", "Entered"), key: "done", width: 170, render: (_, r) => (
                    <Progress percent={r.required ? Math.round((100 * r.completed) / r.required) : 0} size="small"
                      format={() => <span className="ltr">{r.completed}/{r.required}</span>} />
                  ),
                },
                {
                  title: L("الموعد", "Due"), dataIndex: "dueOn", width: 150, render: (d: string, r) => (
                    <span className={`t1-due${r.overdue ? " late" : ""}`}>
                      {r.status === "Approved" ? formatDate(d) : <>{dueText(d)}<br /><small className="muted ltr">{formatDate(d)}</small></>}
                    </span>
                  ),
                },
              ]}
              renderCard={(r) => (
                <>
                  <div className="dt-card-row"><b>{r.unitName}</b><Tag color={submissionStatus[r.status].color}>{submissionStatus[r.status].text}</Tag></div>
                  <small className="muted">{r.parentName ?? unitKind[r.unitKind]} · {r.stepName}{r.status !== "Approved" && ` · ${dueText(r.dueOn)}`}</small>
                  <Progress percent={r.required ? Math.round((100 * r.completed) / r.required) : 0} size="small" format={() => <span className="ltr">{r.completed}/{r.required}</span>} />
                </>
              )}
              empty={view === "mine"
                ? <span><CheckCircleOutlined /> {L("لا شيء ينتظرك الآن في هذه الدورة.", "Nothing is waiting for you in this cycle.")}</span>
                : L("لا نماذج بهذه المرشحات.", "No forms match these filters.")}
            />
          </Panel>
        </>
      )}
    </>
  );
}
