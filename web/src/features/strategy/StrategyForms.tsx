import { useEffect } from "react";
import { Alert, Button, Drawer, Form, Input, InputNumber, Select, Space } from "antd";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { InitiativeNode, InitiativeStatus, OrgUnit, PillarNode, StandardNode, StrategyTree } from "../../api/tier1";
import { errorText } from "../../shared/format";
import { L, useLang } from "../../shared/i18n";
import { initiativeStatus, options } from "../../shared/labels";
import { stdLabel } from "../../shared/pillars";

/** بعد أي تعديل في الخطة: الشجرة والقاموس وقوائم المعايير كلها من جديد. */
export function useRefreshPlan() {
  const qc = useQueryClient();
  return async () => {
    await qc.invalidateQueries({ queryKey: ["strategy"] });
    await qc.invalidateQueries({ queryKey: ["indicators"] });
    await qc.invalidateQueries({ queryKey: ["standards"] });
  };
}

/** درج تحرير مشترك: العنوان، وزرا الحفظ والإلغاء أعلاه، ورسالة الخطأ من الخادم بلغة المستخدم. */
function EditDrawer({ title, saving, error, onSave, onClose, children }: {
  title: string; saving: boolean; error: unknown; onSave: () => void; onClose: () => void; children: React.ReactNode;
}) {
  const lang = useLang();
  return (
    <Drawer open width={620} placement={lang === "ar" ? "left" : "right"} onClose={onClose} title={title} destroyOnHidden
      extra={<Space><Button onClick={onClose}>{L("إلغاء", "Cancel")}</Button><Button type="primary" loading={saving} onClick={onSave}>{L("حفظ", "Save")}</Button></Space>}>
      {!!error && <Alert type="error" showIcon message={errorText(error)} className="mb" />}
      {children}
    </Drawer>
  );
}

interface PillarFields { nameAr: string; nameEn?: string; description?: string; sortOrder?: number }

/** إضافة ركيزة أو تعديلها: الاسم وشرحها؛ الرمز يولد تلقائيًا ولا يتغير. */
export function PillarDrawer({ pillar, onClose }: { pillar?: PillarNode; onClose: () => void }) {
  const [form] = Form.useForm<PillarFields>();
  const refresh = useRefreshPlan();
  useEffect(() => form.setFieldsValue(pillar ? { ...pillar, nameEn: pillar.nameEn ?? undefined, description: pillar.description ?? undefined } : {}), [pillar, form]);
  const save = useMutation({
    mutationFn: (f: PillarFields) => pillar
      ? api.put(`/api/strategy/pillars/${pillar.id}`, { ...f, version: pillar.version })
      : api.post("/api/strategy/pillars", f),
    onSuccess: async () => { await refresh(); onClose(); },
  });
  return (
    <EditDrawer title={pillar ? L(`تعديل الركيزة ${pillar.code}`, `Edit pillar ${pillar.code}`) : L("ركيزة جديدة", "New pillar")}
      saving={save.isPending} error={save.error} onSave={() => form.submit()} onClose={onClose}>
      <Form form={form} layout="vertical" onFinish={(f) => save.mutate(f)} requiredMark="optional">
        <Form.Item name="nameAr" label={L("اسم الركيزة", "Pillar name")} rules={[{ required: true, message: L("اكتب اسم الركيزة", "Enter the name") }]}>
          <Input maxLength={200} autoFocus />
        </Form.Item>
        <Form.Item name="nameEn" label={L("الاسم بالإنجليزية", "English name")}><Input maxLength={200} dir="ltr" /></Form.Item>
        <Form.Item name="description" label={L("شرح الركيزة", "Description")}
          extra={L("ما تهدف إليه الركيزة ولماذا هي في الخطة. يظهر لكل من يفتح الشجرة.", "What the pillar aims at; shown to everyone who opens the tree.")}>
          <Input.TextArea rows={6} maxLength={4000} showCount />
        </Form.Item>
        <Form.Item name="sortOrder" label={L("الترتيب", "Order")}><InputNumber min={1} max={99} /></Form.Item>
      </Form>
    </EditDrawer>
  );
}

