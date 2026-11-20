import { useRef, useState } from "react";
import { Alert, App, Button, Empty, Input, InputNumber, Modal, Select, Space, Tag, Upload } from "antd";
import { CompressOutlined, ExpandOutlined, FilePdfOutlined, LeftOutlined, PartitionOutlined, RightOutlined, StopOutlined, UploadOutlined } from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { api, uploadFile } from "../../api/client";
import { errorText, formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { usePermissions } from "../../shared/session";
import { PageHeader, Panel } from "../../shared/ui";
import "./plan-document.css";
import { queryKeys } from "../../api/queries";

interface DocDto { id: string; fileName: string; contentType: string; sizeBytes: number; uploadedAt: string; description: string | null; voided: boolean; voidReason: string | null }
interface PlanDoc { document: DocDto; uploadedBy: string | null }

/**
 * وثيقة الخطة الاستراتيجية للجامعة: تعرض داخل المنصة لكل أصحاب الحسابات (اطلاع فقط، بلا شريط تنزيل أو طباعة).
 * يرفع مدير الخطة إصداراتها، والأحدث هو المعروض؛ والإصدار القديم يبطل بسبب ولا يحذف.
 */
export default function PlanDocumentPage() {
  const perms = usePermissions();
  const qc = useQueryClient();
  const { message } = App.useApp();
  const docs = useQuery({ queryKey: queryKeys.planDocuments, queryFn: () => api.get<PlanDoc[]>("/api/strategy/documents") });
  const current = (docs.data ?? []).filter((d) => !d.document.voided);
  const [picked, setPicked] = useState<string | null>(null);
  const shown = current.find((d) => d.document.id === picked) ?? current[0];
  const [page, setPage] = useState(1);
  const [full, setFull] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [voiding, setVoiding] = useState<DocDto | null>(null);
  const frame = useRef<HTMLDivElement>(null);

  const go = (p: number) => setPage(Math.max(1, p));
  const toggleFull = async () => {
    if (!document.fullscreenElement) { await frame.current?.requestFullscreen?.(); setFull(true); }
    else { await document.exitFullscreen(); setFull(false); }
  };

  return (
    <>
      <PageHeader title={L("وثيقة الخطة الاستراتيجية", "Strategic plan document")}
        subtitle={L("الخطة الاستراتيجية المعتمدة للجامعة بتفاصيلها، أعدتها وحدة التخطيط الاستراتيجي، للاطلاع من داخل المنصة.",
          "The university's approved strategic plan, prepared by the Strategic Planning Unit, for viewing in the platform.")}
        back={{ to: "/strategy", label: L("شجرة الخطة", "Plan tree") }}
        extra={<>
          <Link to="/strategy"><Button icon={<PartitionOutlined />}>{L("الركائز والمؤشرات", "Pillars and indicators")}</Button></Link>
          {perms.can("kpi.strategy", "A") && (
            <Button type="primary" icon={<UploadOutlined />} onClick={() => setUploading(true)}>{L("رفع إصدار", "Upload a version")}</Button>
          )}
        </>} />
      <Panel query={docs}>
        {() => !shown ? (
          <Empty image={<FilePdfOutlined className="pd-empty" />} description={L("لم ترفع وثيقة الخطة بعد.", "The plan document has not been uploaded yet.")} />
        ) : (
          <>
            <div className="pd-bar">
              <span className="pd-title">
                <FilePdfOutlined /> <b>{shown.document.description || shown.document.fileName.replace(/\.pdf$/i, "")}</b>
                <span className="muted">{L("رفعت", "Uploaded")} {formatDate(shown.document.uploadedAt)}{shown.uploadedBy ? ` · ${shown.uploadedBy}` : ""}</span>
                {current.length > 1 && (
                  <Select size="small" value={shown.document.id} onChange={(v) => { setPicked(v); setPage(1); }} className="pd-versions"
                    options={current.map((d, i) => ({ value: d.document.id, label: `${i === 0 ? L("الإصدار الحالي", "Current") : L("إصدار سابق", "Earlier")} · ${formatDate(d.document.uploadedAt)}` }))} />
                )}
              </span>
              <Space size={6} wrap>
                <Button size="small" icon={<RightOutlined />} onClick={() => go(page - 1)} disabled={page <= 1} aria-label={L("الصفحة السابقة", "Previous page")} />
                <span className="pd-page">
                  {L("الصفحة", "Page")} <InputNumber size="small" min={1} value={page} onChange={(v) => go(Number(v) || 1)} />
                </span>
                <Button size="small" icon={<LeftOutlined />} onClick={() => go(page + 1)} aria-label={L("الصفحة التالية", "Next page")} />
                <Button size="small" icon={full ? <CompressOutlined /> : <ExpandOutlined />} onClick={toggleFull}>{full ? L("خروج", "Exit") : L("ملء الشاشة", "Full screen")}</Button>
                {perms.can("kpi.strategy", "D") && (
                  <Button size="small" danger icon={<StopOutlined />} onClick={() => setVoiding(shown.document)}>{L("إبطال الإصدار", "Void")}</Button>
                )}
              </Space>
            </div>
            <div ref={frame} className="pd-frame">
              {/* شريط أدوات العارض مخفي: العرض للاطلاع، والتنقل من أزرار الصفحة. */}
              <iframe key={`${shown.document.id}-${page}`} title={L("وثيقة الخطة الاستراتيجية", "Strategic plan document")}
                src={`/api/strategy/documents/${shown.document.id}/view#page=${page}&toolbar=0&navpanes=0&view=FitH`} />
            </div>
          </>
        )}
      </Panel>
      {perms.can("kpi.strategy", "D") && (docs.data ?? []).some((d) => d.document.voided) && (
        <Panel title={L("إصدارات مبطلة", "Voided versions")}>
          <ul className="pd-voided">
            {(docs.data ?? []).filter((d) => d.document.voided).map((d) => (
              <li key={d.document.id}><Tag>{formatDate(d.document.uploadedAt)}</Tag> {d.document.fileName} <span className="muted">— {d.document.voidReason}</span></li>
            ))}
          </ul>
        </Panel>
      )}
      {uploading && <UploadPlan onClose={() => setUploading(false)} onDone={async () => {
        setUploading(false); setPicked(null); setPage(1);
        await qc.invalidateQueries({ queryKey: queryKeys.planDocuments });
        message.success(L("رفعت الوثيقة، وصارت الإصدار المعروض.", "Uploaded."));
      }} />}
      {voiding && <VoidPlan doc={voiding} onClose={() => setVoiding(null)} onDone={async () => {
        setVoiding(null); setPicked(null); setPage(1);
        await qc.invalidateQueries({ queryKey: queryKeys.planDocuments });
      }} />}
    </>
  );
}

function UploadPlan({ onClose, onDone }: { onClose: () => void; onDone: () => Promise<void> }) {
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState("");
  const save = useMutation({
    mutationFn: () => uploadFile("/api/strategy/documents", file!, title.trim() ? { description: title.trim() } : undefined),
    onSuccess: onDone,
  });
  return (
    <Modal open title={L("رفع وثيقة الخطة الاستراتيجية", "Upload the strategic plan")} okText={L("رفع", "Upload")} cancelText={L("إلغاء", "Cancel")}
      okButtonProps={{ disabled: !file }} confirmLoading={save.isPending} onOk={() => save.mutate()} onCancel={onClose} destroyOnHidden>
      <Alert type="info" showIcon className="mb" message={L("ملف PDF حتى 60 ميجابايت. يصير الإصدار المعروض لكل أصحاب الحسابات، ويبقى السابق في قائمة الإصدارات.",
        "A PDF up to 60 MB. It becomes the version everyone sees.")} />
      {save.error && <Alert type="error" showIcon className="mb" message={errorText(save.error)} />}
      <Upload.Dragger accept=".pdf,application/pdf" maxCount={1} beforeUpload={(f) => { setFile(f); if (!title) setTitle(f.name.replace(/\.pdf$/i, "")); return false; }}
        onRemove={() => setFile(null)} fileList={file ? [{ uid: "1", name: file.name, status: "done" }] : []}>
        <p className="ant-upload-drag-icon"><FilePdfOutlined /></p>
        <p>{L("اسحب ملف الخطة هنا أو اضغط للاختيار", "Drop the plan PDF here or click to choose")}</p>
      </Upload.Dragger>
      <label className="pd-label">{L("العنوان المعروض", "Display title")}</label>
      <Input value={title} maxLength={300} onChange={(e) => setTitle(e.target.value)} placeholder={L("الخطة الاستراتيجية لجامعة العاصمة 2026–2030", "Strategic plan")} aria-label={L("الخطة الاستراتيجية لجامعة العاصمة 2026–2030", "Strategic plan")} />
    </Modal>
  );
}

function VoidPlan({ doc, onClose, onDone }: { doc: DocDto; onClose: () => void; onDone: () => Promise<void> }) {
  const [reason, setReason] = useState("");
  const save = useMutation({ mutationFn: () => api.post(`/api/documents/${doc.id}/void`, { note: reason }), onSuccess: onDone });
  return (
    <Modal open title={L("إبطال هذا الإصدار", "Void this version")} okText={L("إبطال", "Void")} okButtonProps={{ danger: true, disabled: reason.trim().length < 5 }}
      cancelText={L("إلغاء", "Cancel")} confirmLoading={save.isPending} onOk={() => save.mutate()} onCancel={onClose} destroyOnHidden>
      <Alert type="warning" showIcon className="mb" message={L("يختفي الإصدار عن أصحاب الحسابات ويبقى في السجل؛ يعرض الإصدار السابق إن وجد.", "The version is hidden but kept in the record.")} />
      {save.error && <Alert type="error" showIcon className="mb" message={errorText(save.error)} />}
      <Input.TextArea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} placeholder={L("السبب (مثل: صدر إصدار معدل بقرار مجلس الجامعة)", "Reason")} aria-label={L("السبب (مثل: صدر إصدار معدل بقرار مجلس الجامعة)", "Reason")} />
    </Modal>
  );
}
