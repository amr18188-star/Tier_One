import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import { api } from "../api/client";
import type { Cycle } from "../api/collection";
import type { PlanQuarter } from "../api/scorecard";
import {
  cycleTimeline, defaultCycleSlot, quarterTimeline, resolveCycle, resolveQuarter, resolveYear,
  type PeriodChange, type PeriodMode, type QuarterOptions, type Slot, type Timeline,
} from "./period";

/** تقويم أرباع الخطة؛ متاح لكل من يرى الدورات أو النماذج أو النتائج. الرفض أو الفشل لا يعاد، والسنوات تأتي من الدورات. */
export function usePlanCalendar() {
  return useQuery({ queryKey: ["plan", "calendar"], queryFn: () => api.get<PlanQuarter[]>("/api/plan/calendar"), retry: false, staleTime: 300_000 });
}

export interface SearchState {
  /** معاملات الرابط كما تعرض الآن (مع آخر تغيير لم يصل إلى الرابط بعد). */
  params: URLSearchParams;
  /** يغير معاملات (القيمة الفارغة تحذف) بلا سجل جديد في التاريخ؛ clear يبدأ من رابط فارغ. */
  update: (patch: Record<string, string | undefined>, clear?: boolean) => void;
}

/**
 * معاملات الرابط مع نسخة تعرض فورًا: React Router يغير الرابط داخل انتقال، فيبقى الخيار القديم ظاهرًا حتى يكتمل الرسم.
 * النسخة تسقط متى تغير الرابط فعلًا. normalize يرتب الرابط قبل كل تغيير (مثلًا تحويل معاملات قديمة).
 * الصفحة التي لها مرشحات في الرابط تمرر النسخة نفسها إلى usePeriodParams فلا يكتب أحدهما فوق الآخر.
 */
export function useSearchState(normalize?: (next: URLSearchParams) => void): SearchState {
  const [params, setParams] = useSearchParams();
  const urlKey = params.toString();
  const [pending, setPending] = useState<{ from: string; to: string } | null>(null);
  if (pending && pending.from !== urlKey) setPending(null);
  const shownKey = pending && pending.from === urlKey ? pending.to : urlKey;
  const shown = useMemo(() => new URLSearchParams(shownKey), [shownKey]);

  const update = (patch: Record<string, string | undefined>, clear = false) => {
    const next = clear ? new URLSearchParams() : new URLSearchParams(shown);
    normalize?.(next);
    for (const [k, v] of Object.entries(patch)) {
      if (v) next.set(k, v);
      else next.delete(k);
    }
    setPending({ from: urlKey, to: next.toString() });
    setParams(next, { replace: true });
  };
  return { params: shown, update };
}

export interface PeriodParamsOptions extends QuarterOptions {
  mode: PeriodMode;
  /** cycle: دورات جمع البيانات؛ undefined ما دامت تحمل. */
  cycles?: readonly Cycle[];
  /** سنوات بدل سنوات الخطة. */
  years?: readonly number[];
  /** ما يؤخذ حين يخلو الرابط منه، قبل الافتراضات المدمجة. */
  defaults?: { year?: number; quarter?: number; cycleId?: string };
  /** حالة الرابط المشتركة مع مرشحات الصفحة (useSearchState)؛ بدونها نسخة خاصة. */
  search?: SearchState;
}

export interface PeriodParams {
  /** التقويم وصل أو فشل، والدورات وصلت (cycle). */
  ready: boolean;
  timeline: Timeline;
  /** القيمة بعد الافتراضات؛ تمرر كما هي إلى PeriodNav. */
  value: { year: number | null; quarter: number | null; cycleId: string | null };
  slot: Slot | null;
  cycle: Cycle | null;
  /** cycle= في الرابط كما هو، لطلب البيانات قبل وصول الدورات. */
  urlCycleId: string | null;
  /** onChange لـ PeriodNav: يكتب cycle وyear (cycle)، أو year وquarter (quarter)، أو year. */
  onChange: (next: PeriodChange) => void;
}

/**
 * السنة والربع والدورة في الرابط (year= وquarter= وcycle=) بافتراضاتها:
 * cycle: الدورة في الرابط وسنتها (الروابط القديمة بـ cycle= وحده تفتح سنتها)، وإلا دورة السنة المطلوبة الافتراضية،
 * وإلا سنة الدورة المفتوحة، وإلا أول سنة في الخطة. نقر سنة يفتح دورتها الافتراضية.
 * quarter: السنة والربع في الرابط، وإلا الربع الجاري. نقر سنة يبقي رقم الربع إن كان متاحًا فيها.
 * year: السنة في الرابط، وإلا السنة الجارية.
 */
export function usePeriodParams(opts: PeriodParamsOptions): PeriodParams {
  const own = useSearchState();
  const { params, update } = opts.search ?? own;
  const calendar = usePlanCalendar();
  const { mode, cycles, years, defaults, disabledQuarters, quarterState } = opts;

  const timeline = mode === "cycle"
    ? cycleTimeline(cycles ?? [], calendar.data, years)
    : quarterTimeline(calendar.data, { disabledQuarters, quarterState }, years);
  const ready = !calendar.isPending && (mode !== "cycle" || cycles !== undefined);

  const urlCycleId = params.get("cycle");
  const urlYear = Number(params.get("year")) || null;
  const urlQuarter = Number(params.get("quarter")) || null;
  const year = urlYear ?? defaults?.year ?? null;

  let value: PeriodParams["value"] = { year: null, quarter: null, cycleId: null };
  let slot: Slot | null = null;
  if (mode === "cycle") {
    const sel = resolveCycle(timeline, urlCycleId ?? defaults?.cycleId, year);
    slot = sel.slot;
    value = { year: sel.year, quarter: slot?.quarter ?? null, cycleId: slot?.cycle?.id ?? null };
  } else if (mode === "quarter") {
    const sel = resolveQuarter(timeline, year, urlQuarter ?? defaults?.quarter);
    slot = sel.slot;
    value = { year: sel.year, quarter: slot?.quarter ?? null, cycleId: null };
  } else {
    value = { year: resolveYear(timeline, year), quarter: null, cycleId: null };
  }

  const onChange = (next: PeriodChange) => {
    if (mode === "cycle") {
      const id = next.cycleId ?? defaultCycleSlot(timeline, next.year)?.cycle?.id;
      update({ cycle: id, year: String(next.year) });
    } else if (mode === "quarter") {
      const same = timeline.slots.find((s) => s.year === next.year && s.quarter === value.quarter && !s.disabled);
      const q = next.quarter !== undefined ? next.quarter : (same ?? resolveQuarter(timeline, next.year, null).slot)?.quarter;
      update({ year: String(next.year), quarter: q ? String(q) : undefined });
    } else {
      update({ year: String(next.year) });
    }
  };

  return { ready, timeline, value, slot, cycle: slot?.cycle ?? null, urlCycleId, onChange };
}
