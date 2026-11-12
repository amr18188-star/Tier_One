import type { CycleStatus, Performance, SubmissionStatus, TargetSource } from "../api/collection";
import type {
  Aggregation, CollectionLevel, ConflictSeverity, ConflictStatus, DefinitionStatus, Direction, DistributionStatus, Frequency, InitiativeStatus, OrgUnitKind, TargetKind,
} from "../api/tier1";

/** نصوص الحالات وألوانها في مكان واحد؛ اللون لا يأتي وحده أبدًا، بل مع نص يشرحه. */
export type TagColor = "default" | "processing" | "success" | "warning" | "error" | "gold" | "cyan" | "purple";

export const definitionStatus: Record<DefinitionStatus, { text: string; color: TagColor; hint: string }> = {
  Draft: { text: "مسودة", color: "default", hint: "قيد الإعداد؛ تكمل بياناته ثم يرسل للاعتماد." },
  UnderReview: { text: "قيد الاعتماد", color: "processing", hint: "أرسل للجنة العليا وينتظر قرارها." },
  Approved: { text: "معتمد", color: "success", hint: "التعريف المعتمد الساري." },
  Superseded: { text: "حل محله إصدار أحدث", color: "default", hint: "يبقى للنتائج التي قيست به." },
};

export const conflictSeverity: Record<ConflictSeverity, { text: string; color: TagColor }> = {
  High: { text: "عالية", color: "error" },
  Medium: { text: "متوسطة", color: "warning" },
  Low: { text: "منخفضة", color: "default" },
};

export const conflictStatus: Record<ConflictStatus, { text: string; color: TagColor; hint: string }> = {
  Open: { text: "مفتوح", color: "error", hint: "يحتاج كتابة المعالجة المقترحة ليدخل جدول أعمال اللجنة." },
  Ready: { text: "جاهز للجنة", color: "processing", hint: "على جدول أعمال الجلسة القادمة." },
  Deferred: { text: "مؤجل", color: "warning", hint: "أجلت اللجنة حسمه إلى جلسة لاحقة." },
  Resolved: { text: "محسوم", color: "success", hint: "سجل قرار اللجنة العليا برقم المحضر." },
};

export const distributionStatus: Record<DistributionStatus, { text: string; color: TagColor }> = {
  Covers: { text: "التوزيع يحقق مستهدف الجامعة", color: "success" },
  Short: { text: "التوزيع لا يكفي", color: "error" },
  Incomplete: { text: "التوزيع غير مكتمل", color: "warning" },
  NotComparable: { text: "لا يقارن بالجمع", color: "default" },
  NoUniversityTarget: { text: "لا يوجد مستهدف للجامعة", color: "default" },
};

export const direction: Record<Direction, string> = {
  Undefined: "غير محدد",
  HigherIsBetter: "الأعلى أفضل",
  LowerIsBetter: "الأقل أفضل",
  Range: "الوصول إلى قيمة محددة (الأقرب أفضل)",
};

export const aggregation: Record<Aggregation, string> = {
  Undefined: "غير محددة",
  Sum: "مجموع قيم الجهات",
  RatioOfSums: "نسبة مجموع البسط إلى مجموع المقام",
  DistinctCount: "عدد مميز بعد إزالة التكرار",
  WeightedAverage: "متوسط مرجح",
  Central: "قياس مركزي على مستوى الجامعة",
  StatusOnly: "حالة لكل جهة (لا تجمع)",
};

export const level: Record<CollectionLevel, string> = { Department: "القسم العلمي", College: "الكلية", University: "الجامعة" };

export const frequency: Record<Frequency, string> = { Monthly: "شهري", Quarterly: "ربع سنوي", SemiAnnual: "نصف سنوي", Annual: "سنوي", Custom: "مخصص" };

export const targetKind: Record<TargetKind, string> = { Baseline: "خط الأساس", Target: "مستهدف", EachCollege: "مستهدف موحد لكل كلية" };

export const unitKind: Record<OrgUnitKind, string> = {
  University: "الجامعة", College: "كلية", Institute: "معهد", Department: "قسم علمي", Center: "مركز", Administration: "إدارة",
};

/** خيارات قائمة منسدلة من قاموس نصوص. */
export function options<K extends string>(map: Record<K, string | { text: string }>): { value: K; label: string }[] {
  return (Object.keys(map) as K[]).map((k) => ({ value: k, label: typeof map[k] === "string" ? (map[k] as string) : (map[k] as { text: string }).text }));
}

