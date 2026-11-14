import { createContext, useContext } from "react";
import type { IndicatorResult } from "../../api/collection";
import type { IndicatorNode, InitiativeNode, PillarNode, StandardNode, StrategyTree } from "../../api/tier1";

/**
 * ما تشترك فيه عروض الخطة (الخريطة، والخط الزمني، والهيكل والعلاقات) وصفحة تفاصيل العنصر: العنصر المختار، ولون كل ركيزة،
 * وحالة كل مؤشر (من آخر نتيجة معتمدة إن وجدت، وإلا من اكتمال خطة مستهدفه).
 */

export type Selection =
  | { kind: "root" }
  | { kind: "pillar"; pillar: PillarNode }
  | { kind: "standard"; standard: StandardNode }
  | { kind: "initiative"; initiative: InitiativeNode; standard: StandardNode }
  | { kind: "indicator"; indicator: IndicatorNode; standard: StandardNode };

export type Editing =
  | { kind: "pillar"; pillar?: PillarNode }
  | { kind: "standard"; standard?: StandardNode; pillarId?: string }
  | { kind: "initiative"; initiative?: InitiativeNode; standardId: string }
  | { kind: "indicator"; standardCode: string }
  /** after: مفتاح العنصر الذي تفتح تفاصيله بعد الحذف (P:/S:)، أو "board" للوحة «الهيكل والعلاقات». */
  | { kind: "delete"; title: string; explain: string; url: string; after: string };

/** explore صفحة تفاصيل عنصر واحد (?view=explore&node=…)، تفتح من داخل العروض ولا تبويب لها. */
export type View = "map" | "roadmap" | "explore";

export { paletteAt, tint } from "../../shared/pillars";

export function allStandards(t: StrategyTree): StandardNode[] {
  return [...t.pillars.flatMap((p) => p.standards), ...t.unassigned];
}

export function allIndicators(t: StrategyTree): IndicatorNode[] {
  return allStandards(t).flatMap((s) => s.indicators);
}

export function allInitiatives(t: StrategyTree): InitiativeNode[] {
  return allStandards(t).flatMap((s) => s.initiatives);
}

/** ترتيب ركيزة المعيار (للونه)، و-1 للمعيار بلا ركيزة. */
export function pillarIndexOf(t: StrategyTree, standard: StandardNode): number {
  return t.pillars.findIndex((p) => p.id === standard.pillarId);
}

export function find(t: StrategyTree, key: string): Selection {
  const [kind, id] = [key.slice(0, 2), key.slice(2)];
  if (kind === "P:") {
    const pillar = t.pillars.find((p) => p.id === id);
    if (pillar) return { kind: "pillar", pillar };
  }
  for (const s of allStandards(t)) {
    if (kind === "S:" && s.id === id) return { kind: "standard", standard: s };
    const m = kind === "M:" ? s.initiatives.find((i) => i.id === id) : undefined;
    if (m) return { kind: "initiative", initiative: m, standard: s };
    const i = kind === "I:" ? s.indicators.find((x) => x.code === id) : undefined;
    if (i) return { kind: "indicator", indicator: i, standard: s };
  }
  return { kind: "root" };
}

/** رقم الركيزة للعرض بترتيبها في الخطة: 01…06، كما في الخريطة ولوحة الهيكل. */
export function pillarNum(index: number): string {
  return String(index + 1).padStart(2, "0");
}

/** رابط صفحة تفاصيل عنصر: ?view=explore&node=P:…|S:…|M:…|I:… */
export function detailSearch(key: string): string {
  return `?${new URLSearchParams({ view: "explore", node: key })}`;
}

/**
 * رابط لوحة «الهيكل والعلاقات» (?view=board) لعنصر من الخطة: ركيزته (none للمعيار بلا ركيزة)، والمؤشر أو المبادرة محددًا فيها.
 * إن لم يكن العنصر في الخطة تبقى الركيزة المعطاة إن وجدت؛ واللوحة تصحح ما لا يصلح بنفسها.
 */