interface StandardFields { pillarId?: string | null; nameAr: string; nameEn?: string; description?: string; sortOrder?: number }

/** إضافة معيار أو تعديله: ركيزته (ينقل إلى غيرها بتغييرها) واسمه وشرحه. */
export function StandardDrawer({ standard, pillars, pillarId, onClose }: { standard?: StandardNode; pillars: PillarNode[]; pillarId?: string; onClose: () => void }) {
  const [form] = Form.useForm<StandardFields>();
  const refresh = useRefreshPlan();
  useEffect(() => form.setFieldsValue(standard
    ? { ...standard, nameEn: standard.nameEn ?? undefined, description: standard.description ?? undefined }
    : { pillarId }), [standard, pillarId, form]);
  const save = useMutation({
    mutationFn: (f: StandardFields) => {
      const body = { ...f, pillarId: f.pillarId ?? null };
      return standard ? api.put(`/api/strategy/standards/${standard.id}`, { ...body, version: standard.version }) : api.post("/api/strategy/standards", body);
    },
    onSuccess: async () => { await refresh(); onClose(); },
  });
  return (
    <EditDrawer title={standard ? L(`تعديل المعيار ${standard.code}`, `Edit standard ${standard.code}`) : L("معيار جديد", "New standard")}
      saving={save.isPending} error={save.error} onSave={() => form.submit()} onClose={onClose}>
      <Form form={form} layout="vertical" onFinish={(f) => save.mutate(f)} requiredMark="optional">
        <Form.Item name="pillarId" label={L("الركيزة", "Pillar")} extra={L("تغييرها ينقل المعيار بمؤشراته ومبادراته إلى الركيزة الأخرى.", "Changing it moves the standard with its indicators and initiatives.")}>
          <Select allowClear options={pillars.map((p) => ({ value: p.id, label: `${p.code} — ${p.nameAr}` }))} />
        </Form.Item>
        <Form.Item name="nameAr" label={L("اسم المعيار", "Standard name")} rules={[{ required: true, message: L("اكتب اسم المعيار", "Enter the name") }]}>
          <Input maxLength={200} autoFocus />
        </Form.Item>
        <Form.Item name="nameEn" label={L("الاسم بالإنجليزية", "English name")}><Input maxLength={200} dir="ltr" /></Form.Item>
        <Form.Item name="description" label={L("شرح المعيار", "Description")} extra={L("ما يقيسه المعيار وكيف يخدم ركيزته.", "What the standard measures and how it serves its pillar.")}>
          <Input.TextArea rows={6} maxLength={4000} showCount />
        </Form.Item>
        <Form.Item name="sortOrder" label={L("الترتيب", "Order")}><InputNumber min={1} max={999} /></Form.Item>
      </Form>
    </EditDrawer>
  );
}

interface InitiativeFields {
  standardId: string; nameAr: string; description?: string; expectedOutcome?: string; ownerUnitId?: string | null;
  startYear: number; endYear: number; status: InitiativeStatus; indicatorCodes?: string[];
}

