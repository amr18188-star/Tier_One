import { useState } from "react";
import { Alert, App, Button, Form, Input, Modal, Space, Switch, Tag } from "antd";
import { DeleteOutlined, EditOutlined, PlusOutlined } from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import { queryKeys } from "../../api/queries";
import { normalizeRoot, type EmailDomain } from "../../api/security";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import { QueryState } from "../../shared/QueryState";
import { usePermissions } from "../../shared/session";
import { DataTable, EmptyNote } from "../../shared/ui";
import { useFieldErrors } from "../../shared/useFieldErrors";
import { ReasonModal } from "./IpAllowlistPanel";

const accountsText = (n: number) => L(`${n} حساب`, n === 1 ? "1 account" : `${n} accounts`);

/**
 * ADR-0016: the approved university email domains (Settings > Allowed university domains). A root covers itself and every
 * subdomain; adding a university is a setting, not a code change. Disabling or deleting a domain never deletes an account: the
 * counts below say how many accounts use it, and those accounts keep signing in; only new accounts and email changes are refused.
 */
export function DomainsPanel() {
  const perms = usePermissions();
  const qc = useQueryClient();
  const list = useQuery({ queryKey: queryKeys.emailDomains, queryFn: () => api.get<EmailDomain[]>("/api/security/domains") });
  const [editing, setEditing] = useState<EmailDomain | "new" | null>(null);
  const [toggling, setToggling] = useState<EmailDomain | null>(null);
  const [deleting, setDeleting] = useState<EmailDomain | null>(null);
  const refresh = () => qc.invalidateQueries({ queryKey: queryKeys.emailDomains });
  const can = (cap: string) => perms.can("adm.security", cap);

  const actions = (d: EmailDomain) => (
    <Space wrap>
      {can("E") && <Button size="small" icon={<EditOutlined aria-hidden />} onClick={() => setEditing(d)}>{L("تعديل", "Edit")}</Button>}
      {can("E") && <Button size="small" onClick={() => setToggling(d)}>{d.active ? L("إيقاف", "Disable") : L("تفعيل", "Enable")}</Button>}
      {can("D") && <Button size="small" danger icon={<DeleteOutlined aria-hidden />} onClick={() => setDeleting(d)}>{L("حذف", "Delete")}</Button>}
    </Space>
  );
  const status = (d: EmailDomain) => d.active ? <Tag color="green">{L("مفعل", "Active")}</Tag> : <Tag>{L("موقف", "Disabled")}</Tag>;
  const university = (d: EmailDomain) => <div className="dt-service"><b>{d.universityAr}</b>{d.universityEn && <small className="muted ltr">{d.universityEn}</small>}</div>;
  const root = (d: EmailDomain) => <span><code className="ltr">{d.domain}</code> <small className="muted">{L("ونطاقاته الفرعية", "and its subdomains")}</small></span>;

  return (
    <>
      <div className="dt-card-row mb">
        <span className="muted">{L("البريد الجامعي هو اسم الدخول. يقبل النطاق الجذري وكل نطاقاته الفرعية، مثل eng.capu.edu.eg تحت capu.edu.eg.",
          "The university email is the sign-in name. A root domain and all its subdomains are accepted, such as eng.capu.edu.eg under capu.edu.eg.")}</span>
        {can("A") && <Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => setEditing("new")}>{L("إضافة نطاق جامعة", "Add university domain")}</Button>}
      </div>
      <QueryState query={list}>
        <DataTable<EmailDomain> rowKey={(d) => d.id} loading={list.isLoading} data={list.data ?? []} minWidth={760}
          empty={<EmptyNote>{L("لا نطاقات معتمدة؛ لا يمكن إنشاء حساب حتى يضاف نطاق.", "No approved domain; no account can be created until one is added.")}</EmptyNote>}
          renderCard={(d) => (
            <>
              <div className="dt-card-row">{university(d)}{status(d)}</div>
              <div className="dt-card-row">{root(d)}<span className="muted">{accountsText(d.users)}</span></div>
              <div className="dt-card-row">{actions(d)}</div>
            </>
          )}
          columns={[
            { title: L("الجامعة", "University"), render: (_, d) => university(d) },
            { title: L("النطاق الجذري", "Root domain"), render: (_, d) => root(d) },
            { title: L("الحالة", "Status"), render: (_, d) => status(d) },
            { title: L("الحسابات", "Accounts"), render: (_, d) => accountsText(d.users) },
            { title: "", key: "a", render: (_, d) => actions(d) },
          ]} />
      </QueryState>
      {editing && <DomainModal domain={editing === "new" ? null : editing} onClose={() => setEditing(null)} onSaved={async () => { setEditing(null); await refresh(); }} />}
      {toggling && (
        <ToggleModal domain={toggling} onClose={() => setToggling(null)} onDone={async () => { setToggling(null); await refresh(); }} />
      )}
      {deleting && (
        <ReasonModal title={L(`حذف ${deleting.domain}`, `Delete ${deleting.domain}`)} okText={L("حذف", "Delete")} danger
          warning={deleting.users > 0
            ? L(`${accountsText(deleting.users)} بهذا النطاق. لا يحذف أي حساب، وتبقى تدخل كما هي؛ لكن لا ينشأ حساب جديد به ولا يغير بريد إليه. انقل هذه الحسابات إلى بريد بنطاق معتمد (من تعديل الحساب) أو أوقفها إن لم تعد مطلوبة.`,
              `${accountsText(deleting.users)} use this domain. No account is deleted and they keep signing in; but no new account or email change can use it. Move these accounts to an email at an approved domain (edit the account) or deactivate them if no longer needed.`)
            : L("لا حسابات بهذا النطاق.", "No account uses this domain.")}
          run={(reason) => api.post(`/api/security/domains/${deleting.id}/delete`, { reason })}
          onClose={() => setDeleting(null)} onDone={async () => { setDeleting(null); await refresh(); }} />
      )}
    </>
  );
}

