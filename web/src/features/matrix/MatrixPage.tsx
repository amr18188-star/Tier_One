import { useEffect, useMemo, useState } from "react";
import { Alert, App, Badge, Button, Empty, Form, Input, Modal, Select, Space, Spin, Tag, Typography } from "antd";
import { DeleteOutlined, LockOutlined, PlusOutlined, SendOutlined, UndoOutlined } from "@ant-design/icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../../api/client";
import type { Catalog, GrantView, RbacChange, RoleDetail, RoleSummary } from "../../api/types";
import { errorText } from "../../shared/format";
import { AppLoading } from "../../shared/AppLoading";
import { L } from "../../shared/i18n";
import { QueryState } from "../../shared/QueryState";
import { usePermissions } from "../../shared/session";
import { PageHeader, Panel } from "../../shared/ui";
import { clean, describe, diffKeys, type Grants } from "./grants";
import { MatrixTable } from "./MatrixTable";

export default function MatrixPage() {
  const { roleId } = useParams();
  const nav = useNavigate();
  const perms = usePermissions();
  const catalog = useQuery({ queryKey: ["catalog"], queryFn: () => api.get<Catalog>("/api/iam/catalog"), staleTime: Infinity });
  const roles = useQuery({ queryKey: ["roles"], queryFn: () => api.get<RoleSummary[]>("/api/iam/roles") });
  const selectedId = roleId ?? roles.data?.find((r) => !r.isLocked)?.id;
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    if (!roleId && selectedId) nav(`/matrix/${selectedId}`, { replace: true });
  }, [roleId, selectedId, nav]);

  const header = (
    <PageHeader title={L("مصفوفة الصلاحيات", "Permission matrix")}
      subtitle={L("كل تعديل يرسل طلبًا بالفرق والسبب، ويسري بعد اعتماد مدير أعلى غير الذي طلبه.", "Every edit sends a request with the difference and a reason. It takes effect after another super administrator approves it.")}
      extra={roles.data && perms.can("adm.roles", "A") && <Button type="primary" size="large" icon={<PlusOutlined aria-hidden />} onClick={() => setCreating(true)}>{L("دور جديد", "New role")}</Button>} />
  );
  if (catalog.isLoading || roles.isLoading) return <>{header}<AppLoading /></>;
  if (!catalog.data || !roles.data) return <>{header}<Alert type="error" message={L("تعذر تحميل المصفوفة.", "Could not load the matrix.")} /></>;

  return (
    <>
      {header}
      <div className="matrix-layout">
        <nav className="role-list" aria-label={L("الأدوار", "Roles")}>
          {roles.data.map((r) => (
            <Link key={r.id} to={`/matrix/${r.id}`} className={"role-item" + (r.id === selectedId ? " active" : "")}>
              <span className="role-code">{r.code}</span>
              <span className="role-name">{r.name}</span>
              <span className="role-meta">
                {r.isLocked && <LockOutlined />}
                {r.pendingChangeId && <Badge status="warning" />}
                <span title={L("حاملو الدور", "Holders")}>{r.holders}</span>
              </span>
            </Link>
          ))}
        </nav>
        <Panel>
          {selectedId ? <RoleEditor key={selectedId} roleId={selectedId} catalog={catalog.data} roles={roles.data} /> : <Empty />}
        </Panel>
      </div>
      <CreateRoleModal open={creating} onClose={() => setCreating(false)} roles={roles.data} onCreated={(id) => nav(`/matrix/${id}`)} />
    </>
  );
}

