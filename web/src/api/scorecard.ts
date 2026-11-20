// بطاقة أداء الجامعة وفئتها بمعادلات «الإطار الشامل لتقييم أداء الجامعات» (KPI.xlsx).
import type { Accumulation, Direction } from "./tier1";

export type Level = "NoData" | "InLevel" | "Below";
export type Rag = "Strong" | "Developing" | "AtRisk";
export type TierAlert = "Balanced" | "Warning" | "Downgraded";

export interface PlanQuarter { year: number; yearIndex: number; quarter: number; index: number; start: string; end: string; label: string; current: boolean }

export interface IndicatorScore {
  id: string; code: string; name: string; nameEn: string | null; standardCode: string; unit: string; direction: Direction; accumulation: Accumulation;
  perspectives: number[]; sdgPrimary: number | null; sdgSecondary: number | null; sdgInduced: number | null;
  finalTarget: number | null; yearTargets: (number | null)[]; quarters: (number | null)[]; latest: number | null; latestIndex: number; gap: number | null;
  score: number; rawAchievement: number | null; level: Level;
  yearValues: (number | null)[]; yearScores: number[]; yearScoresVsYearly: (number | null)[]; quarterScores: (number | null)[];
}

export interface StandardScore { code: string; name: string; score: number; yearScores: number[]; yearScoresVsYearly: number[]; indicators: IndicatorScore[] }

export interface PillarScore {
  id: string; code: string; name: string; nameEn: string | null; score: number; tier: number; rag: Rag; indicators: number; evaluated: number; inLevel: number;
  yearScores: number[]; yearScoresVsYearly: number[]; standards: StandardScore[];
}

export interface TierResult { overallTier: number; weakestTier: number; finalTier: number; alert: TierAlert }
export interface TierBand { tier: number; nameAr: string; nameEn: string; pillarMin: number; pillarMax: number; overallMin: number; overallMax: number; gap: number }
export interface TierThresholds { tier1: number; tier2: number; tier3: number; tier4: number; overallOffset: number }

export interface OverallScore {
  score: number; tier: TierResult; weakestPillar: string | null; weakestScore: number;
  yearScores: number[]; yearScoresVsYearly: number[]; yearTiers: TierResult[]; indicators: number; reported: number; inLevel: number;
}

export interface PerspectiveScore {
  number: number; nameAr: string; nameEn: string; indicators: number; evaluated: number; inLevel: number; below: number; inLevelPercent: number | null;
  yearPercent: (number | null)[]; yearPercentVsYearly: (number | null)[];
}

export interface SdgScore {
  number: number; nameAr: string; nameEn: string; primary: number; secondary: number; induced: number; total: number; evaluated: number;
  inLevel: number; below: number; inLevelPercent: number | null; rag: Rag | null; yearPercent: (number | null)[];
}

export interface Scorecard {
  years: number[]; quarters: PlanQuarter[]; lastReported: PlanQuarter | null; thresholds: TierThresholds;
  overall: OverallScore; tiers: TierBand[]; pillars: PillarScore[]; perspectives: PerspectiveScore[]; sdgs: SdgScore[]; canExport: boolean;
}

// ----- مراجعة مسئول ملف Tier 1 ونقل القيم إلى مؤشرات الجامعة -----
export type ReviewState = "NoData" | "Incomplete" | "Ready" | "Transferred" | "Changed";
export interface ReviewCycle { id: string; name: string; year: number; quarter: number; label: string; status: import("./collection").CycleStatus }
export interface ReviewUnit {
  unitId: string; name: string; kind: import("./tier1").OrgUnitKind; value: number | null; numerator: number | null; denominator: number | null;
  reported: number; expected: number; note: string | null; formStatus: import("./collection").SubmissionStatus | null;
  target: number | null; targetSource: import("./collection").TargetSource; achievement: number | null; universityAchievement: number | null; gapToUniversity: number | null;
  /** حصة الجهة من قيمة الجامعة المحسوبة، للمجموع والعدد. */
  share: number | null;
}
export interface OfficialValue {
  value: number | null; numerator: number | null; denominator: number | null; computedValue: number | null; note: string | null;
  transferredByName: string | null; transferredAt: string; version: number;
}
/** تغيير في القيمة الرسمية للجامعة (D8). */
export interface ValueChange {
  oldValue: number | null; newValue: number | null; computedValue: number | null; reported: number; expected: number;
  reason: string | null; changedByName: string | null; changedAt: string;
}

export interface ReviewRow {
  indicatorId: string; code: string; name: string; standardCode: string; pillarCode: string | null; pillarIndex: number; unit: string;
  direction: Direction; aggregation: import("./tier1").Aggregation; level: import("./tier1").CollectionLevel;
  numeratorLabel: string | null; denominatorLabel: string | null; formula: string | null;
  computed: number | null; computedNumerator: number | null; computedDenominator: number | null; reported: number; expected: number; note: string | null;
  universityTarget: number | null; computedAchievement: number | null; official: OfficialValue | null; state: ReviewState;
  /** تقارن الكلية بمستهدف الجامعة (نسبة أو متوسط أو مركزي)، وإلا بحصتها من رقم الجامعة. */
  comparable: boolean; units: ReviewUnit[];
  /** تاريخ القيمة الرسمية في هذا الربع، الأحدث أولًا (D8). */ history: ValueChange[] | null;
}
export interface Review { cycles: ReviewCycle[]; cycle: ReviewCycle | null; rows: ReviewRow[]; canTransfer: boolean }

// ----- تقدم الكلية مقابل مستهدف الجامعة -----
export interface ProgressIndicator {
  id: string; code: string; name: string; standardCode: string; unit: string; direction: Direction; accumulation: Accumulation;
  quarters: (number | null)[]; latest: number | null; latestIndex: number;
  universityTargets: (number | null)[]; collegeTargets: (number | null)[]; universityQuarters: (number | null)[];
  yearTarget: number | null; gap: number | null; yearAchievement: number | null; ownTarget: number | null; ownAchievement: number | null;
  score: number | null; level: Level; yearValues: (number | null)[]; yearScores: (number | null)[];
  /** النسب والمتوسطات تقارن بمستهدف الجامعة؛ المجموع والعدد بمستهدف الكلية إن وجد وبحصتها من رقم الجامعة. */
  comparable: boolean; share: number | null; universityYearTarget: number | null;
}
export interface ProgressStandard { code: string; name: string; score: number; indicators: ProgressIndicator[] }
export interface ProgressPillar { id: string; code: string; name: string; index: number; score: number; universityScore: number | null; standards: ProgressStandard[] }
export interface Progress {
  units: { id: string; name: string; kind: import("./tier1").OrgUnitKind }[]; unit: { id: string; name: string; kind: import("./tier1").OrgUnitKind };
  lastReported: PlanQuarter | null; score: number; universityScore: number | null; indicators: number; reported: number; inLevel: number;
  pillars: ProgressPillar[]; quarters: PlanQuarter[];
}
