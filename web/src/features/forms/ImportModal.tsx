import { useState } from "react";
import { Alert, App, Button, Modal, Table, Tag, Upload } from "antd";
import { DownloadOutlined, InboxOutlined } from "@ant-design/icons";
import { useMutation } from "@tanstack/react-query";
import { uploadFile } from "../../api/client";
import type { ImportPreview, ImportRow } from "../../api/collection";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import { mergeSizes, Pager, usePaged } from "../../shared/Pager";

/** صفوف المعاينة في الصفحة الواحدة؛ يظهر الترقيم متى زادت عنها. */
const IMPORT_PAGE = 10;

const STATUS: Record<ImportRow["status"], { text: string; color: string }> = {
  new: { text: "قيمة جديدة", color: "processing" },
  changed: { text: "تعديل", color: "gold" },
  same: { text: "دون تغيير", color: "default" },
  skip: { text: "فارغ (يتجاهل)", color: "default" },
  error: { text: "خطأ", color: "error" },
};

/**
 * الإدخال من Excel على خطوتين: معاينة كل صف بقواعد الإدخال نفسها، ثم تطبيق كامل أو لا شيء.
 * القالب ينزل من النموذج نفسه فيحمل مؤشراته فقط وقيمه الحالية.
 */
export function ImportModal({ formId, onClose, onApplied }: { formId: string; onClose: () => void; onApplied: () => void }) {
  const { message } = App.useApp();
  const [file, setFile] = useState<File | null>(null);
  const preview = useMutation({ mutationFn: (f: File) => uploadFile<ImportPreview>(`/api/forms/${formId}/import`, f) });
  const apply = useMutation({
    mutationFn: () => uploadFile<ImportPreview>(`/api/forms/${formId}/import?apply=true`, file!),
    onSuccess: (p) => {
      message.success(L(`طبق ${p.changes} تغييرًا من الملف`, `${p.changes} changes applied`));
      onApplied();
      onClose();
    },
  });
  const p = preview.data;
  const shown = p?.items.filter((i) => i.status !== "skip" && i.status !== "same") ?? [];
  const paged = usePaged(shown, IMPORT_PAGE);

  return (
    <Modal open width={860} title={L("إدخال القيم من Excel", "Import values from Excel")} onCancel={onClose}
      footer={[
        <Button key="t" icon={<DownloadOutlined />} href={`/api/forms/${formId}/template.xlsx`}>{L("تنزيل القالب", "Template")}</Button>,
        <Button key="c" onClick={onClose}>{L("إلغاء", "Cancel")}</Button>,
        <Button key="a" type="primary" disabled={!p?.canApply} loading={apply.isPending} onClick={() => apply.mutate()}>
          {p?.canApply ? L(`تطبيق ${p.changes} تغيير`, `Apply ${p.changes} changes`) : L("تطبيق", "Apply")}
        </Button>,
      ]}>
      <ol className="t1-help">
        <li>{L("نزّل القالب: فيه مؤشرات هذا النموذج وقيمها الحالية.", "Download the template with this form's indicators.")}</li>
        <li>{L("املأ أعمدة «القيمة» أو «البسط» و«المقام»، أو اكتب في «الحالة»: غير متاح / لا ينطبق مع السبب.", "Fill value, or numerator and denominator.")}</li>
        <li>{L("ارفع الملف لمعاينته؛ لا يحفظ شيء حتى تضغط «تطبيق».", "Upload to preview; nothing is saved until you apply.")}</li>
      </ol>
      <Upload.Dragger accept=".xlsx" maxCount={1} showUploadList={false}
        customRequest={({ file: f, onSuccess }) => {
          setFile(f as File);
          preview.mutate(f as File);
          onSuccess?.(null);
        }}>
        <p className="ant-upload-drag-icon"><InboxOutlined /></p>
        <p>{file ? file.name : L("اسحب ملف Excel هنا أو اضغط للاختيار", "Drop the Excel file here or click")}</p>
      </Upload.Dragger>
      {preview.isPending && <p className="mt">{L("جار فحص الملف…", "Checking…")}</p>}
      {preview.error && <Alert className="mt" type="error" showIcon message={errorText(preview.error)} />}
      {apply.error && <Alert className="mt" type="error" showIcon message={errorText(apply.error)} />}
      {p && (
        <>
          <Alert className="mt" showIcon type={p.errors > 0 ? "error" : p.changes > 0 ? "success" : "info"}
            message={p.errors > 0
              ? L(`${p.errors} صف به أخطاء؛ صححها في الملف وأعد رفعه. لا يطبق شيء والملف به أخطاء.`, `${p.errors} rows have errors; nothing will be applied.`)
              : p.changes > 0 ? L(`${p.changes} تغيير جاهز للتطبيق${p.warnings ? `، و${p.warnings} تحذير يحتاج تفسيرًا قبل الإرسال` : ""}.`, `${p.changes} changes ready.`)
                : L("لا تغييرات عن القيم المحفوظة.", "No changes.")} />
          {shown.length > 0 && (
            <Table className="mt" size="small" rowKey="row" dataSource={paged.rows} pagination={false}
              columns={[
                { title: L("السطر", "Row"), dataIndex: "row", width: 60 },
                { title: L("المؤشر", "Indicator"), key: "i", render: (_, r) => <span><code className="ltr">{r.code}</code> {r.name}</span> },
                { title: L("القيمة", "Value"), dataIndex: "display", width: 120, render: (v: string | null) => <span className="ltr">{v ?? "—"}</span> },
                { title: L("النتيجة", "Result"), key: "s", width: 260, render: (_, r) => (
                  <>
                    <Tag color={STATUS[r.status].color}>{STATUS[r.status].text}</Tag>
                    {r.issues.map((i) => <div key={i.code} className={`f-issue ${i.severity === "Error" ? "err" : "warn"}`}>{i.messageAr}</div>)}
                  </>
                ) },
              ]} />
          )}
          {shown.length > IMPORT_PAGE && <Pager {...paged.pager} sizes={mergeSizes([IMPORT_PAGE])} />}
        </>
      )}
    </Modal>
  );
}
