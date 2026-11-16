import { useEffect, useRef, useState } from "react";
import { L, useLang } from "./i18n";
// شعار الجامعة بحروف فاتحة على اللوحة الداكنة.
import uniLogo from "../assets/brand/capital-university-logo-dark.png";
import shotScorecard from "../assets/login/shot-scorecard.webp";
import shotStrategy from "../assets/login/shot-strategy.webp";
import shotProgress from "../assets/login/shot-progress.png";

/** مدة بقاء كل شريحة قبل الانتقال إلى التالية. */
const INTERVAL_MS = 5000;

interface Slide {
  id: string;
  img: string;
  /** بداية العنوان، ثم الكلمة المميزة بالذهبي. */
  title: string;
  highlight: string;
  sub: string;
  alt: string;
  /** نسبة عرض/ارتفاع الصورة الأصلية، ليأخذها الإطار فلا يبقى شريط فارغ. */
  ratio: number;
}

function useSlides(): Slide[] {
  useLang(); // يعيد البناء عند تبديل اللغة
  return [
    {
      id: "scorecard",
      ratio: 1672 / 941,
      img: shotScorecard,
      title: L("بطاقة أداء الجامعة ", "University performance "),
      highlight: L("وتصنيفها", "scorecard & ranking"),
      sub: L("مستويات متقدمة من الأداء والتحليل لمتابعة تقدم جامعتك نحو التميز", "Advanced performance levels and analytics to track your university's progress toward excellence"),
      alt: L("شاشة بطاقة أداء الجامعة: الدرجة الكلية وعداد الفئة وسلم الفئات", "University scorecard screen: overall score, category gauge and the category ladder"),
    },
    {
      id: "strategy",
      ratio: 1892 / 922,
      img: shotStrategy,
      title: L("الخطة الاستراتيجية ", "The strategic plan "),
      highlight: "2027–2031",
      sub: L("6 ركائز و12 معيارًا و92 مؤشرًا و231 مبادرة في خريطة واحدة للخطة", "6 pillars, 12 standards, 92 indicators and 231 initiatives on a single plan map"),
      alt: L("شاشة الخطة الاستراتيجية: الركائز والمعايير والمبادرات والمؤشرات", "Strategic plan screen: pillars, standards, initiatives and indicators"),
    },
    {
      id: "progress",
      ratio: 1893 / 926,
      img: shotProgress,
      title: L("تقدّم ", "College "),
      highlight: L("الكليات", "progress"),
      sub: L("قارن أداء كل كلية بمستهدف الجامعة وتابع الفرق في كل ركيزة", "Compare each college with the university target and follow the gap on every pillar"),
      alt: L("شاشة تقدّم الكلية: قيمة الكلية مقابل الجامعة في كل ركيزة", "College progress screen: the college against the university on every pillar"),
    },
  ];
}

function useReducedMotion(): boolean {
  const [reduced, setReduced] = useState(() => typeof window !== "undefined" && window.matchMedia?.("(prefers-reduced-motion: reduce)").matches === true);
  useEffect(() => {
    const mq = window.matchMedia?.("(prefers-reduced-motion: reduce)");
    if (!mq) return;
    const on = () => setReduced(mq.matches);
    mq.addEventListener("change", on);
    return () => mq.removeEventListener("change", on);
  }, []);
  return reduced;
}

/**
 * لوحة العرض الكحلية في يمين/يسار الدخول: عنوان وشرح وشاشة من التطبيق تتبدل كل 5 ثوانٍ.
 * تتوقف عند المرور بالمؤشر أو التركيز، ولا تتحرك تلقائيًا مع تقليل الحركة.
 * الخلفية والشعار ثابتان؛ يتبدل المحتوى فقط.
 */
export function AuthShowcase() {
  const slides = useSlides();
  const reduced = useReducedMotion();
  const [index, setIndex] = useState(0);
  const [hover, setHover] = useState(false);
  const [focus, setFocus] = useState(false);
  const paused = hover || focus;
  const stageRef = useRef<HTMLDivElement>(null);

  // ميل خفيف يتبع الفأرة على الأجهزة ذات المؤشر الدقيق فقط، ويتعطل مع تقليل الحركة.
  function tilt(e: React.MouseEvent) {
    const el = stageRef.current;
    if (!el || reduced || !window.matchMedia("(hover: hover) and (pointer: fine)").matches) return;
    const r = el.getBoundingClientRect();
    const x = Math.max(-1, Math.min(1, ((e.clientX - r.left) / r.width) * 2 - 1));
    const y = Math.max(-1, Math.min(1, ((e.clientY - r.top) / r.height) * 2 - 1));
    el.style.setProperty("--tx", x.toFixed(3));
    el.style.setProperty("--ty", (-y).toFixed(3));
  }
  function untilt() {
    stageRef.current?.style.setProperty("--tx", "0");
    stageRef.current?.style.setProperty("--ty", "0");
  }

  useEffect(() => {
    if (paused || reduced) return;
    const t = window.setTimeout(() => setIndex((i) => (i + 1) % slides.length), INTERVAL_MS);
    return () => window.clearTimeout(t);
  }, [index, paused, reduced, slides.length]);

  return (
    <aside
      className="lgn-show"
      data-section="dark"
      aria-roledescription="carousel"
      aria-label={L("لقطات من المنظومة", "Product highlights")}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => { setHover(false); untilt(); }}
      onMouseMove={tilt}
      onFocus={() => setFocus(true)}
      onBlur={(e) => { if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setFocus(false); }}
    >
      <img className="lgn-uni" src={uniLogo} alt={L("جامعة العاصمة", "Capital University")} />
      <p className="lgn-band-title">{L("الخطة الاستراتيجية وتقييم الأداء", "Strategy and performance")}</p>

      <div className="lgn-copy">
        <div className="lgn-texts" aria-live={paused ? "polite" : "off"}>
          {slides.map((s, i) => (
            <div key={s.id} className="lgn-text" data-active={i === index} aria-hidden={i !== index}>
              <h2>{s.title}<span>{s.highlight}</span></h2>
              <p>{s.sub}</p>
            </div>
          ))}
        </div>
        <div className="lgn-dots" role="group" aria-label={L("اختيار الشريحة", "Choose slide")}>
          {slides.map((s, i) => (
            <button
              key={s.id}
              type="button"
              className="lgn-dot"
              data-active={i === index}
              aria-label={L(`الشريحة ${i + 1} من ${slides.length}`, `Slide ${i + 1} of ${slides.length}`)}
              aria-current={i === index}
              onClick={() => setIndex(i)}
            />
          ))}
        </div>
      </div>

      <div className="lgn-stage" ref={stageRef}>
        <div className="lgn-float">
          <div className="lgn-ground" aria-hidden />
          <div className="lgn-device">
            <div className="lgn-screen" style={{ "--ar": slides[index].ratio } as React.CSSProperties}>
              {slides.map((s, i) => (
                <img key={s.id} className="lgn-shot" data-active={i === index} aria-hidden={i !== index} src={s.img} alt={s.alt} decoding="async" />
              ))}
            </div>
          </div>
        </div>
      </div>
    </aside>
  );
}
