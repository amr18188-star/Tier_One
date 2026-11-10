import { useState } from "react";
import { Alert, App, Button, Checkbox, DatePicker, Descriptions, Drawer, Empty, Form, Input, Modal, Radio, Select, Space, Spin, Table, Tabs, Tag, Typography } from "antd";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { Dayjs } from "dayjs";
import { api } from "../../api/client";
import { CAPS, type Assignment, type Catalog, type Effective, type GrantView, type Override, type RoleSummary, type UserDetail } from "../../api/types";
import type { OrgUnit } from "../../api/tier1";
import { errorText, formatDate, formatDateTime } from "../../shared/format";
import { dirArrow, L, useLang } from "../../shared/i18n";
import { usePermissions, useSession } from "../../shared/session";
import { ReasonModal } from "../matrix/MatrixPage";
import { capName, MatrixTable } from "../matrix/MatrixTable";

// أدوار لا يسندها إلا المدير الأعلى (SOD-9)، كما في كتالوج الخادم.
const RESERVED = ["SA", "SYS", "AUD", "PRES", "HC"];

export function UserDrawer({ userId, onClose }: { userId: string; onClose: () => void }) {
  const perms = usePermissions();
  const { me } = useSession();
  const lang = useLang();
  const qc = useQueryClient();
  const { message, modal } = App.useApp();
  const user = useQuery({ queryKey: ["user", userId], queryFn: () => api.get<UserDetail>(`/api/iam/users/${userId}`) });
  const catalog = useQuery({ queryKey: ["catalog"], queryFn: () => api.get<Catalog>("/api/iam/catalog"), staleTime: Infinity });
  const [reasonFor, setReasonFor] = useState<null | { title: string; ok: string; run: (reason: string) => Promise<void> }>(null);
  const [error, setError] = useState<string | null>(null);
  const self = me?.id === userId;

  async function refresh() {
    await qc.invalidateQueries({ queryKey: ["user", userId] });
    await qc.invalidateQueries({ queryKey: ["users"] });
    await qc.invalidateQueries({ queryKey: ["effective", userId] });
    await qc.invalidateQueries({ queryKey: ["roles"] });
  }

  const u = user.data;
  // الخادم يرفض على أي حال؛ الواجهة تخفي ما لا يملكه المستخدم هنا (حسابه، أو حامل دور محجوز لغير المدير الأعلى).
  const locked = self || (!!u?.holdsReservedRole && !perms.isSuperAdmin);
  return (
    <Drawer open onClose={onClose} width={Math.min(1100, window.innerWidth)} placement={lang === "ar" ? "left" : "right"} title={u ? `${u.fullNameAr}` : L("الحساب", "Account")} destroyOnHidden>
      {!u || !catalog.data ? (
        <div className="center-screen"><Spin /></div>
      ) : (
        <>
          {self && <Alert className="mb" type="info" showIcon message={L("هذا حسابك. لا تدير حسابك أو أدوارك بنفسك (SOD-9).", "This is your account. You cannot manage your own account or roles (SOD-9).")} />}
          {!self && u.holdsReservedRole && !perms.isSuperAdmin && (
            <Alert className="mb" type="info" showIcon message={L("هذا الحساب يحمل دورًا محجوزًا، فلا يديره إلا المدير الأعلى (SOD-9).", "This account holds a reserved role, so only a super administrator can manage it (SOD-9).")} />
          )}
          {error && <Alert className="mb" type="error" showIcon message={error} closable onClose={() => setError(null)} />}
          <Descriptions size="small" column={{ xs: 1, md: 2 }} bordered className="mb">
            <Descriptions.Item label={L("البريد", "Email")}><span className="ltr">{u.email}</span></Descriptions.Item>
            <Descriptions.Item label={L("الاسم بالإنجليزية", "English name")}>{u.fullNameEn ?? "—"}</Descriptions.Item>
            <Descriptions.Item label={L("الحالة", "Status")}>
              <Space wrap>
                {u.active ? <Tag color="green">{L("نشط", "Active")}</Tag> : <Tag>{L("موقوف", "Deactivated")}</Tag>}
                {u.lockedUntil && <Tag color="red">{L("مقفل حتى", "Locked until")} {formatDateTime(u.lockedUntil)}</Tag>}
                {u.mfaEnabled ? <Tag>{L("التحقق بخطوتين مفعل", "Two-step on")}</Tag> : <Tag color="orange">{L("بلا تحقق بخطوتين", "No two-step")}</Tag>}
                {u.mustChangePassword && <Tag color="orange">{L("كلمة مؤقتة", "Temporary password")}</Tag>}
              </Space>
            </Descriptions.Item>
            <Descriptions.Item label={L("آخر دخول", "Last sign-in")}>{formatDateTime(u.lastLoginAt)}</Descriptions.Item>
          </Descriptions>
          {!locked && (
            <Space wrap className="mb">
              {perms.can("adm.users", "E") && u.active && (
                <EditNames user={u} onSaved={refresh} />
              )}
              {perms.can("adm.users", "E") && u.lockedUntil && (
                <Button onClick={() => setReasonFor({ title: L("فك قفل الحساب", "Unlock the account"), ok: L("فك القفل", "Unlock"), run: (r) => api.post(`/api/iam/users/${userId}/unlock`, { reason: r }) })}>
                  {L("فك القفل", "Unlock")}
                </Button>
              )}
              {perms.can("adm.users", "E") && u.mfaEnabled && (
                <Button onClick={() => setReasonFor({ title: L("إعادة ضبط التحقق بخطوتين", "Reset two-step verification"), ok: L("أعد الضبط", "Reset"), run: (r) => api.post(`/api/iam/users/${userId}/reset-mfa`, { reason: r }) })}>
                  {L("إعادة ضبط التحقق بخطوتين", "Reset two-step")}
                </Button>
              )}
              {perms.can("adm.users", "set_password") && (
                <Button onClick={() => setReasonFor({
                  title: L("تعيين كلمة مرور مؤقتة", "Set a temporary password"),
                  ok: L("عيّن", "Set"),
                  run: async (r) => {
                    const res = await api.post<{ temporaryPassword: string }>(`/api/iam/users/${userId}/temporary-password`, { reason: r });
                    modal.success({ title: L("كلمة المرور المؤقتة", "Temporary password"), content: <Typography.Paragraph copyable code className="ltr">{res.temporaryPassword}</Typography.Paragraph> });
                  },
                })}>
                  {L("كلمة مرور مؤقتة", "Temporary password")}
                </Button>
              )}
              {perms.can("adm.users", "D") && u.active && (
                <Button danger onClick={() => setReasonFor({ title: L("إيقاف الحساب", "Deactivate the account"), ok: L("أوقف", "Deactivate"), run: (r) => api.post(`/api/iam/users/${userId}/deactivate`, { reason: r }) })}>
                  {L("إيقاف الحساب", "Deactivate")}
                </Button>
              )}
              {perms.can("adm.users", "E") && !u.active && (
                <Button onClick={() => setReasonFor({ title: L("إعادة تفعيل الحساب", "Reactivate the account"), ok: L("أعد التفعيل", "Reactivate"), run: (r) => api.post(`/api/iam/users/${userId}/reactivate`, { reason: r }) })}>
                  {L("إعادة التفعيل", "Reactivate")}
                </Button>
              )}
            </Space>
          )}
          <Tabs
            items={[
              { key: "roles", label: L("الأدوار", "Roles"), children: <Assignments user={u} self={locked} onChanged={refresh} setReasonFor={setReasonFor} onError={setError} /> },
              { key: "overrides", label: L("الاستثناءات الفردية", "Individual overrides"), children: <Overrides user={u} catalog={catalog.data} self={locked} onChanged={refresh} setReasonFor={setReasonFor} onError={setError} /> },
              ...(perms.can("adm.grants", "V") ? [{ key: "effective", label: L("الصلاحيات الفعلية", "Effective permissions"), children: <EffectiveView userId={userId} catalog={catalog.data} /> }] : []),
            ]}
          />
        </>
      )}
      <ReasonModal
        open={!!reasonFor}
        title={reasonFor?.title ?? ""}
        okText={reasonFor?.ok ?? ""}
        onClose={() => setReasonFor(null)}
        onSubmit={async (reason) => {
          await reasonFor!.run(reason);
          setReasonFor(null);
          message.success(L("تم.", "Done."));
          await refresh();
        }}
      />
    </Drawer>
  );
}

