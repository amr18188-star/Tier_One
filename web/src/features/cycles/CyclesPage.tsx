import { useState } from "react";
import { Alert, App, Button, DatePicker, Drawer, Form, Input, InputNumber, Modal, Space, Switch, Tag } from "antd";
import { CalendarOutlined, EditOutlined, LockOutlined, PlayCircleOutlined, PlusOutlined, UnorderedListOutlined } from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import dayjs, { type Dayjs } from "dayjs";
import { Link } from "react-router-dom";
import { api } from "../../api/client";
import type { Cycle, OpenPreview, SaveCycle, SubmissionStatus } from "../../api/collection";
import { SegmentBar } from "../../shared/charts";
import { DecisionModal } from "../../shared/DecisionModal";
import { errorText, formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { cycleStatus, submissionStatus } from "../../shared/labels";
import { usePermissions } from "../../shared/session";
import { EmptyNote, PageHeader, Panel } from "../../shared/ui";

const STATUS_COLORS: Record<SubmissionStatus, string> = {
  NotStarted: "#c4ccd8", InProgress: "#5aa9c9", InReview: "#d9a227", Returned: "#b42318", Approved: "#2e9e63",
};

/**
 * دورات جمع البيانات: كل دورة تفتح نماذج للجهات بالمؤشرات المعتمدة (أو الحالية في الدورة التجريبية)،
 * وتتابع هنا نسبة الإنجاز بحالة كل نموذج، ثم تغلق بسبب مسجل.
 */
export default function CyclesPage() {
  const perms = usePermissions();
  const canAdd = perms.can("dc.cycles", "A");
  const canEdit = perms.can("dc.cycles", "E");
  const canClose = perms.can("dc.cycles", "D");
  const cycles = useQuery({ queryKey: ["cycles"], queryFn: () => api.get<Cycle[]>("/api/cycles") });
  const [editing, setEditing] = useState<Cycle | "new" | null>(null);
  const [opening, setOpening] = useState<Cycle | null>(null);
  const [closing, setClosing] = useState<Cycle | null>(null);
  const qc = useQueryClient();

  return (
    <>
      <PageHeader title={L("دورات جمع البيانات", "Collection cycles")}
        subtitle={L("افتح دورة لفترة محددة فتصل النماذج للجهات، وتابع الإدخال والاعتماد حتى الإغلاق وإصدار تقرير الجامعة.",
          "Open a cycle for a period, follow entry and approval, then close it and issue the university report.")}
        extra={canAdd && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing("new")}>{L("دورة جديدة", "New cycle")}</Button>} />

      <Panel query={cycles}>
        {() => cycles.data!.length === 0 ? (
          <EmptyNote action={canAdd && <Button type="primary" onClick={() => setEditing("new")}>{L("أنشئ أول دورة", "Create the first cycle")}</Button>}>
            {L("لا توجد دورات بعد. ابدأ ببروفة داخلية يدخل فيها فريق المتابعة جهة واحدة كاملة، ثم الدورة التجريبية للجامعة كلها بالمؤشرات المعتمدة.", "No cycles yet. Start with an internal rehearsal.")}
          </EmptyNote>
        ) : (
          <div className="cy-list">
            {cycles.data!.map((c) => (
              <article key={c.id} className="cy-card">
                <h3>
                  <CalendarOutlined aria-hidden /> {c.name}
                  <Tag color={cycleStatus[c.status].color}>{cycleStatus[c.status].text}</Tag>
                  {c.trial && <Tag color="purple">{L("بروفة داخلية", "Rehearsal")}</Tag>}
                  {c.overdue > 0 && <Tag color="error">{L(`${c.overdue} نموذج متأخر`, `${c.overdue} overdue`)}</Tag>}
                </h3>
                <div className="cy-facts">
                  <span>{L("الفترة:", "Period:")} <b>{c.periodLabel}</b></span>
                  <span>{L("المستهدف المقارن:", "Target year:")} <b className="ltr">{c.targetYear}</b></span>
                  <span>{L("من", "From")} <b className="ltr">{formatDate(c.opensOn)}</b> {L("حتى", "to")} <b className="ltr">{formatDate(c.dueOn)}</b></span>
                  {c.status !== "Draft" && <span>{L("المؤشرات:", "Indicators:")} <b>{c.indicators}</b> · {L("النماذج:", "Forms:")} <b>{c.forms}</b></span>}
                </div>
                {c.forms > 0 && (
                  <SegmentBar label={L("حالة النماذج", "Form status")}
                    segments={(Object.keys(STATUS_COLORS) as SubmissionStatus[]).map((s) => ({ key: s, label: submissionStatus[s].text, count: c.formsByStatus[s] ?? 0, color: STATUS_COLORS[s] }))} />
                )}
                <div className="cy-actions">
                  {c.status !== "Draft" && <Link to={`/forms?cycle=${c.id}&view=all`}><Button icon={<UnorderedListOutlined />}>{L("النماذج", "Forms")}</Button></Link>}
                  {c.status !== "Draft" && <Link to={`/results?cycle=${c.id}`}><Button>{L("النتائج", "Results")}</Button></Link>}
                  {canEdit && c.status !== "Closed" && <Button icon={<EditOutlined />} onClick={() => setEditing(c)}>{L("تعديل", "Edit")}</Button>}
                  {canEdit && c.status === "Draft" && <Button type="primary" icon={<PlayCircleOutlined />} onClick={() => setOpening(c)}>{L("فتح الدورة", "Open the cycle")}</Button>}
                  {canClose && c.status === "Open" && <Button danger icon={<LockOutlined />} onClick={() => setClosing(c)}>{L("إغلاق الدورة", "Close the cycle")}</Button>}
                </div>
              </article>
            ))}
          </div>
        )}
      </Panel>

      {editing && <CycleDrawer cycle={editing === "new" ? null : editing} onClose={() => setEditing(null)} />}
      {opening && <OpenModal cycle={opening} onClose={() => setOpening(null)} />}
      {closing && (
        <DecisionModal title={L(`إغلاق ${closing.name}`, `Close ${closing.name}`)} danger field="note" label={L("سبب الإغلاق", "Reason")} okText={L("إغلاق", "Close")}
          explain={L(`بعد الإغلاق لا يدخل ولا يعتمد شيء. النماذج غير المعتمدة (${closing.forms - (closing.formsByStatus.Approved ?? 0)}) تبقى كما هي، والنتائج من المعتمد فقط.`,
            "After closing nothing can be entered or approved; results use approved forms only.")}
          onSubmit={async (note) => {
            await api.post(`/api/cycles/${closing.id}/close`, { note });
            await qc.invalidateQueries({ queryKey: ["cycles"] });
          }}
          onClose={() => setClosing(null)} />
      )}
    </>
  );
}

