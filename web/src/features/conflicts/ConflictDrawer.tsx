import { useEffect } from "react";
import { Alert, App, Button, DatePicker, Descriptions, Drawer, Form, Input, Radio, Tag } from "antd";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import dayjs, { type Dayjs } from "dayjs";
import { Link } from "react-router-dom";
import { api } from "../../api/client";
import type { Conflict } from "../../api/tier1";
import { errorText, formatDate } from "../../shared/format";
import { L, useLang } from "../../shared/i18n";
import { conflictSeverity, conflictStatus } from "../../shared/labels";
import { useFieldErrors } from "../../shared/useFieldErrors";
import { queryKeys } from "../../api/queries";

/**
 * تفاصيل التعارض: القيمتان المتعارضتان ومصدراهما جنبًا إلى جنب، ثم ما يستطيعه المستخدم:
 * التجهيز (المعالجة المقترحة) لمدير متابعة الخطة، وتسجيل القرار برقم المحضر للجنة العليا.
 */
export function ConflictDrawer({ conflict, canPrepare, canDecide, onClose }: { conflict: Conflict; canPrepare: boolean; canDecide: boolean; onClose: () => void }) {
  const lang = useLang();
  const resolved = conflict.status === "Resolved";
  return (
    <Drawer open width={760} placement={lang === "ar" ? "left" : "right"} onClose={onClose}
      title={<><code>{conflict.code}</code> {conflict.type}</>}>
      <p>
        <Tag color={conflictSeverity[conflict.severity].color}>{L("الخطورة:", "Severity:")} {conflictSeverity[conflict.severity].text}</Tag>
        <Tag color={conflictStatus[conflict.status].color}>{conflictStatus[conflict.status].text}</Tag>
        <span className="t1-help">{conflictStatus[conflict.status].hint}</span>
      </p>
      <p><b>{conflict.description}</b></p>
      <p className="t1-help">
        {L("المؤشرات والمعايير المتأثرة:", "Affected:")}{" "}
        {conflict.scope.map((s) => s.includes("-") ? <Link key={s} to={`/indicators/${s}`} className="ms"><code>{s}</code></Link>
          : <Link key={s} to={`/indicators?standard=${s}`} className="ms"><code>{s}</code></Link>)}
      </p>
      <Descriptions bordered column={1} size="small" className="mb" items={[
        { key: "a", label: L("القيمة أ ومصدرها", "Value A and source"), children: conflict.valueA ?? "—" },
        { key: "b", label: L("القيمة ب ومصدرها", "Value B and source"), children: conflict.valueB ?? "—" },
        { key: "p", label: L("المعالجة المقترحة", "Proposal"), children: conflict.proposal ?? "—" },
        ...(conflict.preparationNote ? [{ key: "n", label: L("ملاحظة التجهيز", "Preparation note"), children: conflict.preparationNote }] : []),
        ...(conflict.decision || conflict.minutesRef ? [{
          key: "d", label: L("القرار", "Decision"),
          children: <>{conflict.decision ?? "—"}<div className="t1-help">{conflict.decidingBody} — {L("محضر", "Minutes")} {conflict.minutesRef} — {formatDate(conflict.decisionDate)}</div></>,
        }] : []),
      ]} />

      {!resolved && canPrepare && <PrepareForm conflict={conflict} />}
      {!resolved && canDecide && <DecideForm conflict={conflict} onDone={onClose} />}
      {resolved && (
        <Alert type="success" showIcon message={L("حُسم التعارض", "Resolved")}
          description={L("القرار لا يغير الأرقام تلقائيًا؛ يطبقه مدير متابعة الخطة تعديلًا مسجلًا في القاموس أو المستهدفات.",
            "The decision does not change numbers automatically; the plan manager applies it as a recorded change.")} />
      )}
    </Drawer>
  );
}

