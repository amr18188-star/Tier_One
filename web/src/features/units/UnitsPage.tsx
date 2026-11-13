import { useMemo, useState } from "react";
import { Alert, App, Button, DatePicker, Form, Input, InputNumber, Modal, Select, Switch, Table, Tag } from "antd";
import { EditOutlined, OrderedListOutlined, PlusOutlined, SearchOutlined, StopOutlined } from "@ant-design/icons";
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
import UnitIndicatorsDrawer from "./UnitIndicatorsDrawer";
import "./units.css";

type Row = OrgUnit & { children?: Row[] };

/** ما يقع تحت كل نوع، كما يفرضه الخادم. */
const CHILD_KINDS: Partial<Record<OrgUnitKind, OrgUnitKind[]>> = {
  University: ["College", "Institute", "Sector", "Administration", "Center"],
  Sector: ["Administration", "Center"],
  Administration: ["Administration", "Center"],
  College: ["Department", "Center", "Administration"],
  Institute: ["Department", "Center", "Administration"],
};

const CENTRAL: OrgUnitKind[] = ["Sector", "Administration", "Center"];

/** ترتيب الأنواع في دليل الألوان، من الجذر إلى الفروع. ألوانها في units.css (un-k-*). */
const LEGEND: OrgUnitKind[] = ["University", "Sector", "Administration", "Center", "College", "Institute", "Department"];

/**
 * الجهات في شجرة: الجامعة ثم القطاعات وإداراتها والكليات والمعاهد ثم الأقسام. الإضافة من صف الجهة الأم مباشرة.
 * لكل جهة «مؤشراتها»: ما تدخله في نموذجها. تغيير الاسم بتاريخ سريان، والإغلاق أو الدمج بدل الحذف.
 */
