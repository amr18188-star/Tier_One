import { useRef, useState } from "react";
import { Alert, Button, Checkbox, Input, InputNumber, Popover, Segmented, Tooltip, Upload } from "antd";
import {
  CheckCircleOutlined, CloseCircleFilled, ExclamationCircleOutlined, InfoCircleOutlined, LoadingOutlined, MinusCircleOutlined, PaperClipOutlined, QuestionCircleOutlined,
  StopOutlined, UploadOutlined, WarningFilled,
} from "@ant-design/icons";
import { useMutation } from "@tanstack/react-query";
import { api, uploadFile } from "../../api/client";
import type { Doc, FormItem, SaveValue, ValueKind } from "../../api/collection";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import { direction as directionText, fileSize, num, withUnit } from "../../shared/labels";

interface Draft { kind: ValueKind; value: number | null; numerator: number | null; denominator: number | null; reason: string; explanation: string }

function draftOf(item: FormItem): Draft {
  const v = item.value;
  return {
    kind: v?.kind ?? "Number", value: v?.value ?? null, numerator: v?.numerator ?? null, denominator: v?.denominator ?? null,
    reason: v?.reason ?? "", explanation: v?.explanation ?? "",
  };
}

function same(a: Draft, b: Draft) {
  return a.kind === b.kind && a.value === b.value && a.numerator === b.numerator && a.denominator === b.denominator
    && a.reason.trim() === b.reason.trim() && a.explanation.trim() === b.explanation.trim();
}

function empty(d: Draft) {
  return d.kind === "Number" && d.value === null && d.numerator === null && d.denominator === null;
}

export interface ReturnMark { checked: boolean; note: string }

/**
 * صف مؤشر في النموذج. الحفظ تلقائي عند مغادرة الصف (لا زر حفظ)، والتحقق من الخادم يظهر تحت الحقل مباشرة.
 * للنسب يدخل البسط والمقام وتحسب النسبة، و«غير متاح» أو «لا ينطبق» يطلب سببًا. المراجع يرى القيمة ويعلّم ما يعاد بملاحظة.
 */