function PrepareForm({ conflict }: { conflict: Conflict }) {
  const [form] = Form.useForm<{ proposal: string; preparationNote?: string }>();
  const qc = useQueryClient();
  const { message } = App.useApp();
  useEffect(() => form.setFieldsValue({ proposal: conflict.proposal ?? "", preparationNote: conflict.preparationNote ?? undefined }), [conflict, form]);
  const save = useMutation({
    mutationFn: (v: { proposal: string; preparationNote?: string }) => api.post<Conflict>(`/api/kpi/conflicts/${conflict.code}/prepare`, { ...v, version: conflict.version }),
    onSuccess: async () => {
      message.success(L("جُهز التعارض ودخل جدول أعمال اللجنة.", "Prepared and added to the committee agenda."));
      await qc.invalidateQueries({ queryKey: queryKeys.conflicts });
      await qc.invalidateQueries({ queryKey: queryKeys.home });
    },
  });
  const saveMarked = useFieldErrors(form, save.error);
  return (
    <section className="lcp-muted-box mb">
      <h3 className="t1-form-sec">{L("التجهيز للجنة العليا", "Prepare for the committee")}</h3>
      {save.error && !saveMarked && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)}>
        <Form.Item name="proposal" label={L("المعالجة المقترحة", "Proposed resolution")} rules={[{ required: true, message: L("اكتب المعالجة المقترحة", "Enter the proposal") }]}
          extra={L("ما تقترح أن تقرره اللجنة، بالأرقام إن أمكن.", "What the committee should decide, with numbers if possible.")}>
          <Input.TextArea rows={3} maxLength={2000} showCount />
        </Form.Item>
        <Form.Item name="preparationNote" label={L("ملاحظة للجنة (اختياري)", "Note for the committee (optional)")}>
          <Input.TextArea rows={2} maxLength={2000} />
        </Form.Item>
        <Button type="primary" htmlType="submit" loading={save.isPending}>{conflict.status === "Open" ? L("تجهيز وإدراج في جدول الأعمال", "Prepare for the agenda") : L("تحديث التجهيز", "Update")}</Button>
      </Form>
    </section>
  );
}

interface DecideValues { outcome: "resolve" | "defer"; decision?: string; decisionDate: Dayjs; minutesRef: string }

function DecideForm({ conflict, onDone }: { conflict: Conflict; onDone: () => void }) {
  const [form] = Form.useForm<DecideValues>();
  const qc = useQueryClient();
  const { message } = App.useApp();
  const outcome = Form.useWatch("outcome", form);
  const save = useMutation({
    mutationFn: (v: DecideValues) => api.post<Conflict>(`/api/kpi/conflicts/${conflict.code}/decide`, {
      defer: v.outcome === "defer", decision: v.decision, decisionDate: v.decisionDate.format("YYYY-MM-DD"), minutesRef: v.minutesRef, version: conflict.version,
    }),
    onSuccess: async (_, v) => {
      message.success(v.outcome === "defer" ? L("سُجل التأجيل.", "Deferral recorded.") : L("سُجل قرار اللجنة العليا.", "Committee decision recorded."));
      await qc.invalidateQueries({ queryKey: queryKeys.conflicts });
      await qc.invalidateQueries({ queryKey: queryKeys.home });
      onDone();
    },
  });
  const saveMarked = useFieldErrors(form, save.error);
  return (
    <section className="lcp-muted-box">
      <h3 className="t1-form-sec">{L("قرار اللجنة العليا لـ Tier 1", "Tier 1 higher committee decision")}</h3>
      {save.error && !saveMarked && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" initialValues={{ outcome: "resolve", decisionDate: dayjs(), decision: conflict.proposal ?? undefined }} onFinish={(v) => save.mutate(v)}>
        <Form.Item name="outcome">
          <Radio.Group optionType="button" options={[{ value: "resolve", label: L("حسم بقرار", "Resolve") }, { value: "defer", label: L("تأجيل", "Defer") }]} />
        </Form.Item>
        <Form.Item name="decision" label={outcome === "defer" ? L("سبب التأجيل (اختياري)", "Reason (optional)") : L("نص القرار", "Decision text")}
          rules={outcome === "defer" ? [] : [{ required: true, min: 5, message: L("اكتب نص القرار", "Enter the decision") }]}
          extra={outcome === "defer" ? undefined : L("المعالجة المقترحة معبأة مسبقًا؛ عدلها بما قررته اللجنة.", "Pre-filled with the proposal; adjust to what was decided.")}>
          <Input.TextArea rows={3} maxLength={2000} />
        </Form.Item>
        <div className="lcp-grid-2">
          <Form.Item name="decisionDate" label={L("تاريخ الجلسة", "Session date")} rules={[{ required: true }]}>
            <DatePicker style={{ width: "100%" }} disabledDate={(d) => d.isAfter(dayjs(), "day")} />
          </Form.Item>
          <Form.Item name="minutesRef" label={L("رقم المحضر", "Minutes reference")} rules={[{ required: true, message: L("اكتب رقم المحضر", "Enter the minutes reference") }]}>
            <Input maxLength={200} />
          </Form.Item>
        </div>
        <Button type="primary" htmlType="submit" loading={save.isPending}>{L("تسجيل القرار", "Record the decision")}</Button>
      </Form>
    </section>
  );
}