function EditNames({ user, onSaved }: { user: UserDetail; onSaved: () => Promise<void> }) {
  const [open, setOpen] = useState(false);
  const [form] = Form.useForm();
  const [error, setError] = useState<string | null>(null);
  return (
    <>
      <Button onClick={() => setOpen(true)}>{L("تعديل الاسم", "Edit name")}</Button>
      <Modal open={open} title={L("تعديل الاسم", "Edit name")} onCancel={() => setOpen(false)} destroyOnHidden onOk={async () => {
        const v = await form.validateFields();
        try {
          await api.put(`/api/iam/users/${user.id}`, v);
          setOpen(false);
          await onSaved();
        } catch (e) {
          setError(errorText(e));
        }
      }}>
        {error && <Alert type="error" showIcon message={error} className="mb" />}
        <Form form={form} layout="vertical" initialValues={{ fullNameAr: user.fullNameAr, fullNameEn: user.fullNameEn }}>
          <Form.Item name="fullNameAr" label={L("الاسم بالعربية", "Arabic name")} rules={[{ required: true }]}><Input /></Form.Item>
          <Form.Item name="fullNameEn" label={L("الاسم بالإنجليزية", "English name")}><Input dir="ltr" /></Form.Item>
        </Form>
      </Modal>
    </>
  );
}