function RoleEditor({ roleId, catalog, roles }: { roleId: string; catalog: Catalog; roles: RoleSummary[] }) {
  const perms = usePermissions();
  const qc = useQueryClient();
  const { message } = App.useApp();
  const nav = useNavigate();
  const detail = useQuery({ queryKey: ["role", roleId], queryFn: () => api.get<RoleDetail>(`/api/iam/roles/${roleId}`) });
  const [draft, setDraft] = useState<Grants>({});
  const [module, setModule] = useState("all");
  const [search, setSearch] = useState("");
  const [compareId, setCompareId] = useState<string | undefined>();
  const [reviewing, setReviewing] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const compare = useQuery({ queryKey: ["role", compareId], queryFn: () => api.get<RoleDetail>(`/api/iam/roles/${compareId}`), enabled: !!compareId });

  const version = detail.data?.role.grantsVersion;
  useEffect(() => {
    if (detail.data) setDraft(detail.data.grants);
  }, [detail.data, version]);

  const original = detail.data?.grants ?? {};
  const changed = useMemo(() => diffKeys(original, draft), [original, draft]);
  if (!detail.data) return <QueryState query={detail} loading={<div className="center-screen"><Spin /></div>}>{null}</QueryState>;
  const role = detail.data.role;
  const editable = perms.can("adm.roles", "E") && !role.isLocked && !role.pendingChangeId;

  return (
    <>
      <div className="editor-head">
        <div>
          <Space wrap>
            <Typography.Title level={3} className="m0">{role.name}</Typography.Title>
            <Tag>{role.code}</Tag>
            {role.isSystem ? <Tag color="blue">{L("دور من الكتالوج", "Catalog role")}</Tag> : <Tag color="purple">{L("دور مخصص", "Custom role")}</Tag>}
            <Tag>{L("الإصدار", "Version")} {role.grantsVersion}</Tag>
            <Tag>{L("الحاملون", "Holders")} {role.holders}</Tag>
          </Space>
          {role.description && <Typography.Paragraph type="secondary" className="m0">{role.description}</Typography.Paragraph>}
        </div>
        <Space wrap>
          {perms.can("adm.roles", "D") && !role.isSystem && role.holders === 0 && (
            <Button danger icon={<DeleteOutlined />} onClick={() => setDeleting(true)}>{L("حذف الدور", "Delete role")}</Button>
          )}
          {editable && (
            <>
              <Button icon={<UndoOutlined />} disabled={changed.length === 0} onClick={() => setDraft(original)}>{L("تراجع", "Undo")}</Button>
              <Button type="primary" icon={<SendOutlined />} disabled={changed.length === 0} onClick={() => setReviewing(true)}>
                {L("أرسل طلب التعديل", "Send change request")} {changed.length > 0 && `(${changed.length})`}
              </Button>
            </>
          )}
        </Space>
      </div>
      {role.isLocked && (
        <Alert className="mb" type="info" showIcon message={L("المدير الأعلى يملك كل الإمكانيات المعرّفة دائمًا، ولا يُعدَّل من المصفوفة. يُقترح حسابان على الأقل.", "The super administrator always has every defined capability and cannot be edited here. At least two accounts are recommended.")} />
      )}
      {role.code === "SA" && !role.isLocked && (
        <Alert className="mb" type="info" showIcon message={L(
          "المدير الأعلى يعدَّل من المصفوفة باعتماد مدير أعلى آخر، ويبقى له جوهر الإدارة الذي لا يُسحب: الأدوار واعتماد المصفوفة، والإسناد، والحسابات وكلمة المرور المؤقتة، وعرض السجل الأمني.",
          "The super administrator is edited from the matrix with another super administrator's approval, and keeps an administration core that cannot be removed: roles and matrix approval, assignments, accounts and temporary passwords, and viewing the security log.")} />
      )}
      {role.pendingChangeId && (
        <Alert className="mb" type="warning" showIcon message={L("لهذا الدور طلب تعديل ينتظر الاعتماد؛ التعديل متوقف حتى يُبت فيه.", "This role has a change waiting for approval; editing is paused until it is decided.")} action={<Button size="small" onClick={() => nav("/changes")}>{L("اذهب للطلب", "Open request")}</Button>} />
      )}
      <div className="lcp-filters mb">
        <Select value={module} onChange={setModule} style={{ minWidth: 220 }} options={[{ value: "all", label: L("كل الوحدات", "All modules") }, ...catalog.modules.map((m) => ({ value: m.key, label: m.name }))]} aria-label={L("الوحدة", "Module")} />
        <Input.Search allowClear placeholder={L("ابحث في الخصائص ومعانيها", "Search features and meanings")} aria-label={L("ابحث في الخصائص ومعانيها", "Search features and meanings")} value={search} onChange={(e) => setSearch(e.target.value)} />
        <Select
          allowClear
          placeholder={L("قارن بدور آخر", "Compare with another role")} aria-label={L("قارن بدور آخر", "Compare with another role")}
          value={compareId}
          onChange={setCompareId}
          style={{ minWidth: 220 }}
          options={roles.filter((r) => r.id !== roleId).map((r) => ({ value: r.id, label: `${r.code} · ${r.name}` }))}
        />
      </div>
      <MatrixTable
        catalog={catalog}
        value={role.isLocked ? superAdminGrants(catalog) : draft}
        original={editable ? original : undefined}
        compare={compare.data?.role.isLocked ? superAdminGrants(catalog) : compare.data?.grants}
        compareName={compare.data?.role.code}
        editable={editable}
        module={module}
        search={search}
        onChange={(f, g) => setDraft((d) => ({ ...d, [f]: g }))}
      />
      <ReviewModal
        open={reviewing}
        onClose={() => setReviewing(false)}
        catalog={catalog}
        original={original}
        draft={draft}
        changed={changed}
        onSubmit={async (reason) => {
          await api.put<RbacChange>(`/api/iam/roles/${roleId}/grants`, { baseVersion: role.grantsVersion, reason, grants: clean(draft) });
          message.success(L("أُرسل الطلب. يسري بعد اعتماد مدير أعلى آخر.", "Request sent. It takes effect after another super administrator approves it."));
          setReviewing(false);
          await qc.invalidateQueries({ queryKey: ["role", roleId] });
          await qc.invalidateQueries({ queryKey: ["roles"] });
          await qc.invalidateQueries({ queryKey: ["changes"] });
        }}
      />
      <ReasonModal
        open={deleting}
        title={L(`حذف الدور ${role.code}`, `Delete role ${role.code}`)}
        okText={L("احذف", "Delete")}
        danger
        onClose={() => setDeleting(false)}
        onSubmit={async (reason) => {
          await api.post(`/api/iam/roles/${roleId}/delete`, { reason });
          message.success(L("حُذف الدور.", "Role deleted."));
          setDeleting(false);
          await qc.invalidateQueries({ queryKey: ["roles"] });
          nav("/matrix");
        }}
      />
    </>
  );
}

