import { Tooltip } from "antd";
import type { ShownTarget, TargetSource } from "../api/collection";
import { num, withUnit } from "./labels";
import { L } from "./i18n";

/** The shown target of a row from an API that predates <c>shownTarget</c>: the measured target and its source. */
export function shownOf(row: { shownTarget?: ShownTarget | null; target: number | null; targetSource: TargetSource }): ShownTarget {
  return row.shownTarget ?? { value: row.target, text: null, source: row.target === null ? "None" : row.targetSource };
}

/**
 * خانة «مستهدف الكلية» بجانب قيمة المؤشر (ADR-0017، DEC-5): مستهدف الكلية إن وجد (رقمًا أو نصًا كما سجل)، وإلا مستهدف الجامعة
 * للعام نفسه وتحته خط ليعرف أنه مستهدف الجامعة. للعرض فقط: إنجاز الكلية يحسب من مستهدفها وحده.
 * `compact` يحذف الوحدة غير المئوية حيث يذكرها الجدول في موضع آخر.
 */
export function CollegeTarget({ target, unit, compact }: { target?: ShownTarget | null; unit: string; compact?: boolean }) {
  if (!target || target.source === "None" || (target.value === null && !target.text))
    return <small className="muted">{L("لا مستهدف", "No target")}</small>;
  const shown = target.value === null ? <span className="t1-target-text">{target.text}</span>
    : <b className="ltr">{compact ? (unit.trim() === "%" ? `${num(target.value)}%` : num(target.value)) : withUnit(target.value, unit)}</b>;
  if (target.source !== "University") return shown;
  return (
    <Tooltip title={L("مستهدف الجامعة للعام: لا مستهدف خاص بالكلية لهذا المؤشر، فلا يحسب منه إنجازها.",
      "The university's target for the year: the college has none for this indicator, so its achievement is not computed from it.")}>
      <u className="t1-uni-target" aria-label={L("مستهدف الجامعة", "University target")}>{shown}</u>
    </Tooltip>
  );
}