interface CycleFields { name: string; periodLabel: string; targetYear: number; dates: [Dayjs, Dayjs]; trial: boolean }

function CycleDrawer({ cycle, onClose }: { cycle: Cycle | null; onClose: () => void }) {
  const [form] = Form.useForm<CycleFields>();
  const qc = useQueryClient();
  const { message } = App.useApp();
  const year = new Date().getFullYear();
  const save = useMutation({
    mutationFn: (v: CycleFields) => {
      const body: SaveCycle = {
        name: v.name, periodLabel: v.periodLabel, targetYear: v.targetYear, trial: v.trial,
        opensOn: v.dates[0].format("YYYY-MM-DD"), dueOn: v.dates[1].format("YYYY-MM-DD"), version: cycle?.version ?? null,
      };
      return cycle ? api.put<Cycle>(`/api/cycles/${cycle.id}`, body) : api.post<Cycle>("/api/cycles", body);
    },
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["cycles"] });
      message.success(L("حفظت الدورة", "Cycle saved"));
      onClose();
    },
  });
  const open = cycle?.status === "Open";
  return (
    <Drawer open width={480} title={cycle ? L("تعديل الدورة", "Edit cycle") : L("دورة جديدة", "New cycle")} onClose={onClose} destroyOnHidden
      extra={<Space><Button onClick={onClose}>{L("إلغاء", "Cancel")}</Button><Button type="primary" loading={save.isPending} onClick={() => form.submit()}>{L("حفظ", "Save")}</Button></Space>}>
      {save.error && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      {open && <Alert type="info" showIcon className="mb" message={L("الدورة مفتوحة: يمكن تمديد الموعد النهائي فيتحدث لكل نموذج لم يغير موعده.", "The cycle is open: extending the deadline updates the forms.")} />}
      <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)} requiredMark="optional"
        initialValues={cycle
          ? { name: cycle.name, periodLabel: cycle.periodLabel, targetYear: cycle.targetYear, trial: cycle.trial, dates: [dayjs(cycle.opensOn), dayjs(cycle.dueOn)] }
          : { name: L(`دورة العام الجامعي ${year - 1}/${year}`, `Academic year ${year - 1}/${year}`), periodLabel: `${year - 1}-${year}`, targetYear: Math.max(2027, year), trial: false, dates: [dayjs(), dayjs().add(30, "day")] }}>
        <Form.Item name="name" label={L("اسم الدورة", "Name")} rules={[{ required: true, message: L("اكتب اسم الدورة", "Enter the name") }]}>
          <Input maxLength={150} />
        </Form.Item>
        <Form.Item name="periodLabel" label={L("الفترة المقيسة", "Period measured")} extra={L("تظهر في التقارير والمقارنات، مثل 2025-2026.", "Shown in reports, e.g. 2025-2026.")}
          rules={[{ required: true, message: L("اكتب الفترة", "Enter the period") }]}>
          <Input maxLength={40} className="ltr" />
        </Form.Item>
        <Form.Item name="targetYear" label={L("سنة المستهدف للمقارنة", "Target year to compare")} rules={[{ required: true }]}>
          <InputNumber min={2026} max={2031} disabled={open} />
        </Form.Item>
        <Form.Item name="dates" label={L("الفتح والموعد النهائي", "Opens and due")} rules={[{ required: true, message: L("حدد التاريخين", "Pick both dates") }]}>
          <DatePicker.RangePicker style={{ width: "100%" }} />
        </Form.Item>
        <Form.Item name="trial" valuePropName="checked" label={L("بروفة داخلية", "Internal rehearsal")}
          extra={L("البروفة تشمل المؤشرات غير المعتمدة بعد بتعريفها الحالي لاختبار النماذج ومسار الاعتماد قبل فتح الدورة للجهات، ولا تقارن نتائجها بالدورات الفعلية. الدورة الفعلية (ومنها التجريبية الأولى للجامعة) بالمؤشرات المعتمدة فقط.",
            "Includes unapproved indicators to test forms before the real cycle; results are not compared with real cycles.")}>
          <Switch disabled={open} />
        </Form.Item>
      </Form>
    </Drawer>
  );
}

