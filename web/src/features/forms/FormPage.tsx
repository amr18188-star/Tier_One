import { useMemo, useState } from "react";
import { Alert, App, Button, DatePicker, Form as AntForm, Input, Modal, Popover, Progress, Segmented, Space, Tag, Timeline, Tooltip } from "antd";
import dayjs, { type Dayjs } from "dayjs";
import {
  AimOutlined, CalendarOutlined, CheckOutlined, DownloadOutlined, FileExcelOutlined, HistoryOutlined, LoadingOutlined, RollbackOutlined, SendOutlined, UnlockOutlined,
  WarningOutlined,
} from "@ant-design/icons";
import { useIsMutating, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams } from "react-router-dom";
import { api } from "../../api/client";
import type { Form, FormItem, FormSection } from "../../api/collection";
import { DecisionModal } from "../../shared/DecisionModal";
import { errorText, formatDate, formatDateTime } from "../../shared/format";
import { L } from "../../shared/i18n";
import { submissionStatus, unitKind } from "../../shared/labels";
import { usePermissions } from "../../shared/session";
import { PageHeader, Panel, Stepper } from "../../shared/ui";
import { ImportModal } from "./ImportModal";
import { savedAt, ValueRow, type ReturnMark } from "./ValueRow";

type Filter = "all" | "missing" | "issues" | "flagged";

/**
 * نموذج الجهة: الإدخال والمراجعة في صفحة واحدة. المسار أعلاها يقول أين النموذج ومن عليه الإجراء،
 * وشريط الإجراء أسفلها ثابت (إرسال، أو اعتماد وإعادة)، والمرشحات تعرض الناقص أو ما به ملاحظات فقط.
 */
