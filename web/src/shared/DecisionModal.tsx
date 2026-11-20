import { Alert, Form, Input, Modal } from "antd";
import { useMutation } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { errorText } from "./format";
import { L } from "./i18n";
import { useFieldErrors } from "./useFieldErrors";

/**
 * نافذة قرار من حقل واحد: رقم محضر الاعتماد، أو ملاحظة الإعادة، أو سبب الإيقاف.
 * الشرح فوق الحقل يقول ما سيحدث بعد التأكيد.
 */
export function DecisionModal({ title, explain, field, label, okText, danger, onSubmit, onClose }: {
  title: string; explain: ReactNode; field: "decisionRef" | "note" | "reason"; label: string; okText: string; danger?: boolean;
  onSubmit: (value: string) => Promise<unknown>; onClose: () => void;
}) {
  const [form] = Form.useForm<Record<string, string>>();
  const run = useMutation({ mutationFn: (v: string) => onSubmit(v), onSuccess: onClose });
  const runMarked = useFieldErrors(form, run.error);
  return (
    <Modal open title={title} okText={okText} cancelText={L("إلغاء", "Cancel")} okButtonProps={{ danger }} onCancel={onClose}
      onOk={() => form.submit()} confirmLoading={run.isPending} destroyOnHidden>
      <Alert type="info" showIcon message={explain} className="mb" />
      {run.error && !runMarked && <Alert type="error" showIcon message={errorText(run.error)} className="mb" />}
      <Form form={form} layout="vertical" onFinish={(v) => run.mutate(v[field])}>
        <Form.Item name={field} label={label} rules={[{ required: true, min: field === "decisionRef" ? 1 : 5, message: L("هذا الحقل مطلوب", "This field is required") }]}>
          {field === "decisionRef" ? <Input maxLength={200} autoFocus /> : <Input.TextArea rows={3} maxLength={1000} autoFocus />}
        </Form.Item>
      </Form>
    </Modal>
  );
}