function OpenModal({ cycle, onClose }: { cycle: Cycle; onClose: () => void }) {
  const qc = useQueryClient();
  const { message } = App.useApp();
  const preview = useQuery({ queryKey: ["cycles", cycle.id, "preview"], queryFn: () => api.get<OpenPreview>(`/api/cycles/${cycle.id}/preview`) });
  const open = useMutation({
    mutationFn: () => api.post<Cycle>(`/api/cycles/${cycle.id}/open`),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["cycles"] });
      message.success(L("فتحت الدورة ووصلت النماذج للجهات", "The cycle is open"));
      onClose();
    },
  });
  const p = preview.data;
  return (
    <Modal open title={L(`فتح ${cycle.name}`, `Open ${cycle.name}`)} okText={L("فتح الدورة", "Open")} cancelText={L("إلغاء", "Cancel")} onCancel={onClose}
      onOk={() => open.mutate()} confirmLoading={open.isPending} okButtonProps={{ disabled: !p || p.indicators === 0 }}>
      {open.error && <Alert type="error" showIcon message={errorText(open.error)} className="mb" />}
      {preview.isLoading && <p>{L("جار حساب ما ستشمله الدورة…", "Calculating…")}</p>}
      {preview.error && <Alert type="error" showIcon message={errorText(preview.error)} />}
      {p && (
        <>
          <p>{L(`ستفتح ${p.forms} نموذجًا تقيس ${p.indicators} مؤشرًا:`, `${p.forms} forms measuring ${p.indicators} indicators:`)}</p>
          <ul>
            {p.colleges > 0 && <li>{L(`${p.colleges} كلية ومعهد`, `${p.colleges} colleges`)}</li>}
            {p.departments > 0 && <li>{L(`${p.departments} قسمًا علميًا`, `${p.departments} departments`)}</li>}
            {p.central > 0 && <li>{L(`${p.central} جهة مركزية لمؤشرات الجامعة`, `${p.central} central units`)}</li>}
          </ul>
          {p.skipped.length > 0 && (
            <Alert type="warning" showIcon message={L(`${p.skipped.length} مؤشرًا لن يدخل الدورة`, `${p.skipped.length} indicators are skipped`)}
              description={<ul className="mt">{p.skipped.slice(0, 8).map((s) => <li key={s}>{s}</li>)}{p.skipped.length > 8 && <li>…</li>}</ul>} />
          )}
          <p className="t1-help mt">{L("تعريفات المؤشرات تثبت في الدورة لحظة فتحها؛ تعديلها لاحقًا لا يغير ما قيس بها.", "Definitions are frozen when the cycle opens.")}</p>
        </>
      )}
    </Modal>
  );
}
