import type { ReviewCycle, ReviewRow, ReviewState } from "../../api/scorecard";
import { L } from "../../shared/i18n";
import { cycleStatus, num } from "../../shared/labels";
import { type PillarInfo } from "../../shared/pillars";
import { pillarNum } from "../strategy/model";

/** The review page's shared texts and helpers: row states, number and period formats, pillar grouping. */
/**
 * مراجعة لجنة Tier 1 بالجامعة: لكل مؤشر في الربع قائمة بتقدم الكليات (قيمة كل كلية ونسبتها من مستهدف الجامعة)، وحساب الجامعة منها
 * بقاعدة التجميع (مجموع البسوط ÷ مجموع المقامات للنسب)، ثم زر ينقل القيمة إلى مؤشر الجامعة في ذلك الربع.
 * القيمة المنقولة وحدها تدخل بطاقة الأداء والفئة وملف المجلس.
 * الركائز لوحات تطوى (لوحة واحدة مفتوحة، كلها مطوية في البداية، والمفتوحة في ?pillar=01…06)؛ رأس كل ركيزة يعرض حال نقلها في الربع.
 */
export const STATES: { key: ReviewState; ar: string; en: string; color: string }[] = [
  { key: "Ready", ar: "جاهز للنقل", en: "Ready", color: "processing" },
  { key: "Changed", ar: "تغير بعد النقل", en: "Changed", color: "warning" },
  { key: "Incomplete", ar: "لم تكتمل الكليات", en: "Incomplete", color: "gold" },
  { key: "Transferred", ar: "منقول", en: "Transferred", color: "success" },
  { key: "NoData", ar: "لا قيم معتمدة", en: "No approved values", color: "default" },
];

export const stateOf = (s: ReviewState) => STATES.find((x) => x.key === s)!;

/** ترتيب الحالات في ملخص رأس الركيزة: ما يحتاج عملًا أولًا (المنقول يعرض وحده في طرف الرأس). */
export const SUMMARY_ORDER: ReviewState[] = ["Changed", "Ready", "Incomplete", "NoData"];

export const fmt = (v: number | null | undefined, unit: string) => (v === null || v === undefined ? "—" : unit.trim() === "%" ? `${num(v)}%` : num(v));

export const levelText = { Department: "من الأقسام", College: "من الكليات", University: "مركزي على مستوى الجامعة" } as const;

export const cycleStatusEn = { Draft: "Draft", Open: "Open", Closed: "Closed" } as const;

/** مفتاح الركيزة في الرابط (?pillar=): رقمها في الخطة 01…06، و«none» للمعايير بلا ركيزة. */
export const pillarKey = (index: number) => (index >= 0 ? pillarNum(index) : "none");

/** «14 مؤشرًا» بقواعد العدد في العربية. */
export function indicatorCount(n: number): string {
  if (n === 0) return L("لا مؤشرات", "No indicators");
  if (n === 1) return L("مؤشر واحد", "1 indicator");
  if (n === 2) return L("مؤشران", "2 indicators");
  return L(n <= 10 ? `${n} مؤشرات` : `${n} مؤشرًا`, `${n} indicators`);
}

export function periodText(c: ReviewCycle): string {
  return L(`${c.label} (${cycleStatus[c.status].text})`, `${c.year} · Q${c.quarter} (${cycleStatusEn[c.status]})`);
}

export interface PillarGroup { key: string; index: number; info: PillarInfo | undefined; all: ReviewRow[]; shown: ReviewRow[] }

/** تغيير الربع يسقط رابط المؤشر (?indicator=) كما كان؛ ويبقي الركيزة المفتوحة. */
export const dropIndicator = (next: URLSearchParams) => next.delete("indicator");
