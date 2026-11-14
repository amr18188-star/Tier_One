import { useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { L } from "../../shared/i18n";

/**
 * ملاحظات المراجعة في بطاقة صغيرة واحدة: عنوان واحد («مطلوب التعديل:») ثم النصوص، كل نص في سطرين وزر «المزيد» إن طال.
 * bad للإعادة وملاحظات المؤشرات، وwarn لإعادة الفتح بعد الاعتماد. الألوان من رموز الحالة (--ds-status-*).
 */
export function NoteCard({ notes, tone = "bad", title, extra, className }: {
  notes: string[]; tone?: "bad" | "warn"; title?: string; extra?: ReactNode; className?: string;
}) {
  const shown = notes.map((n) => n.trim()).filter(Boolean);
  if (shown.length === 0 && !extra) return null;
  return (
    <div className={`fp-note tone-${tone}${className ? ` ${className}` : ""}`} role="note">
      <b className="fp-note-label">{title ?? L("مطلوب التعديل:", "Changes requested:")}</b>
      {shown.map((n, k) => <ClampText key={k} text={n} />)}
      {extra && <div className="fp-note-extra">{extra}</div>}
    </div>
  );
}

function ClampText({ text }: { text: string }) {
  const ref = useRef<HTMLParagraphElement>(null);
  const [open, setOpen] = useState(false);
  const [long, setLong] = useState(false);
  // يقاس النص مطويًا: الزر يظهر فقط إن زاد على سطرين.
  useLayoutEffect(() => {
    const el = ref.current;
    if (el && !open) setLong(el.scrollHeight > el.clientHeight + 1);
  }, [text, open]);
  return (
    <div className="fp-note-item">
      <p ref={ref} className={`fp-note-text${open ? " is-open" : ""}`}>{text}</p>
      {(long || open) && (
        <button type="button" className="fp-note-more" aria-expanded={open} onClick={() => setOpen(!open)}>
          {open ? L("أقل", "Less") : L("المزيد", "More")}
        </button>
      )}
    </div>
  );
}
