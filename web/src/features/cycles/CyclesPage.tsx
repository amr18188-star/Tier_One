import { useState, type ComponentProps } from "react";
import { Alert, App, Button, ConfigProvider, DatePicker, Drawer, Form, Input, Modal, Space, Switch, Tag } from "antd";
import { CalendarOutlined, EditOutlined, LockOutlined, PlayCircleOutlined, PlusOutlined, UnorderedListOutlined } from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import dayjs, { type Dayjs } from "dayjs";
import { Link } from "react-router-dom";
import { api } from "../../api/client";
import type { Cycle, OpenPreview, SaveCycle, SubmissionStatus } from "../../api/collection";
import type { PlanQuarter } from "../../api/scorecard";
import { SegmentBar } from "../../shared/charts";
import { DecisionModal } from "../../shared/DecisionModal";
import { errorText, formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { cycleStatus, submissionStatus } from "../../shared/labels";
import { PeriodNav } from "../../shared/PeriodNav";
import { usePermissions } from "../../shared/session";
import { EmptyNote, PageHeader, Panel } from "../../shared/ui";
import enUS from "antd/locale/en_US";
import "./cycles.css";

// Range picker forced to LTR with English day/month names, whatever the app's language.
// Wrapped in a component so Form.Item still injects value/onChange into the picker itself.
function LtrRangePicker(props: ComponentProps<typeof DatePicker.RangePicker>) {
  return (
    <ConfigProvider direction="ltr" locale={enUS}>
      <DatePicker.RangePicker popupClassName="t1-single-month" {...props} />
    </ConfigProvider>
  );
}

const STATUS_COLORS: Record<SubmissionStatus, string> = {
  NotStarted: "var(--ds-status-none)", InProgress: "var(--ds-status-progress)", InReview: "var(--ds-status-near)", Returned: "var(--ds-status-late-ink)", Approved: "var(--ds-status-ok)",
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
        stats={cycles.data ? [
          { label: L("دورات", "Cycles"), value: cycles.data.length },
          { label: L("مفتوحة الآن", "Open now"), value: cycles.data.filter((c) => c.status === "Open").length, tone: "ok" },
          { label: L("نماذج متأخرة", "Overdue forms"), value: cycles.data.reduce((n, c) => n + c.overdue, 0), tone: cycles.data.some((c) => c.overdue > 0) ? "bad" : undefined },
        ] : undefined}
        extra={canAdd && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing("new")}>{L("دورة جديدة", "New cycle")}</Button>} />

      <Panel query={cycles}>
        {() => cycles.data!.length === 0 ? (
          <EmptyNote action={canAdd && <Button type="primary" onClick={() => setEditing("new")}>{L("أنشئ أول دورة", "Create the first cycle")}</Button>}>
            {L("لا توجد دورات بعد. ابدأ ببروفة داخلية يدخل فيها فريق المتابعة جهة واحدة كاملة، ثم الدورة التجريبية للجامعة كلها بالمؤشرات المعتمدة.", "No cycles yet. Start with an internal rehearsal.")}
          </EmptyNote>
        ) : (
          <div className="cy-list">
            {cycles.data!.map((c) => (
              <article key={c.id} className={`cy-card st-${c.status}`}>
                {c.status === "Open" && <CycleClock cycle={c} />}
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

/** الدورة المفتوحة: كم انقضى من مدتها، وكم اعتمد من نماذجها، في سطر واحد يقارن الوقت بالإنجاز. */
function CycleClock({ cycle: c }: { cycle: Cycle }) {
  const start = dayjs(c.opensOn), end = dayjs(c.dueOn), today = dayjs();
  const total = Math.max(1, end.diff(start, "day"));
  const passed = Math.min(total, Math.max(0, today.diff(start, "day")));
  const left = end.diff(today, "day");
  const time = Math.round((100 * passed) / total);
  const done = c.forms ? Math.round((100 * (c.formsByStatus.Approved ?? 0)) / c.forms) : 0;
  return (
    <div className="cy-clock">
      <div>
        <span>{L("انقضى من مدة الدورة", "Time elapsed")} <b className="ltr">{time}%</b> · {left >= 0 ? L(`باقٍ ${left} يومًا`, `${left} days left`) : L(`تجاوزت الموعد بـ${-left} يومًا`, `${-left} days late`)}</span>
        <span className="cy-bar time"><i style={{ width: `${time}%` }} /></span>
      </div>
      <div>
        <span>{L("نماذج معتمدة نهائيًا", "Forms approved")} <b className="ltr">{done}%</b></span>
        <span className={`cy-bar ${done >= time ? "ahead" : "behind"}`}><i style={{ width: `${done}%` }} /></span>
      </div>
    </div>
  );
}

/**
 * حقل الربع في نموذج الدورة: شريط السنة والربع المشترك (PeriodNav) بقيمة الحقل نفسها، رقم الربع في تقويم الخطة (index)،
 * ومنه السنة والربع عند الحفظ. الربع الذي له دورة رسمية معطل وسببه في سطر الحالة؛ ونقر سنة يبقي رقم الربع إن كان متاحًا فيها.
 */
function QuarterField({ quarters, taken, disabled, value, onChange }: {
  quarters: PlanQuarter[]; taken: Set<string>; disabled?: boolean; value?: number; onChange?: (index: number) => void;
}) {
  const selected = quarters.find((q) => q.index === value);
  const [viewYear, setViewYear] = useState<number | null>(null);
  const year = viewYear ?? selected?.year ?? quarters[0]?.year ?? null;
  const free = (y: number, n: number) => !taken.has(`${y}-${n}`);
  const pick = (y: number, n: number | null | undefined) => {
    setViewYear(y);
    const q = n ? quarters.find((x) => x.year === y && x.quarter === n) : undefined;
    if (q) onChange?.(q.index);
  };
  return (
    <PeriodNav mode="quarter" compact disabled={disabled} ariaLabel={L("الربع الذي تقيسه الدورة", "Quarter measured")}
      value={{ year, quarter: selected && selected.year === year ? selected.quarter : null }}
      disabledQuarters={(y, n) => (free(y, n) ? false : L("له دورة", "has a cycle"))}
      onChange={(next) => {
        if (next.quarter !== undefined) return pick(next.year, next.quarter);
        // نقر سنة: الربع نفسه إن كان متاحًا فيها، وإلا أول ربع متاح، وإلا تعرض السنة دون تغيير القيمة.
        const same = selected && free(next.year, selected.quarter) ? selected.quarter : undefined;
        const first = quarters.find((x) => x.year === next.year && free(x.year, x.quarter))?.quarter;
        pick(next.year, same ?? first);
      }} />
  );
}

interface CycleFields { slot: number; name: string; dates: [Dayjs, Dayjs]; trial: boolean }

/**
 * الدورة لربع من أرباع الخطة العشرين: يختار الربع من تقويم الخطة (يضبط بدايته المدير من الإعدادات)، فيقترح الاسم والفترة والمواعيد:
 * يفتح الإدخال بعد انتهاء الربع ويستمر شهرًا. كل ربع له دورة رسمية واحدة؛ البروفة لا تدخل بطاقة الأداء.
 */
function CycleDrawer({ cycle, onClose }: { cycle: Cycle | null; onClose: () => void }) {
  const [form] = Form.useForm<CycleFields>();
  const qc = useQueryClient();
  const { message } = App.useApp();
  const calendar = useQuery({ queryKey: ["plan", "calendar"], queryFn: () => api.get<PlanQuarter[]>("/api/plan/calendar") });
  const cycles = useQuery({ queryKey: ["cycles"], queryFn: () => api.get<Cycle[]>("/api/cycles") });
  const quarters = calendar.data ?? [];
  const taken = new Set((cycles.data ?? []).filter((x) => x.id !== cycle?.id && !x.trial && x.quarter).map((x) => `${x.targetYear}-${x.quarter}`));
  const suggest = (q: PlanQuarter) => {
    const opens = dayjs(q.end).add(1, "day");
    return { name: L(`دورة ${q.label}`, `Cycle Y${q.yearIndex} Q${q.quarter}`), dates: [opens, opens.add(30, "day")] as [Dayjs, Dayjs] };
  };
  const save = useMutation({
    mutationFn: (v: CycleFields) => {
      const q = quarters.find((x) => x.index === v.slot)!;
      const body: SaveCycle = {
        name: v.name, periodLabel: null, targetYear: q.year, quarter: q.quarter, trial: v.trial,
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
  // الافتراضي للدورة الجديدة: أول ربع انتهى ولا دورة رسمية له، وإلا الربع الحالي.
  const today = dayjs().format("YYYY-MM-DD");
  const firstFree = quarters.find((q) => q.end < today && !taken.has(`${q.year}-${q.quarter}`)) ?? quarters.find((q) => q.current) ?? quarters[0];
  const current = cycle?.quarter ? quarters.find((q) => q.year === cycle.targetYear && q.quarter === cycle.quarter) : undefined;

  return (
    <Drawer open width={520} title={cycle ? L("تعديل الدورة", "Edit cycle") : L("دورة ربعية جديدة", "New quarterly cycle")} onClose={onClose} destroyOnHidden
      extra={<Space><Button onClick={onClose}>{L("إلغاء", "Cancel")}</Button><Button type="primary" loading={save.isPending} disabled={quarters.length === 0} onClick={() => form.submit()}>{L("حفظ", "Save")}</Button></Space>}>
      {save.error && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      {open && <Alert type="info" showIcon className="mb" message={L("الدورة مفتوحة: يمكن تعديل الاسم وتمديد الموعد النهائي فقط.", "The cycle is open: only the name and deadline can change.")} />}
      {quarters.length === 0 ? <p>{L("جار تحميل تقويم الخطة…", "Loading…")}</p> : (
        <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)} requiredMark="optional"
          onValuesChange={(changed) => {
            if (changed.slot && !cycle) {
              const q = quarters.find((x) => x.index === changed.slot);
              if (q) form.setFieldsValue(suggest(q));
            }
          }}
          initialValues={cycle
            ? { slot: current?.index, name: cycle.name, trial: cycle.trial, dates: [dayjs(cycle.opensOn), dayjs(cycle.dueOn)] }
            : { slot: firstFree?.index, trial: false, ...(firstFree ? suggest(firstFree) : {}) }}>
          <Form.Item name="slot" label={L("الربع الذي تقيسه الدورة", "Quarter measured")} rules={[{ required: true, message: L("اختر الربع", "Pick the quarter") }]}
            extra={L("بداية الربع الأول من السنة الأولى يضبطها مدير النظام من «إعدادات النظام».", "Quarter 1 of year 1 is set in system settings.")}>
            <QuarterField quarters={quarters} taken={taken} disabled={open} />
          </Form.Item>
          <Form.Item name="name" label={L("اسم الدورة", "Name")} rules={[{ required: true, message: L("اكتب اسم الدورة", "Enter the name") }]}>
            <Input maxLength={150} />
          </Form.Item>
          <Form.Item name="dates" label={L("فتح الإدخال والموعد النهائي", "Opens and due")} rules={[{ required: true, message: L("حدد التاريخين", "Pick both dates") }]}
            extra={L("المقترح: يفتح الإدخال بعد انتهاء الربع ويستمر شهرًا.", "Suggested: opens after the quarter ends, for a month.")}>
            <LtrRangePicker style={{ width: "100%" }} />
          </Form.Item>
          <Form.Item name="trial" valuePropName="checked" label={L("بروفة داخلية", "Internal rehearsal")}
            extra={L("البروفة لاختبار النماذج ومسار الاعتماد، ولا تدخل قيمها بطاقة الأداء ولا ملف المجلس.", "Rehearsal values never enter the scorecard.")}>
            <Switch disabled={open} />
          </Form.Item>
        </Form>
      )}
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
