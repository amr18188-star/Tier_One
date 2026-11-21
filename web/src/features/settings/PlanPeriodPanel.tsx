import { useState } from "react";
import { Alert, Button, DatePicker, Descriptions, Form, Input, Modal, Tag, Tooltip } from "antd";
import { CalendarOutlined, EditOutlined } from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import dayjs, { type Dayjs } from "dayjs";
import { api } from "../../api/client";
import { queryKeys } from "../../api/queries";
import type { PlanPeriod } from "../../api/tier1";
import { errorText, formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { QueryState } from "../../shared/QueryState";
import { useFieldErrors } from "../../shared/useFieldErrors";

interface Fields { start: Dayjs; end: Dayjs; reason: string }

/**
 * ADR-0017: the plan's period (default 1 September 2026 – 31 August 2031), from which every quarter, plan year and target year
 * follows. Everyone who sees the settings sees it; the super administrator alone changes it, as the ministry instructs.
 */
export function PlanPeriodPanel() {
  const q = useQuery({ queryKey: queryKeys.planPeriod, queryFn: ({ signal }) => api.get<PlanPeriod>("/api/admin/settings/plan-period", { signal }) });
  const [editing, setEditing] = useState(false);
  return (
    <QueryState query={q}>
      {q.data && (
        <>
          <Descriptions size="small" bordered layout="vertical" column={{ xs: 1, sm: 2, xl: 4 }}
            extra={q.data.editable
              ? <Button size="small" icon={<EditOutlined aria-hidden />} onClick={() => setEditing(true)}>{L("تعديل الفترة", "Change period")}</Button>
              : <Tooltip title={L("يغيرها المدير الأعلى وحده.", "Only a super administrator can change it.")}><Tag>{L("للمدير الأعلى", "Super admin")}</Tag></Tooltip>}
            items={[
              { key: "s", label: L("بداية الخطة", "Starts"), children: <b className="ltr-nums nowrap">{formatDate(q.data.start)}</b> },
              { key: "e", label: L("نهاية الخطة", "Ends"), children: <b className="ltr-nums nowrap">{formatDate(q.data.end)}</b> },
              { key: "y", label: L("سنوات الخطة", "Plan years"), children: <span className="ltr-nums">{q.data.years.length} ({q.data.years.join("، ")})</span> },
              { key: "q", label: L("الدورات الربع سنوية", "Quarterly cycles"), children: <span className="ltr-nums">{q.data.quarters} {L("دورة، كل دورة ثلاثة أشهر", "quarters of three months")}</span> },
            ]} />
          <small className="muted">{L("تسمى كل سنة خطة بالسنة التي تنتهي فيها؛ وخط الأساس هو السنة السابقة لأولى سنوات الخطة.",
            "Each plan year is named for the year it ends in; the baseline is the year before the first.")}</small>
          {editing && <EditPeriod current={q.data} onClose={() => setEditing(false)} />}
        </>
      )}
    </QueryState>
  );
}

function EditPeriod({ current, onClose }: { current: PlanPeriod; onClose: () => void }) {
  const [form] = Form.useForm<Fields>();
  const qc = useQueryClient();
  const start = Form.useWatch("start", form);
  const end = Form.useWatch("end", form);
  const save = useMutation({
    mutationFn: (f: Fields) => api.put<PlanPeriod>("/api/admin/settings/plan-period", {
      startYear: f.start.year(), startMonth: f.start.month() + 1, endYear: f.end.year(), endMonth: f.end.month() + 1, reason: f.reason.trim(),
    }),
    // Every quarter, year and target column follows the period, so every screen reloads.
    onSuccess: async () => { await qc.invalidateQueries(); onClose(); },
  });
  const marked = useFieldErrors(form, save.error);
  const months = start && end ? end.diff(start, "month") + 1 : 0;
  return (
    <Modal open title={L("فترة الخطة", "Plan period")} okText={L("حفظ", "Save")} cancelText={L("إلغاء", "Cancel")} confirmLoading={save.isPending}
      onOk={() => form.submit()} onCancel={onClose} destroyOnHidden>
      <Alert type="warning" showIcon className="mb" icon={<CalendarOutlined />}
        message={L("تغيير الفترة يغير الأرباع وسنوات المستهدفات في كل الشاشات. لا تقبل فترة تترك دورة رسمية خارجها.",
          "Changing the period moves the quarters and target years everywhere. A period that leaves an official cycle outside it is refused.")} />
      {save.error && !marked && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" onFinish={(f) => save.mutate(f)}
        initialValues={{ start: dayjs(current.start), end: dayjs(current.end) }}>
        <div className="form-grid">
          <Form.Item name="start" label={L("شهر البداية", "First month")} rules={[{ required: true }]}>
            <DatePicker picker="month" format="MM/YYYY" style={{ width: "100%" }} allowClear={false} />
          </Form.Item>
          <Form.Item name="end" label={L("شهر النهاية", "Last month")} dependencies={["start"]}
            rules={[{ required: true }, ({ getFieldValue }) => ({
              validator: (_, v?: Dayjs) => {
                const s = getFieldValue("start") as Dayjs | undefined;
                if (!s || !v) return Promise.resolve();
                const n = v.diff(s, "month") + 1;
                return n >= 12 && n % 12 === 0 && n <= 120 ? Promise.resolve()
                  : Promise.reject(new Error(L("الفترة سنوات كاملة (12 شهرًا لكل سنة) من سنة إلى عشر سنوات.", "Whole years of twelve months, one to ten.")));
              },
            })]}
            extra={months > 0 && months % 12 === 0 ? L(`${months / 12} سنوات، ${months / 3} دورة ربع سنوية`, `${months / 12} years, ${months / 3} quarters`) : undefined}>
            <DatePicker picker="month" format="MM/YYYY" style={{ width: "100%" }} allowClear={false} />
          </Form.Item>
        </div>
        <Form.Item name="reason" label={L("سبب التعديل (مثل رقم تعليمات الوزارة)", "Reason (e.g. the ministry's instruction)")} rules={[{ required: true, whitespace: true, min: 5 }]}>
          <Input.TextArea rows={2} maxLength={500} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
