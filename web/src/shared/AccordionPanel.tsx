import { useCallback, useEffect, useRef, useState, type CSSProperties, type ReactNode } from "react";
import { DownOutlined } from "@ant-design/icons";
import { useSearchParams } from "react-router-dom";
import { QueryError, type QueryLike } from "./QueryState";
import { PanelSkeleton } from "./PanelSkeleton";
import "./accordion-panel.css";

/**
 * أكورديون بلوحة واحدة مفتوحة، بسلوك الشريط الجانبي (app/Shell.tsx SideNav) لكل الصفحات: فتح لوحة يطوي المفتوحة،
 * والضغط على رأس المفتوحة يطويها، والحركتان معًا (220ms) وفوريتان مع تقليل الحركة.
 *
 *   const { openKey, toggle } = useSingleOpen({ param: "pillar", keys: codes });
 *   {items.map((x) => (
 *     <AccordionPanel key={x.code} id={`p-${x.code}`} open={openKey === x.code} onToggle={() => toggle(x.code)} header={...}>
 *       ...
 *     </AccordionPanel>
 *   ))}
 *
 * اللوحات أخوات في حاوية واحدة (لحساب موضع الرأس بعد طي ما فوقه)، والصفحة تتمرر في النافذة كما في كل صفحات الواجهة.
 */

/**
 * أي لوحة مفتوحة. البداية: لا شيء مفتوح.
 * param: يقرأ اللوحة المفتوحة من معامل الرابط ويكتبها فيه بـ replace (بلا سجل تصفح)، ويبقي المعاملات الأخرى؛ الطي يحذفه.
 *   فإعادة التحميل تبقي اللوحة المفتوحة، ورابط إلى لوحة يفتحها.
 * keys: المفاتيح المعروفة؛ قيمة في الرابط ليست منها تحذف بـ replace. مرر undefined ما دامت المفاتيح لم تحمّل بعد
 *   (مصفوفة فارغة تحذف أي قيمة).
 */
export function useSingleOpen(opts?: { param?: string; keys?: string[] }): {
  openKey: string | null;
  toggle: (key: string) => void;
  setOpenKey: (k: string | null) => void;
} {
  const param = opts?.param;
  const keys = opts?.keys;
  const [params, setParams] = useSearchParams();
  const asked = param ? params.get(param) : null;
  const keySig = keys ? keys.join("\u0001") : null;
  const known = (k: string | null): k is string => k !== null && (!keys || keys.includes(k));
  const [openKey, setOpen] = useState<string | null>(() => (known(asked) ? asked : null));

  // مراجع لآخر قيمة، فتبقى toggle و setOpenKey ثابتتين (تصلحان لمكونات مذكّرة).
  const openRef = useRef(openKey);
  openRef.current = openKey;
  const setParamsRef = useRef(setParams);
  setParamsRef.current = setParams;
  const paramRef = useRef(param);
  paramRef.current = param;

  const writeUrl = useCallback((k: string | null) => {
    const name = paramRef.current;
    if (!name) return;
    setParamsRef.current((prev) => {
      const next = new URLSearchParams(prev);
      if (k === null) next.delete(name);
      else next.set(name, k);
      return next;
    }, { replace: true });
  }, []);

  const setOpenKey = useCallback((k: string | null) => {
    openRef.current = k;
    setOpen(k);
    writeUrl(k);
  }, [writeUrl]);

  const toggle = useCallback((key: string) => setOpenKey(openRef.current === key ? null : key), [setOpenKey]);

  // يتبع الرابط: قيمة معروفة تفتح لوحتها، وغير المعروفة تحذف.
  useEffect(() => {
    if (asked === null) return;
    if (known(asked)) {
      openRef.current = asked;
      setOpen(asked);
    } else writeUrl(null);
  }, [asked, keySig]);

  return { openKey, toggle, setOpenKey };
}

/**
 * لوحة تطوى. الرأس زر (aria-expanded، aria-controls) يحمل header والسهم (نحو نهاية السطر مطويًا، لأسفل مفتوحًا)؛
 * والمحتوى منطقة (role="region"، aria-labelledby الرأس). المحتوى مرسوم دائمًا، والمطوي مخفي بالـ CSS فلا يصله Tab.
 * عند الفتح يمرر الرأس إلى الشاشة إن كان سيقع خارجها (بعد طي المفتوح فوقه)، وإلا لا تمرير.
 * id فريد في الصفحة: منه معرفا الرأس (`${id}-head`) والمحتوى (`${id}-body`).
 * header محتوى زر: عناصر سطرية فقط (span، b، small …).
 */