export default function FormPage() {
  const { id } = useParams<{ id: string }>();
  const qc = useQueryClient();
  const { message } = App.useApp();
  const key = ["form", id];
  const form = useQuery({ queryKey: key, queryFn: () => api.get<Form>(`/api/forms/${id}`) });
  const [filter, setFilter] = useState<Filter>("all");
  const [marks, setMarks] = useState<Record<string, ReturnMark>>({});
  const [dialog, setDialog] = useState<"submit" | "approve" | "return" | "reopen" | "import" | "extend" | null>(null);
  const canExtend = usePermissions().can("dc.cycles", "E");
  // الحفظ التلقائي الجاري في أي صف، لشريط الحالة أسفل النموذج.
  const saving = useIsMutating();

  const refresh = () => qc.invalidateQueries({ queryKey: key });
  const onSaved = (item: FormItem) => {
    qc.setQueryData<Form>(key, (f) => f && {
      ...f,
      sections: f.sections.map((s) => ({ ...s, items: s.items.map((i) => (i.indicatorId === item.indicatorId ? item : i)) })),
    });
    void refresh();
  };

  const submit = useMutation({
    mutationFn: () => api.post<Form>(`/api/forms/${id}/submit`),
    onSuccess: (f) => {
      qc.setQueryData(key, f);
      void qc.invalidateQueries({ queryKey: ["forms"] });
      message.success(L(`أرسل النموذج إلى «${f.path.find((p) => p.state === "current")?.name ?? ""}»`, "Form sent for review"));
    },
  });

  const f = form.data;
  const items = useMemo(() => f?.sections.flatMap((s) => s.items) ?? [], [f]);
  // المعايير بترتيبها من الخادم (الركيزة ثم المعيار)، مجمعة تحت ركائزها.
  const pillars = useMemo(() => {
    const list: { code: string | null; name: string | null; sections: FormSection[] }[] = [];
    for (const s of f?.sections ?? []) {
      const last = list.at(-1);
      if (last && last.code === s.pillarCode) last.sections.push(s);
      else list.push({ code: s.pillarCode, name: s.pillarName, sections: [s] });
    }
    return list;
  }, [f]);
  const flaggedCount = Object.values(marks).filter((m) => m.checked).length;
  const visible = (i: FormItem) =>
    filter === "all" ? true
      : filter === "missing" ? !i.value
        : filter === "issues" ? i.issues.length > 0 || (!!i.evidenceRequired && i.evidence.every((e) => e.voided) && i.value?.kind === "Number")
          : !!i.value?.reviewNote || !!marks[i.indicatorId]?.checked;

  const missing = items.filter((i) => !i.value);
  const lastSaved = items.reduce<string | null>((m, i) => (i.value && (!m || i.value.editedAt > m) ? i.value.editedAt : m), null);
  /** ينتقل إلى أول مؤشر بلا قيمة ويضع المؤشر في حقله. */
  const goToMissing = () => {
    const first = missing[0];
    if (!first) return;
    setFilter("all");
    requestAnimationFrame(() => {
      const row = document.getElementById(`i-${first.code}`);
      row?.scrollIntoView({ behavior: "smooth", block: "center" });
      row?.querySelector<HTMLInputElement>("input")?.focus({ preventScroll: true });
    });
  };

  if (!f) return <Panel query={form}>{null}</Panel>;
  const editable = f.actions.canEdit;
  const reviewing = f.actions.canReview;
  const current = f.path.findIndex((p) => p.state === "current");

  return (
    <>
      <PageHeader back={{ to: "/forms", label: L("النماذج", "Forms") }}
        title={<>{f.unitName} <Tag color={submissionStatus[f.status].color}>{submissionStatus[f.status].text}</Tag>{f.trial && <Tag color="purple">{L("بروفة", "Rehearsal")}</Tag>}</>}
        subtitle={<>{unitKind[f.unitKind]} · {f.cycleName} · {L("الفترة", "Period")} <b className="ltr">{f.periodLabel}</b> · {L("الموعد النهائي", "Due")} <b className="ltr">{formatDate(f.dueOn)}</b>{f.overdue && <Tag color="error" className="ms">{L("متأخر", "Overdue")}</Tag>}</>}
        extra={
          <Space wrap>
            <Button icon={<DownloadOutlined />} href={`/api/forms/${f.id}/template.xlsx`}>{L("تنزيل Excel", "Download Excel")}</Button>
            {editable && <Button icon={<FileExcelOutlined />} onClick={() => setDialog("import")}>{L("إدخال من Excel", "Import from Excel")}</Button>}
            {canExtend && f.cycleStatus === "Open" && f.status !== "Approved" && (
              <Button icon={<CalendarOutlined />} onClick={() => setDialog("extend")}>{L("تمديد موعد الجهة", "Extend deadline")}</Button>
            )}
          </Space>
        } />

      <Panel>
        <Stepper current={current < 0 ? f.path.length : current} steps={f.path.map((p) => ({ title: p.name }))} />
      </Panel>

      {f.status === "Returned" && f.returnNote && (
        <Alert type="error" showIcon className="mb" icon={<RollbackOutlined />} message={L("أعيد النموذج للتصحيح", "The form was returned")} description={f.returnNote} />
      )}
      {f.status === "InProgress" && f.returnNote && f.events.at(-1)?.action === "Reopened" && (
        <Alert type="warning" showIcon className="mb" icon={<UnlockOutlined />} message={L("أعيد فتح النموذج بعد اعتماده", "The form was reopened")} description={f.returnNote} />
      )}
      {f.cycleStatus === "Closed" && <Alert type="info" showIcon className="mb" message={L("الدورة مغلقة؛ النموذج للاطلاع فقط.", "The cycle is closed; read only.")} />}
      {reviewing && (
        <Alert type="info" showIcon className="mb"
          message={L(`النموذج ينتظر مراجعتك (${f.path[current]?.name}). اعتمده لينتقل إلى ${f.nextStepName ?? "الاعتماد النهائي"}، أو علّم المؤشرات التي تحتاج تصحيحًا وأعده.`,
            "This form is waiting for your review.")} />
      )}

      <Panel>
        <div className="f-head">
          <div className="f-progress">
            <span>{L("أدخلت", "Entered")} <b className="ltr">{f.completed}/{f.required}</b></span>
            <Progress percent={f.required ? Math.round((100 * f.completed) / f.required) : 0} showInfo={false} />
          </div>
          <Segmented value={filter} onChange={(v) => setFilter(v as Filter)}
            options={[
              { value: "all", label: L("الكل", "All") },
              { value: "missing", label: L(`الناقص (${items.filter((i) => !i.value).length})`, "Missing") },
              { value: "issues", label: L("به تنبيهات", "With issues") },
              { value: "flagged", label: L("ملاحظات المراجعة", "Review notes") },
            ]} />
        </div>
        {pillars.map((p) => (
          <nav key={p.code ?? "-"} className="f-std-nav" aria-label={p.name ?? L("المعايير", "Standards")}>
            {p.name && <span className="f-pillar-label">{p.code} {p.name}</span>}
            {p.sections.map((s) => (
              <a key={s.standardCode} href={`#s-${s.standardCode}`} className={s.items.every((i) => i.value) ? "done" : undefined} title={s.standardName}>
                {s.standardCode} <span className="ltr">{s.items.filter((i) => i.value).length}/{s.items.length}</span>
              </a>
            ))}
          </nav>
        ))}
      </Panel>

      {/* الإدخال مصنفًا: الركيزة، ثم معاييرها، وفي كل معيار مؤشراته. */}
      {pillars.map((p) => {
        const shown = p.sections.map((s) => ({ s, rows: s.items.filter(visible) })).filter((x) => x.rows.length > 0);
        if (shown.length === 0) return null;
        return (
          <div key={p.code ?? "-"}>
            {p.name && <h2 className="t1-pillar-head"><code className="ltr">{p.code}</code>{p.name}</h2>}
            {shown.map(({ s, rows }) => (
              <Panel key={s.standardCode} id={`s-${s.standardCode}`} title={<><code className="ltr">{s.standardCode}</code> {s.standardName}</>}>
                {rows.map((i) => (
                  <ValueRow key={`${i.indicatorId}:${i.value?.version ?? 0}:${i.evidence.length}`} formId={f.id} item={i} editable={editable} reviewing={reviewing}
                    mark={marks[i.indicatorId]} onMark={(m) => setMarks((x) => ({ ...x, [i.indicatorId]: m }))} onSaved={onSaved} />
                ))}
              </Panel>
            ))}
          </div>
        );
      })}

      {f.events.length > 0 && (
        <Panel title={L("سجل النموذج", "Form history")} icon={<HistoryOutlined />}>
          <Timeline items={[...f.events].reverse().map((e) => ({
            color: e.action === "Returned" ? "red" : e.action === "Approved" ? "green" : e.action === "Reopened" ? "orange" : "blue",
            children: (
              <>
                <b>{eventText(e.action)}</b> — {e.byName} <small className="muted ltr">{formatDateTime(e.at)}</small>
                {e.note && <div className="muted">{e.note}</div>}
              </>
            ),
          }))} />
        </Panel>
      )}

      {(editable || reviewing || f.actions.canReopen) && (
        <div className="f-actions no-print">
          <span className="grow">
            {editable && (
              <span className="f-summary" aria-live="polite">
                <span>{L("أدخلت", "Entered")} <b className="ltr">{f.completed}/{f.required}</b></span>
                {missing.length > 0 && <span>{L("ناقص", "Missing")} <b className="ltr">{missing.length}</b></span>}
                {saving > 0 ? <span><LoadingOutlined aria-hidden /> {L("جار الحفظ…", "Saving…")}</span>
                  : lastSaved && <span>{L("آخر حفظ", "Last saved")} <b className="ltr">{savedAt(lastSaved)}</b></span>}
                {f.blocking.length === 0
                  ? <span className="f-issue" style={{ color: "var(--ok)" }}><CheckOutlined /> {L("مكتمل وجاهز للإرسال", "Complete and ready")}</span>
                  : <Popover title={L("ما يمنع الإرسال", "What blocks sending")} content={<ul style={{ maxWidth: 420, maxHeight: 300, overflow: "auto" }}>{f.blocking.map((b) => <li key={b}>{b}</li>)}</ul>}>
                      <Button type="link" size="small" className="f-issue warn" style={{ padding: 0 }}><WarningOutlined /> {L(`${f.blocking.length} أمر يمنع الإرسال`, `${f.blocking.length} items block sending`)}</Button>
                    </Popover>}
              </span>
            )}
            {reviewing && (flaggedCount > 0
              ? L(`علّمت ${flaggedCount} مؤشرًا للتصحيح.`, `${flaggedCount} marked for correction.`)
              : L("راجع القيم والأدلة، ثم اعتمد أو أعد.", "Review values and evidence, then approve or return."))}
          </span>
          {submit.error && <span className="f-issue err">{errorText(submit.error)}</span>}
          {editable && missing.length > 0 && (
            <Button size="large" icon={<AimOutlined />} onClick={goToMissing}>{L("التالي الناقص", "Next missing")}</Button>
          )}
          {editable && (
            <Tooltip title={!f.actions.canSubmit ? L("يتفعل حين يكتمل النموذج؛ اعرض «ما يمنع الإرسال».", "Enabled once the form is complete.") : undefined}>
              <Button type="primary" size="large" icon={<SendOutlined />} disabled={!f.actions.canSubmit} loading={submit.isPending} onClick={() => setDialog("submit")}>
                {L("إرسال للمراجعة", "Send for review")}
              </Button>
            </Tooltip>
          )}
          {reviewing && (
            <>
              <Button danger size="large" icon={<RollbackOutlined />} onClick={() => setDialog("return")}>{L("إعادة للتصحيح", "Return")}</Button>
              <Button type="primary" size="large" icon={<CheckOutlined />} disabled={flaggedCount > 0} onClick={() => setDialog("approve")}>
                {f.nextStepName ? L("اعتماد وإحالة", "Approve") : L("اعتماد نهائي", "Final approval")}
              </Button>
            </>
          )}
          {f.actions.canReopen && <Button icon={<UnlockOutlined />} onClick={() => setDialog("reopen")}>{L("إعادة فتح للتصحيح", "Reopen")}</Button>}
        </div>
      )}

      {dialog === "submit" && (
        <SubmitModal form={f} items={items} pending={submit.isPending} error={submit.error} onClose={() => setDialog(null)}
          onConfirm={async () => { await submit.mutateAsync(); setDialog(null); }} />
      )}
      {dialog === "approve" && (
        <ApproveModal form={f} onClose={() => setDialog(null)} onDone={(next) => { qc.setQueryData(key, next); void qc.invalidateQueries({ queryKey: ["forms"] }); }} />
      )}
      {dialog === "return" && (
        <DecisionModal title={L("إعادة النموذج للتصحيح", "Return the form")} field="note" label={L("الملاحظة العامة لمدخل البيانات", "Note to the data entry")} okText={L("إعادة", "Return")} danger
          explain={flaggedCount > 0
            ? L(`ستصل ملاحظاتك على ${flaggedCount} مؤشرًا مميزة في النموذج، مع هذه الملاحظة العامة.`, `${flaggedCount} indicator notes will be sent.`)
            : L("لم تعلّم مؤشرات بعينها؛ تكفي ملاحظة عامة واضحة. يمكنك تعليم المؤشرات بخانة «يحتاج تصحيحًا».", "No indicator marked; write a clear note.")}
          onSubmit={async (note) => {
            const next = await api.post<Form>(`/api/forms/${f.id}/return`, {
              note, items: Object.entries(marks).filter(([, m]) => m.checked).map(([indicatorId, m]) => ({ indicatorId, note: m.note || note })),
            });
            setMarks({});
            qc.setQueryData(key, next);
            void qc.invalidateQueries({ queryKey: ["forms"] });
          }}
          onClose={() => setDialog(null)} />
      )}
      {dialog === "reopen" && (
        <DecisionModal title={L("إعادة فتح نموذج معتمد", "Reopen an approved form")} field="note" label={L("سبب إعادة الفتح", "Reason")} okText={L("إعادة فتح", "Reopen")}
          explain={L("يعود النموذج إلى الإدخال ثم يمر بخطوات الاعتماد كلها من جديد. القيم السابقة واعتماداتها تبقى في السجل.", "The form returns to entry and the full approval path.")}
          onSubmit={async (note) => {
            qc.setQueryData(key, await api.post<Form>(`/api/forms/${f.id}/reopen`, { note }));
          }}
          onClose={() => setDialog(null)} />
      )}
      {dialog === "extend" && <ExtendModal form={f} onClose={() => setDialog(null)} onDone={(next) => { qc.setQueryData(key, next); void qc.invalidateQueries({ queryKey: ["forms"] }); }} />}
      {dialog === "import" && <ImportModal formId={f.id} onClose={() => setDialog(null)} onApplied={() => void refresh()} />}
    </>
  );
}

