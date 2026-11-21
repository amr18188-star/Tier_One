import { useState } from "react";
import { Alert, App, Button, Form, Input, Modal, Space, Switch, Table, Tag, Typography } from "antd";
import { DeleteOutlined, EditOutlined, PlusOutlined } from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import { queryKeys } from "../../api/queries";
import { isIpEntry, type IpAllowlist, type IpEntry } from "../../api/security";
import { errorText, formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { QueryState } from "../../shared/QueryState";
import { usePermissions } from "../../shared/session";
import { useFieldErrors } from "../../shared/useFieldErrors";

type Scope = { kind: "global" } | { kind: "user"; userId: string };

const pathOf = (s: Scope) => (s.kind === "global" ? "/api/security/ip-allowlist" : `/api/iam/users/${s.userId}/ip-allowlist`);
const keyOf = (s: Scope) => (s.kind === "global" ? queryKeys.ipAllowlist : queryKeys.userIpAllowlist(s.userId));

/**
 * ADR-0016: one IP allowlist, the global one (Settings) or an account's own (the account's details). Addresses and CIDR ranges,
 * IPv4 and IPv6, a switch, and every change with its reason in the security log. The server checks and enforces everything;
 * this panel only manages the list. `readOnly` for an account the viewer may not manage (their own, or a reserved one).
 */
export function IpAllowlistPanel({ scope, readOnly = false }: { scope: Scope; readOnly?: boolean }) {
  const perms = usePermissions();
  const qc = useQueryClient();
  const path = pathOf(scope);
  const list = useQuery({ queryKey: keyOf(scope), queryFn: () => api.get<IpAllowlist>(path) });
  const [editing, setEditing] = useState<IpEntry | "new" | null>(null);
  const [removing, setRemoving] = useState<IpEntry | null>(null);
  const [switching, setSwitching] = useState(false);
  const can = (cap: string) => !readOnly && perms.can("adm.security", cap);
  const saved = (data: IpAllowlist) => qc.setQueryData(keyOf(scope), data);
  const global = scope.kind === "global";
  const data = list.data;
  const applies = !!data?.enabled && data.entries.length > 0;

  return (
    <QueryState query={list}>
      {data && (
        <>
          <Alert type="info" showIcon className="mb"
            message={global
              ? L("القائمة العامة تسري على كل الحسابات: من يدخل من عنوان فيها يقبل. ولكل حساب قائمته الإضافية في تفاصيله، فلا يلزم تكرار العنوان في القائمتين. إن لم تسر أي قائمة على الحساب فالدخول بلا قيد.",
                "The global list applies to every account: an address in it is accepted. Each account can have an additional list in its details, so an address need not be repeated in both. Where no list applies to an account, access is unrestricted.")
              : L("عناوين يسمح منها لهذا الحساب إضافة إلى القائمة العامة. حين يشغل القيد لا يقبل الحساب إلا من عنوان في إحدى القائمتين.",
                "Addresses this account may use in addition to the global list. With the restriction on, the account is accepted only from an address in one of the two lists.")} />
          <div className="dt-card-row mb">
            <Space wrap>
              <Switch checked={data.enabled} disabled={!can("E")} onChange={() => setSwitching(true)}
                checkedChildren={L("مشغل", "On")} unCheckedChildren={L("موقف", "Off")}
                aria-label={global ? L("قيد عناوين IP العام", "Global IP restriction") : L("قيد عناوين IP لهذا الحساب", "IP restriction for this account")} />
              {applies ? <Tag color="green">{L("يسري", "Applies")}</Tag>
                : data.enabled ? <Tag color="gold">{L("مشغل بلا عناوين: لا أثر له", "On with no address: no effect")}</Tag>
                : <Tag>{L("لا يسري", "Not applied")}</Tag>}
              {global && data.yourAddress && <span className="muted">{L("عنوانك الآن:", "Your address now:")} <b className="ltr">{data.yourAddress}</b></span>}
            </Space>
            {can("A") && <Button icon={<PlusOutlined aria-hidden />} onClick={() => setEditing("new")}>{L("إضافة عنوان أو نطاق", "Add an address or range")}</Button>}
          </div>
          <Table<IpEntry> rowKey="id" size="small" dataSource={data.entries} pagination={false} scroll={{ x: 560 }}
            locale={{ emptyText: L("لا عناوين بعد.", "No addresses yet.") }}
            columns={[
              { title: L("العنوان أو النطاق", "Address or range"), render: (_, e) => <code className="ltr">{e.value}</code> },
              { title: L("الوصف", "Label"), render: (_, e) => e.label ?? <span className="muted">—</span> },
              { title: L("أضيف", "Added"), render: (_, e) => formatDate(e.createdAt) },
              {
                title: "", key: "a", render: (_, e) => (
                  <Space>
                    {can("E") && <Button size="small" icon={<EditOutlined aria-hidden />} onClick={() => setEditing(e)}>{L("تعديل", "Edit")}</Button>}
                    {can("D") && <Button size="small" danger icon={<DeleteOutlined aria-hidden />} onClick={() => setRemoving(e)}>{L("حذف", "Remove")}</Button>}
                  </Space>
                ),
              },
            ]} />
          {editing && <EntryModal path={path} entry={editing === "new" ? null : editing} onClose={() => setEditing(null)} onSaved={(d) => { saved(d); setEditing(null); }} />}
          {removing && (
            <ReasonModal title={L(`حذف ${removing.value}`, `Remove ${removing.value}`)} okText={L("حذف", "Remove")} danger
              warning={applies ? L("من يدخل من هذا العنوان وحده سيمنع فور الحذف.", "Whoever comes only from this address is refused as soon as it is removed.") : null}
              run={(reason) => api.post<IpAllowlist>(`${path}/entries/${removing.id}/delete`, { reason })}
              onClose={() => setRemoving(null)} onDone={(d) => { saved(d); setRemoving(null); }} />
          )}
          {switching && (
            <ReasonModal okText={data.enabled ? L("إيقاف", "Turn off") : L("تشغيل", "Turn on")} danger={!data.enabled}
              title={data.enabled ? L("إيقاف قيد العناوين", "Turn the IP restriction off") : L("تشغيل قيد العناوين", "Turn the IP restriction on")}
              warning={data.enabled ? null : data.entries.length === 0
                ? L("القائمة فارغة: لن يسري القيد حتى يضاف عنوان.", "The list is empty: the restriction applies once an address is added.")
                : global ? L("لن يقبل بعد التشغيل إلا من يدخل من عنوان في القائمة العامة أو في قائمة حسابه. ويرفض الخادم التشغيل إن كان سيمنعك أنت.",
                    "Once on, only addresses in the global list or in an account's own list are accepted. The server refuses it if it would shut you out.")
                  : L("لن يقبل هذا الحساب بعد التشغيل إلا من عنوان في قائمته أو في القائمة العامة.", "Once on, this account is accepted only from an address in its list or the global one.")}
              run={(reason) => api.put<IpAllowlist>(`${path}/enabled`, { enabled: !data.enabled, reason })}
              onClose={() => setSwitching(false)} onDone={(d) => { saved(d); setSwitching(false); }} />
          )}
        </>
      )}
    </QueryState>
  );
}

function EntryModal({ path, entry, onClose, onSaved }: { path: string; entry: IpEntry | null; onClose: () => void; onSaved: (d: IpAllowlist) => void }) {
  const [form] = Form.useForm<{ value: string; label?: string }>();
  const { message } = App.useApp();
  const save = useMutation({
    mutationFn: (v: { value: string; label?: string }) => entry
      ? api.put<IpAllowlist>(`${path}/entries/${entry.id}`, { value: v.value.trim(), label: v.label?.trim() || null, version: entry.version })
      : api.post<IpAllowlist>(`${path}/entries`, { value: v.value.trim(), label: v.label?.trim() || null, version: null }),
    onSuccess: (d) => {
      message.success(L("حفظ العنوان.", "Address saved."));
      onSaved(d);
    },
  });
  const saveMarked = useFieldErrors(form, save.error);
  return (
    <Modal open title={entry ? L("تعديل عنوان", "Edit an address") : L("إضافة عنوان أو نطاق", "Add an address or range")} okText={L("حفظ", "Save")}
      cancelText={L("إلغاء", "Cancel")} onCancel={onClose} onOk={() => form.submit()} confirmLoading={save.isPending} destroyOnHidden>
      {save.error && !saveMarked && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" initialValues={{ value: entry?.value.replace(/\/(32|128)$/, ""), label: entry?.label ?? undefined }} onFinish={(v) => save.mutate(v)}>
        <Form.Item name="value" label={L("العنوان أو النطاق", "Address or range")}
          extra={L("عنوان واحد مثل 41.65.100.20 أو 2001:db8::1، أو نطاق CIDR مثل 192.168.1.0/24 أو 2001:db8::/32.",
            "One address such as 41.65.100.20 or 2001:db8::1, or a CIDR range such as 192.168.1.0/24 or 2001:db8::/32.")}
          rules={[{ required: true, message: L("اكتب العنوان", "Enter the address") },
            { validator: (_, v: string | undefined) => !v || isIpEntry(v) ? Promise.resolve() : Promise.reject(new Error(L("عنوان IP أو نطاق CIDR غير صالح.", "Not a valid IP address or CIDR range."))) }]}>
          <Input dir="ltr" maxLength={64} autoFocus placeholder="192.168.1.0/24" />
        </Form.Item>
        <Form.Item name="label" label={L("الوصف (اختياري)", "Label (optional)")}><Input maxLength={200} placeholder={L("مثل: شبكة كلية الهندسة", "e.g. Faculty of Engineering network")} /></Form.Item>
      </Form>
    </Modal>
  );
}

/** A change that needs a written reason (switching a list, removing an address), with its warning. */
export function ReasonModal<T>({ title, okText, danger, warning, run, onClose, onDone }: {
  title: string; okText: string; danger?: boolean; warning: string | null; run: (reason: string) => Promise<T>; onClose: () => void; onDone: (d: T) => void;
}) {
  const [form] = Form.useForm<{ reason: string }>();
  const go = useMutation({ mutationFn: (reason: string) => run(reason), onSuccess: onDone });
  const goMarked = useFieldErrors(form, go.error);
  return (
    <Modal open title={title} okText={okText} cancelText={L("إلغاء", "Cancel")} okButtonProps={{ danger }} onCancel={onClose} onOk={() => form.submit()}
      confirmLoading={go.isPending} destroyOnHidden>
      {warning && <Alert type="warning" showIcon message={warning} className="mb" />}
      {go.error && !goMarked && <Alert type="error" showIcon message={errorText(go.error)} className="mb" />}
      <Form form={form} layout="vertical" onFinish={(v) => go.mutate(v.reason.trim())}>
        <Form.Item name="reason" label={L("السبب", "Reason")} rules={[{ required: true, whitespace: true, min: 5, message: L("اكتب سببًا واضحًا (5 أحرف على الأقل).", "Enter a clear reason (at least 5 characters).") }]}>
          <Input.TextArea rows={2} maxLength={500} autoFocus />
        </Form.Item>
      </Form>
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>{L("يسجل التغيير وسببه في السجل الأمني باسمك.", "The change and its reason are recorded in the security log under your name.")}</Typography.Paragraph>
    </Modal>
  );
}
