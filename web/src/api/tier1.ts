// أنواع ردود مسارات Tier 1 كما يرسلها الخادم (التعدادات بأسمائها).

export type OrgUnitKind = "University" | "College" | "Institute" | "Department" | "Center" | "Administration" | "Sector";
export type DefinitionStatus = "Draft" | "UnderReview" | "Approved" | "Superseded";
export type Direction = "Undefined" | "HigherIsBetter" | "LowerIsBetter" | "Range";
export type Aggregation = "Undefined" | "Sum" | "RatioOfSums" | "DistinctCount" | "WeightedAverage" | "Central" | "StatusOnly";
export type CollectionLevel = "Department" | "College" | "University";
export type Frequency = "Monthly" | "Quarterly" | "SemiAnnual" | "Annual" | "Custom";
export type TargetKind = "Baseline" | "Target" | "EachCollege";
export type TargetStatus = "Proposed" | "Approved";
export type ConflictSeverity = "Low" | "Medium" | "High";
export type ConflictStatus = "Open" | "Ready" | "Resolved" | "Deferred";
export type DistributionStatus = "Covers" | "Short" | "Incomplete" | "NotComparable" | "NoUniversityTarget";
export type InitiativeStatus = "Planned" | "InProgress" | "Completed" | "Stopped";
/** نوع المؤشر: لحظي (قيمة السنة متوسط أرباعها) أو تراكمي (آخر ربع). */
export type Accumulation = "Snapshot" | "Cumulative";

// ----- شجرة الخطة -----
export interface YearTarget { id: string; year: number; value: number | null; text: string | null; status: TargetStatus; version: number }
export interface IndicatorNode {
  code: string; nameAr: string; definition: string | null; numerator: string | null; denominator: string | null;
  unit: string; direction: Direction; level: CollectionLevel; aggregation: Aggregation;
  status: DefinitionStatus; hasApproved: boolean; suspended: boolean; planStartYear: number | null; planEndYear: number | null; planNote: string | null;
  baseline: number | null; targets: YearTarget[]; eachCollege: string | null; version: number;
  nameEn: string | null; formula: string | null; accumulation: Accumulation; perspectives: number[];
  sdgPrimary: number | null; sdgSecondary: number | null; sdgInduced: number | null;
}
export interface InitiativeNode {
  id: string; code: string; nameAr: string; description: string | null; expectedOutcome: string | null; ownerUnitId: string | null; ownerUnitName: string | null;
  startYear: number; endYear: number; status: InitiativeStatus; indicatorCodes: string[]; version: number;
  /** ADR-0017: the initiative's activities and their average completion (null without activities). */
  activityCount: number; activityProgress: number | null;
}
/** ADR-0017: one activity of an initiative. */
export interface InitiativeActivity {
  id: string; initiativeId: string; titleAr: string; description: string | null; ownerUnitId: string | null; ownerUnitName: string | null;
  startDate: string | null; endDate: string | null; status: InitiativeStatus; progress: number; sortOrder: number; version: number;
}
export interface StandardNode {
  id: string; code: string; nameAr: string; nameEn: string | null; description: string | null; pillarId: string | null; sortOrder: number; version: number;
  initiatives: InitiativeNode[]; indicators: IndicatorNode[];
}
export interface PillarNode { id: string; code: string; nameAr: string; nameEn: string | null; description: string | null; sortOrder: number; version: number; standards: StandardNode[] }
export interface StrategyTree {
  baselineYear: number; years: number[]; pillars: PillarNode[]; unassigned: StandardNode[];
  actions: {
    canEdit: boolean; canAdd: boolean; canDelete: boolean; canAddIndicator: boolean; canEditIndicator: boolean; canDeleteIndicator: boolean;
    canEditTargets: boolean; canDeleteTargets: boolean; canApproveTargets: boolean;
  };
}

