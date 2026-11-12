import { useEffect } from "react";
import { Alert, App, Button, Drawer, Form, Input, Select, Space } from "antd";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { IndicatorDetail, OrgUnit, Version } from "../../api/tier1";
import { errorText } from "../../shared/format";
import { L, useLang } from "../../shared/i18n";
import { aggregation, direction, frequency, level, options } from "../../shared/labels";

type FormValues = Pick<Version, "nameAr" | "nameEn" | "definition" | "purpose" | "exclusions" | "numerator" | "denominator" | "unit" | "direction"
  | "frequency" | "level" | "aggregation" | "aggregationNote" | "dataSource" | "ownerUnitId" | "evidenceRequired"> & { changeReason?: string };

/** أثر كل قاعدة تجميع بجملة واحدة، تحت اختيارها مباشرة. */
const AGG_HELP: Record<Version["aggregation"], string> = {
  Undefined: "اختر القاعدة؛ لا يعتمد المؤشر بدونها.",
  Sum: "للأعداد فقط (طلاب، برامج، أبحاث): الجامعة = مجموع الكليات. لا تصلح للنسب أو المعدلات.",
  RatioOfSums: "للنسب والمعدلات: تدخل كل جهة البسط والمقام، والجامعة = مجموع البسط ÷ مجموع المقام. أدق قاعدة للنسب.",
  DistinctCount: "لما قد يتكرر بين الكليات (براءات، شراكات، دول): تدخل كل كلية عددها، ويدخل عدد الجامعة مركزيًا بعد إزالة التكرار.",
  WeightedAverage: "للاستبيانات والمتوسطات: تدخل كل جهة القيمة ووزنها (عدد المستجيبين)، والجامعة متوسط مرجح بالأوزان.",
  Central: "يدخل مرة واحدة على مستوى الجامعة كلها (اجعل مستوى الجمع «الجامعة»).",
  StatusOnly: "حالة لكل كلية لا تجمع.",
};

const UNITS = ["%", "عدد", "برنامج", "مليون جنيه", "جنيه", "طالب لكل عضو", "بحث / عضو", "ساعة / عضو / سنة", "ساعة / أسبوع", "ساعة / يوم", "دولة", "سرير", "مركز", "حالة"];

/**
 * تعديل تعريف المؤشر في مجموعات قصيرة بعناوين مفهومة، وتحت كل حقل ما المقصود به. البسط والمقام يظهران
 * لقاعدة «نسبة مجموع البسط إلى مجموع المقام» فقط. تعديل تعريف معتمد ينشئ إصدارًا جديدًا ويطلب سببه.
 */