type ReasonSetter = (v: null | { title: string; ok: string; run: (reason: string) => Promise<void> }) => void;

function Assignments({ user, self, onChanged, setReasonFor, onError }: { user: UserDetail; self: boolean; onChanged: () => Promise<void>; setReasonFor: ReasonSetter; onError: (e: string) => void }) {
  const perms = usePermissions();
  const roles = useQuery({ queryKey: ["roles"], queryFn: () => api.get<RoleSummary[]>("/api/iam/roles") });
  // «نطاقه» في Tier 1 جهات: الكلية تغطي أقسامها، فيكفي اختيار الكلية لعميدها.
  const units = useQuery({ queryKey: ["units"], queryFn: () => api.get<OrgUnit[]>("/api/org/units") });
  const unitName = (code: string) => units.data?.find((u) => u.code === code)?.nameAr ?? code;
  const [form] = Form.useForm<{ roleId: string; scopeValues: string[]; validTo?: Dayjs; reason: string }>();
  const [busy, setBusy] = useState(false);
  const canAdd = perms.can("adm.grants", "A") && !self && user.active;
  const canRevoke = perms.can("adm.grants", "D") && !self;
  const available = (roles.data ?? []).filter((r) => perms.isSuperAdmin || !RESERVED.includes(r.code));

  return (
    <>
      <Table<Assignment>
        rowKey="id"
        size="small"
        dataSource={user.assignments}
        pagination={false}
        scroll={{ x: 720 }}
        locale={{ emptyText: L("لا أدوار", "No roles") }}
        rowClassName={(a) => (a.active ? "" : "row-muted")}
        columns={[
          { title: L("الدور", "Role"), render: (_, a) => <Space><Tag color={a.roleCode === "SA" ? "gold" : "geekblue"}>{a.roleCode}</Tag>{a.roleName}</Space> },
          { title: L("نطاقه", "Unit scope"), dataIndex: "scopeValues", render: (v: string[]) => (v.length ? v.map((s) => <Tag key={s} title={s}>{unitName(s)}</Tag>) : "—") },
          { title: L("المدة", "Period"), render: (_, a) => `${formatDate(a.validFrom)} ${dirArrow()} ${a.validTo ? formatDate(a.validTo) : L("مفتوحة", "open")}` },
          { title: L("أسنده", "Granted by"), render: (_, a) => <span title={a.reason ?? ""}>{a.grantedBy?.name ?? "—"}</span> },
          {
            title: L("الحالة", "Status"),
            render: (_, a) =>
              a.revokedAt ? <Tag title={a.revokeReason ?? ""}>{L("مسحوب", "Revoked")}</Tag>
                : a.pendingApproval ? <Tag color="gold">{L("ينتظر اعتماد مدير أعلى مستقل", "Waiting for an independent super administrator")}</Tag>
                : a.active ? <Tag color="green" title={a.approvedBy ? L(`اعتمده ${a.approvedBy.name}`, `Approved by ${a.approvedBy.name}`) : ""}>{L("ساري", "Active")}</Tag>
                : <Tag color="orange">{L("خارج المدة", "Outside period")}</Tag>,
          },
          {
            title: "",
            render: (_, a) => (
              <Space>
                {a.canApprove && (
                  <Button size="small" type="primary" onClick={async () => {
                    try {
                      await api.post(`/api/iam/assignments/${a.id}/approve`);
                      await onChanged();
                    } catch (e) {
                      onError(errorText(e));
                    }
                  }}>
                    {L("اعتمد", "Approve")}
                  </Button>
                )}
                {canRevoke && !a.revokedAt && (perms.isSuperAdmin || !RESERVED.includes(a.roleCode)) && (
                  <Button size="small" danger onClick={() => setReasonFor({ title: L(`سحب الدور ${a.roleCode}`, `Revoke role ${a.roleCode}`), ok: a.pendingApproval ? L("ارفض", "Reject") : L("اسحب", "Revoke"), run: (r) => api.post(`/api/iam/assignments/${a.id}/revoke`, { reason: r }) })}>
                    {a.pendingApproval ? L("ارفض", "Reject") : L("سحب", "Revoke")}
                  </Button>
                )}
              </Space>
            ),
          },
        ]}
      />
      {canAdd && (
        <div className="subform">
          <Typography.Title level={5}>{L("إسناد دور", "Assign a role")}</Typography.Title>
          <Form
            form={form}
            layout="vertical"
            onFinish={async (v) => {
              setBusy(true);
              try {
                await api.post(`/api/iam/users/${user.id}/roles`, { roleId: v.roleId, scopeValues: v.scopeValues ?? [], validTo: v.validTo?.toISOString() ?? null, reason: v.reason });
                form.resetFields();
                await onChanged();
              } catch (e) {
                onError(errorText(e));
              } finally {
                setBusy(false);
              }
            }}
          >
            <div className="form-grid">
              <Form.Item name="roleId" label={L("الدور", "Role")} rules={[{ required: true }]}>
                <Select showSearch optionFilterProp="label" options={available.map((r) => ({ value: r.id, label: `${r.code} · ${r.name}` }))} />
              </Form.Item>
              <Form.Item name="scopeValues" label={L("نطاقه: الجهات", "Unit scope")}
                extra={L("للأدوار المقيدة بجهة (العميد، لجنة الكلية، ممثل القسم). اختيار الكلية يشمل أقسامها.", "For unit-bound roles. A college covers its departments.")}>
                <Select mode="multiple" showSearch optionFilterProp="label" loading={units.isLoading}
                  options={(units.data ?? []).filter((u) => u.kind !== "University").map((u) => ({ value: u.code, label: `${u.nameAr} (${u.code})` }))} />
              </Form.Item>
              <Form.Item name="validTo" label={L("ينتهي في (اختياري)", "Ends on (optional)")}>
                <DatePicker showTime style={{ width: "100%" }} />
              </Form.Item>
            </div>
            <Form.Item name="reason" label={L("السبب", "Reason")} rules={[{ required: true, min: 5 }]}><Input /></Form.Item>
            <Button type="primary" htmlType="submit" loading={busy}>{L("أسند", "Assign")}</Button>
          </Form>
        </div>
      )}
    </>
  );
}

