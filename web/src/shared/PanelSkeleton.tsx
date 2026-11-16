import "./panel-skeleton.css";

/**
 * هيكل تحميل بشكل المحتوى النهائي لجسم لوحة تطوى: صفوف (rows)، أو بطاقات (cards)، أو جدول (table).
 * ألوانه وزواياه من رموز --ds-* وحدها، فيتبع الثيمات الخمسة وأنماط الزوايا والاتجاه (RTL)؛ والوميض يتوقف مع تقليل الحركة.
 * مخفي عن قارئ الشاشة؛ الحالة تُعلن بـ aria-busy على اللوحة.
 */
export function PanelSkeleton({ rows = 3, variant = "rows" }: { rows?: number; variant?: "rows" | "cards" | "table" }) {
  const n = Math.max(1, rows);
  return (
    <div className={`ds-skel ds-skel-${variant}`} aria-hidden>
      {variant === "table" && <div className="ds-skel-tr head"><i /><i /><i /><i /></div>}
      {Array.from({ length: n }, (_, k) =>
        variant === "cards" ? (
          <div key={k} className="ds-skel-card"><i className="w60" /><i className="w90" /><i className="w40" /></div>
        ) : variant === "table" ? (
          <div key={k} className="ds-skel-tr"><i /><i /><i /><i /></div>
        ) : (
          <div key={k} className="ds-skel-row"><i className="dot" /><i className={k % 2 ? "w70" : "w90"} /></div>
        ))}
    </div>
  );
}