const body = (d: EmailDomain, active: boolean) => ({ domain: d.domain, universityAr: d.universityAr, universityEn: d.universityEn, active, version: d.version });

function ToggleModal({ domain: d, onClose, onDone }: { domain: EmailDomain; onClose: () => void; onDone: () => Promise<void> }) {
  const run = useMutation({ mutationFn: () => api.put(`/api/security/domains/${d.id}`, body(d, !d.active)), onSuccess: onDone });
  return (
    <Modal open title={d.active ? L(`إيقاف ${d.domain}`, `Disable ${d.domain}`) : L(`تفعيل ${d.domain}`, `Enable ${d.domain}`)}
      okText={d.active ? L("إيقاف", "Disable") : L("تفعيل", "Enable")} okButtonProps={{ danger: d.active }} cancelText={L("إلغاء", "Cancel")}
      onCancel={onClose} onOk={() => run.mutate()} confirmLoading={run.isPending} destroyOnHidden>
      {run.error && <Alert type="error" showIcon message={errorText(run.error)} className="mb" />}
      <Alert type={d.active ? "warning" : "info"} showIcon
        message={d.active
          ? L(`${accountsText(d.users)} بهذا النطاق تبقى تدخل كما هي. بعد الإيقاف لا ينشأ حساب جديد بهذا النطاق ولا يغير بريد إليه حتى يفعل من جديد.`,
            `${accountsText(d.users)} under this domain keep signing in. Once disabled, no new account or email change can use it until it is enabled again.`)
          : L("بعد التفعيل تقبل الحسابات الجديدة بهذا النطاق ونطاقاته الفرعية.", "Once enabled, new accounts at this domain and its subdomains are accepted.")} />
    </Modal>
  );
}

function DomainModal({ domain, onClose, onSaved }: { domain: EmailDomain | null; onClose: () => void; onSaved: () => Promise<void> }) {
  const [form] = Form.useForm<{ universityAr: string; universityEn?: string; domain: string; active: boolean }>();
  const { message } = App.useApp();
  const save = useMutation({
    mutationFn: (v: { universityAr: string; universityEn?: string; domain: string; active: boolean }) => {
      const payload = { domain: v.domain.trim(), universityAr: v.universityAr.trim(), universityEn: v.universityEn?.trim() || null, active: v.active };
      return domain ? api.put(`/api/security/domains/${domain.id}`, { ...payload, version: domain.version }) : api.post("/api/security/domains", { ...payload, version: null });
    },
    onSuccess: async () => {
      message.success(L("حفظ النطاق.", "Domain saved."));
      await onSaved();
    },
  });
  const saveMarked = useFieldErrors(form, save.error);
  return (
    <Modal open title={domain ? L("تعديل نطاق جامعة", "Edit a university domain") : L("إضافة نطاق جامعة", "Add university domain")} okText={L("حفظ", "Save")}
      cancelText={L("إلغاء", "Cancel")} onCancel={onClose} onOk={() => form.submit()} confirmLoading={save.isPending} destroyOnHidden>
      {save.error && !saveMarked && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      {domain && domain.users > 0 && (
        <Alert type="warning" showIcon className="mb"
          message={L(`${accountsText(domain.users)} بهذا النطاق. تغيير النطاق لا يغير بريدها؛ تبقى تدخل به.`,
            `${accountsText(domain.users)} use this domain. Changing it does not change their emails; they keep signing in with them.`)} />
      )}
      <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)}
        initialValues={{ universityAr: domain?.universityAr, universityEn: domain?.universityEn ?? undefined, domain: domain?.domain, active: domain?.active ?? true }}>
        <Form.Item name="universityAr" label={L("اسم الجامعة", "University name")} rules={[{ required: true, whitespace: true, message: L("اكتب اسم الجامعة", "Enter the university name") }]}>
          <Input maxLength={200} autoFocus placeholder={L("مثل: جامعة الإسكندرية", "e.g. جامعة الإسكندرية")} />
        </Form.Item>
        <Form.Item name="universityEn" label={L("الاسم بالإنجليزية (اختياري)", "English name (optional)")}><Input maxLength={200} dir="ltr" placeholder="Alexandria University" /></Form.Item>
        <Form.Item name="domain" label={L("النطاق الجذري", "Root domain")}
          extra={L("مثل alex.edu.eg. تقبل معه كل نطاقاته الفرعية (eng.alex.edu.eg وmed.alex.edu.eg...) دون تسجيلها.",
            "Such as alex.edu.eg. All its subdomains (eng.alex.edu.eg, med.alex.edu.eg...) are accepted with it, without registering them.")}
          rules={[{ required: true, message: L("اكتب النطاق", "Enter the domain") },
            { validator: (_, v: string | undefined) => !v || normalizeRoot(v) ? Promise.resolve()
              : Promise.reject(new Error(L("نطاق غير صالح: حروف إنجليزية وأرقام وشرطات، ونقطة بين الأجزاء، مثل alex.edu.eg.", "Not a valid domain: letters, digits and hyphens with dots between the parts, such as alex.edu.eg."))) }]}>
          <Input dir="ltr" maxLength={253} placeholder="alex.edu.eg" />
        </Form.Item>
        <Form.Item name="active" label={L("الحالة", "Status")} valuePropName="checked">
          <Switch checkedChildren={L("مفعل", "Active")} unCheckedChildren={L("موقف", "Disabled")} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
