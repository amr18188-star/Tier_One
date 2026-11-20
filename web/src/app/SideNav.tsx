import { cloneElement, useEffect, useId, useState, type MouseEvent as ReactMouseEvent, type ReactElement } from "react";
import { Tooltip } from "antd";
import { DownOutlined } from "@ant-design/icons";
import { NavLink } from "react-router-dom";
import { L, useLang } from "../shared/i18n";
import { TipSide, VisibleGroup } from "./navState";

/** The side menu: groups as an accordion, the collapsed rail with its tooltips, and the badges. */
/**
 * تلميح الشريط المطوي بالمرور والتركيز، نحو المحتوى (antd لا يقلب يمين ويسار مع الاتجاه، والشريط يمين الشاشة في العربية).
 * يغلق عند الضغط: فتح مجموعة يطوي غيرها فيتحرك الزر، ولا يبقى التلميح معلقًا في المكان القديم.
 * بلا عنوان يعرض العنصر وحده، فالقائمة الموسعة والدرج كما هما.
 */
export function RailTip({ title, side, children }: {
  title?: string;
  side: TipSide;
  children: ReactElement<{ onClick?: (event: ReactMouseEvent<HTMLElement>) => void }>;
}) {
  const [open, setOpen] = useState(false);
  useEffect(() => {
    if (!title) setOpen(false);
  }, [title]);
  if (!title) return children;
  const child = cloneElement(children, {
    onClick: (event: ReactMouseEvent<HTMLElement>) => {
      setOpen(false);
      children.props.onClick?.(event);
    },
  });
  return (
    <Tooltip title={title} placement={side} trigger={["hover", "focus"]} mouseEnterDelay={0.15} open={open} onOpenChange={setOpen}>
      {child}
    </Tooltip>
  );
}

/**
 * روابط القائمة الجانبية. كل مجموعة بعنوان لوحة تطوى: الرأس زر يفتحها ويطويها، ويعرض وهي مطوية مجموع شارات روابطها.
 * تعرض القائمة مرتين (الشريط الجانبي ودرج الجوال)، فمعرفات الرؤوس واللوحات من useId لكل نسخة، والحالة المفتوحة مشتركة من Shell.
 * rail (الشريط المطوي، الحاسوب وحده): الأكورديون نفسه والحالة المفتوحة نفسها، لكن الرأس زر بأيقونة المجموعة واسمها في تلميح،
 * والروابط أيقونات تحته. active مفتاح مجموعة الصفحة الحالية: شريط ذهبي على زرها ولو كانت مطوية.
 */
export function SideNav({ groups, open, active, onToggle, onNavigate, rail }: {
  groups: VisibleGroup[];
  open: string | null;
  active?: string;
  onToggle: (key: string) => void;
  onNavigate: () => void;
  rail: boolean;
}) {
  const uid = useId();
  const tipSide: TipSide = useLang() === "ar" ? "left" : "right";
  return (
    <nav className="lcp-nav" aria-label={L("القوائم الرئيسية", "Main menu")}>
      {groups.map((g) => {
        // اسم الرابط نصه (مخفي بصريًا في الشريط المطوي)؛ العدد نقطة على الأيقونة ورقم في التلميح.
        const links = g.items.map((n) => (
          <RailTip key={n.to} title={rail ? (n.badge ? `${n.label} · ${n.badge}` : n.label) : undefined} side={tipSide}>
            <NavLink to={n.to} end={n.end} onClick={onNavigate}>
              {n.icon}<span className="lcp-nav-label">{n.label}</span>
              {!!n.badge && (
                <>
                  {rail ? <span className="lcp-rail-dot" aria-hidden /> : <span className="lcp-badge" aria-hidden>{n.badge}</span>}
                  <span className="sr-only">{n.badgeText}</span>
                </>
              )}
            </NavLink>
          </RailTip>
        ));
        if (!g.title) return <div key={g.key} className="lcp-nav-sec" role="group">{links}</div>;

        const isOpen = open === g.key;
        const headId = `${uid}-${g.key}-head`;
        const panelId = `${uid}-${g.key}-panel`;
        const badged = g.items.filter((n) => !!n.badge);
        const total = badged.reduce((sum, n) => sum + (n.badge ?? 0), 0);
        const sep = L("، ", ", ");
        const badgeTexts = badged.map((n) => n.badgeText).filter(Boolean).join(sep);
        // الشريط المطوي: الاسم عنوان المجموعة، والعدد (والمجموعة مطوية) نقطة على الأيقونة ووصف لقارئ الشاشة ورقم في التلميح.
        const badgeId = `${headId}-badges`;
        const head = rail ? (
          <RailTip title={total > 0 ? `${g.title} · ${total}` : g.title} side={tipSide}>
            <button type="button" id={headId} className="lcp-nav-cat" aria-expanded={isOpen} aria-controls={panelId} aria-label={g.title}
              aria-describedby={!isOpen && total > 0 ? badgeId : undefined} onClick={() => onToggle(g.key)}>
              {g.icon}
              {!isOpen && total > 0 && <><span className="lcp-rail-dot" aria-hidden /><span id={badgeId} className="sr-only">{badgeTexts}</span></>}
            </button>
          </RailTip>
        ) : (
          <button type="button" id={headId} className="lcp-nav-group lcp-nav-head" aria-expanded={isOpen} aria-controls={panelId} onClick={() => onToggle(g.key)}>
            <span className="lcp-nav-title">{g.title}</span>
            {!isOpen && total > 0 && (
              <>
                <span className="lcp-badge" aria-hidden>{total}</span>
                <span className="sr-only">{sep}{badgeTexts}</span>
              </>
            )}
            <DownOutlined className="lcp-nav-chev" aria-hidden />
          </button>
        );
        return (
          <div key={g.key} className="lcp-nav-sec lcp-nav-panel" data-open={isOpen ? "true" : "false"}
            data-active={rail ? (active === g.key ? "true" : "false") : undefined}>
            {head}
            <div id={panelId} className="lcp-nav-body" role="group" aria-labelledby={headId}>
              <div className="lcp-nav-inner">{links}</div>
            </div>
          </div>
        );
      })}
    </nav>
  );
}