/** إضافة مبادرة أو تعديلها: معيارها، وشرحها ومخرجاتها، والجهة المسؤولة، ومدتها، والمؤشرات التي تسهم فيها. */
export function InitiativeDrawer({ initiative, standardId, tree, onClose }: { initiative?: InitiativeNode; standardId: string; tree: StrategyTree; onClose: () => void }) {
  const [form] = Form.useForm<InitiativeFields>();
  const refresh = useRefreshPlan();
  const units = useQuery({ queryKey: ["units"], queryFn: () => api.get<OrgUnit[]>("/api/org/units") });
  const standards = [...tree.pillars.flatMap((p) => p.standards), ...tree.unassigned];
  const chosen = Form.useWatch("standardId", form) ?? standardId;
  const first = tree.years[0];
  const last = tree.years[tree.years.length - 1];
  useEffect(() => form.setFieldsValue(initiative
    ? { ...initiative, description: initiative.description ?? undefined, expectedOutcome: initiative.expectedOutcome ?? undefined }
    : { standardId, startYear: first, endYear: last, status: "Planned", indicatorCodes: [] }), [initiative, standardId, first, last, form]);
  const save = useMutation({
    mutationFn: (f: InitiativeFields) => {
      const body = { ...f, ownerUnitId: f.ownerUnitId ?? null, indicatorCodes: f.indicatorCodes ?? [] };
      return initiative ? api.put(`/api/strategy/initiatives/${initiative.id}`, { ...body, version: initiative.version }) : api.post("/api/strategy/initiatives", body);
    },
    onSuccess: async () => { await refresh(); onClose(); },
  });
  // مؤشرات معيار المبادرة أولًا، ثم بقية المؤشرات لمن تسهم مبادرته في معيار آخر.
  const indicatorOptions = [...standards].sort((a, b) => Number(b.id === chosen) - Number(a.id === chosen))
    .map((s) => ({ label: `${stdLabel(s.code)} — ${s.nameAr}`, options: s.indicators.map((i) => ({ value: i.code, label: `${i.code} ${i.nameAr}` })) }))
    .filter((g) => g.options.length > 0);
  const years = tree.years.map((y) => ({ value: y, label: String(y) }));
  return (
    <EditDrawer title={initiative ? L(`تعديل المبادرة ${initiative.code}`, `Edit initiative ${initiative.code}`) : L("مبادرة جديدة", "New initiative")}
      saving={save.isPending} error={save.error} onSave={() => form.submit()} onClose={onClose}>
      <Form form={form} layout="vertical" onFinish={(f) => save.mutate(f)} requiredMark="optional">
        <Form.Item name="standardId" label={L("المعيار", "Standard")} rules={[{ required: true }]}>
          <Select showSearch optionFilterProp="label" options={standards.map((s) => ({ value: s.id, label: `${stdLabel(s.code)} — ${s.nameAr}` }))} />
        </Form.Item>
        <Form.Item name="nameAr" label={L("اسم المبادرة", "Initiative name")} rules={[{ required: true, message: L("اكتب اسم المبادرة", "Enter the name") }]}>
          <Input maxLength={300} autoFocus />
        </Form.Item>
        <Form.Item name="description" label={L("شرح المبادرة", "Description")} extra={L("ما الذي ستنفذه المبادرة وأنشطتها الرئيسة.", "What the initiative will do.")}>
          <Input.TextArea rows={5} maxLength={4000} showCount />
        </Form.Item>
        <Form.Item name="expectedOutcome" label={L("المخرجات أو الأثر المتوقع", "Expected outcome")}>
          <Input.TextArea rows={3} maxLength={2000} />
        </Form.Item>
        <div className="lcp-grid-2">
          <Form.Item name="startYear" label={L("سنة البدء", "Start year")} rules={[{ required: true }]}><Select options={years} /></Form.Item>
          <Form.Item name="endYear" label={L("سنة الانتهاء", "End year")} rules={[{ required: true }]}><Select options={years} /></Form.Item>
          <Form.Item name="status" label={L("الحالة", "Status")}><Select options={options(initiativeStatus)} /></Form.Item>
          <Form.Item name="ownerUnitId" label={L("الجهة المسؤولة", "Responsible unit")}>
            <Select showSearch allowClear optionFilterProp="label" loading={units.isLoading}
              options={(units.data ?? []).filter((u) => u.active).map((u) => ({ value: u.id, label: u.nameAr }))} />
          </Form.Item>
        </div>
        <Form.Item name="indicatorCodes" label={L("المؤشرات التي تسهم فيها", "Indicators it contributes to")}
          extra={L("تظهر المبادرة مع كل مؤشر تختاره هنا في شجرة الخطة.", "The initiative shows with each chosen indicator in the plan tree.")}>
          <Select mode="multiple" showSearch optionFilterProp="label" options={indicatorOptions} />
        </Form.Item>
      </Form>
    </EditDrawer>
  );
}
