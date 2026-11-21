import { useState } from "react";
import { Alert, Button, DatePicker, Form, Input, InputNumber, Modal, Progress, Select, Table, Tag, Tooltip } from "antd";
import { DeleteOutlined, EditOutlined, PlusOutlined } from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import dayjs, { type Dayjs } from "dayjs";
import { api } from "../../api/client";
import { queryKeys, useUnits } from "../../api/queries";
import type { InitiativeActivity, InitiativeStatus } from "../../api/tier1";
import { DecisionModal } from "../../shared/DecisionModal";
import { errorText, formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { initiativeStatus } from "../../shared/labels";
import { EmptyNote } from "../../shared/ui";
import { useFieldErrors } from "../../shared/useFieldErrors";

interface Fields {
  titleAr: string;
  description?: string;
  ownerUnitId?: string;
  dates?: [Dayjs | null, Dayjs | null];
  status: InitiativeStatus;
  progress: number;
}

/**
 * ADR-0017: an initiative's own activities: who is responsible, when, its status and how far it went. Adding, editing and deleting
 * follow the initiatives' permissions (the plan's add, edit and delete); a deletion asks for a reason, kept in the log.
 */
export function InitiativeActivities({ initiativeId, canAdd, canEdit, canDelete }: { initiativeId: string; canAdd: boolean; canEdit: boolean; canDelete: boolean }) {
  const key = queryKeys.initiativeActivities(initiativeId);
  const list = useQuery({ queryKey: key, queryFn: ({ signal }) => api.get<InitiativeActivity[]>(`/api/strategy/initiatives/${initiativeId}/activities`, { signal }) });
  const qc = useQueryClient();
  const [editing, setEditing] = useState<InitiativeActivity | "new" | null>(null);
  const [deleting, setDeleting] = useState<InitiativeActivity | null>(null);
  // The plan tree shows each initiative's activity count and progress, so it refreshes with the list.
  const refresh = () => Promise.all([qc.invalidateQueries({ queryKey: key }), qc.invalidateQueries({ queryKey: queryKeys.strategy })]);

  if (list.error) return <Alert type="error" showIcon message={errorText(list.error)} />;
  const rows = list.data ?? [];
  return (
    <>
      {canAdd && (
        <div className="mb">
          <Button icon={<PlusOutlined />} onClick={() => setEditing("new")}>{L("إضافة نشاط", "Add activity")}</Button>
        </div>
      )}
      {!list.isLoading && rows.length === 0 ? <EmptyNote>{L("لم تضف أنشطة لهذه المبادرة بعد.", "No activities yet.")}</EmptyNote> : (
        <Table<InitiativeActivity> rowKey="id" size="small" loading={list.isLoading} dataSource={rows} pagination={false} scroll={{ x: 720 }}
          columns={[
            {
              title: L("النشاط", "Activity"), key: "t", render: (_, a) => (
                <span><b>{a.titleAr}</b>{a.description && <><br /><small className="muted">{a.description}</small></>}</span>
              ),
            },
            { title: L("المسؤول", "Responsible"), key: "o", width: 160, render: (_, a) => a.ownerUnitName ?? <span className="muted">—</span> },
            {
              title: L("المدة", "Period"), key: "d", width: 190, render: (_, a) => a.startDate || a.endDate
                ? <span className="ltr-nums">{a.startDate ? formatDate(a.startDate) : "…"} – {a.endDate ? formatDate(a.endDate) : "…"}</span>
                : <span className="muted">—</span>,
            },
            { title: L("الحالة", "Status"), key: "s", width: 110, render: (_, a) => <Tag color={initiativeStatus[a.status].color}>{initiativeStatus[a.status].text}</Tag> },
            { title: L("الإنجاز", "Progress"), key: "p", width: 140, render: (_, a) => <Progress percent={a.progress} size="small" /> },
            ...(canEdit || canDelete ? [{
              title: "", key: "x", width: 90, render: (_: unknown, a: InitiativeActivity) => (
                <span className="nowrap">
                  {canEdit && <Tooltip title={L("تعديل النشاط", "Edit activity")}><Button type="text" size="small" icon={<EditOutlined />} aria-label={L("تعديل النشاط", "Edit activity")} onClick={() => setEditing(a)} /></Tooltip>}
                  {canDelete && <Tooltip title={L("حذف النشاط", "Delete activity")}><Button type="text" size="small" danger icon={<DeleteOutlined />} aria-label={L("حذف النشاط", "Delete activity")} onClick={() => setDeleting(a)} /></Tooltip>}
                </span>
              ),
            }] : []),
          ]} />
      )}
      {editing && <ActivityModal initiativeId={initiativeId} activity={editing === "new" ? null : editing} onClose={() => setEditing(null)} onSaved={refresh} />}
      {deleting && (
        <DecisionModal danger title={L(`حذف النشاط «${deleting.titleAr}»`, `Delete "${deleting.titleAr}"`)} field="reason" label={L("سبب الحذف", "Reason")} okText={L("حذف", "Delete")}
          explain={L("يحذف النشاط من المبادرة، ويبقى حذفه وسببه في السجل.", "The activity is removed; the log keeps the reason.")}
          onSubmit={async (reason) => { await api.post(`/api/strategy/activities/${deleting.id}/delete`, { reason }); await refresh(); }}
          onClose={() => setDeleting(null)} />
      )}
    </>
  );
}

function ActivityModal({ initiativeId, activity, onClose, onSaved }: { initiativeId: string; activity: InitiativeActivity | null; onClose: () => void; onSaved: () => Promise<unknown> }) {
  const [form] = Form.useForm<Fields>();
  const units = useUnits({ staleTime: 300_000 });
  const status = Form.useWatch("status", form);
  const save = useMutation({
    mutationFn: (f: Fields) => {
      const body = {
        titleAr: f.titleAr, description: f.description ?? null, ownerUnitId: f.ownerUnitId ?? null,
        startDate: f.dates?.[0]?.format("YYYY-MM-DD") ?? null, endDate: f.dates?.[1]?.format("YYYY-MM-DD") ?? null,
        status: f.status, progress: f.status === "Completed" ? 100 : f.progress, sortOrder: null, version: activity?.version ?? null,
      };
      return activity ? api.put(`/api/strategy/activities/${activity.id}`, body) : api.post(`/api/strategy/initiatives/${initiativeId}/activities`, body);
    },
    onSuccess: async () => { await onSaved(); onClose(); },
  });
  // The server's refusals (end before start, progress out of range) are marked under their fields (BE-6).
  const saveMarked = useFieldErrors(form, save.error);
  return (
    <Modal open title={activity ? L("تعديل النشاط", "Edit activity") : L("نشاط جديد", "New activity")} okText={L("حفظ", "Save")} cancelText={L("إلغاء", "Cancel")}
      onOk={() => form.submit()} onCancel={onClose} confirmLoading={save.isPending} destroyOnHidden>
      {save.error && !saveMarked && <Alert type="error" showIcon message={errorText(save.error)} className="mb" />}
      <Form form={form} layout="vertical" requiredMark="optional" onFinish={(f) => save.mutate(f)}
        initialValues={activity ? {
          titleAr: activity.titleAr, description: activity.description ?? undefined, ownerUnitId: activity.ownerUnitId ?? undefined,
          dates: [activity.startDate ? dayjs(activity.startDate) : null, activity.endDate ? dayjs(activity.endDate) : null], status: activity.status, progress: activity.progress,
        } : { status: "Planned", progress: 0 }}>
        <Form.Item name="titleAr" label={L("اسم النشاط", "Activity")} rules={[{ required: true, whitespace: true, message: L("اكتب اسم النشاط", "Enter the activity") }]}>
          <Input maxLength={300} autoFocus />
        </Form.Item>
        <Form.Item name="description" label={L("الوصف", "Description")}><Input.TextArea rows={2} maxLength={2000} /></Form.Item>
        <Form.Item name="ownerUnitId" label={L("الجهة المسؤولة", "Responsible unit")}>
          <Select allowClear showSearch optionFilterProp="label" loading={units.isLoading}
            options={(units.data ?? []).filter((u) => u.active).map((u) => ({ value: u.id, label: u.nameAr }))} />
        </Form.Item>
        <Form.Item name="dates" label={L("المدة", "Period")}
          rules={[{ validator: (_, v?: [Dayjs | null, Dayjs | null]) => v?.[0] && v?.[1] && v[1].isBefore(v[0], "day") ? Promise.reject(new Error(L("النهاية قبل البداية", "Ends before it starts"))) : Promise.resolve() }]}>
          <DatePicker.RangePicker allowEmpty={[true, true]} style={{ width: "100%" }} />
        </Form.Item>
        <div className="form-grid">
          <Form.Item name="status" label={L("الحالة", "Status")} rules={[{ required: true }]}>
            <Select options={(Object.keys(initiativeStatus) as InitiativeStatus[]).map((s) => ({ value: s, label: initiativeStatus[s].text }))} />
          </Form.Item>
          <Form.Item name="progress" label={L("نسبة الإنجاز %", "Progress %")} rules={[{ required: true, type: "number", min: 0, max: 100, message: L("من 0 إلى 100", "0 to 100") }]}
            extra={status === "Completed" ? L("النشاط المكتمل إنجازه 100%.", "A completed activity is 100%.") : undefined}>
            <InputNumber min={0} max={100} disabled={status === "Completed"} style={{ width: "100%" }} />
          </Form.Item>
        </div>
      </Form>
    </Modal>
  );
}