export function ValueRow({ formId, item, editable, reviewing, mark, onMark, onSaved }: {
  formId: string; item: FormItem; editable: boolean; reviewing: boolean;
  mark?: ReturnMark; onMark?: (m: ReturnMark) => void; onSaved: (item: FormItem) => void;
}) {
  const saved = draftOf(item);
  const [draft, setDraft] = useState<Draft>(saved);
  const [error, setError] = useState<string | null>(null);
  const row = useRef<HTMLDivElement>(null);
  const pct = item.unit.trim() === "%";
  const weighted = item.aggregation === "WeightedAverage";

  const save = useMutation({
    mutationFn: (d: Draft) => {
      const body: SaveValue = {
        indicatorId: item.indicatorId, kind: d.kind,
        value: d.kind === "Number" && !item.usesRatio ? d.value : null,
        numerator: d.kind === "Number" && item.usesRatio ? d.numerator : null,
        denominator: d.kind === "Number" && (item.usesRatio || weighted) ? d.denominator : null,
        reason: d.kind === "Number" ? null : d.reason || null, explanation: d.explanation || null, version: item.value?.version ?? null,
      };
      return api.put<FormItem>(`/api/forms/${formId}/values`, body);
    },
    onSuccess: (next) => {
      setError(null);
      onSaved(next);
    },
    onError: (e) => setError(errorText(e)),
  });

  const commit = (d: Draft = draft) => {
    if (!editable || same(d, saved) || (empty(d) && !item.value)) return;
    if (item.usesRatio && d.kind === "Number" && (d.numerator === null || d.denominator === null)) return;
    if (d.kind !== "Number" && d.reason.trim().length < 5) return;
    save.mutate(d);
  };
  const update = (patch: Partial<Draft>) => setDraft((d) => ({ ...d, ...patch }));
  // يحفظ حين يغادر التركيز الصف كله، فيكمل المستخدم البسط ثم المقام دون حفظ نصف قيمة.
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
    <div ref={row} id={`i-${item.code}`} className={`f-row${flagged ? " flagged" : ""}${save.isPending ? " saving" : ""}`} onBlur={editable ? onBlur : undefined}>
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
            {L("المستهدف:", "Target:")} <b className="ltr">{withUnit(item.target, item.unit)}</b>
            {item.targetSource === "University" && <small className="muted"> ({L("مستهدف الجامعة؛ لا مستهدف خاص بالجهة", "university target")})</small>}
          </span>
          <span>{L("آخر قيمة معتمدة:", "Last approved:")} <b className="ltr">{withUnit(item.previous, item.unit)}</b></span>
        </div>
        {item.entryNote && <div className="t1-entry-note">{item.entryNote}</div>}
        {item.value?.reviewNote && (
          <Alert className="mt" type="error" showIcon message={L("ملاحظة المراجع", "Reviewer note")} description={item.value.reviewNote} />
        )}
      </div>

      <div className="f-input">
        {editable ? (
          <>
            <Segmented size="small" value={draft.kind}
              onChange={(k) => {
                const d = { ...draft, kind: k as ValueKind };
                setDraft(d);
              }}
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
              <Input value={draft.reason} onChange={(e) => update({ reason: e.target.value })} maxLength={500}
                placeholder={draft.kind === "NotAvailable" ? L("لماذا لا تتوفر القيمة؟", "Why is the value unavailable?") : L("لماذا لا ينطبق المؤشر على الجهة؟", "Why does it not apply?")}
                onPressEnter={() => commit()} />
            )}
            {(warnings.length > 0 || draft.explanation) && (
              <Input.TextArea rows={1} autoSize={{ minRows: 1, maxRows: 4 }} value={draft.explanation} onChange={(e) => update({ explanation: e.target.value })} maxLength={1000}
                placeholder={L("تفسير للمراجع (مطلوب عند الاختلاف الكبير عن العام السابق)", "Explanation for the reviewer")} />
            )}
          </>
        ) : (
          <ReadValue item={item} />
        )}

        {save.isPending && <span className="f-issue"><LoadingOutlined /> {L("جار الحفظ…", "Saving…")}</span>}
        {error && <span className="f-issue err" role="alert"><CloseCircleFilled /> {error}</span>}
        {!error && errors.map((i) => <span key={i.code} className="f-issue err"><CloseCircleFilled /> {i.messageAr}</span>)}
        {warnings.map((i) => <span key={i.code} className="f-issue warn"><WarningFilled /> {i.messageAr}</span>)}

        <Evidence formId={formId} item={item} editable={editable && !!item.value} docs={liveEvidence} onSaved={onSaved} />

        {reviewing && onMark && (
          <div className="f-review">
            <Checkbox checked={mark?.checked} onChange={(e) => onMark({ checked: e.target.checked, note: mark?.note ?? "" })}>
              {L("يحتاج تصحيحًا", "Needs correction")}
            </Checkbox>
            {mark?.checked && (
              <Input className="mt" value={mark.note} onChange={(e) => onMark({ checked: true, note: e.target.value })} maxLength={1000} autoFocus
                placeholder={L("ما المطلوب تصحيحه في هذا المؤشر؟", "What needs correcting?")} />
            )}
          </div>
        )}
      </div>
    </div>
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

function Evidence({ formId, item, editable, docs, onSaved }: { formId: string; item: FormItem; editable: boolean; docs: Doc[]; onSaved: (item: FormItem) => void }) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  if (!item.evidenceRequired && docs.length === 0 && !editable) return null;
  const refresh = async () => {
    const form = await api.get<{ sections: { items: FormItem[] }[] }>(`/api/forms/${formId}`);
    const fresh = form.sections.flatMap((s) => s.items).find((i) => i.indicatorId === item.indicatorId);
    if (fresh) onSaved(fresh);
  };
  return (
    <div className="f-ev">
      {docs.map((d) => (
        <a key={d.id} href={`/api/documents/${d.id}?inline=true`} target="_blank" rel="noreferrer" title={d.description ?? d.fileName}>
          <PaperClipOutlined /> {d.fileName} <small className="muted ltr">{fileSize(d.sizeBytes)}</small>
        </a>
      ))}
      {item.evidenceRequired && docs.length === 0 && <span className="need"><WarningFilled /> {L("الدليل مطلوب", "Evidence required")}</span>}
      {editable && (
        <Upload showUploadList={false} accept=".pdf,.png,.jpg,.jpeg,.docx,.xlsx,.pptx"
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
      )}
      {error && <span className="f-issue err"><CloseCircleFilled /> {error}</span>}
    </div>
  );
}
