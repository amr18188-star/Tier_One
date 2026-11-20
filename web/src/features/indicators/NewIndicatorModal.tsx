import { Alert, Form, Input, Modal, Select } from "antd";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { api } from "../../api/client";
import type { CollectionLevel, IndicatorDetail, Standard } from "../../api/tier1";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import { level, options } from "../../shared/labels";
import { queryKeys } from "../../api/queries";
import { useFieldErrors } from "../../shared/useFieldErrors";

interface Fields { standardCode: string; nameAr: string; unit: string; level: CollectionLevel; reason?: string }

/** إضافة مؤشر: المعيار والاسم فقط؛ الرمز يولد تاليًا في المعيار، وبقية التعريف تكمل في صفحة المؤشر ثم ترسل للاعتماد. */
export function NewIndicatorModal({ standards, standard, onClose }: { standards: Pick<Standard, "code" | "nameAr">[]; standard?: string; onClose: () => void }) {
  const [form] = Form.useForm<Fields>();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const create = useMutation({
    mutationFn: (v: Fields) => api.post<IndicatorDetail>("/api/kpi/indicators", v),
    onSuccess: async (d) => {
      await qc.invalidateQueries({ queryKey: queryKeys.indicators });
      await qc.invalidateQueries({ queryKey: queryKeys.standards });
      await qc.invalidateQueries({ queryKey: queryKeys.strategy });
      navigate(`/indicators/${d.code}?edit=1`);
    },
  });
  const createMarked = useFieldErrors(form, create.error);
  return (
    <Modal open title={L("مؤشر جديد", "New indicator")} okText={L("إنشاء وإكمال التعريف", "Create and complete")} cancelText={L("إلغاء", "Cancel")}
      onCancel={onClose} onOk={() => form.submit()} confirmLoading={create.isPending} destroyOnHidden>
      {create.error && !createMarked && <Alert type="error" showIcon message={errorText(create.error)} className="mb" />}
      <Form form={form} layout="vertical" onFinish={(v) => create.mutate(v)} initialValues={{ standardCode: standard, unit: "%", level: "College" }} requiredMark="optional">
        <Form.Item name="standardCode" label={L("المعيار", "Standard")} rules={[{ required: true, message: L("اختر المعيار", "Pick a standard") }]}>
          <Select showSearch optionFilterProp="label" options={standards.map((s) => ({ value: s.code, label: `${s.code} — ${s.nameAr}` }))} />
        </Form.Item>
        <Form.Item name="nameAr" label={L("اسم المؤشر", "Indicator name")} rules={[{ required: true, message: L("اكتب اسم المؤشر", "Enter the name") }]}>
          <Input maxLength={300} autoFocus />
        </Form.Item>
        <Form.Item name="unit" label={L("وحدة القياس", "Unit")} extra={L("% للنسب، أو عدد، أو مليون جنيه…", "% for ratios, or a count unit")}>
          <Input maxLength={50} style={{ maxWidth: 200 }} />
        </Form.Item>
        <Form.Item name="level" label={L("يجمع على مستوى", "Collected at")}>
          <Select options={options(level)} style={{ maxWidth: 240 }} />
        </Form.Item>
        <Form.Item name="reason" label={L("سبب الإضافة أو مرجعها", "Reason or reference")}>
          <Input maxLength={500} placeholder={L("مثل: قرار اللجنة العليا رقم …", "e.g. committee decision no. …")} />
        </Form.Item>
      </Form>
      <p className="t1-help">{L("يبدأ المؤشر مسودة؛ يكمل تعريفه ومصدره وقاعدة تجميعه ثم يرسل للجنة العليا لاعتماده.", "It starts as a draft to complete and submit.")}</p>
    </Modal>
  );
}
