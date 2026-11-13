import { useMemo, useRef, useState, useSyncExternalStore } from "react";
import { Select } from "antd";
import { LeftOutlined, RightOutlined } from "@ant-design/icons";
import { L, useLang } from "./i18n";
import "./pager.css";

/** خيارات «عدد العناصر لكل صفحة» المشتركة في كل الجداول؛ تضاف إليها خيارات الصفحة نفسها. */
export const PAGE_SIZES: readonly number[] = [10, 20, 30, 50, 100];

/** رقم صفحة، أو فجوة «…» قبل الصفحات القريبة من الحالية أو بعدها. */
export type PageItem = number | "gap-start" | "gap-end";

/**
 * أرقام الصفحات الظاهرة: الأولى والأخيرة دائمًا، والحالية ومعها siblings من كل جانب، و«…» مكان صفحتين مخفيتين أو أكثر.
 * متى زادت الصفحات عن 2×siblings + 5 يبقى عدد العناصر ثابتًا فلا يتحرك الشريط بين صفحة وأخرى؛ وما دون ذلك تظهر كلها.
 */
export function pageItems(current: number, pages: number, siblings = 1): PageItem[] {
  const n = Number.isFinite(pages) ? Math.max(1, Math.floor(pages)) : 1;
  const s = Number.isFinite(siblings) ? Math.max(0, Math.floor(siblings)) : 1;
  const cur = Number.isFinite(current) ? Math.min(Math.max(1, Math.floor(current)), n) : 1;
  if (n <= 2 * s + 5) return Array.from({ length: n }, (_, i) => i + 1);
  // نافذة من 2×siblings + 1 صفحة حول الحالية، لا تقترب من الطرفين أكثر من خانة واحدة.
  const lo = Math.max(3, Math.min(cur - s, n - 2 - 2 * s));
  const hi = lo + 2 * s;
  const items: PageItem[] = [1, lo === 3 ? 2 : "gap-start"];
  for (let p = lo; p <= hi; p++) items.push(p);
  items.push(hi === n - 2 ? n - 1 : "gap-end", n);
  return items;
}

/** خيارات عدد الصفوف: المشتركة مع خيارات الصفحة، مرتبة بلا تكرار. */
export function mergeSizes(own: readonly number[] = []): number[] {
  return [...new Set([...PAGE_SIZES, ...own])].filter((n) => Number.isInteger(n) && n > 0).sort((a, b) => a - b);
}

/** الحد نفسه في pager.css: تحته تكبر الأزرار للمس وتقل الأرقام حول الحالية. */
const NARROW = "(max-width: 640px)";

function subscribeNarrow(onChange: () => void): () => void {
  if (typeof window === "undefined" || !window.matchMedia) return () => {};
  const mq = window.matchMedia(NARROW);
  mq.addEventListener("change", onChange);
  return () => mq.removeEventListener("change", onChange);
}

function isNarrow(): boolean {
  return typeof window !== "undefined" && !!window.matchMedia && window.matchMedia(NARROW).matches;
}

/** يقرأ عرض الشاشة متزامنًا مع أول رسم، فلا يتبدل عدد الأرقام بعد ظهور الشريط. */
function useNarrow(): boolean {
  return useSyncExternalStore(subscribeNarrow, isNarrow, () => false);
}

/** تقطيع قائمة في المتصفح لجدول خارج DataTable؛ ترجع صفوف الصفحة وخصائص Pager. */
export function usePaged<T>(all: readonly T[], defaultSize: number) {
  const [page, setPage] = useState(1);
  const [size, setSize] = useState(defaultSize);
  const pages = Math.max(1, Math.ceil(all.length / size));
  const current = Math.min(page, pages);
  const rows = useMemo(() => all.slice((current - 1) * size, current * size), [all, current, size]);
  return {
    rows,
    pager: {
      page: current,
      size,
      total: all.length,
      count: rows.length,
      onPage: setPage,
      onSize: (n: number) => { setSize(n); setPage(1); },
    },
  };
}