/** ملخص ما سيرسل قبل التأكيد: كم قيمة وكم صفر وغير متاح ولا ينطبق، والتفسيرات والأدلة، وإلى من يذهب النموذج. */
function SubmitModal({ form, items, pending, error, onConfirm, onClose }: {
  form: Form; items: FormItem[]; pending: boolean; error: unknown; onConfirm: () => Promise<void>; onClose: () => void;
}) {
  const v = (i: FormItem) => i.value;
  const numbers = items.filter((i) => v(i)?.kind === "Number");
  const zeros = numbers.filter((i) => (i.usesRatio ? v(i)!.numerator : v(i)!.value) === 0).length;
  const rows = [
    { label: L("قيم مدخلة", "Values entered"), n: numbers.length - zeros },
    { label: L("منها صفر", "Zero values"), n: zeros },
    { label: L("غير متاح (بسبب مكتوب)", "Not available"), n: items.filter((i) => v(i)?.kind === "NotAvailable").length },
    { label: L("لا ينطبق (بسبب مكتوب)", "Not applicable"), n: items.filter((i) => v(i)?.kind === "NotApplicable").length },
    { label: L("تفسيرات للمراجع", "Explanations"), n: items.filter((i) => !!v(i)?.explanation).length },
    { label: L("أدلة مرفقة", "Evidence files"), n: items.reduce((s, i) => s + i.evidence.filter((e) => !e.voided).length, 0) },
  ];
  return (
    <Modal open title={L("مراجعة قبل الإرسال", "Review before sending")} onCancel={onClose} onOk={() => void onConfirm()} confirmLoading={pending}
      okText={L("إرسال للمراجعة", "Send for review")} cancelText={L("رجوع للتعديل", "Back to editing")}>
      {!!error && <Alert type="error" showIcon message={errorText(error)} className="mb" />}
      <p>{L(`${form.unitName} · ${form.periodLabel}: ${form.completed} من ${form.required} مؤشرًا.`, `${form.completed} of ${form.required} indicators.`)}</p>
      <ul className="f-submit-sum">{rows.map((r) => <li key={r.label}><span>{r.label}</span><b className="ltr">{r.n}</b></li>)}</ul>
      <Alert type="info" showIcon message={L(`يقفل النموذج للتعديل وينتقل إلى «${form.path[1]?.name ?? ""}». يعاد إليك إن احتاج تصحيحًا.`, "The form will be locked until returned.")} />
    </Modal>
  );
}

