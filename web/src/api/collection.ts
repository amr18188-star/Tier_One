// أنواع ردود جمع البيانات: الدورات ونماذج الجهات والأدلة والاستيراد والنتائج.
import type { Aggregation, CollectionLevel, Direction, OrgUnitKind } from "./tier1";

export type CycleStatus = "Draft" | "Open" | "Closed";
export type ApprovalStep = "Entry" | "DepartmentReview" | "CollegeCommittee" | "Dean" | "Quality" | "Done";
export type SubmissionStatus = "NotStarted" | "InProgress" | "InReview" | "Returned" | "Approved";
export type ValueKind = "Number" | "NotAvailable" | "NotApplicable";
export type IssueSeverity = "Error" | "Warning";
export type SubmissionAction = "Submitted" | "Approved" | "Returned" | "Reopened";
export type Performance = "Achieved" | "Near" | "Behind" | "NoTarget" | "NoData" | "NotJudged" | "NotStarted" | "Incomplete";
export type TargetSource = "None" | "Unit" | "EachCollege" | "University";
/**
 * ما يعرض في عمود «مستهدف الكلية» بجانب القيمة (ADR-0017، DEC-5): مستهدف الجهة (رقمًا أو نصًا)، وإلا مستهدف الجامعة ومصدره
 * "University" فيعرض وتحته خط. للعرض فقط؛ الإنجاز يحسب من `target`.
 */
export interface ShownTarget { value: number | null; text: string | null; source: TargetSource }

export interface Cycle {
  id: string; name: string; periodLabel: string; targetYear: number; opensOn: string; dueOn: string; status: CycleStatus; trial: boolean;
  indicators: number; forms: number; formsByStatus: Partial<Record<SubmissionStatus, number>>; overdue: number; version: number;
  /** الربع من سنة الخطة (1–4) في الدورات الربع سنوية. */
  quarter: number | null;
}

export interface SaveCycle {
  name: string; periodLabel: string | null; targetYear: number; opensOn: string; dueOn: string; trial: boolean; version: number | null; quarter: number | null;
}

export interface OpenPreview {
  indicators: number; forms: number; departments: number; colleges: number; central: number; skipped: string[];
  /** G-03: units whose form would stop at a step nobody holds (at most 50 lines), and how many there are in all. */
  unheld?: string[] | null; unheldCount?: number;
}

export interface FormRow {
  id: string; cycleId: string; cycleName: string; unitId: string; unitName: string; unitKind: OrgUnitKind; parentName: string | null;
  step: ApprovalStep; stepName: string; status: SubmissionStatus; dueOn: string; overdue: boolean; completed: number; required: number; awaitingMe: boolean;
}

export interface Issue { code: string; severity: IssueSeverity; messageAr: string }

export interface Doc { id: string; fileName: string; contentType: string; sizeBytes: number; uploadedAt: string; description: string | null; voided: boolean; voidReason: string | null }

export interface Value {
  kind: ValueKind; value: number | null; numerator: number | null; denominator: number | null; reason: string | null; explanation: string | null;
  reviewNote: string | null; editedAt: string; version: number;
}

export interface FormItem {
  indicatorId: string; code: string; name: string; unit: string; aggregation: Aggregation; usesRatio: boolean; numeratorLabel: string | null; denominatorLabel: string | null;
  definition: string | null; dataSource: string | null; evidenceRequired: string | null; direction: Direction; previous: number | null; target: number | null;
  value: Value | null; issues: Issue[]; evidence: Doc[]; entryNote: string | null; targetSource: TargetSource;
  /** مستهدف مقترح بانتظار الاعتماد (D3)، للعرض فقط. */ pendingTarget?: number | null; pendingTargetYear?: number | null;
  /** القيمة المعتمدة قبل إعادة الفتح (D4). */ approvedBefore?: ApprovedValue | null;
  shownTarget?: ShownTarget | null;
}

export interface FormSection { pillarCode: string | null; pillarName: string | null; standardCode: string; standardName: string; items: FormItem[] }

export interface PathStep { step: ApprovalStep; name: string; state: "done" | "current" | "pending" | "skipped" }

export interface FormEvent { action: SubmissionAction; fromStep: ApprovalStep; toStep: ApprovalStep; byName: string | null; at: string; note: string | null }

