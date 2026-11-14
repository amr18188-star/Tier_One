import { useEffect, useRef, useState } from "react";

/** عناصر لا يبدأ منها السحب أبدًا: الحقول والأزرار والروابط وعناصر antd وما يقبل الكتابة. */
const INTERACTIVE = [
  "input", "textarea", "select", "button", "a", "label", "summary", "[contenteditable]", "[tabindex]:not([tabindex='-1'])",
  "[role='button']", "[role='link']", "[role='checkbox']", "[role='radio']", "[role='switch']", "[role='slider']", "[role='combobox']",
  "[role='listbox']", "[role='option']", "[role='tab']", "[role='menuitem']", "[role='spinbutton']", "[role='textbox']",
  ".ant-select", ".ant-input", ".ant-input-number", ".ant-picker", ".ant-segmented", ".ant-checkbox-wrapper", ".ant-radio-wrapper",
  ".ant-upload", ".ant-btn", ".ant-popover", ".ant-tooltip", ".ant-tag", ".ant-switch",
].join(", ");
/** سطح السحب: رأس الركيزة كله (عدا أزراره)، ومن غيره الحاويات نفسها فقط (الفجوات والحشوة)، لا النص داخلها. */
const SURFACE = ".fp-head";
const CONTAINERS = ".fp-main, .fp-slide, .lcp-panel, .lcp-panel-head, .f-row, .f-input, .pill-band";

const LOCK = 10; // px قبل تحديد الاتجاه
const THRESHOLD = 0.12; // من عرض العمود

export function useReducedMotion(): boolean {
  const query = "(prefers-reduced-motion: reduce)";
  const [reduced, setReduced] = useState(() => typeof window !== "undefined" && window.matchMedia(query).matches);
  useEffect(() => {
    const m = window.matchMedia(query);
    const on = () => setReduced(m.matches);
    m.addEventListener("change", on);
    return () => m.removeEventListener("change", on);
  }, []);
  return reduced;
}

/** هل يبدأ السحب من هذا العنصر؟ */
export function startsSwipe(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false;
  if (target.closest(INTERACTIVE)) return false;
  if (target.closest(SURFACE)) return true;
  return target.matches(CONTAINERS);
}

/**
 * السحب بين الركائز بالفأرة واللمس (أحداث المؤشر، لا embla: النموذج يعرض ركيزة واحدة بحقولها، والاستثناءات هنا دقيقة):
 * - يبدأ فقط من رأس الركيزة والفجوات والحشوة، لا من حقل أو زر أو رابط أو عنصر antd، ولا والنص محدد.
 * - أول 10px تحدد الاتجاه: العمودي لا يغير الركيزة أبدًا (يبقى تمرير الصفحة، واللمس عليه touch-action: pan-y).
 * - الإفلات بعد 12% من عرض العمود ينتقل؛ وإلا يعود مكانه. العمود يتبع الإصبع إلا مع تقليل الحركة.
 * - الاتجاه من اللغة: في العربية التالية جهة اليسار، فالسحب من اليسار إلى اليمين يكشفها، ومن اليمين إلى اليسار يكشف السابقة.
 */
export function usePillarSwipe(el: HTMLElement | null, opts: {
  rtl: boolean; reduced: boolean; hasPrev: boolean; hasNext: boolean; onPrev: () => void; onNext: () => void;
}) {
  const o = useRef(opts);
  o.current = opts;
  useEffect(() => {
    if (!el) return;
    let start: { x: number; y: number; id: number; w: number } | null = null;
    let mode: "pending" | "drag" = "pending";
    let dx = 0;
    const selected = () => !!window.getSelection()?.toString();
    const forward = (d: number) => (o.current.rtl ? d > 0 : d < 0);
    const reset = (animate: boolean) => {
      el.removeAttribute("data-dragging");
      if (animate && !o.current.reduced) {
        el.style.transition = "transform 180ms cubic-bezier(0.2, 0.8, 0.2, 1)";
        window.setTimeout(() => { el.style.transition = ""; }, 200);
      }
      el.style.transform = "";
    };

    const down = (e: PointerEvent) => {
      if (start || !e.isPrimary || (e.pointerType === "mouse" && e.button !== 0) || selected() || !startsSwipe(e.target)) return;
      start = { x: e.clientX, y: e.clientY, id: e.pointerId, w: el.clientWidth };
      mode = "pending";
      dx = 0;
    };
    const move = (e: PointerEvent) => {
      if (!start || e.pointerId !== start.id) return;
      const mx = e.clientX - start.x;
      const my = e.clientY - start.y;
      if (mode === "pending") {
        if (Math.hypot(mx, my) < LOCK) return;
        if (Math.abs(mx) <= Math.abs(my) || selected()) {
          start = null; // عمودي (أو نص محدد): الإيماءة كلها للصفحة.
          return;
        }
        mode = "drag";
        el.setAttribute("data-dragging", "");
        window.getSelection()?.removeAllRanges();
        try { el.setPointerCapture(e.pointerId); } catch { /* المؤشر انتهى */ }
      }
      e.preventDefault();
      dx = mx;
      const open = forward(dx) ? o.current.hasNext : o.current.hasPrev;
      if (!o.current.reduced) el.style.transform = `translateX(${open ? dx : dx * 0.25}px)`;
    };
    const end = (e: PointerEvent) => {
      if (!start || e.pointerId !== start.id) return;
      const w = start.w;
      const dragged = mode === "drag";
      start = null;
      if (!dragged) return;
      try { el.releasePointerCapture(e.pointerId); } catch { /* سبق إطلاقه */ }
      // النقرة التي تلي السحب لا تصل إلى ما تحت المؤشر.
      const swallow = (c: MouseEvent) => { c.preventDefault(); c.stopPropagation(); };
      el.addEventListener("click", swallow, { capture: true, once: true });
      window.setTimeout(() => el.removeEventListener("click", swallow, { capture: true }), 0);
      const go = e.type === "pointerup" && Math.abs(dx) > w * THRESHOLD;
      const next = forward(dx);
      if (go && (next ? o.current.hasNext : o.current.hasPrev)) {
        reset(false);
        (next ? o.current.onNext : o.current.onPrev)();
      } else reset(true);
    };

    el.addEventListener("pointerdown", down);
    window.addEventListener("pointermove", move, { passive: false });
    window.addEventListener("pointerup", end);
    window.addEventListener("pointercancel", end);
    return () => {
      el.removeEventListener("pointerdown", down);
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", end);
      window.removeEventListener("pointercancel", end);
    };
  }, [el]);
}