function ApproveModal({ form, onClose, onDone }: { form: Form; onClose: () => void; onDone: (f: Form) => void }) {
  const run = useMutation({ mutationFn: () => api.post<Form>(`/api/forms/${form.id}/approve`, { note: null }), onSuccess: (f) => { onDone(f); onClose(); } });
  const final = !form.nextStepName;
  return (
    <Modal open title={final ? L("الاعتماد النهائي", "Final approval") : L("اعتماد وإحالة", "Approve and forward")} onCancel={onClose} onOk={() => run.mutate()} confirmLoading={run.isPending}
      okText={L("اعتماد", "Approve")} cancelText={L("إلغاء", "Cancel")}>
      {run.error && <Alert type="error" showIcon message={errorText(run.error)} className="mb" />}
      <p>{final
        ? L(`تعتمد قيم ${form.unitName} نهائيًا فتدخل نتائج الجامعة، ويقفل النموذج.`, "Values become final and enter the results.")
        : L(`ينتقل النموذج إلى «${form.nextStepName}».`, `The form moves to ${form.nextStepName}.`)}</p>
    </Modal>
  );
}

function ExtendModal({ form, onClose, onDone }: { form: Form; onClose: () => void; onDone: (f: Form) => void }) {
  const [fields] = AntForm.useForm<{ dueOn: Dayjs; reason: string }>();
  const run = useMutation({
    mutationFn: (v: { dueOn: Dayjs; reason: string }) => api.post<Form>(`/api/forms/${form.id}/extend`, { dueOn: v.dueOn.format("YYYY-MM-DD"), reason: v.reason }),
    onSuccess: (f) => { onDone(f); onClose(); },
  });
  return (
    <Modal open title={L(`تمديد موعد ${form.unitName}`, `Extend ${form.unitName}`)} onCancel={onClose} onOk={() => fields.submit()} confirmLoading={run.isPending}
      okText={L("تمديد", "Extend")} cancelText={L("إلغاء", "Cancel")} destroyOnHidden>
      {run.error && <Alert type="error" showIcon message={errorText(run.error)} className="mb" />}
      <p>{L("يمدد موعد هذه الجهة وحدها بقرار مسجل؛ موعد الدورة لبقية الجهات لا يتغير.", "Extends this unit only; the cycle deadline stays.")}</p>
      <AntForm form={fields} layout="vertical" onFinish={(v) => run.mutate(v)} initialValues={{ dueOn: dayjs(form.dueOn).add(7, "day") }}>
        <AntForm.Item name="dueOn" label={L("الموعد الجديد", "New deadline")} rules={[{ required: true }]}>
          <DatePicker disabledDate={(d) => !d.isAfter(dayjs(form.dueOn), "day")} />
        </AntForm.Item>
        <AntForm.Item name="reason" label={L("السبب أو رقم القرار", "Reason or decision")} rules={[{ required: true, min: 5, message: L("اكتب السبب", "Enter the reason") }]}>
          <Input maxLength={500} />
        </AntForm.Item>
      </AntForm>
    </Modal>
  );
}

function eventText(a: Form["events"][number]["action"]) {
  return a === "Submitted" ? L("أرسل للمراجعة", "Submitted") : a === "Approved" ? L("اعتمد", "Approved") : a === "Returned" ? L("أعيد للتصحيح", "Returned") : L("أعيد فتحه", "Reopened");
}
