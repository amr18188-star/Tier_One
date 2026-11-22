import { useId, useRef, useState } from "react";
import { App, Button, Checkbox, Input, InputNumber, Popover, Segmented, Tooltip, Upload } from "antd";
import {
  CheckCircleOutlined, CloseCircleFilled, ExclamationCircleOutlined, InfoCircleOutlined, LoadingOutlined, MinusCircleOutlined, PaperClipOutlined, QuestionCircleOutlined,
  StopOutlined, UploadOutlined, WarningFilled,
} from "@ant-design/icons";
import { api, uploadFile } from "../../api/client";
import type { Doc, FormItem, ValueKind } from "../../api/collection";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import { direction as directionText, fileSize, num, withUnit } from "../../shared/labels";
import { PendingTarget } from "../../shared/PendingTarget";
import { CollegeTarget, shownOf } from "../../shared/CollegeTarget";
import { DecisionModal } from "../../shared/DecisionModal";
import { NoteCard } from "./NoteCard";
import { incompleteReason, useValueAutosave, type Draft, type SaveError, type SaveState } from "./useValueAutosave";

export interface ReturnMark { checked: boolean; note: string }

/**
 * صف مؤشر في النموذج. الحفظ تلقائي أثناء الكتابة (بعد توقفها قليلًا، وفورًا عند مغادرة الصف أو Enter؛ useValueAutosave)،
 * وحالته بجانب الحقل، والتحقق من الخادم تحته مباشرة.
 * للنسب يدخل البسط والمقام وتحسب النسبة، و«غير متاح» أو «لا ينطبق» يطلب سببًا. المراجع يرى القيمة ويعلّم ما يعاد بملاحظة.
 */
