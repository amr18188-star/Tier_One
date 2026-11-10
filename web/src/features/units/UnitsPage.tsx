import { useMemo, useState } from "react";
import { Alert, App, Button, DatePicker, Form, Input, InputNumber, Modal, Select, Switch, Table, Tag } from "antd";
import { EditOutlined, PlusOutlined, StopOutlined } from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import dayjs, { type Dayjs } from "dayjs";
import { Link } from "react-router-dom";
import { api } from "../../api/client";
import type { OrgUnit, OrgUnitKind } from "../../api/tier1";
import { errorText, formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { unitKind } from "../../shared/labels";
import { usePermissions } from "../../shared/session";
import { PageHeader, Panel } from "../../shared/ui";

type Row = OrgUnit & { children?: Row[] };

/** ما يقع تحت كل نوع، كما يفرضه الخادم. */
const CHILD_KINDS: Partial<Record<OrgUnitKind, OrgUnitKind[]>> = {
  University: ["College", "Institute", "Center", "Administration"],
  College: ["Department", "Center", "Administration"],
  Institute: ["Department", "Center", "Administration"],
};

/**
 * الجهات في شجرة: الجامعة ثم الكليات والمعاهد والمراكز ثم الأقسام. الإضافة من صف الجهة الأم مباشرة، فلا يختار المستخدم
 * الأم يدويًا. تغيير الاسم بتاريخ سريان، والإغلاق أو الدمج بدل الحذف.
 */
export default function UnitsPage() {
  const perms = usePermissions();
  const [showClosed, setShowClosed] = useState(false);
  const [add, setAdd] = useState<OrgUnit | null>(null);
  const [edit, setEdit] = useState<OrgUnit | null>(null);
  const [close, setClose] = useState<OrgUnit | null>(null);
  const units = useQuery({ queryKey: ["units", showClosed], queryFn: () => api.get<OrgUnit[]>(`/api/org/units?includeInactive=${showClosed}`) });

  const tree = useMemo(() => {
    const byParent = new Map<string | null, OrgUnit[]>();
    for (const u of units.data ?? []) byParent.set(u.parentId, [...(byParent.get(u.parentId) ?? []), u]);
    const build = (parent: string | null): Row[] =>
      (byParent.get(parent) ?? []).map((u) => {
        const children = build(u.id);
        return children.length ? { ...u, children } : { ...u };
      });
    return build(null);
  }, [units.data]);
  const colleges = (units.data ?? []).filter((u) => u.kind === "College" || u.kind === "Institute").length;

  return (
    <>
      <PageHeader title={L("الجهات", "Units")}
        subtitle={L(`الجامعة و${colleges} كلية ومعهدًا بأقسامها. الرمز ثابت لا يتغير لأنه يستخدم في الصلاحيات وقوالب Excel.`,
          `The university and ${colleges} colleges with departments. Codes never change: they are used in permissions and Excel templates.`)}
        extra={<label className="lcp-switch"><Switch size="small" checked={showClosed} onChange={setShowClosed} /> {L("إظهار المغلقة", "Show closed")}</label>} />
      <Panel query={units}>
        {() => (
          <Table<Row> rowKey="id" dataSource={tree} pagination={false} size="middle" expandable={{ defaultExpandedRowKeys: tree.map((t) => t.id) }}
            scroll={{ x: 820 }}
            columns={[
              {
                title: L("الجهة", "Unit"), dataIndex: "nameAr", width: 300,
                render: (n: string, u) => <>{n}{!u.active && <Tag className="ms">{L("مغلقة", "Closed")} {formatDate(u.closedOn)}</Tag>}</>,
              },
              { title: L("النوع", "Kind"), dataIndex: "kind", width: 120, render: (k: OrgUnitKind) => unitKind[k] },
              { title: L("الرمز", "Code"), dataIndex: "code", width: 130, render: (c: string) => <code>{c}</code> },
              {
                title: "", key: "actions", width: 330,
                render: (_, u) => u.active && (
                  <span className="t1-sticky-actions">
                    {perms.can("org.units", "A") && CHILD_KINDS[u.kind] && (
                      <Button size="small" icon={<PlusOutlined />} onClick={() => setAdd(u)}>
                        {u.kind === "University" ? L("كلية أو معهد", "College") : L("قسم", "Department")}
                      </Button>
                    )}
                    {perms.can("org.units", "E") && <Button size="small" icon={<EditOutlined />} onClick={() => setEdit(u)} aria-label={L("تعديل", "Edit")} />}
                    {perms.can("org.units", "D") && u.kind !== "University" && <Button size="small" danger icon={<StopOutlined />} onClick={() => setClose(u)} aria-label={L("إغلاق", "Close")} />}
                    {(u.kind === "College" || u.kind === "Institute") && perms.can("org.structure", "V") && <Link to={`/structure/${u.id}`}>{L("الهيكل", "Structure")}</Link>}
                  </span>
                ),
              },
            ]} />
        )}
      </Panel>
      {add && <AddUnit parent={add} onClose={() => setAdd(null)} />}
      {edit && <EditUnit unit={edit} onClose={() => setEdit(null)} />}
      {close && <CloseUnit unit={close} all={units.data ?? []} onClose={() => setClose(null)} />}
    </>
  );
}

function useRefresh() {
  const qc = useQueryClient();
  return async () => {
    await qc.invalidateQueries({ queryKey: ["units"] });
    await qc.invalidateQueries({ queryKey: ["structure"] });
  };
}

function AddUnit({ parent, onClose }: { parent: OrgUnit; onClose: () => void }) {
  const [form] = Form.useForm<{ kind: OrgUnitKind; code: string; nameAr: string; nameEn?: string; sortOrder?: number; more?: boolean }>();
  const refresh = useRefresh();
  const { message } = App.useApp();
  const kinds = CHILD_KINDS[parent.kind] ?? [];
  const save = useMutation({
    mutationFn: (v: { kind: OrgUnitKind; code: string; nameAr: string; nameEn?: string; sortOrder?: number }) =>
      api.post<OrgUnit>("/api/org/units", { ...v, parentId: parent.id, sortOrder: v.sortOrder ?? 0, establishedOn: null }),
    onSuccess: async (u, v) => {
      message.success(L(`أضيفت «${u.nameAr}».`, `Added ${u.nameAr}.`));
      await refresh();
      // «إضافة أخرى» يبقي النافذة مفتوحة بالنوع نفسه لإدخال قائمة كليات أو أقسام متتابعة بسرعة.
      if (form.getFieldValue("more")) form.setFieldsValue({ code: "", nameAr: "", nameEn: undefined, kind: v.kind });
      else onClose();
    },
  });
  return (
    <Modal open title={`${L("إضافة تحت", "Add under")} ${parent.nameAr}`} okText={L("إضافة", "Add")} cancelText={L("إغلاق", "Close")}
      onCancel={onClose} onOk={() => form.submit()} confirmLoading={save.isPending} destroyOnHidden>
      {save.error && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" initialValues={{ kind: kinds[0], more: true }} onFinish={(v) => save.mutate(v)}>
        <Form.Item name="kind" label={L("النوع", "Kind")}><Select options={kinds.map((k) => ({ value: k, label: unitKind[k] }))} /></Form.Item>
        <Form.Item name="nameAr" label={L("الاسم", "Name")} rules={[{ required: true, message: L("اكتب الاسم", "Enter the name") }]}><Input maxLength={200} autoFocus /></Form.Item>
        <div className="lcp-grid-2">
          <Form.Item name="code" label={L("الرمز", "Code")} rules={[{ required: true, pattern: /^[A-Za-z0-9][A-Za-z0-9-]{0,31}$/, message: L("حروف إنجليزية وأرقام وشرطة", "Letters, digits and dashes") }]}
            extra={parent.kind === "University" ? L("مثل ENG", "E.g. ENG") : L(`مثل ${parent.code}-CIV`, `E.g. ${parent.code}-CIV`)}>
            <Input dir="ltr" maxLength={32} style={{ textTransform: "uppercase" }} />
          </Form.Item>
          <Form.Item name="sortOrder" label={L("الترتيب", "Order")}><InputNumber min={0} style={{ width: "100%" }} /></Form.Item>
        </div>
        <Form.Item name="nameEn" label={L("الاسم بالإنجليزية", "English name")}><Input dir="ltr" maxLength={200} /></Form.Item>
        <Form.Item name="more" valuePropName="checked"><Switch size="small" /> <span className="t1-help">{L("إضافة أخرى بعد الحفظ", "Add another after saving")}</span></Form.Item>
      </Form>
    </Modal>
  );
}

function EditUnit({ unit, onClose }: { unit: OrgUnit; onClose: () => void }) {
  const [form] = Form.useForm<{ nameAr: string; nameEn?: string; sortOrder: number; effectiveOn?: Dayjs; reason?: string }>();
  const refresh = useRefresh();
  const nameAr = Form.useWatch("nameAr", form);
  const renamed = nameAr !== undefined && nameAr.trim() !== unit.nameAr;
  const save = useMutation({
    mutationFn: (v: { nameAr: string; nameEn?: string; sortOrder: number; effectiveOn?: Dayjs; reason?: string }) =>
      api.put(`/api/org/units/${unit.id}`, { ...v, effectiveOn: v.effectiveOn?.format("YYYY-MM-DD") ?? null, version: unit.version }),
    onSuccess: async () => { await refresh(); onClose(); },
  });
  return (
    <Modal open title={`${L("تعديل", "Edit")} ${unit.nameAr}`} okText={L("حفظ", "Save")} cancelText={L("إلغاء", "Cancel")}
      onCancel={onClose} onOk={() => form.submit()} confirmLoading={save.isPending} destroyOnHidden>
      {save.error && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" initialValues={{ nameAr: unit.nameAr, nameEn: unit.nameEn ?? undefined, sortOrder: unit.sortOrder, effectiveOn: dayjs() }} onFinish={(v) => save.mutate(v)}>
        <Form.Item name="nameAr" label={L("الاسم", "Name")} rules={[{ required: true }]}><Input maxLength={200} /></Form.Item>
        {renamed && (
          <>
            <Alert type="info" showIcon className="mb" message={L("تغيير الاسم يسجل في تاريخ الجهة؛ بياناتها السابقة تبقى مرتبطة بها.", "The rename is recorded in the unit's history.")} />
            <div className="lcp-grid-2">
              <Form.Item name="effectiveOn" label={L("تاريخ السريان", "Effective date")}><DatePicker style={{ width: "100%" }} /></Form.Item>
              <Form.Item name="reason" label={L("السبب أو رقم القرار", "Reason or decision")} rules={[{ required: true, min: 5 }]}><Input maxLength={500} /></Form.Item>
            </div>
          </>
        )}
        <Form.Item name="nameEn" label={L("الاسم بالإنجليزية", "English name")}><Input dir="ltr" maxLength={200} /></Form.Item>
        <Form.Item name="sortOrder" label={L("الترتيب", "Order")}><InputNumber min={0} /></Form.Item>
      </Form>
    </Modal>
  );
}

function CloseUnit({ unit, all, onClose }: { unit: OrgUnit; all: OrgUnit[]; onClose: () => void }) {
  const [form] = Form.useForm<{ closedOn: Dayjs; reason: string; mergedIntoId?: string }>();
  const refresh = useRefresh();
  const save = useMutation({
    mutationFn: (v: { closedOn: Dayjs; reason: string; mergedIntoId?: string }) =>
      api.post(`/api/org/units/${unit.id}/close`, { closedOn: v.closedOn.format("YYYY-MM-DD"), reason: v.reason, mergedIntoId: v.mergedIntoId ?? null }),
    onSuccess: async () => { await refresh(); onClose(); },
  });
  return (
    <Modal open title={`${L("إغلاق", "Close")} ${unit.nameAr}`} okText={L("إغلاق الجهة", "Close unit")} okButtonProps={{ danger: true }} cancelText={L("إلغاء", "Cancel")}
      onCancel={onClose} onOk={() => form.submit()} confirmLoading={save.isPending} destroyOnHidden>
      <Alert type="warning" showIcon className="mb" message={L("لا تحذف الجهة: تبقى بسجلاتها وتختفي من النماذج الجديدة. أغلق أقسامها أولًا.", "The unit is kept with its records and hidden from new forms. Close its children first.")} />
      {save.error && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" initialValues={{ closedOn: dayjs() }} onFinish={(v) => save.mutate(v)}>
        <Form.Item name="closedOn" label={L("تاريخ الإغلاق", "Closing date")} rules={[{ required: true }]}><DatePicker style={{ width: "100%" }} /></Form.Item>
        <Form.Item name="mergedIntoId" label={L("دمجت في (اختياري)", "Merged into (optional)")}>
          <Select allowClear showSearch optionFilterProp="label"
            options={all.filter((u) => u.active && u.id !== unit.id && u.kind === unit.kind).map((u) => ({ value: u.id, label: u.nameAr }))} />
        </Form.Item>
        <Form.Item name="reason" label={L("السبب أو رقم القرار", "Reason or decision")} rules={[{ required: true, min: 5 }]}><Input.TextArea rows={2} maxLength={500} /></Form.Item>
      </Form>
    </Modal>
  );
}