export interface OrgUnit {
  id: string; code: string; nameAr: string; nameEn: string | null; kind: OrgUnitKind; parentId: string | null; path: string;
  active: boolean; sortOrder: number; establishedOn: string | null; closedOn: string | null; version: number;
  /** يطلع من يعمل فيها على الجامعة كاملة (عرضًا وطباعة). */
  viewsWholeUniversity: boolean;
  /** مؤشراتها محددة بقائمة بدل الافتراضي لمستواها. */
  customIndicators: boolean;
  /** ما تدخله (أو أقسامها للكلية) من المؤشرات بحسب تكليفها. */
  indicatorCount: number | null;
}

// ----- تكليف الجهات بالمؤشرات -----
export interface UnitIndicatorItem { id: string; code: string; nameAr: string; level: CollectionLevel; aggregation: string; selected: boolean; takenBy: string | null }
export interface UnitIndicatorStandard { code: string; nameAr: string; pillarCode: string; pillarName: string; indicators: UnitIndicatorItem[] }
export interface UnitIndicators {
  unitId: string; unitName: string; kind: OrgUnitKind; custom: boolean; followsUnit: string | null; canEdit: boolean; openCycles: number;
  standards: UnitIndicatorStandard[];
}

export interface OrgUnitChange { kind: string; effectiveOn: string; oldValue: string | null; newValue: string | null; reason: string | null; at: string }

// ----- الصفحة الأولى -----
export interface TaskItem { key: string; titleAr: string; hintAr: string; count: number; link: string; tone: "bad" | "warn" | "brand" | "info" }
export interface CountItem { key: string; labelAr: string; count: number }
export interface StandardProgress { code: string; nameAr: string; indicators: number; approved: number; openConflicts: number }
export interface Home {
  displayName: string; tasks: TaskItem[]; definitionStatus: CountItem[]; conflictSeverity: CountItem[]; openConflicts: number; resolvedConflicts: number;
  standards: StandardProgress[]; structureAveragePercent: number | null; collegeCount: number;
}

/** ADR-0017: the plan's period, as GET /api/admin/settings/plan-period returns it. */
export interface PlanPeriod {
  startYear: number; startMonth: number; endYear: number; endMonth: number; start: string; end: string;
  years: number[]; quarters: number; editable: boolean; updatedAt: string | null;
}

// ----- القاموس -----
export interface Standard {
  id: string; code: string; nameAr: string; pillarCode: string | null; pillarName: string | null; classificationNote: string | null;
  indicatorCount: number; approvedCount: number; openConflicts: number;
}

export interface IndicatorRow {
  code: string; standardCode: string; nameAr: string; unit: string; direction: Direction; level: CollectionLevel; aggregation: Aggregation;
  status: DefinitionStatus; versionNumber: number; hasApproved: boolean; suspended: boolean; missingCount: number; openConflicts: number;
  /** ADR-0017: the university target for the plan's last year. */
  baseline: number | null; finalTarget: number | null; collegeTarget: string | null;
}

export interface Version {
  id: string; number: number; status: DefinitionStatus; nameAr: string; nameEn: string | null; alternateName: string | null; definition: string | null;
  purpose: string | null; exclusions: string | null; numerator: string | null; denominator: string | null; unit: string; direction: Direction;
  frequency: Frequency; level: CollectionLevel; aggregation: Aggregation; aggregationNote: string | null; dataSource: string | null;
  ownerUnitId: string | null; ownerUnitName: string | null; evidenceRequired: string | null; sourcePages: string | null; changeReason: string | null;
  submittedAt: string | null; approvedAt: string | null; decisionRef: string | null; returnNote: string | null; missing: string[]; version: number;
  formula: string | null; accumulation: Accumulation;
}

export interface VersionSummary { number: number; status: DefinitionStatus; changeReason: string | null; createdAt: string; approvedAt: string | null; decisionRef: string | null }

