import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import useEmblaCarousel from "embla-carousel-react";
import type { EmblaCarouselType } from "embla-carousel";
import { ArrowLeftOutlined, ArrowRightOutlined, LeftOutlined, RightOutlined } from "@ant-design/icons";
import { L, useLang } from "../../shared/i18n";

/**
 * شريط الركائز: بطاقات تسحب يمينًا ويسارًا (embla)، ثلاث في العرض على الشاشة العريضة واثنتان على اللوحي وواحدة على الجوال
 * (عرض الشريحة في strategy.css). الاتجاه من لغة الواجهة، فالسحب والترتيب والأسهم تتبع اتجاه القراءة.
 * بلا التفاف: للركائز ترتيب وأول وآخر، والزران يتعطلان عند الطرفين.
 * السحب بالفأرة واللمس من embla، ولوحة اللمس بالعجلة الأفقية هنا (خطوة لكل إيماءة)، والأسهم وHome/End من لوحة المفاتيح.
 * النقرة بعد السحب يمنعها embla نفسه (مستمع click في طور الالتقاط على الإطار) فلا تفتح البطاقة.
 * مع تقليل الحركة ينتقل الشريط فورًا بلا انزلاق.
 * وفي أسفل الصفحة تكمل عجلة الفأرة ولوحة اللمس (لأسفل ولأعلى) حركتها في الشريط؛ القواعد عند المستمع أدناه.
 */
export interface CarouselSlide {
  key: string;
  node: ReactNode;
  /** اسم الشريحة لقارئ الشاشة (النقاط وحالة الموضع). */
  name?: string;
}

const REDUCE = "(prefers-reduced-motion: reduce)";
const FINE_POINTER = "(hover: hover) and (pointer: fine)";
const HINT_KEY = "sp-car-wheel-hint";
let hintShown = false;

/** الصفحة في أسفلها (بسماح 2px). */
function atPageBottom(): boolean {
  const doc = document.scrollingElement ?? document.documentElement;
  return doc.scrollTop + window.innerHeight >= doc.scrollHeight - 2;
}

/** حاوية تمرير داخلية تحت المؤشر ما زالت تتحرك في اتجاه العجلة: العجلة لها لا للشريط. */
function innerScroller(from: Element, scope: Element, dy: number): boolean {
  for (let el: Element | null = from; el && el !== scope; el = el.parentElement) {
    const oy = getComputedStyle(el).overflowY;
    if ((oy === "auto" || oy === "scroll") && el.scrollHeight > el.clientHeight + 1
      && (dy > 0 ? el.scrollTop + el.clientHeight < el.scrollHeight - 1 : el.scrollTop > 0)) return true;
  }
  return false;
}

/** يظهر التلميح مرة في الجلسة؛ وإن منع المتصفح التخزين فمرة لكل تحميل. */
function takeHint(): boolean {
  if (hintShown) return false;
  hintShown = true;
  try {
    if (sessionStorage.getItem(HINT_KEY)) return false;
    sessionStorage.setItem(HINT_KEY, "1");
  } catch {
    // بلا تخزين: يكفي المتغير.
  }
  return true;
}

function useReducedMotion(): boolean {
  const [reduced, setReduced] = useState(() => window.matchMedia(REDUCE).matches);
  useEffect(() => {
    const m = window.matchMedia(REDUCE);
    const on = () => setReduced(m.matches);
    m.addEventListener("change", on);
    return () => m.removeEventListener("change", on);
  }, []);
  return reduced;
}

/**
 * single: بطاقة واحدة في العرض على كل الشاشات (مرشح الركيزة في الخط الزمني)، والموضع selected يأتي من الأب
 * ويرجع إليه بـ onSelect؛ والعجلة في أسفل الصفحة لا تعمل فيه (الشريط مرشح أعلى المحتوى، لا قائمة تكمل التمرير).
 */