export function ValueRow({ formId, item, editable, reviewing, mark, onMark, onSaved }: {
  formId: string; item: FormItem; editable: boolean; reviewing: boolean;
  mark?: ReturnMark; onMark?: (m: ReturnMark) => void; onSaved: (item: FormItem) => void;
}) {
  const auto = useValueAutosave({ formId, item, editable, onSaved });
  const draft = auto.draft;
  const uid = useId();
  const ids = { reason: `${uid}-reason`, explanation: `${uid}-explanation`, note: `${uid}-note`, issues: `${uid}-issues`, incomplete: `${uid}-incomplete` };
  // UX-3: a draft the server would refuse is not sent; say why instead of dropping it silently.
  const incomplete = editable ? incompleteReason(item, draft) : null;
  const row = useRef<HTMLDivElement>(null);
  const pct = item.unit.trim() === "%";
  const weighted = item.aggregation === "WeightedAverage";

  const commit = () => void auto.flush();
  const update = (patch: Partial<Draft>) => auto.change({ ...draft, ...patch });
  // يحفظ فورًا حين يغادر التركيز الصف كله؛ وأثناء الكتابة بعد توقفها. النسبة لا تحفظ قبل البسط والمقام معًا.
  const onBlur = (e: React.FocusEvent) => {
    if (!row.current?.contains(e.relatedTarget as Node | null)) commit();
  };

  const computed = item.usesRatio && draft.numerator !== null && draft.denominator && draft.denominator > 0
    ? (draft.numerator / draft.denominator) * (pct ? 100 : 1) : null;
  const errors = item.issues.filter((i) => i.severity === "Error");
  const warnings = item.issues.filter((i) => i.severity === "Warning");
  const liveEvidence = item.evidence.filter((e) => !e.voided);
  const flagged = !!item.value?.reviewNote || mark?.checked;

  const info = (
    <div style={{ maxWidth: 380 }}>
      {item.definition && <p><b>{L("التعريف:", "Definition:")}</b> {item.definition}</p>}
      {item.usesRatio && <p><b>{L("البسط:", "Numerator:")}</b> {item.numeratorLabel ?? "—"}<br /><b>{L("المقام:", "Denominator:")}</b> {item.denominatorLabel ?? "—"}</p>}
      {weighted && <p><b>{L("أساس الترجيح:", "Weight:")}</b> {item.denominatorLabel ?? "—"}</p>}
      {item.dataSource && <p><b>{L("مصدر البيانات:", "Source:")}</b> {item.dataSource}</p>}
      {item.evidenceRequired && <p><b>{L("الدليل المطلوب:", "Evidence:")}</b> {item.evidenceRequired}</p>}
      <p className="muted">{directionText[item.direction]}</p>
    </div>
  );

  return (
    <div ref={row} id={`i-${item.code}`} className={`f-row${flagged ? " flagged" : ""}`} onBlur={editable ? onBlur : undefined}>
      <div>
        <div className="f-name">
          <code className="ltr">{item.code}</code>
          <b>{item.name}</b>
          <Popover content={info} title={L("عن المؤشر", "About this indicator")} trigger="click">
            <Button type="text" size="small" icon={<InfoCircleOutlined />} aria-label={L("تعريف المؤشر", "Definition")} />
          </Popover>
          <RowStatus item={item} />
        </div>
        <div className="f-meta">
          <span>{L("الوحدة:", "Unit:")} <b>{item.unit}</b></span>
          <span>
            {L("المستهدف:", "Target:")} <CollegeTarget target={shownOf(item)} unit={item.unit} />
            {shownOf(item).source === "University" && <small className="muted"> ({L("مستهدف الجامعة؛ لا مستهدف خاص بالجهة", "university target")})</small>}
            <PendingTarget value={item.pendingTarget} year={item.pendingTargetYear} unit={item.unit} />
          </span>
          <span>{L("آخر قيمة معتمدة:", "Last approved:")} <b className="ltr">{withUnit(item.previous, item.unit)}</b></span>
          {item.approvedBefore && (
            <span className="t1-approved-before">
              {L("المعتمدة قبل إعادة الفتح:", "Approved before reopening:")}{" "}
              <b className="ltr">{item.approvedBefore.kind === "Number" ? withUnit(item.approvedBefore.value, item.unit) : L("غير متاح أو غير منطبق", "n/a")}</b>
            </span>
          )}
        </div>
        {item.entryNote && <div className="t1-entry-note">{item.entryNote}</div>}
        {item.value?.reviewNote && <NoteCard className="mt" notes={[item.value.reviewNote]} />}
      </div>

      <div className="f-input">
        {editable ? (
          <>
            <Segmented size="small" value={draft.kind}
              onChange={(k) => update({ kind: k as ValueKind })}
              options={[
                { value: "Number", label: <Tooltip title={L("رقم فعلي، والصفر قيمة صحيحة.", "An actual number; zero is valid.")}>{L("قيمة", "Value")}</Tooltip> },
                { value: "NotAvailable", label: <Tooltip title={L("المؤشر ينطبق لكن الرقم لم يتوفر بعد؛ اكتب السبب.", "Applies but the number is not available yet.")}>{L("غير متاح", "Not available")}</Tooltip> },
                { value: "NotApplicable", label: <Tooltip title={L("المؤشر لا يخص الجهة أصلًا (مثل دراسات عليا في جهة بلا برامج لها).", "Does not concern this unit.")}>{L("لا ينطبق", "Not applicable")}</Tooltip> },
              ]} />
            {draft.kind === "Number" ? (
              item.usesRatio ? (
                <div className="f-line">
                  <InputNumber min={0} value={draft.numerator} onChange={(v) => update({ numerator: v })} placeholder={L("البسط", "Numerator")}
                    aria-label={`${L("البسط", "Numerator")}: ${item.numeratorLabel ?? ""}`} onPressEnter={() => commit()} />
                  <span aria-hidden>÷</span>
                  <InputNumber min={0} value={draft.denominator} onChange={(v) => update({ denominator: v })} placeholder={L("المقام", "Denominator")}
                    aria-label={`${L("المقام", "Denominator")}: ${item.denominatorLabel ?? ""}`} onPressEnter={() => commit()} />
                  <span className="f-computed ltr">= {computed === null ? "—" : withUnit(Math.round(computed * 100) / 100, item.unit)}</span>
                </div>
              ) : (
                <div className="f-line">
                  <InputNumber min={0} max={pct ? 100 : undefined} value={draft.value} onChange={(v) => update({ value: v })}
                    addonAfter={item.unit} aria-label={item.name} onPressEnter={() => commit()} />
                  {weighted && (
                    <Tooltip title={L(`${item.denominatorLabel ?? "أساس الترجيح"}: يرجح به متوسط الجامعة، فلا تتساوى كلية صغيرة بكلية كبيرة. مطلوب.`,
                      "The weight used for the university average. Required.")}>
                      <InputNumber min={1} value={draft.denominator} onChange={(v) => update({ denominator: v })} placeholder={item.denominatorLabel ?? L("أساس الترجيح", "Weight")}
                        aria-label={item.denominatorLabel ?? L("أساس الترجيح", "Weight")} onPressEnter={() => commit()} style={{ minWidth: 170 }}
                        addonBefore={L("الوزن", "Weight")} />
                    </Tooltip>
                  )}
                </div>
              )
            ) : (
              <div className="f-field">
                {/* A11Y-2: a visible label, not a placeholder only; the save rule and the server issues are read with it. */}
                <label htmlFor={ids.reason} className="f-label">
                  {draft.kind === "NotAvailable" ? L("سبب عدم التوفر", "Why it is not available") : L("سبب عدم الانطباق", "Why it does not apply")}
                </label>
                <Input id={ids.reason} value={draft.reason} onChange={(e) => update({ reason: e.target.value })} maxLength={500}
                  placeholder={draft.kind === "NotAvailable" ? L("لماذا لا تتوفر القيمة؟", "Why is the value unavailable?") : L("لماذا لا ينطبق المؤشر على الجهة؟", "Why does it not apply?")}
                  aria-describedby={`${ids.incomplete} ${ids.issues}`} onPressEnter={() => commit()} />
              </div>
            )}
            {(warnings.length > 0 || draft.explanation) && (
              <div className="f-field">
                <label htmlFor={ids.explanation} className="f-label">{L("تفسير للمراجع", "Explanation for the reviewer")}</label>
                <Input.TextArea id={ids.explanation} rows={1} autoSize={{ minRows: 1, maxRows: 4 }} value={draft.explanation} onChange={(e) => update({ explanation: e.target.value })} maxLength={1000}
                  placeholder={L("مطلوب عند الاختلاف الكبير عن العام السابق", "Required when far from last year")} aria-describedby={ids.issues} />
              </div>
            )}
            {incomplete && <span id={ids.incomplete} className="f-issue warn" role="status"><WarningFilled aria-hidden /> {L("لم يحفظ بعد:", "Not saved yet:")} {incomplete}</span>}
          </>
        ) : (
          <ReadValue item={item} />
        )}

        {editable && <SaveStatus state={auto.state} error={auto.error} onRetry={auto.retry} onDiscard={auto.discard} />}
        <span id={ids.issues}>
          {!auto.error && errors.map((i) => <span key={i.code} className="f-issue err"><CloseCircleFilled aria-hidden /> {i.messageAr}</span>)}
          {warnings.map((i) => <span key={i.code} className="f-issue warn"><WarningFilled aria-hidden /> {i.messageAr}</span>)}
        </span>

        <Evidence formId={formId} item={item} editable={editable} hasValue={!!item.value} docs={liveEvidence} onSaved={onSaved} />

        {reviewing && onMark && (
          <div className="f-review">
            <Checkbox checked={mark?.checked} onChange={(e) => onMark({ checked: e.target.checked, note: mark?.note ?? "" })}>
              {L("يحتاج تصحيحًا", "Needs correction")}
            </Checkbox>
            {mark?.checked && (
              <div className="f-field mt">
                <label htmlFor={ids.note} className="f-label">{L("ما المطلوب تصحيحه", "What needs correcting")}</label>
                <Input id={ids.note} value={mark.note} onChange={(e) => onMark({ checked: true, note: e.target.value })} maxLength={1000} autoFocus
                  placeholder={L("ما المطلوب تصحيحه في هذا المؤشر؟", "What needs correcting?")} />
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

/**
 * حالة الحفظ بجانب الحقل، في منطقة aria-live واحدة: جارٍ الحفظ، ثم «تم الحفظ» تختفي بعد ثانيتين، أو الخطأ بزر إعادة المحاولة.
 * تعارض الإصدار يشرح ما حدث ويخير بين حفظ القيمة فوق الأحدث والعودة إليها.
 */
function SaveStatus({ state, error, onRetry, onDiscard }: { state: SaveState; error: SaveError | null; onRetry: () => void; onDiscard: () => void }) {
  return (
    <span className="fp-save" data-state={state} role="status" aria-live="polite">
      {state === "saving" && <><LoadingOutlined aria-hidden /> {L("جارٍ الحفظ…", "Saving…")}</>}
      {state === "saved" && <><CheckCircleOutlined aria-hidden /> {L("تم الحفظ ✓", "Saved ✓")}</>}
      {state === "error" && error && (
        <span className="fp-save-err">
          <span><CloseCircleFilled aria-hidden /> {L("لم يحفظ:", "Not saved:")} {error.text}</span>
          <span className="fp-save-actions">
            <Button size="small" onClick={onRetry}>{error.conflict ? L("احفظ قيمتي فوقها", "Save mine over it") : L("أعد المحاولة", "Retry")}</Button>
            {error.conflict && <Button size="small" type="link" onClick={onDiscard}>{L("اعرض القيمة الأحدث", "Show the latest value")}</Button>}
          </span>
        </span>
      )}
    </span>
  );
}

/** وقت الحفظ بتوقيت القاهرة (ساعة ودقيقة). */
export function savedAt(iso: string) {
  return new Intl.DateTimeFormat("en-GB", { timeZone: "Africa/Cairo", hour: "2-digit", minute: "2-digit" }).format(new Date(iso));
}

/**
 * حالة المؤشر في النموذج بنص ورمز لا باللون وحده، فلا يلتبس الصفر بغير المتاح ولا غير المنطبق بما لم يدخل:
 * لم يدخل بعد، أو صفر، أو غير متاح، أو لا ينطبق، أو مدخل؛ ومعها وقت آخر حفظ.
 */
function RowStatus({ item }: { item: FormItem }) {
  const v = item.value;
  if (!v) return <span className="f-state none"><MinusCircleOutlined aria-hidden /> {L("لم يُدخل بعد", "Not entered")}</span>;
  const zero = v.kind === "Number" && (item.usesRatio ? v.numerator === 0 : v.value === 0);
  const [cls, icon, text] = v.kind === "NotAvailable" ? ["na", <QuestionCircleOutlined key="i" aria-hidden />, L("غير متاح", "Not available")]
    : v.kind === "NotApplicable" ? ["napp", <StopOutlined key="i" aria-hidden />, L("لا ينطبق", "Not applicable")]
    : zero ? ["zero", <CheckCircleOutlined key="i" aria-hidden />, L("صفر (قيمة مدخلة)", "Zero (entered)")]
    : ["ok", <CheckCircleOutlined key="i" aria-hidden />, L("مُدخل", "Entered")];
  return (
    <span className={`f-state ${cls}`}>
      {icon} {text}
      <small> · {L("حُفظ", "saved")} <bdi className="ltr">{savedAt(v.editedAt)}</bdi></small>
    </span>
  );
}

function ReadValue({ item }: { item: FormItem }) {
  const v = item.value;
  if (!v) return <span className="muted"><ExclamationCircleOutlined /> {L("لم تدخل قيمة", "No value")}</span>;
  if (v.kind !== "Number")
    return <span><b>{v.kind === "NotAvailable" ? L("غير متاح", "Not available") : L("لا ينطبق", "Not applicable")}</b><br /><small className="muted">{v.reason}</small></span>;
  return (
    <span>
      <span className="f-readonly ltr">{withUnit(v.value, item.unit)}</span>
      {item.usesRatio && <small className="muted ltr"> ({num(v.numerator)} ÷ {num(v.denominator)})</small>}
      {item.aggregation === "WeightedAverage" && v.denominator !== null && <small className="muted"> ({L("الوزن", "weight")} <span className="ltr">{num(v.denominator)}</span>)</small>}
      {v.explanation && <><br /><small className="muted">{L("تفسير:", "Note:")} {v.explanation}</small></>}
    </span>
  );
}

/** Evidence types and size the server accepts (DocumentsService.Allowed, Files:MaxMb). */
const EVIDENCE_TYPES = [".pdf", ".png", ".jpg", ".jpeg", ".docx", ".xlsx", ".pptx"];
const EVIDENCE_MAX_MB = 20;

/**
 * UX-2: evidence of a value. The types and size limit are shown and checked before uploading; a wrong file is voided with a
 * reason (it stays in the log); before a value exists the button says why it waits; upload errors are announced.
 */
function Evidence({ formId, item, editable, hasValue, docs, onSaved }: {
  formId: string; item: FormItem; editable: boolean; hasValue: boolean; docs: Doc[]; onSaved: (item: FormItem) => void;
}) {
  const { message } = App.useApp();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [voiding, setVoiding] = useState<Doc | null>(null);
  if (!item.evidenceRequired && docs.length === 0 && !editable) return null;
  const refresh = async () => {
    const form = await api.get<{ sections: { items: FormItem[] }[] }>(`/api/forms/${formId}`);
    const fresh = form.sections.flatMap((s) => s.items).find((i) => i.indicatorId === item.indicatorId);
    if (fresh) onSaved(fresh);
  };
  const check = (file: File): string | null => {
    const ext = file.name.slice(file.name.lastIndexOf(".")).toLowerCase();
    if (!EVIDENCE_TYPES.includes(ext)) return L("نوع الملف غير مقبول. المقبول: PDF وصور PNG/JPG وWord وExcel وPowerPoint.", "File type not accepted: PDF, PNG/JPG, Word, Excel or PowerPoint.");
    if (file.size > EVIDENCE_MAX_MB * 1024 * 1024) return L(`الملف أكبر من ${EVIDENCE_MAX_MB} ميجابايت.`, `The file is larger than ${EVIDENCE_MAX_MB} MB.`);
    return null;
  };
  const hint = hasValue
    ? L(`PDF أو صورة أو Word/Excel/PowerPoint، حتى ${EVIDENCE_MAX_MB} ميجابايت.`, `PDF, image, Word, Excel or PowerPoint, up to ${EVIDENCE_MAX_MB} MB.`)
    : L("أدخل القيمة أولًا ثم أرفق الدليل.", "Enter the value first, then attach the evidence.");
  return (
    <div className="f-ev">
      {docs.map((d) => (
        <span key={d.id} className="f-ev-doc">
          <a href={`/api/documents/${d.id}?inline=true`} target="_blank" rel="noreferrer" title={d.description ?? d.fileName}>
            <PaperClipOutlined aria-hidden /> {d.fileName} <small className="muted ltr">{fileSize(d.sizeBytes)}</small>
          </a>
          {editable && (
            <Button size="small" type="link" danger onClick={() => setVoiding(d)} aria-label={L(`إبطال الدليل ${d.fileName}`, `Void evidence ${d.fileName}`)}>
              {L("إبطال", "Void")}
            </Button>
          )}
        </span>
      ))}
      {item.evidenceRequired && docs.length === 0 && <span className="need"><WarningFilled aria-hidden /> {L("الدليل مطلوب", "Evidence required")}</span>}
      {editable && (hasValue ? (
        <Upload showUploadList={false} accept={EVIDENCE_TYPES.join(",")}
          beforeUpload={(file) => {
            const problem = check(file as File);
            setError(problem);
            return problem ? Upload.LIST_IGNORE : true;
          }}
          customRequest={async ({ file }) => {
            setBusy(true);
            setError(null);
            try {
              await uploadFile(`/api/forms/${formId}/values/${item.indicatorId}/evidence`, file as File);
              await refresh();
            } catch (e) {
              setError(errorText(e));
            } finally {
              setBusy(false);
            }
          }}>
          <Button size="small" icon={busy ? <LoadingOutlined /> : <UploadOutlined />} disabled={busy}>{L("إرفاق دليل", "Attach evidence")}</Button>
        </Upload>
      ) : (
        <Button size="small" icon={<UploadOutlined />} disabled>{L("إرفاق دليل", "Attach evidence")}</Button>
      ))}
      {editable && <small className="muted f-ev-hint">{hint}</small>}
      {error && <span className="f-issue err" role="alert"><CloseCircleFilled aria-hidden /> {error}</span>}
      {voiding && (
        <DecisionModal title={L("إبطال دليل", "Void evidence")} field="note" label={L("سبب الإبطال", "Reason")} okText={L("إبطال", "Void")} danger
          explain={L(`يبطل «${voiding.fileName}» فلا يراه المراجع دليلًا، ويبقى في سجل النموذج بسببه. ارفع الملف الصحيح بعده.`,
            `"${voiding.fileName}" is voided: reviewers no longer see it, and it stays in the form's log with the reason.`)}
          onSubmit={async (note) => {
            await api.post(`/api/documents/${voiding.id}/void`, { note });
            await refresh();
            message.success(L("أبطل الدليل.", "Evidence voided."));
          }}
          onClose={() => setVoiding(null)} />
      )}
    </div>
  );
}