export default function UnitsPage() {
  const perms = usePermissions();
  const [showClosed, setShowClosed] = useState(false);
  const [q, setQ] = useState("");
  const [add, setAdd] = useState<OrgUnit | null>(null);
  const [edit, setEdit] = useState<OrgUnit | null>(null);
  const [close, setClose] = useState<OrgUnit | null>(null);
  const [tasks, setTasks] = useState<OrgUnit | null>(null);
  const units = useQuery({ queryKey: ["units", showClosed], queryFn: () => api.get<OrgUnit[]>(`/api/org/units?includeInactive=${showClosed}`) });
  const byId = useMemo(() => new Map((units.data ?? []).map((u) => [u.id, u])), [units.data]);

  // البحث يبقي الجهة المطابقة وأسلافها، فتظهر في مكانها من الشجرة.
  const { tree, expanded } = useMemo(() => {
    const term = q.trim().toUpperCase();
    const all = units.data ?? [];
    let keep: Set<string> | null = null;
    if (term) {
      keep = new Set<string>();
      for (const u of all) {
        if (!u.nameAr.toUpperCase().includes(term) && !u.code.includes(term)) continue;
        for (let x: OrgUnit | undefined = u; x && !keep.has(x.id); x = x.parentId ? byId.get(x.parentId) : undefined) keep.add(x.id);
      }
    }
    // مستخدم الجهة يستلم جهته وما تحتها فقط: جذر شجرته جهته، لا الجامعة.
    const ids = new Set(all.map((u) => u.id));
    const byParent = new Map<string | null, OrgUnit[]>();
    for (const u of all) {
      if (keep && !keep.has(u.id)) continue;
      const parent = u.parentId && ids.has(u.parentId) ? u.parentId : null;
      byParent.set(parent, [...(byParent.get(parent) ?? []), u]);
    }
    const build = (parent: string | null): Row[] =>
      (byParent.get(parent) ?? []).map((u) => {
        const children = build(u.id);
        return children.length ? { ...u, children } : { ...u };
      });
    const rows = build(null);
    return { tree: rows, expanded: keep ? [...keep] : rows.map((t) => t.id) };
  }, [units.data, q, byId]);
  const list = units.data ?? [];
  const colleges = list.filter((u) => u.kind === "College" || u.kind === "Institute").length;
  const root = list.find((u) => u.kind === "University");
  const wide = list.filter((u) => u.viewsWholeUniversity);

  return (
    <>
      <PageHeader title={L("الجهات", "Units")}
        subtitle={L(`الجامعة بقطاعاتها وإداراتها و${colleges} كلية ومعهدًا بأقسامها. لكل جهة مؤشراتها التي تدخلها، ومن يعمل فيها لا يرى غير جهته.`,
          `The university, its sectors and ${colleges} colleges with departments. Each unit enters its own indicators and its staff see only their unit.`)}
        stats={units.data ? [
          { label: L("كليات ومعاهد", "Colleges"), value: colleges },
          { label: L("أقسام علمية", "Departments"), value: list.filter((u) => u.kind === "Department").length },
          { label: L("قطاعات وإدارات ومراكز", "Central units"), value: list.filter((u) => CENTRAL.includes(u.kind)).length },
          { label: L("تطلع على الجامعة كاملة", "See the whole university"), value: wide.length },
        ] : undefined}
        extra={<>
          <label className="lcp-switch"><Switch size="small" checked={showClosed} onChange={setShowClosed} /> {L("إظهار المغلقة", "Show closed")}</label>
          {perms.can("org.units", "A") && root && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setAdd(root)}>{L("جهة جديدة", "New unit")}</Button>
          )}
        </>} />
      <div className="un-tools">
        <Input allowClear prefix={<SearchOutlined />} placeholder={L("ابحث باسم الجهة أو رمزها", "Search by name or code")} value={q} onChange={(e) => setQ(e.target.value)} />
        <span className="un-legend" aria-label={L("ألوان الأنواع", "Kind colours")}>
          {LEGEND.filter((k) => list.some((u) => u.kind === k)).map((k) => <span key={k} className={`un-chip un-k-${k}`}><i />{unitKind[k]}</span>)}
        </span>
      </div>
      <Panel query={units}>
        {() => (
          <Table<Row> key={q.trim() ? "search" : "tree"} rowKey="id" dataSource={tree} pagination={false} size="middle"
            className="un-tree" rowClassName={(u) => `un-k-${u.kind}`}
            expandable={{ defaultExpandedRowKeys: expanded }}
            scroll={{ x: 980 }}
            columns={[
              {
                title: L("الجهة", "Unit"), dataIndex: "nameAr", width: 340,
                render: (n: string, u) => <>{n}{!u.active && <Tag className="ms">{L("مغلقة", "Closed")} {formatDate(u.closedOn)}</Tag>}</>,
              },
              { title: L("النوع", "Kind"), dataIndex: "kind", width: 100, render: (k: OrgUnitKind) => <span className={`un-chip un-k-${k}`}><i />{unitKind[k]}</span> },
              { title: L("الرمز", "Code"), dataIndex: "code", width: 100, render: (c: string) => <code>{c}</code> },
              {
                title: L("المؤشرات", "Indicators"), key: "ind", width: 150,
                render: (_, u) => u.kind === "University" ? <span className="muted un-ind">{L("ما لم يكلف به غيرها", "Whatever is unassigned")}</span>
                  : <span className="un-ind">
                    <b>{u.indicatorCount ?? 0}</b> {u.customIndicators || CENTRAL.includes(u.kind) ? L("مكلفة بها", "assigned")
                      : u.kind === "Department" && u.parentId && byId.get(u.parentId)?.customIndicators ? L("بقائمة كليتها", "from its college") : L("كل مستواها", "all of its level")}
                  </span>,
              },
              {
                title: "", key: "actions", width: 360,
                render: (_, u) => u.active && (
                  <span className="t1-sticky-actions">
                    {u.kind !== "University" && (
                      <Button size="small" icon={<OrderedListOutlined />} onClick={() => setTasks(u)}>{L("المؤشرات", "Indicators")}</Button>
                    )}
                    {perms.can("org.units", "A") && CHILD_KINDS[u.kind] && (
                      <Button size="small" icon={<PlusOutlined />} onClick={() => setAdd(u)} aria-label={L("إضافة تحتها", "Add under")}>
                        {u.kind === "University" ? L("إضافة جهة", "Add unit") : null}
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
      {close && <CloseUnit unit={close} all={list} onClose={() => setClose(null)} />}
      {tasks && <UnitIndicatorsDrawer unit={tasks} onClose={() => setTasks(null)} />}
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
        <Form.Item name="kind" label={L("النوع", "Kind")}
          extra={parent.kind === "University"
            ? L("الكلية والمعهد تتبعهما أقسام؛ والقطاع تتبعه إدارات عامة ومراكز؛ والإدارة والمركز جهتان مركزيتان.", "Colleges have departments; sectors have administrations; administrations and centres are central units.")
            : parent.kind === "College" || parent.kind === "Institute"
              ? L("القسم العلمي يتبع الكلية؛ ويمكن إضافة وحدة أو مركز تابع للكلية.", "Departments belong to the college.")
              : L("إدارة أو مركز يتبع هذه الجهة.", "An administration or centre under this unit.")}>
          <Select options={kinds.map((k) => ({ value: k, label: unitKind[k] }))} />
        </Form.Item>
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

type EditValues = { nameAr: string; nameEn?: string; sortOrder: number; effectiveOn?: Dayjs; reason?: string; viewsWholeUniversity: boolean };

function EditUnit({ unit, onClose }: { unit: OrgUnit; onClose: () => void }) {
  const perms = usePermissions();
  const [form] = Form.useForm<EditValues>();
  const refresh = useRefresh();
  const nameAr = Form.useWatch("nameAr", form);
  const renamed = nameAr !== undefined && nameAr.trim() !== unit.nameAr;
  // توسيع اطلاع جهة إلى الجامعة كاملة قرار صلاحيات: لمن يدير الصلاحيات فقط.
  const canWiden = perms.isSuperAdmin || perms.can("adm.grants", "E");
  const save = useMutation({
    mutationFn: (v: EditValues) =>
      api.put(`/api/org/units/${unit.id}`, {
        ...v, effectiveOn: v.effectiveOn?.format("YYYY-MM-DD") ?? null, version: unit.version,
        viewsWholeUniversity: canWiden ? v.viewsWholeUniversity : null,
      }),
    onSuccess: async () => { await refresh(); onClose(); },
  });
  return (
    <Modal open title={`${L("تعديل", "Edit")} ${unit.nameAr}`} okText={L("حفظ", "Save")} cancelText={L("إلغاء", "Cancel")}
      onCancel={onClose} onOk={() => form.submit()} confirmLoading={save.isPending} destroyOnHidden>
      {save.error && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" initialValues={{ nameAr: unit.nameAr, nameEn: unit.nameEn ?? undefined, sortOrder: unit.sortOrder, effectiveOn: dayjs(), viewsWholeUniversity: unit.viewsWholeUniversity }} onFinish={(v) => save.mutate(v)}>
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
        {unit.kind !== "University" && unit.kind !== "Department" && (
          <Form.Item name="viewsWholeUniversity" valuePropName="checked" label={L("الاطلاع", "Visibility")}
            extra={canWiden
              ? L("من يعمل في هذه الجهة يرى الجامعة كاملة (عرضًا وطباعة) لدوره في Tier 1، ويبقى إدخاله وتعديله في نماذج جهته وحدها. يسري خلال دقيقة.",
                "Staff of this unit see the whole university; they still edit only their own unit.")
              : L("يغيره من يدير الصلاحيات فقط.", "Only those who manage grants can change this.")}>
            <Switch disabled={!canWiden} checkedChildren={L("الجامعة كاملة", "Whole university")} unCheckedChildren={L("جهته فقط", "Own unit")} />
          </Form.Item>
        )}
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
