import { ApartmentOutlined, ClusterOutlined, SafetyCertificateOutlined, TeamOutlined } from "@ant-design/icons";
import { Link } from "react-router-dom";
import { useState } from "react";
import { Alert, Button, Form, Input, InputNumber, Modal, Switch, Tag, Tooltip } from "antd";
import { EditOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../api/client";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import { QueryState } from "../../shared/QueryState";
import { usePermissions } from "../../shared/session";
import { DataTable, EmptyNote, PageHeader } from "../../shared/ui";
import { queryKeys } from "../../api/queries";
import { DomainsPanel } from "../security/DomainsPanel";
import { IpAllowlistPanel } from "../security/IpAllowlistPanel";

/** إعداد نظام كما يعيده GET /api/admin/settings (أوله seat_hold_hours: مهلة المقعد المؤقت، D12). */
export interface SystemSetting {
  key: string;
  value: number | string;
  defaultValue: number | string;
  ar: string;
  en: string;
  unit: string | null;
  min: number | null;
  max: number | null;
  /** لا يغيره إلا المدير الأعلى (D11). */ superAdminOnly?: boolean;
  /** تشغيل (1) أو إيقاف (0). */ isSwitch?: boolean;
  /** يستطيع المستخدم الحالي تعديله. */ editable?: boolean;
}

function unitLabel(unit: string | null): string {
  switch (unit) {
    case null: case "": return "";
    case "hours": return L("ساعة", "hours");
    case "days": return L("يوم", "days");
    case "minutes": return L("دقيقة", "minutes");
    case "percent": return "%";
    default: return unit;
  }
}

const numeric = (s: SystemSetting) => typeof s.value === "number" || s.min != null || s.max != null;

/** إعدادات النظام (adm.settings): العرض بـV والتعديل بـE، وكل تعديل بسبب يحفظ في السجل. */
export default function SettingsPage() {
  const perms = usePermissions();
  const canView = perms.can("adm.settings", "V");
  // ADR-0016: sign-in security has its own permission (adm.security), apart from the operational settings.
  const canSecurity = perms.can("adm.security", "V");
  const list = useQuery({ queryKey: queryKeys.adminSettings, queryFn: () => api.get<SystemSetting[]>("/api/admin/settings"), enabled: canView });
  const [editing, setEditing] = useState<SystemSetting | null>(null);
  const value = (s: SystemSetting, v: number | string) => s.isSwitch
    ? <Tag color={Number(v) === 1 ? "green" : "default"}>{Number(v) === 1 ? L("مشغل", "On") : L("موقف", "Off")}</Tag>
    : <span className="nowrap"><b className="ltr">{v}</b> {unitLabel(s.unit)}</span>;
  const editable = (s: SystemSetting) => s.editable ?? (perms.can("adm.settings", "E") && !s.superAdminOnly);
  const edit = (s: SystemSetting) => editable(s)
    ? <Button size="small" icon={<EditOutlined aria-hidden />} onClick={(e) => { e.stopPropagation(); setEditing(s); }}>{s.isSwitch ? L("تغيير", "Change") : L("تعديل", "Edit")}</Button>
    : s.superAdminOnly && perms.can("adm.settings", "E")
      ? <Tooltip title={L("يغيره المدير الأعلى وحده.", "Only a super administrator can change it.")}><Tag>{L("للمدير الأعلى", "Super admin")}</Tag></Tooltip>
      : null;
  return (
    <>
      <PageHeader title={L("إعدادات النظام", "System settings")}
        subtitle={L("قيم تضبط سلوك المنظومة. كل تعديل يُسجل بسببه.", "Values that tune how the system behaves. Every change is recorded with its reason.")} />
      {/* ما يبحث عنه المدير عادة في «الإعدادات» وله صفحاته الخاصة. */}
      <nav className="set-links" aria-label={L("إدارة المنظومة", "Administration")}>
        {[
          { to: "/units", icon: <ApartmentOutlined />, ar: "الجهات", en: "Units", hint: "إضافة الكليات والمعاهد والأقسام والإدارات والمراكز وتعديلها وإغلاقها", need: perms.can("org.units", "V") },
          { to: "/users", icon: <TeamOutlined />, ar: "الحسابات والأدوار", en: "Accounts and roles", hint: "إنشاء حسابات الموظفين وإسناد الأدوار ونطاق كل دور", need: perms.can("adm.users", "V") || perms.can("adm.grants", "V") },
          { to: "/matrix", icon: <SafetyCertificateOutlined />, ar: "مصفوفة الصلاحيات", en: "Permission matrix", hint: "ما يستطيعه كل دور في كل شاشة", need: perms.can("adm.roles", "V") },
          { to: "/structure", icon: <ClusterOutlined />, ar: "هياكل Tier 1 بالكليات", en: "College structures", hint: "أعضاء فرق Tier 1 في كل كلية", need: perms.can("org.structure", "V") },
        ].filter((x) => x.need).map((x) => (
          <Link key={x.to} to={x.to}>
            <span className="set-links-icon" aria-hidden>{x.icon}</span>
            <span><b>{L(x.ar, x.en)}</b><small>{L(x.hint, "")}</small></span>
          </Link>
        ))}
      </nav>
      {!canView ? (!canSecurity && <EmptyNote>{L("لا صلاحية لعرض الإعدادات.", "You are not allowed to view the settings.")}</EmptyNote>) : (
        <QueryState query={list}>
          <DataTable<SystemSetting> rowKey={(s) => s.key} loading={list.isLoading} data={list.data ?? []} minWidth={720}
            empty={<EmptyNote>{L("لا إعدادات.", "No settings.")}</EmptyNote>}
            renderCard={(s) => (
              <>
                <div className="dt-card-row"><b>{L(s.ar, s.en)}</b>{edit(s)}</div>
                <div className="dt-card-row">{value(s, s.value)}<span className="muted">{L("الافتراضي", "Default")} {value(s, s.defaultValue)}</span></div>
              </>
            )}
            columns={[
              { title: L("الإعداد", "Setting"), render: (_, s) => <div className="dt-service"><b>{L(s.ar, s.en)}</b></div> },
              { title: L("القيمة", "Value"), render: (_, s) => <>{value(s, s.value)}{String(s.value) !== String(s.defaultValue) && <Tag color="gold" className="ms">{L("معدّل", "Changed")}</Tag>}</> },
              { title: L("الافتراضي", "Default"), render: (_, s) => <span className="muted">{value(s, s.defaultValue)}</span> },
              { title: L("المدى", "Range"), render: (_, s) => s.isSwitch ? <span className="muted">{L("تشغيل أو إيقاف", "On or off")}</span> : s.min != null || s.max != null ? <span className="muted ltr">{s.min ?? "…"} – {s.max ?? "…"}</span> : "—" },
              { title: "", key: "e", render: (_, s) => edit(s) },
            ]} />
        </QueryState>
      )}
      {canSecurity && (
        <>
          <section className="mb" aria-labelledby="set-domains">
            <h2 id="set-domains" className="t1-form-sec">{L("إعدادات المصادقة: نطاقات البريد الجامعي المعتمدة", "Authentication settings: allowed university domains")}</h2>
            <DomainsPanel />
          </section>
          <section className="mb" aria-labelledby="set-ip">
            <h2 id="set-ip" className="t1-form-sec">{L("الأمان: قيود عناوين IP العامة", "Security: global IP restrictions")}</h2>
            <IpAllowlistPanel scope={{ kind: "global" }} />
          </section>
        </>
      )}
      {editing && <EditSetting setting={editing} onClose={() => setEditing(null)} onDone={async () => { setEditing(null); await list.refetch(); }} />}
    </>
  );
}

function EditSetting({ setting: s, onClose, onDone }: { setting: SystemSetting; onClose: () => void; onDone: () => Promise<void> }) {
  const [form] = Form.useForm<{ value: number | string; reason: string }>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  return (
    <Modal open title={L(s.ar, s.en)} okText={L("حفظ", "Save")} confirmLoading={busy} onCancel={onClose} destroyOnHidden
      onOk={async () => {
        const v = await form.validateFields();
        setBusy(true);
        setError(null);
        try {
          await api.put(`/api/admin/settings/${encodeURIComponent(s.key)}`, { value: v.value, reason: v.reason.trim() });
          await onDone();
        } catch (e) {
          setError(errorText(e));
        } finally {
          setBusy(false);
        }
      }}>
      {error && <Alert type="error" showIcon message={error} className="mb" />}
      {s.isSwitch && s.superAdminOnly && (
        <Alert type="info" showIcon className="mb"
          message={L("عند التشغيل يطلب من كل من يعتمد أو يقرر أو يدير الحسابات والصلاحيات تسجيل تطبيق المصادقة عند دخوله التالي، وتتوقف جلساته الحالية غير المتحقق منها عن الإجراءات حتى يدخل من جديد. أدوار الأمان الثابتة يلزمها التحقق دائمًا.",
            "When on, everyone who approves, decides or manages access must enrol an authenticator at next sign-in; their current unverified sessions stop working until they sign in again. Core security roles always need it.")} />
      )}
      <Form form={form} layout="vertical" initialValues={{ value: s.value }}>
        {s.isSwitch ? (
          <Form.Item name="value" label={L("الحالة", "State")} valuePropName="checked"
            getValueFromEvent={(on: boolean) => (on ? 1 : 0)} getValueProps={(v) => ({ checked: Number(v) === 1 })}>
            <Switch checkedChildren={L("مشغل", "On")} unCheckedChildren={L("موقف", "Off")} />
          </Form.Item>
        ) : (
          <Form.Item name="value" label={L(`القيمة (الافتراضي ${s.defaultValue})`, `Value (default ${s.defaultValue})`)} rules={[{ required: true }]}>
            {numeric(s)
              ? <InputNumber min={s.min ?? undefined} max={s.max ?? undefined} style={{ width: "100%" }} addonAfter={unitLabel(s.unit) || undefined} />
              : <Input maxLength={500} />}
          </Form.Item>
        )}
        <Form.Item name="reason" label={L("سبب التعديل", "Reason for the change")} rules={[{ required: true, whitespace: true, min: 5 }]}><Input.TextArea rows={2} maxLength={500} /></Form.Item>
      </Form>
    </Modal>
  );
}
