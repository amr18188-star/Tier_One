import { useState } from "react";
import { App as AntApp, Button, Segmented, Tag, Tooltip } from "antd";
import { RedoOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../api/client";
import { errorText, formatDateTime } from "../../shared/format";
import { L } from "../../shared/i18n";
import { QueryState } from "../../shared/QueryState";
import { usePermissions } from "../../shared/session";
import { DataTable, EmptyNote, PageHeader } from "../../shared/ui";

/** رسالة في الصندوق الصادر كما يعيدها GET /api/admin/mail، بعنوان مقنّع. */
export interface MailRow {
  id: number;
  kind: string;
  to: string | null;
  subject: string;
  createdAt: string;
  attempts: number;
  lastError: string | null;
  sentAt: string | null;
  failedAt: string | null;
  status: "pending" | "sent" | "failed" | "cancelled" | "skipped";
  resendable: boolean;
  resentAs: number | null;
  resentFrom: number | null;
}

type Filter = "failed" | "pending" | "sent" | "all";

const STATUS: Record<MailRow["status"], { color: string; ar: string; en: string }> = {
  pending: { color: "blue", ar: "في الانتظار", en: "Waiting" },
  sent: { color: "green", ar: "أرسلت", en: "Sent" },
  failed: { color: "red", ar: "فشلت", en: "Failed" },
  cancelled: { color: "default", ar: "ألغيت", en: "Cancelled" },
  skipped: { color: "default", ar: "تخطيت", en: "Skipped" },
};

/**
 * البريد الصادر (adm.mail، BE-11): ما ينتظر وما أرسل وما فشل وسببه. الإشعار الفاشل يعاد إرساله مرة واحدة إلى العنوان الحالي لصاحبه؛
 * رسائل الحساب (روابط الدخول والاستعادة) لا تعاد، يطلب صاحبها رابطًا جديدًا.
 */
export default function MailPage() {
  const perms = usePermissions();
  const { message } = AntApp.useApp();
  const canResend = perms.can("adm.mail", "E");
  const [filter, setFilter] = useState<Filter>("failed");
  const [busy, setBusy] = useState<number | null>(null);
  const list = useQuery({ queryKey: ["admin-mail", filter], queryFn: () => api.get<MailRow[]>(`/api/admin/mail?status=${filter}`) });

  const resend = async (m: MailRow) => {
    setBusy(m.id);
    try {
      await api.post(`/api/admin/mail/${m.id}/resend`);
      message.success(L("أضيفت الرسالة إلى الإرسال من جديد.", "The mail was queued again."));
      await list.refetch();
    } catch (e) {
      message.error(errorText(e));
    } finally {
      setBusy(null);
    }
  };

  const action = (m: MailRow) => {
    if (m.resentAs) return <span className="muted">{L(`أعيدت (#${m.resentAs})`, `Resent (#${m.resentAs})`)}</span>;
    if (m.status !== "failed" || !canResend) return null;
    if (!m.resendable)
      return <Tooltip title={L("رسالة حساب: يطلب صاحبها رابطًا جديدًا.", "An account mail: its owner asks for a new link.")}><Tag>{L("لا تعاد", "Not resendable")}</Tag></Tooltip>;
    return (
      <Button size="small" icon={<RedoOutlined aria-hidden />} loading={busy === m.id} onClick={(e) => { e.stopPropagation(); void resend(m); }}
        aria-label={L(`إعادة إرسال: ${m.subject}`, `Resend: ${m.subject}`)}>
        {L("إعادة الإرسال", "Resend")}
      </Button>
    );
  };
  const status = (m: MailRow) => (
    <span className="nowrap">
      <Tag color={STATUS[m.status].color}>{L(STATUS[m.status].ar, STATUS[m.status].en)}</Tag>
      {m.attempts > 0 && <small className="muted">{L(`المحاولات: ${m.attempts}`, `${m.attempts} attempts`)}</small>}
    </span>
  );

  return (
    <>
      <PageHeader title={L("البريد الصادر", "Outgoing mail")}
        subtitle={L("الرسائل الفاشلة وسبب فشلها، وإعادة إرسال الإشعار الفاشل. يظهر كل إشعار لصاحبه داخل المنظومة أيضًا.",
          "Failed mail and why it failed, and resending a failed notification. Every notification also shows in its owner's inbox.")} />
      <Segmented<Filter> className="mb" value={filter} onChange={setFilter} options={[
        { value: "failed", label: L("الفاشلة", "Failed") },
        { value: "pending", label: L("في الانتظار", "Waiting") },
        { value: "sent", label: L("المرسلة", "Sent") },
        { value: "all", label: L("الكل", "All") },
      ]} />
      <QueryState query={list}>
        <DataTable<MailRow> rowKey={(m) => String(m.id)} loading={list.isLoading} data={list.data ?? []} minWidth={760} resetKey={filter}
          empty={<EmptyNote>{filter === "failed" ? L("لا رسائل فاشلة.", "No failed mail.") : L("لا رسائل.", "No mail.")}</EmptyNote>}
          renderCard={(m) => (
            <>
              <div className="dt-card-row"><b>{m.subject}</b>{status(m)}</div>
              <div className="dt-card-row"><span className="ltr muted">{m.to}</span><span className="muted">{formatDateTime(m.createdAt)}</span></div>
              {m.lastError && <div className="dt-card-row muted ltr">{m.lastError}</div>}
              <div className="dt-card-row">{action(m)}</div>
            </>
          )}
          columns={[
            { title: L("الموضوع", "Subject"), render: (_, m) => <div className="dt-service"><b>{m.subject}</b><small className="muted ltr">{m.kind}</small></div> },
            { title: L("إلى", "To"), render: (_, m) => <span className="ltr">{m.to}</span> },
            { title: L("الحالة", "Status"), render: (_, m) => status(m) },
            { title: L("السبب", "Reason"), render: (_, m) => m.lastError ? <span className="muted ltr">{m.lastError}</span> : "—" },
            { title: L("أضيفت", "Queued"), render: (_, m) => formatDateTime(m.createdAt) },
            { title: "", key: "a", render: (_, m) => action(m) },
          ]} />
      </QueryState>
    </>
  );
}
