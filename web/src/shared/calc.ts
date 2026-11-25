import type { Aggregation, CollectionLevel, Direction } from "../api/tier1";

export interface CalcSource {
  aggregation: Aggregation; level: CollectionLevel; direction: Direction; unit: string; numerator: string | null; denominator: string | null;
}

/**
 * كيف تحسب قيمة المؤشر لكل مستوى، بجمل يفهمها غير المتخصص (القواعد نفسها في الخادم):
 * ما تدخله الجهة، وكيف تحسب قيمة الكلية، وكيف تحسب قيمة الجامعة، وكيف يحكم على الأداء.
 */
export function howCalculated(c: CalcSource): { entry: string; college: string; university: string; judge: string } {
  const from = c.level === "Department" ? "الأقسام" : "الكليات";
  const n = c.numerator ? `«${c.numerator}»` : "البسط";
  const d = c.denominator ? `«${c.denominator}»` : "المقام";
  const judge = c.direction === "Range"
    ? "المطلوب الوصول إلى القيمة المستهدفة نفسها؛ الابتعاد عنها زيادة أو نقصًا يحسب تأخرًا (محقق حتى 5%، وقريب حتى 10%)."
    : c.direction === "LowerIsBetter" ? "الأقل أفضل: نسبة الإنجاز = المستهدف ÷ الفعلي."
    : c.direction === "HigherIsBetter" ? "الأعلى أفضل: نسبة الإنجاز = الفعلي ÷ المستهدف."
    : "الاتجاه لم يحدد بعد، فلا يحكم على الأداء.";

  if (c.level === "University" || c.aggregation === "Central")
    return { entry: "يدخل مرة واحدة على مستوى الجامعة كلها من الجهة المالكة.", college: "لا يقاس للكليات.", university: "القيمة المدخلة نفسها.", judge };

  switch (c.aggregation) {
    case "RatioOfSums": {
      const percent = c.unit.trim() === "%";
      return {
        entry: `تدخل كل جهة الرقمين: ${n} و${d}، ولا تدخل النسبة نفسها.`,
        college: c.level === "Department"
          ? `مجموع ${n} لأقسام الكلية ÷ مجموع ${d}${percent ? " × 100" : ""}.`
          : `${n} ÷ ${d}${percent ? " × 100" : ""} للكلية نفسها، وتقارن بمستهدفها أو بالمستهدف الموحد لكل كلية؛ ولا تقارن بمستهدف الجامعة (يعرض بجانبها للاطلاع).`,
        university: `مجموع ${n} لكل ${from} ÷ مجموع ${d} لها${percent ? " × 100" : ""}، لا متوسط نسب ${from}، فالكلية الكبيرة تؤثر بحجمها.`,
        judge,
      };
    }
    case "WeightedAverage":
      return {
        entry: `تدخل كل جهة القيمة و${d} (أساس الترجيح).`,
        college: c.level === "Department" ? `متوسط قيم الأقسام مرجحًا بـ${d}.` : "القيمة المدخلة للكلية.",
        university: `متوسط قيم ${from} مرجحًا بـ${d}: مجموع (القيمة × الوزن) ÷ مجموع الأوزان.`,
        judge,
      };
    case "Sum":
      return {
        entry: "تدخل كل جهة عددها.",
        college: c.level === "Department" ? "مجموع أعداد أقسام الكلية." : "عدد الكلية المدخل.",
        university: `مجموع أعداد ${from}. لا يحكم عليه حتى تعتمد كل ${from} (يظهر «بيانات جزئية»).`,
        judge,
      };
    case "DistinctCount":
      return {
        entry: c.level === "Department"
          ? "يدخل كل قسم عدده، وتدخل الكلية عددها بعد إزالة التكرار بين أقسامها، وتدخل الجهة المركزية عدد الجامعة بعد إزالة التكرار."
          : "تدخل كل كلية عددها، وتدخل الجهة المركزية عدد الجامعة كلها بعد إزالة التكرار.",
        college: "العدد الذي أدخلته الكلية لنفسها (لا مجموع أقسامها).",
        university: `العدد المدخل مركزيًا بعد إزالة التكرار، لا مجموع ${from} (ما تشترك فيه كليتان يعد مرة واحدة).`,
        judge,
      };
    case "StatusOnly":
      return { entry: "تدخل كل جهة حالتها.", college: "حالة الكلية.", university: "لا تجمع؛ تعرض حالة كل كلية.", judge };
    default:
      return { entry: "قاعدة التجميع لم تحدد بعد.", college: "—", university: "لا تحسب حتى تحدد القاعدة ويعتمد التعريف.", judge };
  }
}
