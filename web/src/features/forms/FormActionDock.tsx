import { Button, Popover, Tooltip } from "antd";
import { AimOutlined, CheckOutlined, LoadingOutlined, RollbackOutlined, SendOutlined, UnlockOutlined, WarningOutlined } from "@ant-design/icons";
import type { Form, FormItem } from "../../api/collection";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import type { FormDialog } from "./FormDialogs";
import { PillarMobileBar } from "./FormPillars";
import type { FormSubmit } from "./useFormPage";
import { savedAt } from "./ValueRow";

/** What the form page decided the current user may do, for the action bar. */
export interface FormRights {
  editable: boolean;
  reviewing: boolean;
  canReturn: boolean;
  canApprove: boolean;
  directApproval: boolean;
  directLabel: string;
  canDecideReopen: boolean;
}

/**
 * The bar fixed at the foot of the form: the mobile pillar bar, then the actions of whoever holds the form now (entry progress
 * and send; approve, approve directly or return; reopen, or decide a reopen request).
 */
export function FormActionDock({ f, rights, pillarCount, position, onPillar, missing, saving, lastSaved, flaggedCount, submit, onNextMissing, onSend, onDialog }: {
  f: Form;
  rights: FormRights;
  pillarCount: number;
  position: number;
  onPillar: (to: number) => void;
  missing: FormItem[];
  saving: number;
  lastSaved: string | null;
  flaggedCount: number;
  submit: FormSubmit;
  onNextMissing: () => void;
  onSend: () => void;
  onDialog: (d: FormDialog) => void;
}) {
  const { editable, reviewing, canReturn, canApprove, directApproval, directLabel, canDecideReopen } = rights;
  if (!(pillarCount > 1 || editable || reviewing || f.actions.canReopen || canDecideReopen)) return null;
  return (
      <div className="fp-dock no-print">
      {pillarCount > 1 && (
        <PillarMobileBar position={position} total={pillarCount} hasPrev={position > 0} hasNext={position < pillarCount - 1}
          onPrev={() => onPillar(position - 1)} onNext={() => onPillar(position + 1)} />
      )}
      {(editable || reviewing || f.actions.canReopen || canDecideReopen) && (
        <div className="f-actions">
          <span className="grow">
            {editable && (
              <span className="f-summary" aria-live="polite">
                <span>{L("أدخلت", "Entered")} <b className="ltr">{f.completed}/{f.required}</b></span>
                {missing.length > 0 && <span>{L("ناقص", "Missing")} <b className="ltr">{missing.length}</b></span>}
                {saving > 0 ? <span><LoadingOutlined aria-hidden /> {L("جار الحفظ…", "Saving…")}</span>
                  : lastSaved && <span>{L("آخر حفظ", "Last saved")} <b className="ltr">{savedAt(lastSaved)}</b></span>}
                {f.blocking.length === 0
                  ? <span className="f-issue" style={{ color: "var(--ok)" }}><CheckOutlined /> {L("مكتمل وجاهز للإرسال", "Complete and ready")}</span>
                  : <Popover title={L("ما يمنع الإرسال", "What blocks sending")} content={<ul style={{ maxWidth: 420, maxHeight: 300, overflow: "auto" }}>{f.blocking.map((b) => <li key={b}>{b}</li>)}</ul>}>
                      <Button type="link" size="small" className="f-issue warn" style={{ padding: 0 }}><WarningOutlined /> {L(`${f.blocking.length} أمر يمنع الإرسال`, `${f.blocking.length} items block sending`)}</Button>
                    </Popover>}
              </span>
            )}
            {canReturn && (flaggedCount > 0
              ? L(`علّمت ${flaggedCount} مؤشرًا للتصحيح.`, `${flaggedCount} marked for correction.`)
              : L("راجع القيم والأدلة، ثم اعتمد أو أعد.", "Review values and evidence, then approve or return."))}
          </span>
          {submit.error && <span className="f-issue err">{errorText(submit.error)}</span>}
          {editable && missing.length > 0 && (
            <Button size="large" icon={<AimOutlined />} onClick={onNextMissing}>{L("التالي الناقص", "Next missing")}</Button>
          )}
          {editable && (
            <Tooltip title={!f.actions.canSubmit ? L("يتفعل حين يكتمل النموذج؛ اعرض «ما يمنع الإرسال».", "Enabled once the form is complete.") : undefined}>
              <Button type="primary" size="large" icon={<SendOutlined />} disabled={!f.actions.canSubmit} loading={submit.isPending} onClick={onSend}>
                {L("إرسال للمراجعة", "Send for review")}
              </Button>
            </Tooltip>
          )}
          {reviewing && (
            <>
              {canReturn && <Button danger size="large" icon={<RollbackOutlined />} onClick={() => onDialog("return")}>{L("إعادة للتصحيح", "Return")}</Button>}
              {directApproval && (
                <Button type={canApprove ? "default" : "primary"} size="large" icon={<CheckOutlined />} disabled={flaggedCount > 0} onClick={() => onDialog("direct")}>
                  {directLabel}
                </Button>
              )}
              {canApprove && (
                <Button type="primary" size="large" icon={<CheckOutlined />} disabled={flaggedCount > 0} onClick={() => onDialog("approve")}>
                  {f.nextStepName ? L("اعتماد وإحالة", "Approve") : L("اعتماد نهائي", "Final approval")}
                </Button>
              )}
            </>
          )}
          {f.actions.canReopen && <Button icon={<UnlockOutlined />} onClick={() => onDialog("reopen")}>{L("طلب إعادة فتح للتصحيح", "Request reopening")}</Button>}
          {canDecideReopen && (
            <>
              <Button danger size="large" onClick={() => onDialog("reopenReject")}>{L("رفض طلب إعادة الفتح", "Reject the request")}</Button>
              <Button type="primary" size="large" icon={<UnlockOutlined />} onClick={() => onDialog("reopenApprove")}>{L("اعتماد إعادة الفتح", "Approve reopening")}</Button>
            </>
          )}
        </div>
      )}
      </div>
  );
}