function Overrides({ user, catalog, self, onChanged, setReasonFor, onError }: { user: UserDetail; catalog: Catalog; self: boolean; onChanged: () => Promise<void>; setReasonFor: ReasonSetter; onError: (e: string) => void }) {
  const perms = usePermissions();
  const [form] = Form.useForm<{ feature: string; caps: string[]; specials: string[]; effect: "Allow" | "Deny"; scope: string; validTo?: Dayjs; reason: string }>();
  const feature = Form.useWatch("feature", form);
  const def = catalog.features.find((f) => f.key === feature);
  const [busy, setBusy] = useState(false);
  const name = (k: string) => catalog.features.find((f) => f.key === k)?.name ?? k;
  return (
    <>
      <Typography.Paragraph type="secondary">
        {L("المنع يغلب منح الأدوار دائمًا. المنح الفردي لا يتجاوز ما يملكه المانح نفسه (SOD-9).", "A deny always wins over role grants. An individual grant cannot exceed what the granter has (SOD-9).")}
      </Typography.Paragraph>
      <Table<Override>
        rowKey="id"
        size="small"
        dataSource={user.overrides}
        pagination={false}
        scroll={{ x: 720 }}
        locale={{ emptyText: L("لا استثناءات", "No overrides") }}
        rowClassName={(o) => (o.active ? "" : "row-muted")}
        columns={[
          { title: L("النوع", "Type"), dataIndex: "effect", render: (v) => (v === "Allow" ? <Tag color="green">{L("منح", "Allow")}</Tag> : <Tag color="red">{L("منع", "Deny")}</Tag>) },
          { title: L("الخاصية", "Feature"), dataIndex: "feature", render: (v) => name(v) },
          { title: L("الإمكانيات", "Capabilities"), render: (_, o) => <code>{o.caps || "—"}</code> },
          { title: L("السبب", "Reason"), dataIndex: "reason" },
          { title: L("ينتهي", "Ends"), dataIndex: "validTo", render: (v) => (v ? formatDate(v) : L("مفتوح", "Open")) },
          {
            title: "",
            render: (_, o) =>
              perms.can("adm.grants", "D") && !self && !o.revokedAt ? (
                <Button size="small" danger onClick={() => setReasonFor({ title: L("سحب الاستثناء", "Revoke the override"), ok: L("اسحب", "Revoke"), run: (r) => api.post(`/api/iam/overrides/${o.id}/revoke`, { reason: r }) })}>{L("سحب", "Revoke")}</Button>
              ) : o.revokedAt ? <Tag>{L("مسحوب", "Revoked")}</Tag> : null,
          },
        ]}
      />
      {perms.can("adm.grants", "A") && !self && (
        <div className="subform">
          <Typography.Title level={5}>{L("استثناء جديد", "New override")}</Typography.Title>
          <Form
            form={form}
            layout="vertical"
            initialValues={{ effect: "Allow", scope: "A", caps: [], specials: [] }}
            onFinish={async (v) => {
              setBusy(true);
              try {
                await api.post(`/api/iam/users/${user.id}/overrides`, {
                  feature: v.feature, caps: CAPS.filter((c) => v.caps?.includes(c)).join(""), specials: v.specials ?? [], effect: v.effect, scope: v.scope, validTo: v.validTo?.toISOString() ?? null, reason: v.reason,
                });
                form.resetFields();
                await onChanged();
              } catch (e) {
                onError(errorText(e));
              } finally {
                setBusy(false);
              }
            }}
          >
            <div className="form-grid">
              <Form.Item name="effect" label={L("النوع", "Type")}>
                <Radio.Group optionType="button" options={[{ value: "Allow", label: L("منح", "Allow") }, { value: "Deny", label: L("منع", "Deny") }]} />
              </Form.Item>
              <Form.Item name="feature" label={L("الخاصية", "Feature")} rules={[{ required: true }]}>
                <Select showSearch optionFilterProp="label" options={catalog.modules.map((m) => ({ label: m.name, options: catalog.features.filter((f) => f.module === m.key).map((f) => ({ value: f.key, label: f.name })) }))} />
              </Form.Item>
              <Form.Item name="scope" label={L("النطاق", "Scope")} extra={L("«نطاقه» يأتي من إسناد الدور، لا من الاستثناء الفردي.", "The unit scope comes from role assignments, not individual overrides.")}>
                <Select options={catalog.scopes.filter((s) => s.key !== "U").map((s) => ({ value: s.key, label: s.name }))} />
              </Form.Item>
              <Form.Item name="validTo" label={L("ينتهي في (اختياري)", "Ends on (optional)")}>
                <DatePicker showTime style={{ width: "100%" }} />
              </Form.Item>
            </div>
            {def && (
              <>
                <Form.Item name="caps" label={L("الإمكانيات", "Capabilities")}>
                  <Checkbox.Group options={def.caps.map((c) => ({ value: c.cap, label: `${capName(c.cap)} — ${c.meaning}` }))} className="vertical-checks" />
                </Form.Item>
                {def.specials.length > 0 && (
                  <Form.Item name="specials" label={L("إجراءات خاصة", "Special actions")}>
                    <Checkbox.Group options={def.specials.map((s) => ({ value: s.key, label: s.name }))} />
                  </Form.Item>
                )}
              </>
            )}
            <Form.Item name="reason" label={L("السبب", "Reason")} rules={[{ required: true, min: 5 }]}><Input /></Form.Item>
            <Button type="primary" htmlType="submit" loading={busy}>{L("أضف", "Add")}</Button>
          </Form>
        </div>
      )}
    </>
  );
}

