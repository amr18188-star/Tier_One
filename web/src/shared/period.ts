// منطق شريط السنة والربع (PeriodNav) بلا واجهة: السنوات، وخانات الأرباع وحالاتها، والتحديد الافتراضي، والتنقل والنصوص.
import type { Cycle } from "../api/collection";
import type { PlanQuarter } from "../api/scorecard";
import { formatDate } from "./format";
import { L } from "./i18n";
import { dueText } from "./labels";

/**
 * cycle: الخانات دورات جمع البيانات بسنتها وربعها، والربع بلا دورة «لا دورة» معطل.
 * quarter: الخانات أرباع تقويم الخطة، وحالتها من تواريخها (انتهى، جارٍ، لم يبدأ).
 * year: السنوات وحدها.
 */
export type PeriodMode = "cycle" | "quarter" | "year";

/** حالة الخانة: مغلق/انتهى، مفتوح/جارٍ، لم يفتح/لم يبدأ، أو لا دورة له. */
export type SlotState = "closed" | "open" | "future" | "none";
/** ربع من أرباع السنة، أو دورة خارج الأرباع الأربعة: بروفة، أو دورة سنوية بلا ربع. */
export type SlotKind = "quarter" | "trial" | "annual";

export interface Slot {
  /** معرف الدورة، أو «السنة-الربع». */
  key: string;
  year: number;
  quarter: number | null;
  kind: SlotKind;
  source: "cycle" | "plan";
  cycle: Cycle | null;
  plan: PlanQuarter | null;
  state: SlotState;
  disabled: boolean;
  /** سبب التعطيل أو ملاحظة تضاف إلى سطر الحالة (quarter). */
  note?: string;
}

export interface Timeline {
  years: number[];
  /** بترتيب الزمن: لكل سنة أرباعها الأربعة ثم دوراتها الأخرى. */
  slots: Slot[];
}

/** القيمة المتحكم بها: السنة، والربع (quarter)، والدورة (cycle). */
export interface PeriodValue { year?: number | null; quarter?: number | null; cycleId?: string | null }
/** ما يرسله الشريط: نقر سنة يرسل { year } وحده، ونقر خانة يرسل سنتها وربعها (ودورتها في cycle). */
export interface PeriodChange { year: number; quarter?: number | null; cycleId?: string }

export interface QuarterOptions {
  /** أرباع معطلة (quarter): قائمة، أو دالة ترجع true أو نص السبب («له دورة»). */
  disabledQuarters?: ReadonlyArray<{ year: number; quarter: number }> | ((year: number, quarter: number) => boolean | string);
  /** تجاوز حالة الربع المحسوبة من تواريخه (quarter). "none" يعطله. */
  quarterState?: (year: number, quarter: number, plan: PlanQuarter | null) => SlotState | undefined;
}

const RANK: Record<Cycle["status"], number> = { Open: 0, Closed: 1, Draft: 2 };

function cycleState(c: Cycle | null): SlotState {
  if (!c) return "none";
  return c.status === "Open" ? "open" : c.status === "Closed" ? "closed" : "future";
}

/** تاريخ اليوم بتوقيت القاهرة (YYYY-MM-DD). */
export function cairoToday(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo" }).format(new Date());
}

function uniqueSorted(list: number[]): number[] {
  return [...new Set(list)].sort((a, b) => a - b);
}

/** سنوات الخطة: من التقويم (ومعها سنة كل دورة غير مسودة خارجه في cycle)، وبلا تقويم من الدورات غير المسودة. */
export function planYears(mode: PeriodMode, calendar: readonly PlanQuarter[] | undefined, cycles: readonly Cycle[] = []): number[] {
  const fromCycles = mode === "cycle" ? cycles.filter((c) => c.status !== "Draft").map((c) => c.targetYear) : [];
  return uniqueSorted([...(calendar ?? []).map((q) => q.year), ...fromCycles]);
}

function findPlan(calendar: readonly PlanQuarter[] | undefined, year: number, quarter: number | null) {
  return quarter ? calendar?.find((q) => q.year === year && q.quarter === quarter) ?? null : null;
}

/**
 * cycle: كل ربع يأخذ دورته الرسمية (غير البروفة)، ولا دورة له خانة معطلة. البروفة والدورة السنوية (بلا ربع) خانة
 * إضافية بعد الأرباع الأربعة في سنتها.
 */
