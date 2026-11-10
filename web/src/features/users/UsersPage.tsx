import { useState } from "react";
import { Alert, App, Button, Form, Input, Modal, Space, Tag, Typography } from "antd";
import { LockOutlined, PlusOutlined, SafetyOutlined } from "@ant-design/icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { UserListItem } from "../../api/types";
import { errorText, formatDateTime } from "../../shared/format";
import { L } from "../../shared/i18n";
import { QueryState } from "../../shared/QueryState";
import { usePermissions } from "../../shared/session";
import { DataTable, EmptyNote, PageHeader } from "../../shared/ui";
import { UserDrawer } from "./UserDrawer";

export default function UsersPage() {
  const perms = usePermissions();
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const users = useQuery({ queryKey: ["users", search], queryFn: () => api.get<UserListItem[]>(`/api/iam/users/?q=${encodeURIComponent(search)}`) });

  return (
    <>
      <PageHeader title={L("الحسابات والأدوار", "Accounts and roles")}
        subtitle={L("لا أحد يدير حسابه أو أدواره بنفسه، والأدوار المحجوزة يسندها المدير الأعلى وحده.", "Nobody manages their own account or roles, and reserved roles are assigned by a super administrator only.")}
        extra={perms.can("adm.users", "A") && <Button type="primary" size="large" icon={<PlusOutlined aria-hidden />} onClick={() => setCreating(true)}>{L("حساب موظف جديد", "New staff account")}</Button>} />
      <div className="lcp-filters">
        <Input.Search allowClear placeholder={L("ابحث بالاسم أو البريد", "Search by name or email")} onSearch={setSearch} />
      </div>
      <QueryState query={users}>
        <DataTable<UserListItem>
          rowKey={(u) => u.id}
          loading={users.isLoading}
          data={users.data ?? []}
          resetKey={search}
          minWidth={880}
          onRowClick={(u) => setSelected(u.id)}
          empty={<EmptyNote>{L("لا حسابات بهذا البحث.", "No accounts match this search.")}</EmptyNote>}
          renderCard={(u) => (
            <>
              <div className="dt-card-row"><b>{u.name}</b><UserStatus user={u} /></div>
              <span className="muted ltr">{u.email}</span>
              <div className="dt-card-row"><UserRoles roles={u.roles} /><span className="muted nowrap">{formatDateTime(u.lastLoginAt)}</span></div>
            </>
          )}
          columns={[
            {
              title: L("الاسم", "Name"), dataIndex: "name",
              render: (v, u) => (
                <Space direction="vertical" size={0}>
                  <Button type="link" className="cell-link" onClick={(e) => { e.stopPropagation(); setSelected(u.id); }}><b>{v}</b></Button>
                  <span className="muted ltr">{u.email}</span>
                </Space>
              ),
            },
            { title: L("الأدوار", "Roles"), dataIndex: "roles", render: (roles: string[]) => <UserRoles roles={roles} /> },
            { title: L("الحالة", "Status"), render: (_, u) => <UserStatus user={u} /> },
            { title: L("آخر دخول", "Last sign-in"), dataIndex: "lastLoginAt", render: (v) => <span className="nowrap">{formatDateTime(v)}</span> },
          ]}
        />
      </QueryState>
      {selected && <UserDrawer userId={selected} onClose={() => setSelected(null)} />}
      <CreateUserModal open={creating} onClose={() => setCreating(false)} />
    </>
  );
}

function UserRoles({ roles }: { roles: string[] }) {
  return roles.length ? <span>{roles.map((r) => <Tag key={r} color={r === "SA" ? "gold" : "geekblue"}>{r}</Tag>)}</span> : <span className="muted">{L("بلا دور", "No role")}</span>;
}

function UserStatus({ user: u }: { user: UserListItem }) {
  return (
    <Space wrap>
      {u.active ? <Tag color="green">{L("نشط", "Active")}</Tag> : <Tag>{L("موقوف", "Deactivated")}</Tag>}
      {u.locked && <Tag color="red" icon={<LockOutlined />}>{L("مقفل", "Locked")}</Tag>}
      {u.mfaEnabled && <Tag icon={<SafetyOutlined />}>{L("خطوتان", "2-step")}</Tag>}
    </Space>
  );
}

function CreateUserModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [form] = Form.useForm();
  const qc = useQueryClient();
  const { modal } = App.useApp();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  return (
    <Modal
      open={open}
      title={L("حساب موظف جديد", "New staff account")}
      okText={L("أنشئ", "Create")}
      confirmLoading={busy}
      onCancel={onClose}
      destroyOnHidden
      onOk={async () => {
        const v = await form.validateFields();
        setBusy(true);
        setError(null);
        try {
          const r = await api.post<{ userId: string; temporaryPassword: string }>("/api/iam/users/", v);
          await qc.invalidateQueries({ queryKey: ["users"] });
          form.resetFields();
          onClose();
          modal.success({
            title: L("أنشئ الحساب", "Account created"),
            content: (
              <div>
                <p>{L("كلمة المرور المؤقتة تظهر الآن فقط. سلّمها للموظف، وسيُطلب منه تغييرها عند أول دخول.", "The temporary password is shown only now. Hand it to the staff member; they must change it at first sign-in.")}</p>
                <Typography.Paragraph copyable code className="ltr">{r.temporaryPassword}</Typography.Paragraph>
                <p className="muted">{L("الحساب بلا دور حتى يُسند له دور من تبويب الأدوار.", "The account has no role until one is assigned in the Roles tab.")}</p>
              </div>
            ),
          });
        } catch (e) {
          setError(errorText(e));
        } finally {
          setBusy(false);
        }
      }}
    >
      {error && <Alert type="error" showIcon message={error} className="mb" />}
      <Form form={form} layout="vertical">
        <Form.Item name="email" label={L("البريد الجامعي", "University email")} rules={[{ required: true, type: "email" }]}><Input dir="ltr" /></Form.Item>
        <Form.Item name="fullNameAr" label={L("الاسم بالعربية", "Arabic name")} rules={[{ required: true }]}><Input /></Form.Item>
        <Form.Item name="fullNameEn" label={L("الاسم بالإنجليزية", "English name")}><Input dir="ltr" /></Form.Item>
      </Form>
    </Modal>
  );
}
