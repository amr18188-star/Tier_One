import { useEffect, useRef, useState } from "react";
import { prefersReducedMotion } from "./entrance";

const decimalsOf = (n: number) => (String(n).split(".")[1] ?? "").length;
const easeOut = (p: number) => 1 - Math.pow(1 - p, 3);

/**
 * يعد من 0 إلى القيمة (نحو 1000ms، ease-out) عند أول ظهور لها في كل تركيب للصفحة.
 * العد يبدأ حين تصل البيانات (حين يركب المكوّن بقيمتها)، لا قبله. مع تقليل الحركة: القيمة النهائية فورًا.
 * يعيد رقمًا بنفس عدد منازل القيمة الأصلية، فتبقى الأرقام لاتينية وبنفس صياغتها (String).
 */
export function useCountUp(target: number, duration = 1000, delay = 200): string {
  // يعد عند كل تركيب (ولو والبيانات مخزنة)، ما لم تُقلل الحركة؛ يُحسم عند التركيب.
  const play = useRef(!prefersReducedMotion());
  const [value, setValue] = useState(play.current ? 0 : target);

  useEffect(() => {
    if (!play.current) {
      setValue(target);
      return;
    }
    let raf = 0;
    const startAt = performance.now() + delay;
    const tick = (now: number) => {
      const p = Math.min(1, Math.max(0, (now - startAt) / duration));
      setValue(target * easeOut(p));
      if (p < 1) raf = requestAnimationFrame(tick);
      else play.current = false;
    };
    raf = requestAnimationFrame(tick);
    // تبويب في الخلفية يوقف requestAnimationFrame: يضمن هذا المؤقت ألا تبقى القيمة عالقة عند 0.
    const done = window.setTimeout(() => { play.current = false; setValue(target); }, delay + duration + 100);
    return () => { cancelAnimationFrame(raf); window.clearTimeout(done); };
  }, [target, duration, delay]);

  const dec = decimalsOf(target);
  return value === target ? String(target) : value.toFixed(dec);
}

/** رقم يعد صاعدًا: `<CountUp value={n} />`؛ لاحقة مثل «%» تمرر في suffix. */
export function CountUp({ value, suffix = "" }: { value: number; suffix?: string }) {
  return <>{useCountUp(value)}{suffix}</>;
}