export function AccordionPanel({ id, open, onToggle, header, children, className, style, query, skeleton, onIntent }: {
  id: string;
  open: boolean;
  onToggle: () => void;
  header: ReactNode;
  children: ReactNode;
  className?: string;
  style?: CSSProperties;
  /**
   * استعلام جسم اللوحة (إن كان محتواها من الخادم): تفتح اللوحة فورًا بحركتها المعتادة وتعرض skeleton أثناء التحميل الأول،
   * ثم يكبر ارتفاعها بسلاسة إلى المحتوى (الشبكة 0fr ← 1fr تتبع ارتفاع المحتوى مهما تغير)، وعند الفشل تعرض خطأ مع «أعد المحاولة» داخلها.
   * الاستعلام المخزن (data موجودة) لا يعرض هيكلًا. children تُرسم بعد وصول البيانات فقط، فيجوز أن تقرأ data مباشرة.
   */
  query?: QueryLike;
  /** شكل الهيكل؛ الافتراضي PanelSkeleton بثلاثة صفوف. */
  skeleton?: ReactNode;
  /** تحويم أو تركيز على الرأس: مكان جلب مسبق (queryClient.prefetchQuery). */
  onIntent?: () => void;
}) {
  const headId = `${id}-head`, bodyId = `${id}-body`;
  let body: ReactNode = children;
  if (query) {
    if (query.isLoading) body = skeleton ?? <PanelSkeleton />;
    else if (query.isError && query.data === undefined)
      body = <div className="ds-acc-state"><QueryError error={query.error} onRetry={() => void query.refetch()} /></div>;
  }
  return (
    <div className={className ? `ds-acc ${className}` : "ds-acc"} data-open={open ? "true" : "false"} style={style}>
      <button type="button" id={headId} className="ds-acc-head" aria-expanded={open} aria-controls={bodyId}
        onMouseEnter={onIntent} onFocus={onIntent}
        onClick={(e) => {
          if (!open) keepHeadInView(e.currentTarget);
          onToggle();
        }}>
        <span className="ds-acc-title">{header}</span>
        <DownOutlined className="ds-acc-chev" aria-hidden />
      </button>
      <div id={bodyId} className="ds-acc-body" role="region" aria-labelledby={headId} aria-busy={query?.isLoading ? true : undefined}>
        <div className="ds-acc-inner">{body}</div>
      </div>
    </div>
  );
}

/**
 * بعد فتح لوحة يبقى رأسها على الشاشة: طي المفتوحة فوقها يرفعه بارتفاع محتواها، فيحسب موضعه بعد الطي قبل أن يبدأ،
 * ويمرر إليه (بسلاسة، وفوريًا مع تقليل الحركة) إن كان سيقع تحت الشريط العلوي أو تحت شريط الجوال السفلي أو خارج الشاشة.
 */
function keepHeadInView(head: HTMLElement) {
  const panel = head.closest<HTMLElement>(".ds-acc");
  if (!panel) return;
  let lift = 0;
  for (let el = panel.previousElementSibling; el; el = el.previousElementSibling) {
    if (!el.classList.contains("ds-acc")) continue;
    const body = el.querySelector<HTMLElement>(":scope > .ds-acc-body");
    if (body) lift += body.getBoundingClientRect().height;
  }
  const r = head.getBoundingClientRect();
  const top = r.top - lift, bottom = r.bottom - lift;
  const ceil = Math.max(0, document.querySelector(".lcp-top")?.getBoundingClientRect().bottom ?? 0);
  const bar = document.querySelector(".lcp-tabbar")?.getBoundingClientRect();
  const floor = bar && bar.height > 0 ? bar.top : window.innerHeight;
  if (top >= ceil && bottom <= floor) return;
  const reduce = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
  window.scrollTo({ top: Math.max(0, window.scrollY + top - ceil - 8), behavior: reduce ? "auto" : "smooth" });
}

/**
 * جسم يطوى بالحركة نفسها (220ms، 0fr ← 1fr) لعنصر ليس له رأس لوحة (مثل تفصيل صف): يرسم children عند أول فتح ويبقيها بعده
 * فتنطوي بحركة وتحفظ حالتها، والمطوي مخفي عن Tab وقارئ الشاشة. الزر المتحكم يحمل aria-expanded وaria-controls={id}.
 */
export function Reveal({ open, id, children }: { open: boolean; id: string; children: ReactNode }) {
  const [shown, setShown] = useState(open);
  if (open && !shown) setShown(true);
  // أول فتح: يرسم مطويًا ثم يفتح بعد 30ms، فتجري الحركة بدل ظهور الجسم مفتوحًا دفعة واحدة.
  const [armed, setArmed] = useState(false);
  useEffect(() => {
    if (!open || armed) return;
    const t = window.setTimeout(() => setArmed(true), 30);
    return () => window.clearTimeout(t);
  }, [open, armed, shown]);
  if (!shown) return null;
  return (
    <div className="ds-acc ds-reveal" data-open={open && armed ? "true" : "false"}>
      <div id={id} className="ds-acc-body" role="region">
        <div className="ds-acc-inner">{children}</div>
      </div>
    </div>
  );
}
