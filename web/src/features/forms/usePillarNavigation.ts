import { useEffect, useRef, useState } from "react";
import { useLang } from "../../shared/i18n";
import { useSearchState } from "../../shared/usePeriodParams";
import type { PillarGroup } from "./FormPillars";
import { usePillarSwipe, useReducedMotion } from "./usePillarSwipe";

/**
 * The pillar shown in the form (pillar= in the URL, else the first) and moving between pillars: by the side buttons, the cards,
 * a swipe or the mobile bar. Unsaved values are saved first, then the view scrolls to the pillar's top, a standard, or one row.
 */
export function usePillarNavigation(pillars: PillarGroup[], flushAll: () => Promise<void>) {
  const { params, update } = useSearchState();
  // الركيزة المعروضة من الرابط (pillar=رمزها)، وإلا الأولى.
  const position = Math.max(0, pillars.findIndex((p) => p.key === params.get("pillar")));
  const pillar = pillars[position];

  // بعد الانتقال إلى ركيزة: أعلاها، أو معيار فيها، أو صف بعينه (التالي الناقص).
  const scrollTo = useRef<{ id: string; focus?: boolean } | null>(null);
  const [, setTick] = useState(0);
  useEffect(() => {
    const target = scrollTo.current;
    if (!target) return;
    scrollTo.current = null;
    requestAnimationFrame(() => {
      const el = document.getElementById(target.id);
      if (!el) return;
      el.scrollIntoView({ behavior: "smooth", block: target.focus ? "center" : "start" });
      if (target.focus) el.querySelector<HTMLInputElement>("input")?.focus({ preventScroll: true });
      else if (target.id === "fp-pillar") el.focus({ preventScroll: true });
    });
  });
  /** ينتقل إلى ركيزة (بالأزرار أو البطاقات أو السحب أو شريط الجوال) بعد حفظ ما لم يحفظ، ثم إلى أعلاها. */
  const [enter, setEnter] = useState<"next" | "prev" | null>(null);
  const goPillar = async (key: string, scrollId = "fp-pillar", focus = false) => {
    await flushAll();
    scrollTo.current = { id: scrollId, focus };
    const to = pillars.findIndex((p) => p.key === key);
    if (key !== pillar?.key) {
      setEnter(to > position ? "next" : "prev");
      update({ pillar: key });
    } else setTick((t) => t + 1);
  };
  // السحب بين الركائز على العمود (usePillarSwipe): التالية جهة نهاية السطر، يسارًا في العربية.
  const [stage, setStage] = useState<HTMLElement | null>(null);
  const rtl = useLang() === "ar";
  const reduced = useReducedMotion();
  usePillarSwipe(stage, {
    rtl, reduced, hasPrev: position > 0, hasNext: position < pillars.length - 1,
    onPrev: () => void goPillar(pillars[position - 1].key),
    onNext: () => void goPillar(pillars[position + 1].key),
  });

  return { position, pillar, goPillar, enter, setStage };
}
