import { useId, type ReactNode } from "react";
import { DownOutlined } from "@ant-design/icons";

/**
 * لوحة قابلة للطي: الرأس زر (aria-expanded وaria-controls)، والمحتوى منطقة تحته.
 * الطي بالشبكة (0fr ← 1fr) كالقائمة الجانبية؛ المطوي مخفي عن Tab وقارئ الشاشة (forms.css).
 */
export function Fold({ open, onToggle, head, children, className }: {
  open: boolean; onToggle: () => void; head: ReactNode; children: ReactNode; className?: string;
}) {
  const id = useId();
  return (
    <section className={className ? `fm-fold ${className}` : "fm-fold"} data-open={open}>
      <h3 className="fm-fold-title">
        <button id={`${id}h`} type="button" className="fm-fold-head" aria-expanded={open} aria-controls={`${id}p`} onClick={onToggle}>
          <DownOutlined className="fm-fold-chev" aria-hidden />
          {head}
        </button>
      </h3>
      <div className="fm-fold-body">
        <div id={`${id}p`} role="region" aria-labelledby={`${id}h`} className="fm-fold-inner">{children}</div>
      </div>
    </section>
  );
}
