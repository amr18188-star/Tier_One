import { useEffect, useMemo } from "react";
import { Alert, App, Form, Input, InputNumber, Modal, Radio } from "antd";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { Target, TargetKind } from "../../api/tier1";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import { num, targetKind } from "../../shared/labels";

export interface TargetSlot { unitId: string; unitName: string; year: number; kind: TargetKind; existing: Target | null }

interface FormValues { mode: "number" | "text"; value?: number; text?: string; numerator?: number; denominator?: number; source?: string; note?: string; reason?: string; decisionRef?: string }

/**
 * إدخال مستهدف أو تعديله. القيمة رقم، أو وصف حين لم يحسم رقمها بعد. للنسب يكفي إدخال البسط والمقام فتحسب النسبة
 * تلقائيًا ولا تعدل يدويًا. تعديل المعتمد يطلب السبب ورقم القرار، والقيمة السابقة تحفظ.
 */
export function TargetEditor({ code, unit, slot, onClose }: { code: string; unit: string; slot: TargetSlot; onClose: () => void }) {
  const [form] = Form.useForm<FormValues>();
  const qc = useQueryClient();
  const { message } = App.useApp();
  const percent = unit.trim() === "%";
  const approved = slot.existing?.status === "Approved";
  const mode = Form.useWatch("mode", form);
  const n = Form.useWatch("numerator", form);
  const d = Form.useWatch("denominator", form);
  const computed = useMemo(() => (n !== undefined && n !== null && d ? (n / d) * (percent ? 100 : 1) : undefined), [n, d, percent]);

  useEffect(() => {
    const t = slot.existing;
    form.setFieldsValue({
      mode: t?.text && t.value === null ? "text" : "number",
      value: t?.value ?? undefined, text: t?.text ?? undefined, numerator: t?.numerator ?? undefined, denominator: t?.denominator ?? undefined,
      source: t?.source ?? undefined, note: t?.note ?? undefined,
    });
  }, [slot, form]);

  const save = useMutation({
    mutationFn: (v: FormValues) => api.put(`/api/kpi/indicators/${code}/targets`, {
      unitId: slot.unitId, year: slot.year, kind: slot.kind,
      value: v.mode === "text" ? null : computed !== undefined ? Math.round(computed * 10000) / 10000 : v.value ?? null,
      text: v.mode === "text" ? v.text : null,
      numerator: v.mode === "number" ? v.numerator ?? null : null, denominator: v.mode === "number" ? v.denominator ?? null : null,
      source: v.source, note: v.note, reason: v.reason, decisionRef: v.decisionRef, version: slot.existing?.version,
    }),
    onSuccess: async () => {
      message.success(L("حُفظ المستهدف.", "Target saved."));
      await qc.invalidateQueries({ queryKey: ["targets", code] });
      await qc.invalidateQueries({ queryKey: ["distribution", code] });
      onClose();
    },
  });

  return (
    <Modal open title={`${targetKind[slot.kind]} — ${slot.unitName} — ${slot.year}`} okText={L("حفظ", "Save")} cancelText={L("إلغاء", "Cancel")}
      onCancel={onClose} onOk={() => form.submit()} confirmLoading={save.isPending} destroyOnHidden>
      {save.error && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      {approved && (
        <Alert type="warning" showIcon className="mb" message={L("هذا المستهدف معتمد", "This target is approved")}
          description={L("تعديله يحتاج سببًا ورقم قرار اللجنة العليا، وتحفظ القيمة السابقة للمقارنة.", "Changing it needs a reason and the committee decision reference; the previous value is kept.")} />
      )}
      <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)} requiredMark="optional">
        <Form.Item name="mode" label={L("نوع القيمة", "Value type")}>
          <Radio.Group optionType="button" options={[
            { value: "number", label: L("رقم", "Number") },
            { value: "text", label: L("وصف (لم يحسم الرقم بعد)", "Description (number not set yet)") },
          ]} />
        </Form.Item>
        {mode === "text" ? (
          <Form.Item name="text" label={L("وصف المستهدف", "Target description")} rules={[{ required: true, message: L("اكتب وصف المستهدف", "Enter the description") }]}
            extra={L("مثل: «مشروعان لكل كلية». يفضل تحويله إلى رقم قبل الاعتماد.", "E.g. “two projects per college”. Prefer a number before approval.")}>
            <Input maxLength={300} showCount />
          </Form.Item>
        ) : (
          <>
            {percent && (
              <div className="lcp-grid-2">
                <Form.Item name="numerator" label={L("البسط (اختياري)", "Numerator (optional)")} extra={L("مع المقام تحسب النسبة تلقائيًا", "With the denominator the ratio is computed")}>
                  <InputNumber min={0} style={{ width: "100%" }} />
                </Form.Item>
                <Form.Item name="denominator" label={L("المقام", "Denominator")} dependencies={["numerator"]}
                  rules={[({ getFieldValue }) => ({
                    validator: (_, v) => {
                      const nn = getFieldValue("numerator");
                      if ((nn ?? null) === null && (v ?? null) === null) return Promise.resolve();
                      if ((nn ?? null) === null || (v ?? null) === null) return Promise.reject(L("أدخل البسط والمقام معًا", "Enter both"));
                      if (v <= 0) return Promise.reject(L("المقام أكبر من صفر", "Must be above zero"));
                      if (nn > v) return Promise.reject(L(`البسط ${num(nn)} أكبر من المقام ${num(v)}؛ راجع إحدى القيمتين`, `Numerator ${num(nn)} exceeds denominator ${num(v)}`));
                      return Promise.resolve();
                    },
                  })]}>
                  <InputNumber min={0} style={{ width: "100%" }} />
                </Form.Item>
              </div>
            )}
            {computed !== undefined ? (
              <Alert type="info" className="mb" message={<>{L("النسبة المحسوبة:", "Computed ratio:")} <b className="ltr">{num(computed)}%</b> = {num(n)} ÷ {num(d)} × 100</>} />
            ) : (
              <Form.Item name="value" label={`${L("القيمة المستهدفة", "Target value")} (${unit})`}
                rules={[{ required: true, message: L("اكتب القيمة", "Enter the value") }]}
                extra={percent ? L("نسبة من 0 إلى 100", "A percentage from 0 to 100") : undefined}>
                <InputNumber min={0} max={percent ? 100 : undefined} style={{ width: "100%" }} />
              </Form.Item>
            )}
          </>
        )}
        <Form.Item name="source" label={L("المصدر", "Source")} extra={L("مثل: ملف Tier 1، أو قرار اللجنة رقم …", "E.g. the Tier 1 file, or committee decision no. …")}>
          <Input maxLength={300} />
        </Form.Item>
        <Form.Item name="note" label={L("ملاحظة", "Note")}><Input.TextArea rows={2} maxLength={1000} /></Form.Item>
        {approved && (
          <>
            <Form.Item name="reason" label={L("سبب التعديل", "Reason")} rules={[{ required: true, min: 5, message: L("اكتب سببًا واضحًا", "Enter a clear reason") }]}>
              <Input.TextArea rows={2} maxLength={500} />
            </Form.Item>
            <Form.Item name="decisionRef" label={L("رقم قرار اللجنة العليا", "Committee decision reference")} rules={[{ required: true, message: L("اكتب رقم القرار", "Enter the reference") }]}>
              <Input maxLength={200} />
            </Form.Item>
          </>
        )}
      </Form>
    </Modal>
  );
}
