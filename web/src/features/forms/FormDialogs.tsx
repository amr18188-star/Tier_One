import { useState } from "react";
import { Alert, DatePicker, Form as AntForm, Input, Modal } from "antd";
import dayjs, { type Dayjs } from "dayjs";
import { useMutation } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { Form, FormItem } from "../../api/collection";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import { DecisionModal } from "../../shared/DecisionModal";
import { ImportModal } from "./ImportModal";
import { skippedSteps } from "./model";
import type { FormSubmit } from "./useFormPage";
import type { ReturnMark } from "./ValueRow";
import { useFieldErrors } from "../../shared/useFieldErrors";

/* The form page's dialogs: send for review, approve (normal or direct), return, reopen, extend a deadline, import from Excel. */

/** ملخص ما سيرسل قبل التأكيد: كم قيمة وكم صفر وغير متاح ولا ينطبق، والتفسيرات والأدلة، وإلى من يذهب النموذج. */
export function SubmitModal({ form, items, pending, error, onConfirm, onClose }: {
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

export function ApproveModal({ form, direct, onClose, onDone }: { form: Form; direct: boolean; onClose: () => void; onDone: (f: Form) => void }) {
  const [version] = useState(form.version);
  const [fields] = AntForm.useForm<{ note: string }>();
  const run = useMutation({
    mutationFn: (note: string | null) => api.post<Form>(`/api/forms/${form.id}/approve`, { note, version, direct }),
    onSuccess: (f) => { onDone(f); onClose(); },
  });
  const final = !form.nextStepName;
  const skipped = direct ? skippedSteps(form, form.actions.directStepName) : [];
  const title = direct
    ? form.actions.directDeanApproval ? L("اعتماد مباشر من العميد", "Direct dean approval") : L("اعتماد مباشر لنموذج القسم", "Direct approval of the department form")
    : final ? L("الاعتماد النهائي", "Final approval") : L("اعتماد وإحالة", "Approve and forward");
  const runMarked = useFieldErrors(fields, run.error);
  return (
    <Modal open title={title} onCancel={onClose} onOk={() => (direct ? fields.submit() : run.mutate(null))} confirmLoading={run.isPending}
      okText={L("اعتماد", "Approve")} cancelText={L("إلغاء", "Cancel")} destroyOnHidden>
      {run.error && !runMarked && <Alert type="error" showIcon message={errorText(run.error)} className="mb" />}
      {direct ? (
        <>
          <p>{L(`ينتقل النموذج إلى «${form.actions.directStepName ?? ""}» متخطيًا: ${skipped.join("، ")}. يسجل التخطي وسببه في سجل النموذج.`,
            `The form moves to ${form.actions.directStepName ?? ""}, skipping ${skipped.join(", ")}. The skip and its reason are recorded.`)}</p>
          <AntForm form={fields} layout="vertical" onFinish={(v) => run.mutate(v.note.trim())}>
            <AntForm.Item name="note" label={L("سبب التخطي", "Reason for skipping")}
              rules={[{ required: true, whitespace: true, min: 5, message: L("اكتب سبب التخطي (5 أحرف على الأقل)", "Enter the reason (at least 5 characters)") }]}>
              <Input.TextArea rows={3} maxLength={2000} autoFocus />
            </AntForm.Item>
          </AntForm>
        </>
      ) : (
        <p>{final
          ? L(`تعتمد قيم ${form.unitName} نهائيًا فتدخل نتائج الجامعة، ويقفل النموذج.`, "Values become final and enter the results.")
          : L(`ينتقل النموذج إلى «${form.nextStepName}».`, `The form moves to ${form.nextStepName}.`)}</p>
      )}
    </Modal>
  );
}

export function ExtendModal({ form, onClose, onDone }: { form: Form; onClose: () => void; onDone: (f: Form) => void }) {
  const [fields] = AntForm.useForm<{ dueOn: Dayjs; reason: string }>();
  const run = useMutation({
    mutationFn: (v: { dueOn: Dayjs; reason: string }) => api.post<Form>(`/api/forms/${form.id}/extend`, { dueOn: v.dueOn.format("YYYY-MM-DD"), reason: v.reason }),
    onSuccess: (f) => { onDone(f); onClose(); },
  });
  const runMarked = useFieldErrors(fields, run.error);
  return (
    <Modal open title={L(`تمديد موعد ${form.unitName}`, `Extend ${form.unitName}`)} onCancel={onClose} onOk={() => fields.submit()} confirmLoading={run.isPending}
      okText={L("تمديد", "Extend")} cancelText={L("إلغاء", "Cancel")} destroyOnHidden>
      {run.error && !runMarked && <Alert type="error" showIcon message={errorText(run.error)} className="mb" />}
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

export type FormDialog = "submit" | "approve" | "direct" | "return" | "reopen" | "reopenApprove" | "reopenReject" | "import" | "extend";

/** The open dialog of the form page, wired to its request and to what the decision changes (applyDecision). */
export function FormDialogHost({ dialog, onClose, f, items, submit, flushAll, marks, clearMarks, applyDecision, refresh }: {
  dialog: FormDialog | null;
  onClose: () => void;
  f: Form;
  items: FormItem[];
  submit: FormSubmit;
  flushAll: () => Promise<void>;
  marks: Record<string, ReturnMark>;
  clearMarks: () => void;
  applyDecision: (next: Form) => void;
  refresh: () => void;
}) {
  const reopen = f.reopen ?? null;
  const flaggedCount = Object.values(marks).filter((m) => m.checked).length;
  return (
    <>
      {dialog === "submit" && (
        <SubmitModal form={f} items={items} pending={submit.isPending} error={submit.error} onClose={onClose}
          onConfirm={async () => { await flushAll(); await submit.mutateAsync(); onClose(); }} />
      )}
      {(dialog === "approve" || dialog === "direct") && (
        <ApproveModal form={f} direct={dialog === "direct"} onClose={onClose} onDone={applyDecision} />
      )}
      {dialog === "return" && (
        <DecisionModal title={L("إعادة النموذج للتصحيح", "Return the form")} field="note" label={L("الملاحظة العامة لمدخل البيانات", "Note to the data entry")} okText={L("إعادة", "Return")} danger
          explain={flaggedCount > 0
            ? L(`ستصل ملاحظاتك على ${flaggedCount} مؤشرًا مميزة في النموذج، مع هذه الملاحظة العامة.`, `${flaggedCount} indicator notes will be sent.`)
            : L("لم تعلّم مؤشرات بعينها؛ تكفي ملاحظة عامة واضحة. يمكنك تعليم المؤشرات بخانة «يحتاج تصحيحًا».", "No indicator marked; write a clear note.")}
          onSubmit={async (note) => {
            const next = await api.post<Form>(`/api/forms/${f.id}/return`, {
              note, items: Object.entries(marks).filter(([, m]) => m.checked).map(([indicatorId, m]) => ({ indicatorId, note: m.note || note })),
              version: f.version,
            });
            clearMarks();
            applyDecision(next);
          }}
          onClose={onClose} />
      )}
      {dialog === "reopen" && (
        <DecisionModal title={L("طلب إعادة فتح نموذج معتمد", "Request reopening an approved form")} field="note" label={L("سبب إعادة الفتح", "Reason")} okText={L("إرسال الطلب", "Send request")}
          explain={L("يرسل الطلب إلى اللجنة العليا ويبقى النموذج معتمدًا ومقفلًا حتى تعتمده برقم المحضر. عندها يعود إلى الإدخال ويمر بخطوات الاعتماد كلها من جديد، وتبقى قيمه المعتمدة الحالية رسمية في النتائج حتى يعاد اعتماده.",
            "The request goes to the higher committee; the form stays approved and locked until it approves with a decision reference.")}
          onSubmit={async (note) => applyDecision(await api.post<Form>(`/api/forms/${f.id}/reopen`, { note, version: f.version }))}
          onClose={onClose} />
      )}
      {dialog === "reopenApprove" && (
        <DecisionModal title={L("اعتماد إعادة فتح النموذج", "Approve reopening the form")} field="decisionRef" label={L("رقم محضر اللجنة العليا", "Committee decision reference")} okText={L("اعتماد وإعادة الفتح", "Approve and reopen")}
          explain={<>{L("سبب الطلب:", "Reason:")} {reopen?.reason}<br />{L("يعود النموذج إلى الإدخال، وتحفظ قيمه المعتمدة الحالية وتبقى الرسمية في النتائج حتى يعاد اعتماده.", "The form returns to entry; its approved values are kept and stay official until re-approval.")}</>}
          onSubmit={async (decisionRef) => applyDecision(await api.post<Form>(`/api/forms/${f.id}/reopen/approve`, { decisionRef, note: null }))}
          onClose={onClose} />
      )}
      {dialog === "reopenReject" && (
        <DecisionModal title={L("رفض طلب إعادة الفتح", "Reject the reopen request")} field="note" label={L("سبب الرفض", "Reason")} okText={L("رفض", "Reject")} danger
          explain={<>{L("سبب الطلب:", "Reason:")} {reopen?.reason}<br />{L("يبقى النموذج معتمدًا ومقفلًا، ويرى مقدم الطلب سبب الرفض.", "The form stays approved; the requester sees the reason.")}</>}
          onSubmit={async (note) => applyDecision(await api.post<Form>(`/api/forms/${f.id}/reopen/reject`, { note }))}
          onClose={onClose} />
      )}
      {dialog === "extend" && <ExtendModal form={f} onClose={onClose} onDone={applyDecision} />}
      {dialog === "import" && <ImportModal formId={f.id} onClose={onClose} onApplied={refresh} />}
    </>
  );
}