function superAdminGrants(catalog: Catalog): Grants {
  return Object.fromEntries(catalog.features.map((f) => [f.key, { caps: f.caps.map((c) => c.cap).join(""), scope: "A", specials: f.specials.map((s) => s.key) } as GrantView]));
}

function ReviewModal({ open, onClose, catalog, original, draft, changed, onSubmit }: {
  open: boolean; onClose: () => void; catalog: Catalog; original: Grants; draft: Grants; changed: string[]; onSubmit: (reason: string) => Promise<void>;
}) {
  const [form] = Form.useForm<{ reason: string }>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  return (
    <Modal
      open={open}
      onCancel={onClose}
      title={L("مراجعة طلب التعديل", "Review the change request")}
      okText={L("أرسل للاعتماد", "Send for approval")}
      confirmLoading={busy}
      width={760}
      destroyOnHidden
      onOk={async () => {
        const v = await form.validateFields();
        setBusy(true);
        setError(null);
        try {
          await onSubmit(v.reason);
          form.resetFields();
        } catch (e) {
          setError(errorText(e));
        } finally {
          setBusy(false);
        }
      }}
    >
      {error && <Alert type="error" showIcon message={error} className="mb" />}
      <table className="diff-table">
        <thead><tr><th>{L("الخاصية", "Feature")}</th><th>{L("الحالي", "Current")}</th><th>{L("المقترح", "Proposed")}</th></tr></thead>
        <tbody>
          {changed.map((k) => {
            const f = catalog.features.find((x) => x.key === k);
            return (
              <tr key={k}>
                <td>{f?.name ?? k}</td>
                <td className="before">{describe(catalog, f, original[k])}</td>
                <td className="after">{describe(catalog, f, draft[k])}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <Form form={form} layout="vertical" className="mt">
        <Form.Item name="reason" label={L("سبب التعديل", "Reason for the change")} rules={[{ required: true, min: 5, message: L("سبب واضح من 5 أحرف على الأقل", "A clear reason of at least 5 characters") }]}>
          <Input.TextArea rows={3} maxLength={1000} showCount />
        </Form.Item>
      </Form>
    </Modal>
  );
}

export function ReasonModal({ open, title, okText, danger, onClose, onSubmit }: { open: boolean; title: string; okText: string; danger?: boolean; onClose: () => void; onSubmit: (reason: string) => Promise<void> }) {
  const [form] = Form.useForm<{ reason: string }>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  return (
    <Modal
      open={open}
      title={title}
      okText={okText}
      okButtonProps={{ danger }}
      confirmLoading={busy}
      onCancel={() => { setError(null); onClose(); }}
      destroyOnHidden
      onOk={async () => {
        const v = await form.validateFields();
        setBusy(true);
        setError(null);
        try {
          await onSubmit(v.reason);
          form.resetFields();
        } catch (e) {
          setError(errorText(e));
        } finally {
          setBusy(false);
        }
      }}
    >
      {error && <Alert type="error" showIcon message={error} className="mb" />}
      <Form form={form} layout="vertical">
        <Form.Item name="reason" label={L("السبب", "Reason")} rules={[{ required: true, min: 5, message: L("سبب واضح من 5 أحرف على الأقل", "A clear reason of at least 5 characters") }]}>
          <Input.TextArea rows={3} maxLength={500} showCount />
        </Form.Item>
      </Form>
    </Modal>
  );
}

function CreateRoleModal({ open, onClose, roles, onCreated }: { open: boolean; onClose: () => void; roles: RoleSummary[]; onCreated: (id: string) => void }) {
  const [form] = Form.useForm();
  const qc = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  return (
    <Modal
      open={open}
      title={L("دور جديد", "New role")}
      okText={L("أنشئ", "Create")}
      confirmLoading={busy}
      onCancel={onClose}
      destroyOnHidden
      onOk={async () => {
        const v = await form.validateFields();
        setBusy(true);
        setError(null);
        try {
          const role = await api.post<RoleSummary>("/api/iam/roles", v);
          await qc.invalidateQueries({ queryKey: ["roles"] });
          await qc.invalidateQueries({ queryKey: ["changes"] });
          form.resetFields();
          onClose();
          onCreated(role.id);
        } catch (e) {
          setError(errorText(e));
        } finally {
          setBusy(false);
        }
      }}
    >
      {error && <Alert type="error" showIcon message={error} className="mb" />}
      <Form form={form} layout="vertical">
        <Form.Item name="code" label={L("رمز الدور", "Role code")} extra={L("حروف إنجليزية كبيرة وأرقام، مثل TRL_LEAD", "Capital letters and digits, e.g. TRL_LEAD")} rules={[{ required: true, pattern: /^[A-Za-z][A-Za-z0-9_]{1,15}$/ }]}>
          <Input dir="ltr" />
        </Form.Item>
        <Form.Item name="nameAr" label={L("الاسم بالعربية", "Arabic name")} rules={[{ required: true }]}><Input /></Form.Item>
        <Form.Item name="nameEn" label={L("الاسم بالإنجليزية", "English name")}><Input dir="ltr" /></Form.Item>
        <Form.Item name="descriptionAr" label={L("الوصف", "Description")}><Input.TextArea rows={2} maxLength={500} /></Form.Item>
        <Form.Item name="copyFromRoleId" label={L("انسخ صلاحيات دور قائم", "Copy permissions from a role")} extra={L("الدور يبدأ فارغًا، والصلاحيات المنسوخة طلب تعديل ينتظر اعتماد مدير أعلى آخر. الأدوار المحجوزة لا تنسخ.", "The role starts empty; the copied permissions become a change request waiting for another super administrator. Reserved roles cannot be copied.")}>
          <Select allowClear options={roles.filter((r) => !["SA", "SYS", "AUD", "DIR"].includes(r.code)).map((r) => ({ value: r.id, label: `${r.code} · ${r.name}` }))} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