export function cycleTimeline(cycles: readonly Cycle[], calendar: readonly PlanQuarter[] | undefined, years?: readonly number[]): Timeline {
  const ys = years ? uniqueSorted([...years]) : planYears("cycle", calendar, cycles);
  const slots: Slot[] = [];
  for (const year of ys) {
    const inYear = cycles.filter((c) => c.targetYear === year);
    const used = new Set<string>();
    for (let q = 1; q <= 4; q++) {
      const official = inYear.filter((c) => !c.trial && c.quarter === q).sort((a, b) => RANK[a.status] - RANK[b.status])[0] ?? null;
      if (official) used.add(official.id);
      slots.push({
        key: official?.id ?? `${year}-${q}`, year, quarter: q, kind: "quarter", source: "cycle",
        cycle: official, plan: findPlan(calendar, year, q), state: cycleState(official), disabled: !official,
      });
    }
    inYear
      .filter((c) => !used.has(c.id))
      .sort((a, b) => (a.quarter ?? 5) - (b.quarter ?? 5) || a.opensOn.localeCompare(b.opensOn))
      .forEach((c) => slots.push({
        key: c.id, year, quarter: c.quarter, kind: c.trial ? "trial" : c.quarter ? "quarter" : "annual", source: "cycle",
        cycle: c, plan: findPlan(calendar, year, c.quarter), state: cycleState(c), disabled: false,
      }));
  }
  return { years: ys, slots };
}

/** حالة ربع الخطة من تواريخه: انتهى (closed)، جارٍ (open)، لم يبدأ (future). */
export function planState(plan: PlanQuarter | null, today = cairoToday()): SlotState {
  if (!plan) return "future";
  if (plan.end < today) return "closed";
  return plan.current || plan.start <= today ? "open" : "future";
}

/** quarter: أرباع التقويم الأربعة لكل سنة، بحالتها من تواريخها أو من quarterState، والمعطل من disabledQuarters. */
export function quarterTimeline(calendar: readonly PlanQuarter[] | undefined, opts: QuarterOptions = {}, years?: readonly number[]): Timeline {
  const ys = years ? uniqueSorted([...years]) : planYears("quarter", calendar);
  const today = cairoToday();
  const off = opts.disabledQuarters;
  const disabledBy = (year: number, quarter: number): boolean | string =>
    !off ? false : typeof off === "function" ? off(year, quarter) : off.some((d) => d.year === year && d.quarter === quarter);
  const slots: Slot[] = [];
  for (const year of ys)
    for (let q = 1; q <= 4; q++) {
      const plan = findPlan(calendar, year, q);
      const state = opts.quarterState?.(year, q, plan) ?? planState(plan, today);
      const why = disabledBy(year, q);
      slots.push({
        key: `${year}-${q}`, year, quarter: q, kind: "quarter", source: "plan", cycle: null, plan, state,
        disabled: state === "none" || why !== false, note: typeof why === "string" ? why : undefined,
      });
    }
  return { years: ys, slots };
}

// ----- التحديد -----

/** الدورة التي تفتح عليها السنة: المفتوحة، وإلا آخر مغلقة، وإلا أول دورة قيد الإعداد. */
export function defaultCycleSlot(tl: Timeline, year: number): Slot | null {
  const ys = tl.slots.filter((s) => s.year === year && s.cycle);
  return ys.find((s) => s.state === "open" && s.kind === "quarter")
    ?? ys.find((s) => s.state === "open")
    ?? ys.findLast((s) => s.state === "closed" && s.kind === "quarter")
    ?? ys.findLast((s) => s.state === "closed")
    ?? ys.find((s) => s.state === "future")
    ?? null;
}

/**
 * cycle: الدورة المطلوبة وسنتها، ولو كانت مغلقة. وإلا السنة المطلوبة، وإلا سنة الدورة المفتوحة، وإلا أول سنة في الخطة؛
 * ثم دورتها الافتراضية.
 */
export function resolveCycle(tl: Timeline, cycleId: string | null | undefined, year: number | null | undefined): { year: number | null; slot: Slot | null } {
  const byId = cycleId ? tl.slots.find((s) => s.cycle?.id === cycleId) : undefined;
  if (byId) return { year: byId.year, slot: byId };
  let y = year != null && tl.years.includes(year) ? year : null;
  if (y === null) {
    const open = tl.slots.find((s) => s.state === "open" && s.kind === "quarter") ?? tl.slots.find((s) => s.state === "open");
    y = open?.year ?? tl.years[0] ?? null;
  }
  return { year: y, slot: y === null ? null : defaultCycleSlot(tl, y) };
}

/** السنة الجارية في الخطة (فيها الربع الجاري)، وإلا أول سنة. */
export function currentYear(tl: Timeline): number | null {
  return tl.slots.find((s) => s.plan?.current)?.year ?? tl.years[0] ?? null;
}

/**
 * quarter: في السنة المطلوبة (أو الجارية) الربع المطلوب ولو كان معطلًا، وإلا الربع الجاري، وإلا أول ربع متاح.
 */
export function resolveQuarter(tl: Timeline, year: number | null | undefined, quarter: number | null | undefined): { year: number | null; slot: Slot | null } {
  const y = year != null && tl.years.includes(year) ? year : currentYear(tl);
  if (y === null) return { year: null, slot: null };
  const inYear = tl.slots.filter((s) => s.year === y);
  const exact = quarter != null ? inYear.find((s) => s.quarter === quarter) : undefined;
  if (exact) return { year: y, slot: exact };
  return { year: y, slot: inYear.find((s) => s.plan?.current && !s.disabled) ?? inYear.find((s) => !s.disabled) ?? null };
}

