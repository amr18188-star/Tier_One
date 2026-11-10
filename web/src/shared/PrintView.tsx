import { useEffect, useId, useLayoutEffect, useRef, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { Button, Space } from "antd";
import { CloseOutlined, PrinterOutlined } from "@ant-design/icons";
import { L } from "./i18n";

/** نافذة antd ظاهرة فوق العرض تغلق نفسها بـ Escape أولًا. */
function modalOpen(): boolean {
  return [...document.querySelectorAll(".ant-modal-wrap")].some((el) => el.getClientRects().length > 0);
}

/**
 * عرض للطباعة فوق الصفحة: يطبع محتواه وحده (بطاقة الدخول، كشف اللجنة، المستند الرسمي).
 * يوضع خارج جذر التطبيق، وقاعدة الطباعة تخفي ما سواه. ينقل التركيز إليه عند فتحه ويعيده عند إغلاقه.
 */
export function PrintView({ title, onClose, children, autoPrint }: { title: ReactNode; onClose: () => void; children: ReactNode; autoPrint?: boolean }) {
  const host = useRef<HTMLDivElement>(null);
  const titleId = useId();
  // آخر onClose في مرجع، فلا يعاد تسجيل المستمع ولا مؤقت الطباعة مع كل إعادة رسم للأب.
  const close = useRef(onClose);
  useLayoutEffect(() => {
    close.current = onClose;
  });
  const printOnOpen = useRef(autoPrint);

  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    host.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !e.defaultPrevented && !modalOpen()) close.current();
    };
    window.addEventListener("keydown", onKey);
    const timer = printOnOpen.current ? window.setTimeout(() => window.print(), 300) : undefined;
    return () => {
      window.removeEventListener("keydown", onKey);
      if (timer) window.clearTimeout(timer);
      if (previous && previous.isConnected) previous.focus();
    };
  }, []);

  // العرض قد يفتح من درج antd؛ Escape داخله يغلق العرض وحده ولا يصل إلى الدرج عبر شجرة React.
  const onKeyDown = (e: ReactKeyboardEvent) => {
    if (e.key !== "Escape" || e.defaultPrevented || modalOpen()) return;
    e.stopPropagation();
    close.current();
  };

  return createPortal(
    <div ref={host} className="print-host" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1} onKeyDown={onKeyDown}>
      <div className="print-toolbar">
        <b id={titleId}>{title}</b>
        <Space>
          <Button type="primary" icon={<PrinterOutlined />} onClick={() => window.print()}>{L("طباعة", "Print")}</Button>
          <Button icon={<CloseOutlined />} onClick={() => close.current()}>{L("إغلاق", "Close")}</Button>
        </Space>
      </div>
      <div className="print-sheet">{children}</div>
    </div>,
    document.body,
  );
}
