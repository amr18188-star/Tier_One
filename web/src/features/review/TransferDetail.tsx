import { useState } from "react";
import { Alert, App, Button, Input, InputNumber, Tag } from "antd";
import { CheckCircleOutlined, SendOutlined, SwapOutlined } from "@ant-design/icons";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import { invalidate } from "../../api/invalidate";
import type { ReviewRow } from "../../api/scorecard";
import { howCalculated } from "../../shared/calc";
import { errorText, formatDateTime } from "../../shared/format";
import { L } from "../../shared/i18n";
import { num, submissionStatus } from "../../shared/labels";
import { fmt } from "./reviewParts";

/** One indicator's detail in the review: each college's value and the transfer to the university indicator. */
export function Detail({ row, cycleId, canTransfer }: { row: ReviewRow; cycleId: string; canTransfer: boolean }) {
  const qc = useQueryClient();
  const { message } = App.useApp();
  const [value, setValue] = useState<number | null>(row.computed);
  const [note, setNote] = useState("");
  const adjusted = value !== row.computed;
  const partial = row.reported < row.expected;
  const needNote = adjusted || partial;
  const calc = howCalculated({ aggregation: row.aggregation, level: row.level, direction: row.direction, unit: row.unit, numerator: row.numeratorLabel, denominator: row.denominatorLabel });
  const ratio = row.aggregation === "RatioOfSums";
  const transfer = useMutation({
    mutationFn: () => api.post<ReviewRow>("/api/review/transfer", { cycleId, indicatorId: row.indicatorId, value, note: note.trim() || null, version: row.official?.version ?? null }),
    onSuccess: async () => {
      message.success(L(`نقلت قيمة ${row.code} إلى مؤشر الجامعة.`, "Transferred."));
      await invalidate.afterTransfer(qc);
    },
  });
  // عناوين الأعمدة؛ على الجوال تصير الصفوف بطاقات وكل خلية تحمل عنوانها (data-label).
  const col = {
    form: L("النموذج", "Form"),
    num: row.numeratorLabel ?? L("البسط", "Numerator"),
    den: row.denominatorLabel ?? L("المقام", "Denominator"),
    value: L("القيمة", "Value"),
    target: L("مستهدف الكلية", "College target"),
    a: row.comparable ? L("من مستهدف الجامعة", "vs university target") : L("إنجازها من مستهدفها", "vs own target"),
    b: row.comparable ? L("البعد عنه", "Gap") : L("حصتها من رقم الجامعة", "Share of university"),
  };

  return (
    <div className="rv-detail">
      <div className="rv-table" role="table" aria-label={L("قيمة كل كلية", "Value per college")}>
        <div className="rv-tr head" role="row">
          <span role="columnheader">{row.level === "University" ? L("الجهة المدخلة", "Entering unit") : L("الكلية", "College")}</span>
          <span role="columnheader">{col.form}</span>
          {ratio && <span role="columnheader">{col.num}</span>}
          {ratio && <span role="columnheader">{col.den}</span>}
          <span role="columnheader">{col.value}</span>
          <span role="columnheader">{col.target}</span>
          <span role="columnheader">{col.a}</span>
          <span role="columnheader">{col.b}</span>
        </div>
        {row.units.map((u) => (
          <div key={u.unitId} className="rv-tr" role="row">
            <span role="cell" className="rv-td-name"><b>{u.name}</b>{u.expected > 1 && <small className="muted"> ({L(`${u.reported} من ${u.expected} قسمًا`, `${u.reported}/${u.expected}`)})</small>}</span>
            <span role="cell" data-label={col.form}>{u.formStatus ? <Tag color={submissionStatus[u.formStatus].color}>{submissionStatus[u.formStatus].text}</Tag> : <span className="muted">—</span>}</span>
            {ratio && <span role="cell" className="ltr" data-label={col.num}>{u.numerator === null ? "—" : num(u.numerator)}</span>}
            {ratio && <span role="cell" className="ltr" data-label={col.den}>{u.denominator === null ? "—" : num(u.denominator)}</span>}
            <span role="cell" className="ltr" data-label={col.value}><b>{fmt(u.value, row.unit)}</b></span>
            <span role="cell" className="ltr" data-label={col.target}>{u.targetSource === "Unit" || u.targetSource === "EachCollege" ? fmt(u.target, row.unit) : <span className="muted">—</span>}</span>
            {row.comparable ? (
              <>
                <span role="cell" className="ltr" data-label={col.a}>{u.universityAchievement === null ? "—" : `${num(u.universityAchievement)}%`}</span>
                <span role="cell" className="ltr" data-label={col.b}>{u.gapToUniversity === null ? "—" : <span className={(row.direction === "LowerIsBetter" ? u.gapToUniversity <= 0 : u.gapToUniversity >= 0) ? "rv-up" : "rv-down"}>{u.gapToUniversity > 0 ? "+" : ""}{num(u.gapToUniversity)}</span>}</span>
              </>
            ) : (
              <>
                <span role="cell" className="ltr" data-label={col.a}>{u.achievement === null ? "—" : `${num(u.achievement)}%`}</span>
                <span role="cell" className="ltr" data-label={col.b}>{u.share === null ? "—" : `${num(u.share)}%`}</span>
              </>
            )}
          </div>
        ))}
        <div className="rv-tr total" role="row">
          <span role="cell" className="rv-td-name"><b>{L("الجامعة (محسوبة)", "University (computed)")}</b></span>
          <span role="cell" data-label={L("الجهات المعتمدة", "Units reported")}><small className="ltr">{row.reported}/{row.expected}</small></span>
          {ratio && <span role="cell" className="ltr" data-label={col.num}><b>{row.computedNumerator === null ? "—" : num(row.computedNumerator)}</b></span>}
          {ratio && <span role="cell" className="ltr" data-label={col.den}><b>{row.computedDenominator === null ? "—" : num(row.computedDenominator)}</b></span>}
          <span role="cell" className="ltr" data-label={col.value}><b>{fmt(row.computed, row.unit)}</b></span>
          <span role="cell" className="rv-td-target">{L("مستهدف الجامعة", "Univ. target")} <b className="ltr">{fmt(row.universityTarget, row.unit)}</b></span>
          <span role="cell" className="ltr" data-label={col.a}>{row.computedAchievement === null ? "—" : `${num(row.computedAchievement)}%`}</span>
          <span role="cell" className="ltr" data-label={col.b}>{!row.comparable ? (row.computed !== null ? "100%" : "—") : row.computed !== null && row.universityTarget !== null ? `${row.computed - row.universityTarget > 0 ? "+" : ""}${num(row.computed - row.universityTarget)}` : "—"}</span>
        </div>
      </div>

      <div className="rv-calc">
        <p><b>{L("حساب الجامعة:", "University value:")}</b> {calc.university}
          {ratio && row.computedNumerator !== null && row.computedDenominator ? (
            <span className="rv-eq ltr"> {num(row.computedNumerator)} ÷ {num(row.computedDenominator)}{row.unit.trim() === "%" ? " × 100" : ""} = {fmt(row.computed, row.unit)}</span>
          ) : null}
        </p>
        {row.formula && <p className="muted"><b>{L("معادلة التقييم في الإطار:", "Framework formula:")}</b> {row.formula}</p>}
        {row.note && <p className="muted">{row.note}</p>}
      </div>

      {row.official && (
        <Alert type={row.state === "Changed" ? "warning" : "success"} showIcon icon={row.state === "Changed" ? <SwapOutlined /> : <CheckCircleOutlined />} className="mb"
          message={row.state === "Changed"
            ? L(`نقل ${fmt(row.official.value, row.unit)} ثم تغيرت قيم الكليات فصارت المحسوبة ${fmt(row.computed, row.unit)}. انقل من جديد إن كانت الجديدة هي الصحيحة.`, "Changed since transfer.")
            : L(`منقولة: ${fmt(row.official.value, row.unit)}`, "Transferred")}
          description={<>{row.official.transferredByName} · <span className="ltr">{formatDateTime(row.official.transferredAt)}</span>{row.official.note && <> · {row.official.note}</>}</>} />
      )}
      {(row.history?.length ?? 0) > 1 && (
        <details className="mb">
          <summary>{L(`تاريخ القيمة الرسمية (${row.history!.length})`, `Official value history (${row.history!.length})`)}</summary>
          <ul className="t1-history">
            {row.history!.map((h, n) => (
              <li key={`${h.changedAt}-${n}`}>
                <span className="ltr">{h.oldValue === null ? "—" : fmt(h.oldValue, row.unit)} → <b>{fmt(h.newValue, row.unit)}</b></span>
                {h.computedValue !== null && h.computedValue !== h.newValue && <> <small className="muted">({L("المحسوبة", "computed")} <span className="ltr">{fmt(h.computedValue, row.unit)}</span>)</small></>}
                {h.reason && <> — {h.reason}</>}
                <div className="muted"><small>{h.changedByName ?? ""} · <span className="ltr">{formatDateTime(h.changedAt)}</span> · {L(`${h.reported} من ${h.expected} جهة`, `${h.reported}/${h.expected} units`)}</small></div>
              </li>
            ))}
          </ul>
        </details>
      )}

      {canTransfer && (
        <div className="rv-transfer">
          <label>
            <span>{L("القيمة التي تنقل إلى مؤشر الجامعة", "Value to transfer")}</span>
            <InputNumber value={value} onChange={(v) => setValue(v)} className="ltr" style={{ width: 180 }} suffix={row.unit.trim() === "%" ? "%" : undefined} />
          </label>
          {needNote && (
            <label className="grow">
              <span>{partial ? L(`اعتمدت ${row.reported} من ${row.expected} جهة فقط؛ سبب النقل الآن`, "Reason (partial)") : L("سبب تعديل القيمة عن المحسوبة", "Reason for the adjustment")}</span>
              <Input value={note} onChange={(e) => setNote(e.target.value)} maxLength={1000} />
            </label>
          )}
          <Button type="primary" icon={<SendOutlined />} loading={transfer.isPending} disabled={value === null || (needNote && note.trim().length < 5)} onClick={() => transfer.mutate()}>
            {row.official ? L("نقل من جديد", "Transfer again") : L("نقل إلى مؤشر الجامعة", "Transfer to the university")}
          </Button>
          {adjusted && row.computed !== null && <Button type="link" onClick={() => setValue(row.computed)}>{L("استعادة المحسوبة", "Use computed")}</Button>}
        </div>
      )}
      {transfer.error && <Alert type="error" showIcon className="mt" message={errorText(transfer.error)} />}
    </div>
  );
}