/** year: السنة المطلوبة إن كانت في الخطة، وإلا السنة الجارية، وإلا أول سنة. */
export function resolveYear(tl: Timeline, year: number | null | undefined): number | null {
  return year != null && tl.years.includes(year) ? year : currentYear(tl);
}

/** الخانة المحددة في القيمة كما هي (بلا افتراضات): دورة بمعرفها، أو ربع بسنته ورقمه. */
export function selectedSlot(tl: Timeline, mode: PeriodMode, value: PeriodValue): Slot | null {
  if (mode === "cycle") return value.cycleId ? tl.slots.find((s) => s.cycle?.id === value.cycleId) ?? null : null;
  if (mode === "quarter") return tl.slots.find((s) => s.year === value.year && s.quarter === value.quarter) ?? null;
  return null;
}

/** الخانة السابقة أو التالية المتاحة، عبر حدود السنوات. بلا خانة محددة تبدأ من طرف السنة المعروضة. */
export function stepSlot(tl: Timeline, current: Slot | null, year: number | null, dir: -1 | 1): Slot | null {
  let i = current ? tl.slots.findIndex((s) => s.key === current.key) : -1;
  if (i < 0) {
    const inYear = tl.slots.map((s, k) => (s.year === year ? k : -1)).filter((k) => k >= 0);
    if (inYear.length === 0) return null;
    i = dir === 1 ? inYear[0] - 1 : inYear[inYear.length - 1] + 1;
  }
  for (let k = i + dir; k >= 0 && k < tl.slots.length; k += dir) if (!tl.slots[k].disabled) return tl.slots[k];
  return null;
}

// ----- النصوص -----

const ORDINAL = ["", "الأول", "الثاني", "الثالث", "الرابع"];

export function quarterName(year: number, quarter: number): string {
  return L(`الربع ${ORDINAL[quarter] ?? quarter} ${year}`, `Q${quarter} ${year}`);
}

/** اسم الخانة: «الربع الثالث 2027»، والبروفة والدورة السنوية باسم دورتها. */
export function slotName(s: Slot): string {
  if (s.kind === "quarter" && s.quarter && !s.cycle?.trial) return quarterName(s.year, s.quarter);
  return s.cycle?.name ?? String(s.year);
}

/** نص الخانة القصير في الشريط. */
export function chipLabel(s: Slot): string {
  const q = s.quarter ? L(`ر${s.quarter}`, `Q${s.quarter}`) : "";
  if (s.kind === "trial") return q ? `${L("بروفة", "Trial")} ${q}` : L("بروفة", "Trial");
  if (s.kind === "annual") return L("سنوية", "Annual");
  return q;
}

/** الأيام الباقية حتى الموعد بتوقيت القاهرة (بالقاعدة نفسها في dueText). */
export function daysLeft(dueOn: string): number {
  return Math.round((Date.parse(dueOn) - Date.parse(cairoToday())) / 86_400_000);
}

function dueEn(dueOn: string): string {
  const d = daysLeft(dueOn);
  if (d === 0) return "due today";
  if (d === 1) return "due tomorrow";
  return d > 0 ? `${d} days left` : `${-d} days late`;
}

/** «باقٍ 5 أيام» أو «متأخر يومين» بلغة الواجهة. */
export function dueLabel(dueOn: string): string {
  return L(dueText(dueOn), dueEn(dueOn));
}

/** سطر الحالة تحت اسم الخانة. أعداد الدورة من الخادم في نطاق المستخدم. */
export function stateLine(s: Slot): string {
  const c = s.cycle;
  if (s.source === "plan") {
    const p = s.plan;
    const range = p ? `${formatDate(p.start)} – ${formatDate(p.end)}` : "";
    const base = s.state === "none" ? L("غير متاح", "Not available")
      : !p ? ""
      : s.state === "closed" ? L(`انتهى · ${range}`, `Ended · ${range}`)
      : s.state === "open" ? L(`الربع الجاري · ${range}`, `Current quarter · ${range}`)
      : L(`لم يبدأ بعد · يبدأ ${formatDate(p.start)}`, `Not started · starts ${formatDate(p.start)}`);
    return [base, s.note].filter(Boolean).join(" · ");
  }
  if (!c) return L("لا دورة", "No cycle");
  if (s.state === "future") return L(`لم يُفتح بعد · يفتح ${formatDate(c.opensOn)}`, `Not open yet · opens ${formatDate(c.opensOn)}`);
  if (s.state === "open") return L(`مفتوح حتى ${formatDate(c.dueOn)} · ${dueText(c.dueOn)}`, `Open until ${formatDate(c.dueOn)} · ${dueEn(c.dueOn)}`);
  const approved = c.formsByStatus.Approved ?? 0;
  if (c.forms === 0) return L("مغلق", "Closed");
  if (approved >= c.forms) return L("مغلق · اعتُمد بالكامل", "Closed · fully approved");
  return L(`مغلق · ${approved}/${c.forms} معتمد`, `Closed · ${approved}/${c.forms} approved`);
}
