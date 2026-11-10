import { useState } from "react";
import { Alert, Button, List, Tag, Upload } from "antd";
import { FileOutlined, LoadingOutlined, StopOutlined, UploadOutlined } from "@ant-design/icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, uploadFile } from "../../api/client";
import type { Doc } from "../../api/collection";
import { DecisionModal } from "../../shared/DecisionModal";
import { errorText, formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { fileSize } from "../../shared/labels";
import { usePermissions } from "../../shared/session";
import { Panel } from "../../shared/ui";

/** مستندات هيكل الكلية: صورة الهيكل المعتمد وقرارات التشكيل. لا حذف نهائيًا؛ الإبطال بسبب يبقيها في السجل. */
export function StructureDocs({ collegeId, editable }: { collegeId: string; editable: boolean }) {
  const perms = usePermissions();
  const qc = useQueryClient();
  const key = ["structure", "docs", collegeId];
  const docs = useQuery({ queryKey: key, queryFn: () => api.get<Doc[]>(`/api/structure/colleges/${collegeId}/documents`) });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [voiding, setVoiding] = useState<Doc | null>(null);
  const canAdd = editable && perms.can("org.structure", "A");
  const canVoid = editable && perms.can("org.structure", "D");

  const upload = (
    <Upload showUploadList={false} accept=".pdf,.png,.jpg,.jpeg,.docx"
      customRequest={async ({ file }) => {
        setBusy(true);
        setError(null);
        try {
          await uploadFile(`/api/structure/colleges/${collegeId}/documents`, file as File);
          await qc.invalidateQueries({ queryKey: key });
        } catch (e) {
          setError(errorText(e));
        } finally {
          setBusy(false);
        }
      }}>
      <Button icon={busy ? <LoadingOutlined /> : <UploadOutlined />} disabled={busy}>{L("رفع الهيكل المعتمد أو قرار التشكيل", "Upload")}</Button>
    </Upload>
  );

  return (
    <>
      <Panel title={L("المستندات المعتمدة", "Approved documents")} icon={<FileOutlined />} className="no-print" extra={canAdd && upload} query={docs}>
        {() => (
          <>
            {error && <Alert type="error" showIcon message={error} className="mb" />}
            <List size="small" dataSource={docs.data!}
              locale={{ emptyText: L("لم يرفع الهيكل المعتمد بعد (PDF أو صورة).", "No documents yet.") }}
              renderItem={(d) => (
                <List.Item actions={canVoid && !d.voided ? [<Button key="v" size="small" type="link" danger icon={<StopOutlined />} onClick={() => setVoiding(d)}>{L("إبطال", "Void")}</Button>] : undefined}>
                  <span>
                    {d.voided ? <s>{d.fileName}</s> : <a href={`/api/documents/${d.id}?inline=true`} target="_blank" rel="noreferrer">{d.fileName}</a>}
                    <small className="muted ltr"> {fileSize(d.sizeBytes)} · {formatDate(d.uploadedAt)}</small>
                    {d.voided && <Tag className="ms" title={d.voidReason ?? undefined}>{L("مبطل", "Voided")}</Tag>}
                  </span>
                </List.Item>
              )} />
          </>
        )}
      </Panel>
      {voiding && (
        <DecisionModal title={L(`إبطال ${voiding.fileName}`, `Void ${voiding.fileName}`)} field="reason" label={L("السبب", "Reason")} okText={L("إبطال", "Void")} danger
          explain={L("يبقى المستند في السجل مع سبب الإبطال، ولا يظهر كمستند ساري.", "The document stays on record with the reason.")}
          onSubmit={async (note) => {
            await api.post(`/api/documents/${voiding.id}/void`, { note });
            await qc.invalidateQueries({ queryKey: key });
          }}
          onClose={() => setVoiding(null)} />
      )}
    </>
  );
}
