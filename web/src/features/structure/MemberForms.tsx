import { useEffect } from "react";
import { Alert, App, Checkbox, DatePicker, Form, Input, Modal } from "antd";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import dayjs, { type Dayjs } from "dayjs";
import { api } from "../../api/client";
import type { Member, Partner, Position } from "../../api/tier1";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import { useFieldErrors } from "../../shared/useFieldErrors";
import { queryKeys } from "../../api/queries";

const INDUSTRY = "عضو من جهة صناعية";

interface MemberValues { fullName: string; title?: string; employer?: string; email?: string; phone?: string; startDate: Dayjs; decisionRef?: string; notes?: string; replaceCurrent?: boolean }

/**
 * إضافة عضو إلى منصب أو تعديل بياناته. المنصب والكلية والقسم محددة من الرسم نفسه، فيكتب المستخدم بيانات الشخص فقط.
 * إن كان المنصب مشغولًا يظهر خيار «استبدال الحالي» فتنتهي عضويته في اليوم السابق تلقائيًا.
 */
export function MemberModal({ collegeId, departmentId, position, member, occupiedBy, onClose }: {
  collegeId: string; departmentId: string | null; position: Position; member: Member | null; occupiedBy: string | null; onClose: () => void;
}) {
  const [form] = Form.useForm<MemberValues>();
  const qc = useQueryClient();
  const { message } = App.useApp();
  useEffect(() => {
    form.setFieldsValue(member
      ? { ...member, title: member.title ?? undefined, employer: member.employer ?? undefined, email: member.email ?? undefined, phone: member.phone ?? undefined, decisionRef: member.decisionRef ?? undefined, notes: member.notes ?? undefined, startDate: dayjs(member.startDate) }
      : { startDate: dayjs(), replaceCurrent: false });
  }, [member, form]);

  const save = useMutation({
    mutationFn: (v: MemberValues) => {
      const body = {
        collegeId, departmentId, positionId: position.id, fullName: v.fullName, title: v.title, employer: v.employer, email: v.email, phone: v.phone,
        startDate: v.startDate.format("YYYY-MM-DD"), decisionRef: v.decisionRef, notes: v.notes, replaceCurrent: !!v.replaceCurrent, version: member?.version,
      };
      return member ? api.put(`/api/structure/members/${member.id}`, body) : api.post("/api/structure/members", body);
    },
    onSuccess: async () => {
      message.success(member ? L("حُفظت بيانات العضو.", "Member saved.") : L("أضيف العضو إلى الهيكل.", "Member added."));
      await qc.invalidateQueries({ queryKey: queryKeys.structure });
      await qc.invalidateQueries({ queryKey: queryKeys.home });
      onClose();
    },
  });

  const saveMarked = useFieldErrors(form, save.error);
  return (
    <Modal open title={`${member ? L("تعديل بيانات", "Edit") : L("إضافة", "Add")}: ${position.titleAr}`} okText={L("حفظ", "Save")} cancelText={L("إلغاء", "Cancel")}
      onCancel={onClose} onOk={() => form.submit()} confirmLoading={save.isPending} destroyOnHidden>
      {save.error && !saveMarked && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)} requiredMark="optional">
        <Form.Item name="fullName" label={L("الاسم", "Full name")} rules={[{ required: true, message: L("اكتب اسم العضو", "Enter the name") }]}><Input maxLength={200} autoFocus /></Form.Item>
        <Form.Item name="title" label={L("الدرجة أو الصفة الوظيفية", "Title")} extra={L("مثل: أستاذ — وكيل الكلية للدراسات العليا", "E.g. Professor — Vice dean")}><Input maxLength={200} /></Form.Item>
        {position.titleAr === INDUSTRY && <Form.Item name="employer" label={L("جهة العمل", "Employer")} rules={[{ required: true }]}><Input maxLength={200} /></Form.Item>}
        <div className="lcp-grid-2">
          <Form.Item name="email" label={L("البريد الإلكتروني", "Email")} rules={[{ type: "email", message: L("البريد غير صحيح", "Invalid email") }]}><Input dir="ltr" maxLength={254} /></Form.Item>
          <Form.Item name="phone" label={L("الهاتف", "Phone")}><Input dir="ltr" maxLength={30} /></Form.Item>
          <Form.Item name="startDate" label={L("بداية العضوية", "Start date")} rules={[{ required: true }]}><DatePicker style={{ width: "100%" }} /></Form.Item>
          <Form.Item name="decisionRef" label={L("رقم قرار التشكيل وتاريخه", "Formation decision")}><Input maxLength={200} /></Form.Item>
        </div>
        <Form.Item name="notes" label={L("ملاحظات", "Notes")}><Input.TextArea rows={2} maxLength={1000} /></Form.Item>
        {!member && occupiedBy && (
          <Form.Item name="replaceCurrent" valuePropName="checked"
            extra={L(`المنصب يشغله ${occupiedBy}؛ تنتهي عضويته في اليوم السابق لبداية العضو الجديد، ويبقى في سجل الهيكل.`, `Held by ${occupiedBy}; their membership ends the day before.`)}>
            <Checkbox>{L("استبدال الحالي", "Replace the current holder")}</Checkbox>
          </Form.Item>
        )}
      </Form>
    </Modal>
  );
}