export function PillarCarousel({ label, slides, single = false, selected, onSelect }: {
  label: string; slides: CarouselSlide[]; single?: boolean; selected?: number; onSelect?: (index: number) => void;
}) {
  const rtl = useLang() === "ar";
  const dir = rtl ? "rtl" : "ltr";
  const reduced = useReducedMotion();
  const [startIndex] = useState(() => selected ?? 0);
  const [viewportRef, api] = useEmblaCarousel({ direction: dir, align: "start", containScroll: "trimSnaps", loop: false, startIndex });
  const [snaps, setSnaps] = useState(0);
  const [index, setIndex] = useState(0);
  const [perView, setPerView] = useState(1);
  const trackId = useId();
  const total = slides.length;

  const sync = useCallback((a: EmblaCarouselType) => {
    const nodes = a.slideNodes();
    setSnaps(a.scrollSnapList().length);
    setIndex(a.selectedScrollSnap());
    // عرض الشريحة يشمل فجوتها، والإطار أضيق بفجوة واحدة؛ فالتقريب يعطي عدد البطاقات في العرض.
    setPerView(nodes.length ? Math.max(1, Math.round(a.rootNode().clientWidth / nodes[0].offsetWidth)) : 1);
  }, []);

  useEffect(() => {
    if (!api) return;
    sync(api);
    api.on("select", sync).on("reInit", sync).on("resize", sync);
    return () => {
      api.off("select", sync).off("reInit", sync).off("resize", sync);
    };
  }, [api, sync]);

  // الموضع المختار يخرج إلى الأب (الرابط)، ويتبعه الشريط إن تغير من خارجه (رابط مباشر، رجوع وتقدم).
  const report = useRef(onSelect);
  report.current = onSelect;
  useEffect(() => {
    if (!api) return;
    const out = () => report.current?.(api.selectedScrollSnap());
    api.on("select", out);
    return () => {
      api.off("select", out);
    };
  }, [api]);
  useEffect(() => {
    if (api && selected !== undefined && selected !== api.selectedScrollSnap()) api.scrollTo(selected, true);
  }, [api, selected]);

  // أثناء السحب: لا تحديد للنص ومؤشر القبضة. ومع تقليل الحركة يستقر الشريط على موضعه فورًا بعد الإفلات.
  // في وضع البطاقة الواحدة تكون البطاقة بعرض الإطار، فلا تعيدها embla إلا بسحب نصفه أو بدفعة سريعة؛
  // هنا يكفي سحب هادئ بثُمن الإطار تقريبًا (12%) لينتقل إلى الركيزة المجاورة.
  useEffect(() => {
    if (!api) return;
    const root = api.rootNode();
    let from = 0;
    let fromIndex = 0;
    const down = () => {
      root.setAttribute("data-dragging", "");
      from = api.internalEngine().location.get();
      fromIndex = api.selectedScrollSnap();
    };
    const up = () => {
      root.removeAttribute("data-dragging");
      const moved = api.internalEngine().location.get() - from; // سالب = نحو التالي، في الاتجاهين
      if (single && api.selectedScrollSnap() === fromIndex && Math.abs(moved) > root.clientWidth * 0.12) {
        if (moved < 0) api.scrollNext(reduced); else api.scrollPrev(reduced);
      } else if (reduced) api.scrollTo(api.selectedScrollSnap(), true);
    };
    api.on("pointerDown", down).on("pointerUp", up);
    return () => {
      api.off("pointerDown", down).off("pointerUp", up);
    };
  }, [api, reduced, single]);

  // لوحة اللمس والعجلة الأفقية (أو Shift مع العجلة): خطوة واحدة لكل إيماءة. التمرير العمودي يبقى للصفحة.
  useEffect(() => {
    if (!api) return;
    const root = api.rootNode();
    let sum = 0;
    let locked = false;
    let timer = 0;
    const onWheel = (e: WheelEvent) => {
      const unit = e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? root.clientWidth : 1;
      const dx = e.deltaX * unit;
      if (Math.abs(dx) <= Math.abs(e.deltaY * unit)) return;
      e.preventDefault();
      window.clearTimeout(timer);
      timer = window.setTimeout(() => { sum = 0; locked = false; }, 200);
      if (locked) return;
      sum += dx;
      if (Math.abs(sum) < 40) return;
      locked = true;
      // deltaX موجب يكشف ما على اليمين: التالي في الإنجليزية، والسابق في العربية.
      if (sum > 0 !== rtl) api.scrollNext(reduced); else api.scrollPrev(reduced);
    };
    root.addEventListener("wheel", onWheel, { passive: false });
    return () => {
      root.removeEventListener("wheel", onWheel);
      window.clearTimeout(timer);
    };
  }, [api, rtl, reduced]);

  // العجلة في أسفل الصفحة تكمل الإيماءة نفسها في الشريط: لأسفل يتقدم نحو الركيزة التالية بقدر deltaY (لا شريحة لكل نقرة)،
  // ولأعلى يرجع حتى أوله ثم تصعد الصفحة. لا يمنع التمرير إلا حين يتحرك الشريط فعلًا، فلا تعلق الصفحة أبدًا.
  // معطل مع تقليل الحركة، ومع Ctrl/⌘ (التكبير)، وحين لا يتحرك الشريط. اللمس لا يرسل wheel فيبقى سحبه كما هو.
  // بعد توقف العجلة يستقر على أقرب بطاقة، فتبقى الحواف والنقاط والأزرار على موضع واضح.
  const [hint, setHint] = useState(false);
  useEffect(() => {
    if (!api || reduced || snaps < 2 || single) return;
    const root = api.rootNode();
    const scope = root.closest(".lcp-content") ?? document.body;
    let settleTimer = 0;
    let hintTimer = 0;
    const visible = () => {
      const r = root.getBoundingClientRect();
      return Math.min(r.bottom, window.innerHeight) - Math.max(r.top, 0) >= Math.min(200, r.height * 0.4);
    };
    const hide = () => {
      window.clearTimeout(hintTimer);
      setHint(false);
    };
    const settle = () => {
      const e = api.internalEngine();
      e.scrollBody.useBaseFriction().useBaseDuration();
      e.scrollTo.distance(0, true);
    };
    const onWheel = (ev: WheelEvent) => {
      if (ev.defaultPrevented || ev.ctrlKey || ev.metaKey) return;
      const unit = ev.deltaMode === 1 ? 16 : ev.deltaMode === 2 ? window.innerHeight : 1;
      const dy = ev.deltaY * unit;
      if (!dy || Math.abs(ev.deltaX * unit) >= Math.abs(dy)) return;
      const from = ev.target instanceof Element ? ev.target : null;
      if (!from || !scope.contains(from) || innerScroller(from, scope, dy)) return;
      if (!atPageBottom() || !visible()) return;
      const e = api.internalEngine();
      const at = e.target.get();
      // في محرك embla التقدم نحو التالي ينقص الموضع (في الاتجاهين)، وحداه limit.min (الآخر) وlimit.max (الأول).
      const room = dy > 0 ? at - e.limit.min : e.limit.max - at;
      // لأعلى: إن بلغ الهدف أول الشريط والشريط ما زال ينزلق إليه، تنتظر الصفحة حتى يستقر فلا يتحركان معًا.
      if (room < 0.5) {
        if (dy < 0 && e.limit.max - e.location.get() >= 1) ev.preventDefault();
        return;
      }
      ev.preventDefault();
      hide();
      const step = Math.min(Math.abs(dy), room);
      e.scrollBody.useFriction(0.3).useDuration(0.75);
      e.scrollTo.distance(dy > 0 ? -step : step, false);
      window.clearTimeout(settleTimer);
      settleTimer = window.setTimeout(settle, 160);
    };
    const onScroll = () => {
      if (!atPageBottom()) return hide();
      if (!visible() || !api.canScrollNext() || !window.matchMedia(FINE_POINTER).matches || !takeHint()) return;
      setHint(true);
      hintTimer = window.setTimeout(() => setHint(false), 6000);
    };
    window.addEventListener("wheel", onWheel, { passive: false });
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => {
      window.removeEventListener("wheel", onWheel);
      window.removeEventListener("scroll", onScroll);
      window.clearTimeout(settleTimer);
      window.clearTimeout(hintTimer);
    };
  }, [api, reduced, snaps, single]);

  const canPrev = index > 0;
  const canNext = index < snaps - 1;
  const scrollable = snaps > 1;

  /** ينقل الشريط إلى الموضع to؛ وإن كان التركيز على بطاقة ستخرج من العرض انتقل إلى أقرب بطاقة ظاهرة. */
  const go = (to: number, fromKeyboard = false) => {
    if (!api || to < 0 || to >= snaps) return;
    api.scrollTo(to, reduced);
    if (!fromKeyboard) return;
    const nodes = api.slideNodes();
    const at = nodes.findIndex((n) => n.contains(document.activeElement));
    if (at < 0) return;
    const first = Math.min(to, nodes.length - perView);
    if (at >= first && at < first + perView) return;
    const target = nodes[at < first ? first : first + perView - 1];
    target?.querySelector<HTMLElement>("button, a[href], [tabindex]:not([tabindex='-1'])")?.focus({ preventScroll: true });
  };

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.altKey || e.ctrlKey || e.metaKey || e.shiftKey || !scrollable) return;
    if ((e.target as HTMLElement).closest("input, textarea, select, [contenteditable='true']")) return;
    const forward = rtl ? "ArrowLeft" : "ArrowRight";
    const back = rtl ? "ArrowRight" : "ArrowLeft";
    const to = e.key === forward ? index + 1 : e.key === back ? index - 1 : e.key === "Home" ? 0 : e.key === "End" ? snaps - 1 : null;
    if (to === null) return;
    e.preventDefault();
    go(to, true);
  };

  const from = Math.min(index, Math.max(0, total - perView)) + 1;
  const to = Math.min(total, from + perView - 1);
  const PrevIcon = rtl ? RightOutlined : LeftOutlined;
  const NextIcon = rtl ? LeftOutlined : RightOutlined;

  return (
    <div className={`sp-car${single ? " single" : ""}${scrollable ? "" : " static"}`} role="region" aria-roledescription={L("عرض متتابع", "carousel")} aria-label={label} dir={dir} onKeyDown={onKeyDown}>
      <button type="button" className="sp-car-btn sp-car-prev" aria-controls={trackId} aria-disabled={!canPrev} tabIndex={scrollable ? 0 : -1}
        aria-label={L("الركائز السابقة", "Previous pillars")} onClick={() => canPrev && go(index - 1)}>
        <PrevIcon aria-hidden />
      </button>
      <div className="sp-car-viewport" ref={viewportRef} dir={dir}>
        <div className="sp-car-track" id={trackId}>
          {slides.map((s, i) => (
            <div key={s.key} className="sp-car-slide" role="group" aria-roledescription={L("شريحة", "slide")} aria-label={L(`${i + 1} من ${total}`, `${i + 1} of ${total}`)}>
              {s.node}
            </div>
          ))}
        </div>
      </div>
      <button type="button" className="sp-car-btn sp-car-next" aria-controls={trackId} aria-disabled={!canNext} tabIndex={scrollable ? 0 : -1}
        aria-label={L("الركائز التالية", "Next pillars")} onClick={() => canNext && go(index + 1)}>
        <NextIcon aria-hidden />
      </button>
      <div className="sp-car-dots" role="group" aria-label={L("موضع العرض", "Position")}>
        {scrollable && Array.from({ length: snaps }, (_, i) => (
          <button key={i} type="button" aria-controls={trackId} aria-current={i === index ? "true" : undefined}
            aria-label={single && slides[i]?.name ? slides[i].name : L(`الموضع ${i + 1} من ${snaps}`, `Position ${i + 1} of ${snaps}`)} onClick={() => go(i)} />
        ))}
      </div>
      {hint && (
        <p className="sp-car-hint" aria-hidden>
          {L("واصل التمرير لأسفل لعرض بقية الركائز", "Keep scrolling down for more pillars")} {rtl ? <ArrowLeftOutlined /> : <ArrowRightOutlined />}
        </p>
      )}
      <p className="sr-only sp-car-status" aria-live="polite">
        {scrollable && (single
          ? L(`${slides[index]?.name ?? ""} (${index + 1} من ${total})`, `${slides[index]?.name ?? ""} (${index + 1} of ${total})`)
          : L(`الركائز ${from}–${to} من ${total}`, `Pillars ${from}–${to} of ${total}`))}
      </p>
    </div>
  );
}