/** رقم بفواصل الآلاف وحتى منزلتين عشريتين، أو شرطة إن لم يوجد. */
export function num(v: number | null | undefined): string {
  if (v === null || v === undefined) return "—";
  return new Intl.NumberFormat("en-US", { maximumFractionDigits: 2 }).format(v);
}

/** القيمة بوحدتها: 82% أو 1,800 عدد. */
export function withUnit(v: number | null | undefined, unit: string): string {
  if (v === null || v === undefined) return "—";
  return unit.trim() === "%" ? `${num(v)}%` : `${num(v)} ${unit}`;
}

// ----- جمع البيانات -----

export const cycleStatus: Record<CycleStatus, { text: string; color: TagColor; hint: string }> = {
  Draft: { text: "قيد الإعداد", color: "default", hint: "لم تفتح بعد؛ لا يراها مدخلو البيانات." },
  Open: { text: "مفتوحة", color: "processing", hint: "النماذج متاحة للإدخال والمراجعة." },
  Closed: { text: "مغلقة", color: "success", hint: "انتهت الدورة؛ النتائج من القيم المعتمدة فيها." },
};

export const submissionStatus: Record<SubmissionStatus, { text: string; color: TagColor }> = {
  NotStarted: { text: "لم يبدأ", color: "default" },
  InProgress: { text: "قيد الإدخال", color: "processing" },
  InReview: { text: "قيد المراجعة", color: "gold" },
  Returned: { text: "معاد للتصحيح", color: "error" },
  Approved: { text: "معتمد", color: "success" },
};

/** حالة الأداء: نقص البيانات حالة مستقلة رمادية، لا تحسب ضعفًا. */
export const performance: Record<Performance, { text: string; color: TagColor; tone: "good" | "near" | "bad" | "none" }> = {
  Achieved: { text: "محقق", color: "success", tone: "good" },
  Near: { text: "قريب من المستهدف", color: "warning", tone: "near" },
  Behind: { text: "متأخر", color: "error", tone: "bad" },
  NoTarget: { text: "بلا مستهدف", color: "default", tone: "none" },
  NoData: { text: "لا بيانات معتمدة", color: "default", tone: "none" },
  NotJudged: { text: "لا يحكم عليه", color: "default", tone: "none" },
  NotStarted: { text: "لم يبدأ مستهدفه", color: "default", tone: "none" },
  Incomplete: { text: "بيانات جزئية", color: "default", tone: "none" },
};

/** مصدر المستهدف الذي قورنت به الجهة. */
export const targetSource: Record<TargetSource, string> = {
  None: "",
  Unit: "مستهدف الجهة",
  EachCollege: "المستهدف الموحد لكل كلية",
  University: "مستهدف الجامعة",
};

export const initiativeStatus: Record<InitiativeStatus, { text: string; color: TagColor }> = {
  Planned: { text: "مخططة", color: "default" },
  InProgress: { text: "قيد التنفيذ", color: "processing" },
  Completed: { text: "مكتملة", color: "success" },
  Stopped: { text: "متوقفة", color: "error" },
};

/**
 * مدة المستهدف بكلمات (بالقاعدة نفسها في الخادم): قصير المدى سنتان أو أقل، ومتوسط ثلاث، وطويل أكثر؛ ومتى يبدأ إن تأخر بدؤه.
 */
export function horizon(start: number | null, end: number | null, first: number, last: number): string {
  const s = start ?? first;
  const e = end ?? last;
  const years = e - s + 1;
  const kind = years <= 2 ? "قصير المدى" : years <= 3 ? "متوسط المدى" : "طويل المدى";
  const span = years === 1 ? "سنة واحدة" : years === 2 ? "سنتان" : `${years} سنوات`;
  return s > first ? `${kind} (${span})، يبدأ في ${s} ويتحقق في ${e}` : `${kind} (${span})، يتحقق في ${e}`;
}

/** الموعد بالأيام الباقية بتوقيت القاهرة: «اليوم» أو «باقٍ 5 أيام» أو «متأخر 2 يوم». */
export function dueText(dueOn: string): string {
  const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo" }).format(new Date());
  const days = Math.round((Date.parse(dueOn) - Date.parse(today)) / 86_400_000);
  if (days === 0) return "اليوم";
  if (days === 1) return "غدًا";
  return days > 0 ? `باقٍ ${daysText(days)}` : `متأخر ${daysText(-days)}`;
}

function daysText(n: number): string {
  return n === 1 ? "يومًا واحدًا" : n === 2 ? "يومين" : n <= 10 ? `${n} أيام` : `${n} يومًا`;
}

/** حجم الملف مقروءًا. */
export function fileSize(bytes: number): string {
  return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