export function DefinitionDrawer({ detail, onClose }: { detail: IndicatorDetail; onClose: () => void }) {
  const [form] = Form.useForm<FormValues>();
  const lang = useLang();
  const qc = useQueryClient();
  const { message } = App.useApp();
  const v = detail.current;
  const createsVersion = detail.actions.editCreatesVersion;
  const agg = Form.useWatch("aggregation", form);
  const units = useQuery({ queryKey: ["units"], queryFn: () => api.get<OrgUnit[]>("/api/org/units") });

  useEffect(() => {
    // سبب الإصدار السابق لا يعبأ: كل إصدار جديد يكتب سببه.
    form.setFieldsValue({ ...v, nameEn: v.nameEn ?? undefined, changeReason: undefined });
  }, [v, form]);

  const save = useMutation({
    mutationFn: (f: FormValues) => api.put<IndicatorDetail>(`/api/kpi/indicators/${detail.code}/definition`, { ...f, version: createsVersion ? undefined : v.version }),
    onSuccess: async (updated) => {
      qc.setQueryData(["indicator", detail.code], updated);
      await qc.invalidateQueries({ queryKey: ["indicators"] });
      await qc.invalidateQueries({ queryKey: ["home"] });
      message.success(updated.current.missing.length === 0
        ? L("حُفظ التعريف وهو مكتمل؛ يمكنك إرساله للاعتماد.", "Saved and complete; you can submit it for approval.")
        : L(`حُفظ التعريف. ما زال ينقصه: ${updated.current.missing.join("، ")}.`, `Saved. Still missing: ${updated.current.missing.join(", ")}.`));
      onClose();
    },
  });

  return (
    <Drawer open width={720} placement={lang === "ar" ? "left" : "right"} onClose={onClose}
      title={`${L("تعديل تعريف", "Edit definition")} ${detail.code}`}
      extra={<Space><Button onClick={onClose}>{L("إلغاء", "Cancel")}</Button><Button type="primary" loading={save.isPending} onClick={() => form.submit()}>{L("حفظ", "Save")}</Button></Space>}>
      {createsVersion && (
        <Alert type="warning" showIcon className="mb" message={L("التعريف معتمد", "The definition is approved")}
          description={L(`الحفظ ينشئ الإصدار ${v.number + 1} مسودةً، ويبقى الإصدار ${v.number} المعتمد ساريًا حتى تعتمد اللجنة الجديد.`,
            `Saving creates version ${v.number + 1} as a draft; version ${v.number} stays in force until the committee approves the new one.`)} />
      )}
      {save.error && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" onFinish={(f) => save.mutate(f)} requiredMark="optional" scrollToFirstError>
        {createsVersion && (
          <Form.Item name="changeReason" label={L("سبب التعديل", "Reason for the change")} rules={[{ required: true, min: 5, message: L("اكتب سببًا واضحًا", "Enter a clear reason") }]}>
            <Input.TextArea rows={2} maxLength={500} />
          </Form.Item>
        )}

        <h3 className="t1-form-sec">{L("١. ما المؤشر؟", "1. What is it?")}</h3>
        <Form.Item name="nameAr" label={L("اسم المؤشر", "Name")} rules={[{ required: true, message: L("اكتب اسم المؤشر", "Enter the name") }]}>
          <Input maxLength={300} />
        </Form.Item>
        <Form.Item name="nameEn" label={L("الاسم بالإنجليزية", "English name")}><Input maxLength={300} dir="ltr" /></Form.Item>
        <Form.Item name="definition" label={L("التعريف", "Definition")} rules={[{ required: true, message: L("التعريف مطلوب للاعتماد", "Required for approval") }]}
          extra={L("ما الذي يقيسه بالضبط، بلغة يفهمها مدخل البيانات دون شرح إضافي.", "Exactly what it measures, clear enough for data entry without extra explanation.")}>
          <Input.TextArea rows={3} maxLength={2000} showCount />
        </Form.Item>
        <Form.Item name="purpose" label={L("الغرض", "Purpose")}><Input.TextArea rows={2} maxLength={1000} /></Form.Item>
        <Form.Item name="exclusions" label={L("الاستبعادات", "Exclusions")} extra={L("ما لا يدخل في الحساب، مثل البرامج المجمدة.", "What is not counted.")}>
          <Input.TextArea rows={2} maxLength={1000} />
        </Form.Item>

        <h3 className="t1-form-sec">{L("٢. كيف يقاس؟", "2. How is it measured?")}</h3>
        <div className="lcp-grid-2">
          <Form.Item name="unit" label={L("وحدة القياس", "Unit")} rules={[{ required: true }]}>
            <Select showSearch options={UNITS.map((u) => ({ value: u, label: u }))} />
          </Form.Item>
          <Form.Item name="direction" label={L("الاتجاه المطلوب", "Desired direction")} extra={L("هل ارتفاع القيمة أفضل أم انخفاضها؟", "Is higher better, or lower?")}>
            <Select options={options(direction)} />
          </Form.Item>
          <Form.Item name="frequency" label={L("الدورية", "Frequency")}><Select options={options(frequency)} /></Form.Item>
          <Form.Item name="level" label={L("مستوى جمع البيانات", "Collection level")} extra={L("أدنى جهة تدخل البيانات.", "The lowest unit that enters the data.")}>
            <Select options={options(level)} />
          </Form.Item>
        </div>
        <Form.Item name="aggregation" label={L("قاعدة التجميع", "Aggregation rule")}
          extra={L("كيف تحسب قيمة الكلية والجامعة من قيم الجهات. النسب والمعدلات (% أو «لكل عضو») تدخل ببسطها ومقامها وتجمع منهما، لا من متوسط النسب ولا بجمعها.",
            "How the college and university values are computed. Ratios are combined from numerators and denominators, not averaged or added.")}>
          <Select options={options(aggregation)} />
        </Form.Item>
        {agg && <p className="t1-help" style={{ marginTop: -12 }}>{AGG_HELP[agg]}</p>}
        {agg === "WeightedAverage" && (
          <Form.Item name="denominator" label={L("أساس الترجيح", "Weighting basis")} rules={[{ required: true, message: L("مطلوب ليحسب متوسط الجامعة بدقة", "Required for an exact university average") }]}
            extra={L("ما يرجح به متوسط كل جهة، مثل «عدد المستجيبين للاستبيان». تدخله كل جهة مع قيمتها.", "What each unit's average is weighted by, e.g. number of respondents.")}>
            <Input maxLength={500} />
          </Form.Item>
        )}
        {agg === "RatioOfSums" && (
          <div className="lcp-grid-2">
            <Form.Item name="numerator" label={L("البسط", "Numerator")} rules={[{ required: true, message: L("مطلوب لقاعدة النسبة", "Required for a ratio") }]}>
              <Input.TextArea rows={2} maxLength={500} />
            </Form.Item>
            <Form.Item name="denominator" label={L("المقام", "Denominator")} rules={[{ required: true, message: L("مطلوب لقاعدة النسبة", "Required for a ratio") }]}>
              <Input.TextArea rows={2} maxLength={500} />
            </Form.Item>
          </div>
        )}
        <Form.Item name="aggregationNote" label={L("ملاحظة على التجميع", "Aggregation note")}><Input maxLength={500} /></Form.Item>

        <h3 className="t1-form-sec">{L("٣. المصدر والمسؤولية", "3. Source and ownership")}</h3>
        <Form.Item name="dataSource" label={L("مصدر البيانات", "Data source")} rules={[{ required: true, message: L("المصدر مطلوب للاعتماد", "Required for approval") }]}
          extra={L("النظام أو التقرير أو السجل الذي تؤخذ منه القيمة.", "The system, report or register the value comes from.")}>
          <Input.TextArea rows={2} maxLength={500} />
        </Form.Item>
        <Form.Item name="ownerUnitId" label={L("الجهة المالكة", "Owner unit")} rules={[{ required: true, message: L("الجهة المالكة مطلوبة للاعتماد", "Required for approval") }]}>
          <Select showSearch allowClear optionFilterProp="label" loading={units.isLoading}
            options={(units.data ?? []).filter((u) => u.kind !== "Department").map((u) => ({ value: u.id, label: u.nameAr }))} />
        </Form.Item>
        <Form.Item name="evidenceRequired" label={L("الأدلة المطلوبة", "Required evidence")} extra={L("ما يرفق مع القيمة ليقبلها المراجع.", "What must be attached for the reviewer to accept the value.")}>
          <Input.TextArea rows={2} maxLength={1000} />
        </Form.Item>
      </Form>
    </Drawer>
  );
}