export interface Form {
  id: string; cycleId: string; cycleName: string; periodLabel: string; cycleStatus: CycleStatus; trial: boolean; targetYear: number;
  unitId: string; unitName: string; unitKind: OrgUnitKind; step: ApprovalStep; status: SubmissionStatus; dueOn: string; overdue: boolean;
  path: PathStep[]; nextStepName: string | null; returnNote: string | null; lastEditedAt: string | null;
  completed: number; required: number; sections: FormSection[]; blocking: string[]; events: FormEvent[];
  actions: { canEdit: boolean; canSubmit: boolean; canReview: boolean; canReopen: boolean; directDepartmentApproval: boolean; directDeanApproval: boolean;
    /** الاعتماد العادي إلى nextStepName. */ canApprove?: boolean;
    /** الاعتماد المباشر (اختيار صريح بسبب مكتوب): الخطوة التي يحيل إليها. */ directStepName?: string | null;
    /** قرار طلب إعادة الفتح المعلق (اللجنة العليا، D4). */ canDecideReopen?: boolean }; version: number;
  /** آخر طلب لإعادة فتح النموذج المعتمد (D4). */ reopen?: FormReopen | null;
}

export type ReopenStatus = "Pending" | "Approved" | "Rejected";
export interface FormReopen {
  id: string; status: ReopenStatus; reason: string; requestedByName: string | null; requestedAt: string;
  decidedByName: string | null; decidedAt: string | null; decisionRef: string | null; decisionNote: string | null;
}
/** القيمة كما اعتمدت قبل إعادة فتح النموذج؛ تبقى الرسمية في النتائج حتى يعاد اعتماده (D4). */
export interface ApprovedValue { kind: ValueKind; value: number | null; numerator: number | null; denominator: number | null; reason: string | null; approvedAt: string | null }

export interface SaveValue {
  indicatorId: string; kind: ValueKind; value: number | null; numerator: number | null; denominator: number | null;
  reason: string | null; explanation: string | null; version: number | null;
}

export interface ImportRow { row: number; code: string; name: string | null; status: "new" | "changed" | "same" | "skip" | "error"; display: string | null; issues: Issue[] }
export interface ImportPreview { rows: number; changes: number; errors: number; warnings: number; canApply: boolean; items: ImportRow[]; stamp?: string | null }

// ----- النتائج -----
export interface ResultUnit { id: string; name: string; kind: OrgUnitKind }
export interface ResultsCycle { id: string; name: string; periodLabel: string; targetYear: number; status: CycleStatus; trial: boolean }

export interface IndicatorResult {
  indicatorId: string; code: string; name: string; standardCode: string; unit: string; direction: Direction; level: CollectionLevel;
  actual: number | null; target: number | null; baseline: number | null; previous: number | null; achievement: number | null; performance: Performance;
  reported: number; expected: number; note: string | null; summary: string;
  targetSource: TargetSource; targetYear: number | null; universityTarget: number | null; universityAchievement: number | null;
  /** مستهدف مقترح بانتظار الاعتماد (D3)، للعرض فقط. */ pendingTarget?: number | null; pendingTargetYear?: number | null;
  /** لكلية في مؤشر مجموع: مستهدف الجامعة الكلي، وحصة مستهدف الكلية وما حققته منه (0–1). */
  universityTotalTarget?: number | null; targetShare?: number | null; actualShare?: number | null;
  shownTarget?: ShownTarget | null;
}

export interface StandardResult { code: string; name: string; indicators: number; achieved: number; near: number; behind: number; noData: number; noTarget: number }
export interface HeatCell { unitId: string; standardCode: string; indicators: number; achieved: number; near: number; behind: number; noData: number }

export interface Results {
  cycle: ResultsCycle; unit: ResultUnit; units: ResultUnit[]; previousCycle: ResultsCycle | null; formsApproved: number; formsTotal: number;
  standards: StandardResult[]; indicators: IndicatorResult[]; heatUnits: ResultUnit[]; heat: HeatCell[]; canExport: boolean;
  /** نماذج قيد التصحيح بعد إعادة فتح معتمدة، تحتسب بقيمها المعتمدة السابقة (D4). */ formsUnderCorrection?: number;
}

export interface UnitResult {
  unitId: string; unitName: string; actual: number | null; target: number | null; achievement: number | null; performance: Performance; reported: number; expected: number;
  note: string | null; targetSource: TargetSource; numerator: number | null; denominator: number | null;
}
export interface TrendPoint { cycleId: string; label: string; actual: number | null; target: number | null }
export interface IndicatorResults { university: IndicatorResult; colleges: UnitResult[]; trend: TrendPoint[] }