function EffectiveView({ userId, catalog }: { userId: string; catalog: Catalog }) {
  const eff = useQuery({ queryKey: ["effective", userId], queryFn: () => api.get<Effective>(`/api/iam/users/${userId}/effective`) });
  const [module, setModule] = useState("all");
  if (!eff.data) return <Spin />;
  // عرض الصلاحيات الفعلية بالشبكة نفسها؛ النطاق المعروض أوسع نطاق بين إمكانيات الخاصية.
  const order = ["O", "S", "U", "A"];
  const grants: Record<string, GrantView> = Object.fromEntries(
    Object.entries(eff.data.features).map(([k, f]) => {
      const scopes = Object.values(f.caps);
      const widest = scopes.sort((a, b) => order.indexOf(b!) - order.indexOf(a!))[0] ?? "A";
      return [k, { caps: CAPS.filter((c) => f.caps[c]).join(""), scope: widest!, specials: f.specials } as GrantView];
    }),
  );
  if (Object.keys(grants).length === 0) return <Empty description={L("لا صلاحيات فعلية.", "No effective permissions.")} />;
  return (
    <>
      <Space className="mb" wrap>
        {eff.data.isSuperAdmin && <Tag color="gold">{L("مدير أعلى: كل شيء", "Super administrator: everything")}</Tag>}
        {eff.data.roles.map((r) => <Tag key={r}>{r}</Tag>)}
        <Select value={module} onChange={setModule} style={{ minWidth: 220 }} options={[{ value: "all", label: L("كل الوحدات", "All modules") }, ...catalog.modules.map((m) => ({ value: m.key, label: m.name }))]} />
      </Space>
      <MatrixTable catalog={catalog} value={grants} editable={false} module={module} search="" />
    </>
  );
}