export interface PagerProps {
  /** الصفحة الحالية (من 1). */
  page: number;
  size: number;
  total: number;
  /** صفوف هذه الصفحة فعلًا، لسطر «x–y من N»؛ إن غاب أو كان صفرًا (أثناء التحميل) يحسب من size. */
  count?: number;
  onPage: (page: number) => void;
  /** خيارات عدد الصفوف؛ دونها لا تظهر القائمة (كالترقيم من الخادم). */
  sizes?: readonly number[];
  onSize?: (size: number) => void;
  /** اسم شريط الترقيم لقارئ الشاشة، حين يكون في الصفحة أكثر من جدول. */
  label?: string;
}

/**
 * تذييل الجداول الموحد: «عدد العناصر لكل صفحة» و«x–y من N»، ثم «السابق» وأرقام الصفحات و«التالي».
 * على الشاشة الضيقة: الأزرار 40 بكسل، و«السابق/التالي» أيقونة فقط، ورقم واحد حول الحالية.
 */
export function Pager({ page, size, total, count, onPage, sizes, onSize, label }: PagerProps) {
  const lang = useLang();
  const narrow = useNarrow();
  const navRef = useRef<HTMLElement>(null);
  const pages = Math.max(1, Math.ceil(total / size));
  const current = Math.min(Math.max(1, page), pages);
  const from = total === 0 ? 0 : (current - 1) * size + 1;
  const to = Math.min((current - 1) * size + (count || size), total);
  const items = pageItems(current, pages, narrow ? 0 : 1);
  const PrevIcon = lang === "ar" ? RightOutlined : LeftOutlined;
  const NextIcon = lang === "ar" ? LeftOutlined : RightOutlined;
  const sizeLabel = L("عدد العناصر لكل صفحة", "Rows per page");
  const prevText = L("السابق", "Previous");
  const nextText = L("التالي", "Next");

  const go = (p: number) => {
    if (p >= 1 && p <= pages && p !== current) onPage(p);
  };
  // عند الطرف يتعطل زر «السابق» أو «التالي» الذي ضغط للتو؛ ينتقل التركيز إلى الصفحة الحالية بدل أن يضيع.
  const step = (p: number) => {
    go(p);
    if (p === 1 || p === pages) {
      requestAnimationFrame(() => navRef.current?.querySelector<HTMLButtonElement>('[aria-current="page"]')?.focus());
    }
  };

  return (
    <div className="pg">
      <div className="pg-meta">
        {sizes && onSize && (
          <span className="pg-size">
            {narrow ? L("لكل صفحة", "Per page") : sizeLabel}
            {/* القائمة بعرض محتواها: بعرض الحقل الصغير تقطع «100» و«120». */}
            <Select size={narrow ? "large" : "small"} variant="borderless" value={size} onChange={onSize} popupMatchSelectWidth={false}
              options={sizes.map((n) => ({ value: n, label: n }))} aria-label={sizeLabel} />
          </span>
        )}
        <span className="pg-range" aria-live="polite" aria-atomic="true">
          <bdi><span dir="ltr">{from}–{to}</span> {L("من", "of")} {total}</bdi>
        </span>
      </div>
      <nav ref={navRef} className="pg-nav" aria-label={label ?? L("ترقيم الصفحات", "Pagination")}>
        <ul className="pg-list">
          <li>
            <button type="button" className="pg-step" disabled={current <= 1} onClick={() => step(current - 1)} aria-label={prevText}>
              <PrevIcon aria-hidden />
              <span className="pg-step-text">{prevText}</span>
            </button>
          </li>
          {items.map((it) =>
            typeof it === "number" ? (
              <li key={it}>
                <button type="button" className="pg-btn" aria-current={it === current ? "page" : undefined}
                  aria-label={L(`صفحة ${it}`, `Page ${it}`)} onClick={() => go(it)}>
                  {it}
                </button>
              </li>
            ) : (
              <li key={it} className="pg-gap" aria-hidden>…</li>
            ),
          )}
          <li>
            <button type="button" className="pg-step" disabled={current >= pages} onClick={() => step(current + 1)} aria-label={nextText}>
              <span className="pg-step-text">{nextText}</span>
              <NextIcon aria-hidden />
            </button>
          </li>
        </ul>
      </nav>
    </div>
  );
}