/** إنهاء عضوية أو شراكة بتاريخ وسبب؛ لا حذف، فيبقى الهيكل السابق قابلًا للعرض. */
export function EndModal({ title, path, onClose }: { title: string; path: string; onClose: () => void }) {
  const [form] = Form.useForm<{ endDate: Dayjs; reason: string }>();
  const qc = useQueryClient();
  const { message } = App.useApp();
  const save = useMutation({
    mutationFn: (v: { endDate: Dayjs; reason: string }) => api.post(path, { endDate: v.endDate.format("YYYY-MM-DD"), reason: v.reason }),
    onSuccess: async () => {
      message.success(L("سُجل الإنهاء.", "Ended."));
      await qc.invalidateQueries({ queryKey: queryKeys.structure });
      onClose();
    },
  });
  const saveMarked = useFieldErrors(form, save.error);
  return (
    <Modal open title={title} okText={L("إنهاء", "End")} okButtonProps={{ danger: true }} cancelText={L("إلغاء", "Cancel")}
      onCancel={onClose} onOk={() => form.submit()} confirmLoading={save.isPending} destroyOnHidden>
      <Alert type="info" showIcon className="mb" message={L("لا يحذف شيء: يبقى السجل ويظهر في الهيكل كما كان قبل هذا التاريخ.", "Nothing is deleted; the record stays and shows in earlier views.")} />
      {save.error && !saveMarked && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" initialValues={{ endDate: dayjs() }} onFinish={(v) => save.mutate(v)}>
        <Form.Item name="endDate" label={L("تاريخ الانتهاء", "End date")} rules={[{ required: true }]}><DatePicker style={{ width: "100%" }} /></Form.Item>
        <Form.Item name="reason" label={L("السبب", "Reason")} rules={[{ required: true, min: 5, message: L("اكتب سببًا واضحًا", "Enter a clear reason") }]}><Input.TextArea rows={2} maxLength={500} /></Form.Item>
      </Form>
    </Modal>
  );
}

interface PartnerValues { nameAr: string; sector?: string; coordinatorName?: string; coordinatorEmail?: string; coordinatorPhone?: string; agreementDate?: Dayjs; agreementRef?: string }

export function PartnerModal({ departmentId, departmentName, partner, onClose }: { departmentId: string; departmentName: string; partner: Partner | null; onClose: () => void }) {
  const [form] = Form.useForm<PartnerValues>();
  const qc = useQueryClient();
  const { message } = App.useApp();
  useEffect(() => {
    if (partner) form.setFieldsValue({
      nameAr: partner.nameAr, sector: partner.sector ?? undefined, coordinatorName: partner.coordinatorName ?? undefined, coordinatorEmail: partner.coordinatorEmail ?? undefined,
      coordinatorPhone: partner.coordinatorPhone ?? undefined, agreementDate: partner.agreementDate ? dayjs(partner.agreementDate) : undefined, agreementRef: partner.agreementRef ?? undefined,
    });
  }, [partner, form]);
  const save = useMutation({
    mutationFn: (v: PartnerValues) => {
      const body = { departmentId, ...v, agreementDate: v.agreementDate?.format("YYYY-MM-DD") ?? null, version: partner?.version };
      return partner ? api.put(`/api/structure/partners/${partner.id}`, body) : api.post("/api/structure/partners", body);
    },
    onSuccess: async () => {
      message.success(L("حُفظ الشريك الصناعي.", "Partner saved."));
      await qc.invalidateQueries({ queryKey: queryKeys.structure });
      onClose();
    },
  });
  const saveMarked = useFieldErrors(form, save.error);
  return (
    <Modal open title={`${L("الشريك الصناعي لقسم", "Industry partner of")} ${departmentName}`} okText={L("حفظ", "Save")} cancelText={L("إلغاء", "Cancel")}
      onCancel={onClose} onOk={() => form.submit()} confirmLoading={save.isPending} destroyOnHidden>
      {save.error && !saveMarked && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)} requiredMark="optional">
        <Form.Item name="nameAr" label={L("اسم الجهة الشريكة", "Partner name")} rules={[{ required: true }]}><Input maxLength={200} autoFocus /></Form.Item>
        <Form.Item name="sector" label={L("القطاع أو النشاط", "Sector")}><Input maxLength={150} /></Form.Item>
        <div className="lcp-grid-2">
          <Form.Item name="coordinatorName" label={L("المنسق لدى الشريك", "Coordinator")}><Input maxLength={200} /></Form.Item>
          <Form.Item name="coordinatorPhone" label={L("هاتف المنسق", "Coordinator phone")}><Input dir="ltr" maxLength={30} /></Form.Item>
          <Form.Item name="coordinatorEmail" label={L("بريد المنسق", "Coordinator email")} rules={[{ type: "email" }]}><Input dir="ltr" maxLength={254} /></Form.Item>
          <Form.Item name="agreementDate" label={L("تاريخ الاتفاقية", "Agreement date")}><DatePicker style={{ width: "100%" }} /></Form.Item>
        </div>
        <Form.Item name="agreementRef" label={L("رقم الاتفاقية أو البروتوكول", "Agreement reference")}><Input maxLength={200} /></Form.Item>
      </Form>
    </Modal>
  );
}
