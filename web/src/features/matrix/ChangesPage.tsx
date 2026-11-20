import { useState } from "react";
import { Alert, App, Button, Input, Modal, Popconfirm, Segmented, Space, Tag, Typography } from "antd";
import { CheckOutlined, CloseOutlined, RollbackOutlined } from "@ant-design/icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { Catalog, ChangeStatus, RbacChange } from "../../api/types";
import { errorText, formatDateTime } from "../../shared/format";
import { L } from "../../shared/i18n";
import { QueryState } from "../../shared/QueryState";
import { EmptyNote, PageHeader, Panel } from "../../shared/ui";
import { describe } from "./grants";

const STATUS: Record<ChangeStatus, { color: string; label: () => string }> = {
  Pending: { color: "gold", label: () => L("ينتظر الاعتماد", "Waiting for approval") },
  Approved: { color: "green", label: () => L("معتمد وساري", "Approved and in effect") },
  Rejected: { color: "red", label: () => L("مرفوض", "Rejected") },
  Cancelled: { color: "default", label: () => L("مسحوب", "Withdrawn") },
  Stale: { color: "orange", label: () => L("تجاوزه تعديل أحدث", "Superseded") },
};

export default function ChangesPage() {
  const [tab, setTab] = useState("Pending");
  const catalog = useQuery({ queryKey: ["catalog"], queryFn: () => api.get<Catalog>("/api/iam/catalog"), staleTime: Infinity });
  const changes = useQuery({
    queryKey: ["changes", tab === "Pending" ? "Pending" : "all"],
    queryFn: () => api.get<RbacChange[]>(tab === "Pending" ? "/api/iam/changes?status=Pending" : "/api/iam/changes"),
  });
  return (
    <>
      <PageHeader title={L("طلبات تعديل المصفوفة", "Matrix change requests")}
        subtitle={L("لا يعتمد صاحب الطلب طلبه (SOD-10). الاعتماد يطبق الفرق كاملًا ويرفع إصدار الصلاحيات، فيسري خلال دقيقة دون إعادة دخول.",
          "The requester cannot approve their own request (SOD-10). Approval applies the whole difference and raises the permission version, so it takes effect within a minute without signing in again.")} />
      <div className="lcp-filters">
        <Segmented className="lcp-seg" value={tab} onChange={(v) => setTab(v as string)}
          options={[{ value: "Pending", label: L("بانتظار الاعتماد", "Waiting") }, { value: "all", label: L("كل الطلبات", "All requests") }]} />
      </div>
      <QueryState query={[changes, catalog]}>
        {() => changes.data?.length
          ? changes.data.map((c) => <ChangeCard key={c.id} change={c} catalog={catalog.data!} />)
          : <Panel><EmptyNote>{L("لا طلبات هنا.", "No requests here.")}</EmptyNote></Panel>}
      </QueryState>
    </>
  );
}

function ChangeCard({ change: c, catalog }: { change: RbacChange; catalog: Catalog }) {
  const qc = useQueryClient();
  const { message } = App.useApp();
  const [rejecting, setRejecting] = useState(false);
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function act(path: string, body: unknown, done: string) {
    setBusy(true);
    setError(null);
    try {
      await api.post(path, body);
      message.success(done);
      setRejecting(false);
      await qc.invalidateQueries();
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }

  const status = STATUS[c.status];
  return (
    <Panel
      title={<Space wrap><span>{c.roleName}</span><Tag>{c.roleCode}</Tag><Tag color={status.color}>{status.label()}</Tag></Space>}
      extra={<span className="muted nowrap">{formatDateTime(c.requestedAt)}</span>}
    >
      <p className="m0"><b>{L("الطالب:", "Requested by:")}</b> {c.requestedBy.name}</p>
      <p><b>{L("السبب:", "Reason:")}</b> {c.reason}</p>
      {error && <Alert type="error" showIcon message={error} className="mb" />}
      <div className="matrix-wrap">
        <table className="diff-table">
          <thead><tr><th>{L("الخاصية", "Feature")}</th><th>{L("قبل", "Before")}</th><th>{L("بعد", "After")}</th></tr></thead>
          <tbody>
            {c.diff.map((d) => {
              const f = catalog.features.find((x) => x.key === d.feature);
              return (
                <tr key={d.feature}>
                  <td>{f?.name ?? d.feature} <code className="feature-code">{d.feature}</code></td>
                  <td className="before">{describe(catalog, f, d.before)}</td>
                  <td className="after">{describe(catalog, f, d.after)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {c.decidedBy && (
        <p className="mt m0">
          <b>{L("القرار:", "Decision:")}</b> {c.decidedBy.name} · {formatDateTime(c.decidedAt)} {c.decisionNote && `· ${c.decisionNote}`}
        </p>
      )}
      {c.status === "Pending" && (
        <Space className="mt" wrap>
          {c.canDecide && (
            <>
              <Popconfirm title={L("اعتماد التعديل وتطبيقه الآن؟", "Approve and apply this change now?")} okText={L("اعتمد", "Approve")} onConfirm={() => act(`/api/iam/changes/${c.id}/approve`, { note: null }, L("اعتُمد التعديل وسرى.", "Approved and in effect."))}>
                <Button type="primary" icon={<CheckOutlined />} loading={busy}>{L("اعتمد", "Approve")}</Button>
              </Popconfirm>
              <Button danger icon={<CloseOutlined />} onClick={() => setRejecting(true)}>{L("ارفض", "Reject")}</Button>
            </>
          )}
          {c.canCancel && (
            <>
              <Typography.Text type="secondary">{L("طلبك ينتظر اعتماد مدير أعلى آخر.", "Your request is waiting for another super administrator.")}</Typography.Text>
              <Popconfirm title={L("سحب الطلب؟", "Withdraw the request?")} onConfirm={() => act(`/api/iam/changes/${c.id}/cancel`, {}, L("سُحب الطلب.", "Request withdrawn."))}>
                <Button icon={<RollbackOutlined />}>{L("اسحب الطلب", "Withdraw")}</Button>
              </Popconfirm>
            </>
          )}
        </Space>
      )}
      <Modal
        open={rejecting}
        title={L("رفض الطلب", "Reject the request")}
        okText={L("ارفض", "Reject")}
        okButtonProps={{ danger: true, disabled: note.trim().length < 5 }}
        confirmLoading={busy}
        onCancel={() => setRejecting(false)}
        onOk={() => act(`/api/iam/changes/${c.id}/reject`, { note }, L("رُفض الطلب.", "Request rejected."))}
      >
        <Input.TextArea rows={3} value={note} onChange={(e) => setNote(e.target.value)} placeholder={L("سبب الرفض (5 أحرف على الأقل)", "Reason for rejecting (at least 5 characters)")} aria-label={L("سبب الرفض (5 أحرف على الأقل)", "Reason for rejecting (at least 5 characters)")} />
      </Modal>
    </Panel>
  );
}
