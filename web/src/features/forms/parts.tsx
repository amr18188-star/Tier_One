import { Tag } from "antd";
import { BankOutlined } from "@ant-design/icons";
import { Link } from "react-router-dom";
import type { FormRow } from "../../api/collection";
import { formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { submissionStatus } from "../../shared/labels";
import { dueLabel, isCollegeForm, statusText } from "./model";

/** شريط تقدم رفيع بألوان السمة؛ label يجعله progressbar مقروءًا، وبدونه زخرفة بجانب نص يقول الرقم. */
export function Bar({ value, max, tone = "work", label }: { value: number; max: number; tone?: "work" | "ok"; label?: string }) {
  const pct = max > 0 ? Math.min(100, Math.round((100 * value) / max)) : 0;
  const a11y = label
    ? { role: "progressbar", "aria-label": label, "aria-valuemin": 0, "aria-valuemax": max, "aria-valuenow": value }
    : { "aria-hidden": true };
  return <span className={`fm-bar tone-${tone}`} {...a11y}><i style={{ width: `${pct}%` }} /></span>;
}

export function StatusTag({ row }: { row: FormRow }) {
  return <Tag color={submissionStatus[row.status].color}>{statusText(row.status)}</Tag>;
}

/** الموعد: الأيام الباقية والتاريخ في الدورة المفتوحة؛ والتاريخ وحده بعد الاعتماد أو إغلاق الدورة. */
export function Due({ row, cycleOpen }: { row: FormRow; cycleOpen: boolean }) {
  if (row.status === "Approved" || !cycleOpen) return <span className="fm-due"><span className="ltr">{formatDate(row.dueOn)}</span></span>;
  return (
    <span className={`fm-due${row.overdue ? " is-late" : ""}`}>
      {dueLabel(row.dueOn)} <small className="ltr">{formatDate(row.dueOn)}</small>
    </span>
  );
}

/** صف نموذج يفتحه: الجهة وحالتها وعند من هو وموعده. نموذج الكلية بعلامة، والقسم بإزاحة تحتها. */
export function FormLink({ row: r, cycleOpen, withParent, readOnly }: { row: FormRow; cycleOpen: boolean; withParent?: boolean; readOnly?: boolean }) {
  const own = isCollegeForm(r);
  return (
    <Link to={`/forms/${r.id}`} className={`fm-row${own ? " is-own" : ""}${r.awaitingMe ? " is-mine" : ""}`}
      title={readOnly ? L("عرض النموذج", "View the form") : L("فتح النموذج", "Open the form")}>
      <span className="fm-row-mark" aria-hidden>{own ? <BankOutlined /> : null}</span>
      <span className="fm-row-unit">
        <b>{r.unitName}</b>
        {own && <small> {L("(نموذج الكلية)", "(college form)")}</small>}
        {!own && withParent && r.parentName && <small> · {r.parentName}</small>}
        {r.awaitingMe && <span className="fm-row-flag"> · {L("مطلوب منك", "Waiting for you")}</span>}
      </span>
      <span className="fm-row-tag"><StatusTag row={r} /></span>
      <span className="fm-row-at">{r.status !== "Approved" && <>{L("عند:", "At:")} {r.stepName}</>}</span>
      <Due row={r} cycleOpen={cycleOpen} />
    </Link>
  );
}