export interface IndicatorDetail {
  code: string; standardId: string; standardCode: string; standardName: string; suspended: boolean;
  current: Version; effective: Version | null; history: VersionSummary[]; conflictCodes: string[];
  actions: { canEdit: boolean; canSubmit: boolean; canApprove: boolean; canSuspend: boolean; editCreatesVersion: boolean };
  framework: {
    perspectives: number[]; sdgPrimary: number | null; sdgSecondary: number | null; sdgInduced: number | null;
    pssRationale: string | null; sdgRationale: string | null; planNote: string | null;
  };
}

export interface Target {
  id: string; unitId: string; unitCode: string; unitName: string; year: number; kind: TargetKind; value: number | null; text: string | null;
  numerator: number | null; denominator: number | null; source: string | null; note: string | null; status: TargetStatus; decisionRef: string | null; version: number;
}

/** تعديل مستهدف معتمد أو حذفه، ومن عدله وأين (D8). */
export interface TargetRevision {
  oldValue: number | null; oldText: string | null; newValue: number | null; newText: string | null; reason: string; decisionRef: string | null; changedAt: string;
  action?: "Revised" | "Deleted"; year?: number | null; kind?: string | null; unitName?: string | null; changedByName?: string | null;
}

export interface IndicatorTargets {
  indicatorCode: string; unit: string; direction: Direction; aggregation: Aggregation; level: CollectionLevel; years: number[];
  university: Target[]; eachCollege: Target | null; colleges: Target[]; revisions: TargetRevision[];
  actions: { canEditUniversity: boolean; canEditColleges: boolean; canApprove: boolean };
}

export interface CollegeShare { unitId: string; name: string; value: number | null; numerator: number | null; denominator: number | null; text: string | null; fromEachCollegeDefault: boolean }
export interface Distribution {
  year: number;
  result: { status: DistributionStatus; universityValue: number | null; collegesValue: number | null; gap: number | null; collegeCount: number; missingCount: number; summaryAr: string };
  shares: CollegeShare[];
  /** المستخدم يرى كليات نطاقه وحدها مع إجمالي التوزيع (SEC-3). */ ownUnitsOnly?: boolean;
}

// ----- التعارضات -----
export interface Conflict {
  id: string; code: string; scope: string[]; type: string; severity: ConflictSeverity; description: string; valueA: string | null; valueB: string | null;
  proposal: string | null; status: ConflictStatus; preparationNote: string | null; preparedAt: string | null; decision: string | null; decidingBody: string;
  decisionDate: string | null; minutesRef: string | null; version: number;
}
export interface ConflictList { items: Conflict[]; actions: { canPrepare: boolean; canDecide: boolean } }

// ----- الهيكل -----
export interface Position { id: string; titleAr: string; single: boolean; required: boolean; suggestedRoleCode: string | null }
export interface TemplateUnit { id: string; code: string; nameAr: string; roleAr: string; duties: string[]; isHead: boolean; perDepartment: boolean; positions: Position[] }
export interface Member {
  id: string; positionId: string; departmentId: string | null; fullName: string; title: string | null; employer: string | null; email: string | null;
  phone: string | null; startDate: string; endDate: string | null; decisionRef: string | null; notes: string | null; version: number;
}
export interface Partner {
  id: string; departmentId: string; nameAr: string; sector: string | null; coordinatorName: string | null; coordinatorEmail: string | null;
  coordinatorPhone: string | null; agreementDate: string | null; agreementRef: string | null; endedOn: string | null; version: number;
}
export interface Department { id: string; code: string; nameAr: string }
export interface CompletenessIssue { kind: string; messageAr: string; departmentId: string | null; positionId: string | null }
export interface Completeness { required: number; filled: number; issues: CompletenessIssue[]; percent: number }
export interface CollegeChart {
  collegeId: string; code: string; nameAr: string; asOf: string; canEdit: boolean; contactsVisible: boolean;
  units: TemplateUnit[]; members: Member[]; departments: Department[]; partners: Partner[]; completeness: Completeness;
}
export interface CollegeCompleteness { collegeId: string; code: string; nameAr: string; percent: number; issueCount: number; firstIssue: string | null }