export function boardSearch(t: StrategyTree, key: string | null, pillar?: string | null): string {
  const out = new URLSearchParams({ view: "board" });
  const sel = key ? find(t, key) : null;
  const id = !sel || sel.kind === "root" ? pillar ?? null
    : sel.kind === "pillar" ? sel.pillar.id
    : pillarIndexOf(t, sel.standard) >= 0 ? sel.standard.pillarId : "none";
  if (id) out.set("pillar", id);
  if (key && sel && (sel.kind === "indicator" || sel.kind === "initiative")) out.set("focus", key);
  return `?${out}`;
}

/** سنة البدء وسنة التحقق الفعليتان (الفارغة تعني أول الخطة أو آخرها). */
export function span(i: IndicatorNode, years: number[]) {
  return { start: i.planStartYear ?? years[0], end: i.planEndYear ?? years[years.length - 1] };
}

export type HorizonKind = "short" | "medium" | "long";

export function horizonKind(i: IndicatorNode, years: number[]): HorizonKind {
  const { start, end } = span(i, years);
  const n = end - start + 1;
  return n <= 2 ? "short" : n <= 3 ? "medium" : "long";
}

export function targetOf(i: IndicatorNode, year: number): number | null {
  return i.targets.find((t) => t.year === year)?.value ?? null;
}

/** المستهدف النهائي: قيمة سنة التحقق، أو آخر قيمة قبلها إن لم تحدد. */
export function finalTarget(i: IndicatorNode, years: number[]): number | null {
  const { start, end } = span(i, years);
  for (let y = end; y >= start; y--) {
    const v = targetOf(i, y);
    if (v !== null) return v;
  }
  return null;
}

/** خطة المستهدف مكتملة حين يكون لكل سنة من البدء حتى التحقق قيمة. */
export function targetsComplete(i: IndicatorNode, years: number[]): boolean {
  const { start, end } = span(i, years);
  for (let y = start; y <= end; y++) if (targetOf(i, y) === null) return false;
  return true;
}

export type DotState = "Achieved" | "Near" | "Behind" | "planned" | "gap";

/** حالة المؤشر في الخريطة: الأداء في آخر نتيجة إن حكم عليه، وإلا هل اكتملت خطة مستهدفه. */
export function dotState(i: IndicatorNode, years: number[], r: IndicatorResult | undefined): DotState {
  if (r && (r.performance === "Achieved" || r.performance === "Near" || r.performance === "Behind")) return r.performance;
  return targetsComplete(i, years) ? "planned" : "gap";
}

export const dotText: Record<DotState, string> = {
  Achieved: "محقق في آخر قياس",
  Near: "قريب من المستهدف",
  Behind: "متأخر عن المستهدف",
  planned: "خطة مستهدفه مكتملة، ولم يحكم عليه بعد",
  gap: "خطة مستهدفه ناقصة",
};

export function countDots(list: IndicatorNode[], years: number[], results: ResultMap) {
  const c: Record<DotState, number> = { Achieved: 0, Near: 0, Behind: 0, planned: 0, gap: 0 };
  for (const i of list) c[dotState(i, years, results.get(i.code))]++;
  return c;
}

export type ResultMap = Map<string, IndicatorResult>;

export interface PlanContext {
  tree: StrategyTree;
  results: ResultMap;
  /** اسم الجهة والدورة اللتين أخذت منهما النتائج، أو null إن لم تتح. */
  resultsFrom: string | null;
  /** سنة الخطة التي تقيسها دورة النتائج (لموضع القيمة الفعلية على مسار المستهدف). */
  resultsYear: number | null;
  select: (key: string) => void;
  edit: (e: Editing) => void;
}

export const PlanCtx = createContext<PlanContext | null>(null);

export function usePlan(): PlanContext {
  const ctx = useContext(PlanCtx);
  if (!ctx) throw new Error("usePlan outside PlanCtx");
  return ctx;
}
