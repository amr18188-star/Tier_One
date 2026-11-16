// منطق صفحة النماذج بلا واجهة: أوضاع الصفحة بحسب الصلاحية، والمرشحات، وتجميع النماذج بالكلية.
// خط السنوات والأرباع وتحديد الدورة في shared/period.ts (شريط PeriodNav المشترك).
import type { FormEvent, FormRow, SubmissionStatus } from "../../api/collection";
import type { CapLetter, ScopeCode } from "../../api/types";
import { L } from "../../shared/i18n";
import { submissionStatus } from "../../shared/labels";

export { dueLabel } from "../../shared/period";

// ----- الحالات والترتيب -----

const STATUS_EN: Record<SubmissionStatus, string> = {
  NotStarted: "Not started", InProgress: "In progress", InReview: "In review", Returned: "Returned", Approved: "Approved",
};

export function statusText(s: SubmissionStatus): string {
  return L(submissionStatus[s].text, STATUS_EN[s]);
}

export function formEventText({ action, fromStep, toStep }: Pick<FormEvent, "action" | "fromStep" | "toStep">): string {
  switch (action) {
    case "Submitted": return L("أرسل للمراجعة", "Submitted");
    case "Approved": {
      const direct = (fromStep === "DepartmentReview" && toStep === "Dean")
        || ((fromStep === "DepartmentReview" || fromStep === "CollegeCommittee") && toStep === "Quality");
      return direct ? L("اعتماد مباشر", "Direct approval") : L("اعتمد", "Approved");
    }
    case "Returned": return L("أعيد للتصحيح", "Returned");
    case "Reopened": return L("أعيد فتحه", "Reopened");
  }
}

/** الأولوية في كل القوائم: المعاد للتصحيح، ثم ما ينتظرني، ثم الأقرب موعدًا. */
export function byPriority(a: FormRow, b: FormRow): number {
  return Number(b.status === "Returned") - Number(a.status === "Returned") || Number(b.awaitingMe) - Number(a.awaitingMe) || a.dueOn.localeCompare(b.dueOn);
}

export interface Filters { view: SubmissionStatus | undefined; mine: boolean; q: string }

export function matches(r: FormRow, f: Filters): boolean {
  return (!f.view || r.status === f.view) && (!f.mine || r.awaitingMe) && (!f.q || r.unitName.includes(f.q) || (r.parentName ?? "").includes(f.q));
}

/** نموذج الكلية نفسها (أو المعهد)، لا نموذج قسم فيها. */
export function isCollegeForm(r: FormRow): boolean {
  return r.unitKind === "College" || r.unitKind === "Institute";
}

/** الإجراء الوحيد في بطاقة المهمة، من حالة النموذج. */
export function taskAction(r: FormRow): string {
  if (r.status === "Returned") return L("صحّح وأعد الإرسال", "Correct and resubmit");
  if (r.status === "InReview") return L("راجع النموذج", "Review the form");
  if (r.status === "NotStarted") return L("ابدأ الإدخال", "Start entry");
  return L("أكمل الإدخال", "Continue entry");
}

// ----- أوضاع الصفحة بحسب الدور -----

export type Mode = "tasks" | "monitor";

export interface Access {
  isSuperAdmin: boolean;
  can: (feature: string, cap: CapLetter | string) => boolean;
  scopeOf: (feature: string, cap: CapLetter) => ScopeCode | undefined;
}

const FORMS = "dc.forms";
/** إجراءات المراجعة في مسار الاعتماد (permission-catalog.json). */
export const REVIEW_SPECIALS = ["review_department", "review_college", "approve_college", "approve_final"] as const;
/** من يراجع على مستوى الكلية أو الجامعة يتابع جهات كثيرة؛ ممثل القسم لا. */
const OVERSIGHT = ["review_college", "approve_college", "approve_final"];

export interface Modes { modes: Mode[]; initial: Mode; readOnly: boolean }

/**
 * «مهامي» لمن عليه عمل: يدخل (E) أو يراجع (إجراء مراجعة) أو ينتظره نموذج فعلًا.
 * «متابعة الجهات» لمن يشرف: المدير الأعلى، أو عرضه على الجامعة كلها (A)، أو يراجع على مستوى الكلية أو الجامعة.
 * من له الاثنان يرى التبويبين ويبدأ بالمتابعة. من لا عمل عليه ولا إشراف (اطلاع في نطاقه) متابعة للقراءة فقط.
 */
export function formModes(a: Access, anyAwaiting: boolean): Modes {
  const enters = a.can(FORMS, "E");
  const reviews = REVIEW_SPECIALS.some((s) => a.can(FORMS, s));
  const works = enters || reviews || anyAwaiting;
  const oversees = a.isSuperAdmin || a.scopeOf(FORMS, "V") === "A" || OVERSIGHT.some((s) => a.can(FORMS, s));
  if (works && oversees) return { modes: ["monitor", "tasks"], initial: "monitor", readOnly: false };
  if (works) return { modes: ["tasks"], initial: "tasks", readOnly: false };
  return { modes: ["monitor"], initial: "monitor", readOnly: true };
}

// ----- التجميع بالكلية -----

export interface CollegeGroup {
  key: string;
  name: string;
  central: boolean;
  /** نموذج الكلية نفسها أولًا، ثم أقسامها؛ مما يطابق المرشحات فقط. */
  shown: FormRow[];
  total: number;
  approved: number;
  returned: number;
  overdue: number;
}

const CENTRAL = "central";

function groupKey(r: FormRow): string {
  if (isCollegeForm(r)) return `c:${r.unitName}`;
  if (r.unitKind === "Department" && r.parentName) return `c:${r.parentName}`;
  return CENTRAL;
}

/**
 * الأقسام تحت كليتها، ونموذج الكلية (أو المعهد) نموذجها هي؛ وما لا كلية له (الجامعة، الإدارات، المراكز) في «الوحدات المركزية».
 * العدادات من كل نماذج المجموعة، والصفوف المعروضة مما يطابق المرشحات. المجموعة بلا صف مطابق لا تظهر.
 */
export function groupByCollege(all: readonly FormRow[], shown: readonly FormRow[]): CollegeGroup[] {
  const groups = new Map<string, CollegeGroup>();
  for (const r of all) {
    const key = groupKey(r);
    let g = groups.get(key);
    if (!g) {
      const central = key === CENTRAL;
      g = { key, name: central ? L("الوحدات المركزية", "Central units") : key.slice(2), central, shown: [], total: 0, approved: 0, returned: 0, overdue: 0 };
      groups.set(key, g);
    }
    g.total++;
    if (r.status === "Approved") g.approved++;
    if (r.status === "Returned") g.returned++;
    if (r.overdue) g.overdue++;
  }
  for (const r of shown) groups.get(groupKey(r))?.shown.push(r);
  for (const g of groups.values()) g.shown.sort((a, b) => Number(isCollegeForm(b)) - Number(isCollegeForm(a)));
  return [...groups.values()]
    .filter((g) => g.shown.length > 0)
    .sort((a, b) => Number(a.central) - Number(b.central) || a.name.localeCompare(b.name, "ar"));
}
