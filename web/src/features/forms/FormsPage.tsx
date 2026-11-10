import { Input, Progress, Segmented, Select, Tag } from "antd";
import { CheckCircleOutlined, ClockCircleOutlined, InboxOutlined, RollbackOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { useNavigate, useSearchParams } from "react-router-dom";
import { api, query } from "../../api/client";
import type { Cycle, FormRow, SubmissionStatus } from "../../api/collection";
import { formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { submissionStatus, unitKind } from "../../shared/labels";
import { DataTable, EmptyNote, oneOf, PageHeader, Panel, StatTile } from "../../shared/ui";

const VIEWS = ["mine", "all", "NotStarted", "InProgress", "InReview", "Returned", "Approved"] as const;
type View = (typeof VIEWS)[number];

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
  const rows = (all.data ?? []).filter((r) =>
    (view === "mine" ? r.awaitingMe : view === "all" ? true : r.status === view)
    && (!q || r.unitName.includes(q) || (r.parentName ?? "").includes(q)));
  const count = (f: (r: FormRow) => boolean) => all.data?.filter(f).length ?? 0;

  return (
    <>
      <PageHeader title={L("نماذج الجهات", "Unit forms")}
        subtitle={L("كل جهة تدخل قيم مؤشراتها في نموذج واحد، ثم يمر على المراجعين حتى اعتماد الجودة المركزية.",
          "Each unit enters its values in one form, which then passes through reviewers up to central quality approval.")}
        extra={live.length > 1 && (
          <Select value={cycleId} onChange={(v) => set("cycle", v)} style={{ minWidth: 260 }} aria-label={L("الدورة", "Cycle")}
            options={live.map((c) => ({ value: c.id, label: `${c.name}${c.status === "Closed" ? L(" (مغلقة)", " (closed)") : ""}` }))} />
        )} />

      {cycles.isSuccess && live.length === 0 ? (
        <Panel><EmptyNote>{L("لا توجد دورة مفتوحة الآن. تفتح الدورات من صفحة «دورات جمع البيانات».", "No open cycle right now.")}</EmptyNote></Panel>
      ) : (
        <>
          <div className="lcp-stats">
            <StatTile icon={<InboxOutlined />} label={L("مطلوب مني الآن", "Waiting for me")} value={count((r) => r.awaitingMe)} loading={all.isLoading}
              to={`/forms${query({ cycle: cycleId, view: "mine" })}`} />
            <StatTile icon={<RollbackOutlined />} tone="rose" label={L("معاد للتصحيح", "Returned")} value={count((r) => r.status === "Returned")} loading={all.isLoading}
              to={`/forms${query({ cycle: cycleId, view: "Returned" })}`} />
            <StatTile icon={<ClockCircleOutlined />} tone="gold" label={L("متأخر عن الموعد", "Overdue")} value={count((r) => r.overdue)} loading={all.isLoading} />
            <StatTile icon={<CheckCircleOutlined />} tone="green" label={L("معتمد نهائيًا", "Approved")} value={`${count((r) => r.status === "Approved")}/${all.data?.length ?? 0}`}
              loading={all.isLoading} to={`/forms${query({ cycle: cycleId, view: "Approved" })}`} />
          </div>

          <Panel>
            <div className="lcp-filters">
              <Segmented value={view} onChange={(v) => set("view", v === "mine" ? undefined : String(v))}
                options={[
                  { value: "mine", label: L("مطلوب مني", "Waiting for me") },
                  { value: "all", label: L("الكل", "All") },
                  ...(["NotStarted", "InProgress", "InReview", "Returned", "Approved"] as SubmissionStatus[]).map((s) => ({ value: s, label: submissionStatus[s].text })),
                ]} />
              <Input.Search allowClear defaultValue={q} placeholder={L("اسم الجهة أو الكلية", "Unit or college name")} onSearch={(v) => set("q", v || undefined)} />
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
                  title: L("الموعد", "Due"), dataIndex: "dueOn", width: 130, render: (d: string, r) => (
                    <span className={r.overdue ? "f-issue err" : undefined}>{formatDate(d)}{r.overdue && ` · ${L("متأخر", "late")}`}</span>
                  ),
                },
              ]}
              renderCard={(r) => (
                <>
                  <div className="dt-card-row"><b>{r.unitName}</b><Tag color={submissionStatus[r.status].color}>{submissionStatus[r.status].text}</Tag></div>
                  <small className="muted">{r.parentName ?? unitKind[r.unitKind]} · {r.stepName}</small>
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
