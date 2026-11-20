import {
  useEffect, useId, useLayoutEffect, useRef, useState, type CSSProperties, type FocusEvent, type KeyboardEvent as ReactKeyboardEvent, type MouseEvent, type ReactNode, type RefObject,
} from "react";
import { createPortal } from "react-dom";
import { Popover } from "antd";
import {
  AimOutlined, CheckCircleFilled, CloseCircleFilled, LeftOutlined, LinkOutlined, MinusCircleFilled, RightOutlined, RocketOutlined,
} from "@ant-design/icons";
import type { IndicatorResult } from "../../api/collection";
import type { IndicatorNode } from "../../api/tier1";
import { L, useLang } from "../../shared/i18n";
import { num, performance } from "../../shared/labels";
import { StdCode } from "../../shared/pillars";
import { tint } from "./model";
import { ColumnCtx, ItemState, Links, elsewhereText, keyboard, linkedText } from "./pillarBoard.model";

/** One indicator or initiative in the pillar board, its "linked to" menu and its result chip. */
/**
 * صف مؤشر أو مبادرة: زر بحالة aria-pressed (المحدد)، وأيقونة تتبع الحالة فلا يكون اللون وحده العلامة
 * (علامة صح للمحدد، وسلسلة للمرتبط، وأيقونة النوع لغيرهما). تحت المحدد زر «تفاصيل» وعدد المرتبط في ركائز أخرى.
 * النقرة تحدد؛ وعلى المحدد: نقرة الفأرة أو Space تلغي، وEnter يفتح نافذة «مرتبط بـ» وينقل التركيز إليها، واللمسة تفتحها.
 * وتفتح أيضًا بتحويم الفأرة على المحدد وبالتركيز عليه بـTab. النقر المزدوج يفتح التفاصيل (النقرة الثانية منه لا تلغي التحديد).
 * دون 640px تعرض النافذة لوحة سفلية ثابتة (في body) بين الشريط العلوي الثابت وشريط التبويبات السفلي، يقاسان عند الفتح،
 * وبأقصى ارتفاع يتمرر داخله؛ فلا تخرج من الشاشة في أي موضع تمرير. الخيارات ولوحة المفاتيح كما في النافذة.
 */
export function Item({ k, state, kind, code, std, name, chip, ctx }: {
  k: string; state: ItemState; kind: "indicator" | "initiative"; code: string; std: string; name: string; chip?: ReactNode; ctx: ColumnCtx;
}) {
  const selected = state === "selected";
  const rtl = useLang() === "ar";
  const Go = rtl ? LeftOutlined : RightOutlined;
  const [open, setOpen] = useState(false);
  const [focusIn, setFocusIn] = useState(false);
  const rowRef = useRef<HTMLButtonElement>(null);
  const popId = useId();
  const pointer = useRef("mouse");
  const timer = useRef(0);
  const schedule = (to: boolean, ms = to ? 120 : 200) => {
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setOpen(to), ms);
  };
  const inPop = (node: Node | null) => !!node && !!document.getElementById(popId)?.contains(node);
  const sheet = !ctx.wide && selected && open;
  const [sheetBox, setSheetBox] = useState<CSSProperties>({});
  useLayoutEffect(() => {
    if (!sheet) return;
    const place = () => {
      const top = document.querySelector(".lcp-top")?.getBoundingClientRect();
      const bar = document.querySelector(".lcp-tabbar")?.getBoundingClientRect();
      const upper = top && top.height > 0 ? Math.max(0, top.bottom) : 0;
      const lower = bar && bar.height > 0 && bar.top < window.innerHeight ? bar.top : window.innerHeight;
      setSheetBox({ bottom: window.innerHeight - lower + 12, maxHeight: Math.max(160, lower - upper - 24) });
    };
    place();
    window.addEventListener("resize", place);
    return () => window.removeEventListener("resize", place);
  }, [sheet]);

  useEffect(() => () => window.clearTimeout(timer.current), []);
  // يغلق حين يزول التحديد؛ ويفتح إن حدد بالفأرة والمؤشر ما زال فوقه.
  useEffect(() => {
    if (!selected) {
      window.clearTimeout(timer.current);
      setOpen(false);
    } else if (pointer.current === "mouse" && rowRef.current?.matches(":hover")) schedule(true);
  }, [selected]);
  // نقرة خارج الصف والنافذة تغلقها.
  useEffect(() => {
    if (!open) return;
    const onDown = (e: PointerEvent) => {
      const t = e.target as Node;
      if (rowRef.current?.parentElement?.contains(t) || inPop(t)) return;
      setOpen(false);
    };
    document.addEventListener("pointerdown", onDown, true);
    return () => document.removeEventListener("pointerdown", onDown, true);
  }, [open]);
  // فتح بلوحة المفاتيح: التركيز إلى أول خيار بعد ظهور النافذة.
  useEffect(() => {
    if (!open || !focusIn) return;
    let tries = 0;
    const move = () => {
      const opt = document.getElementById(popId)?.querySelector<HTMLElement>("[data-pb-opt]");
      if (opt) { opt.focus(); setFocusIn(false); } else if (tries++ < 20) requestAnimationFrame(move);
    };
    requestAnimationFrame(move);
  }, [open, focusIn]);

  const onClick = (e: MouseEvent<HTMLButtonElement>) => {
    if (e.detail > 1) return;
    if (selected && e.detail > 0 && pointer.current !== "mouse") { setOpen((o) => !o); return; }
    ctx.onToggle(k);
  };
  const onKeyDown = (e: ReactKeyboardEvent<HTMLButtonElement>) => {
    if (!selected) return;
    if (e.key === "Enter") {
      e.preventDefault();
      window.clearTimeout(timer.current);
      setOpen(true);
      setFocusIn(true);
    } else if (e.key === "Escape" && open) {
      e.preventDefault();
      setOpen(false);
    }
  };
  const onFocus = () => { if (selected && Date.now() - keyboard.lastTabAt < 600) schedule(true, 0); };
  const onBlur = (e: FocusEvent<HTMLButtonElement>) => { if (!inPop(e.relatedTarget)) schedule(false); };
  const close = (refocus: boolean) => {
    window.clearTimeout(timer.current);
    setOpen(false);
    if (refocus) rowRef.current?.focus();
  };

  const row = (
    <button ref={rowRef} type="button" className="pb-row" aria-pressed={selected}
      aria-haspopup={selected ? "menu" : undefined} aria-expanded={selected ? open : undefined} aria-controls={selected && open ? popId : undefined}
      onPointerDown={(e) => { pointer.current = e.pointerType; }} onClick={onClick} onDoubleClick={() => ctx.onDetails(k)}
      onKeyDown={onKeyDown} onFocus={onFocus} onBlur={onBlur}>
      <span className="pb-ico" aria-hidden>{selected ? <CheckCircleFilled /> : state === "linked" ? <LinkOutlined /> : kind === "indicator" ? <AimOutlined /> : <RocketOutlined />}</span>
      <span className="pb-text"><StdCode code={code} standard={std} map={ctx.map} />{name}</span>
      {chip}
      {state === "linked" && <span className="sr-only">{L("، مرتبط بالمحدد", ", linked to the selection")}</span>}
    </button>
  );

  const menu = (
    <LinkedMenu id={popId} code={code} kind={kind} links={ctx.links} groupIndex={ctx.group.index} rowRef={rowRef}
      onEnter={() => window.clearTimeout(timer.current)} onLeave={() => schedule(false)} onClose={close}
      onOpen={(key) => { close(false); ctx.onOpen(key); }} onDetails={() => { close(false); ctx.onDetails(k); }} />
  );

  return (
    <li className="pb-item" data-state={state} data-pb-key={k}
      onMouseEnter={() => { if (selected && pointer.current === "mouse") schedule(true); }}
      onMouseLeave={() => { if ((open || selected) && pointer.current === "mouse") schedule(false); }}>
      {/* الصف داخل Popover دائمًا (مغلقًا لغير المحدد)، فلا يعاد تركيب الزر عند التحديد ولا يضيع تركيزه. */}
      <Popover open={ctx.wide && selected && open} trigger={[]} arrow destroyOnHidden rootClassName="pb-pop-root"
        placement={rtl ? "leftTop" : "rightTop"} content={menu}>
        {row}
      </Popover>
      {sheet && createPortal(<div className="pb-sheet" style={sheetBox}>{menu}</div>, document.body)}
      {selected && (
        <div className="pb-selbar">
          {ctx.links.elsewhere > 0 && <span className="pb-elsewhere"><LinkOutlined aria-hidden /> {elsewhereText(ctx.links.elsewhere)}</span>}
          {/* والنافذة مفتوحة يخرج هذا الزر من ترتيب Tab: فيها «تفاصيل هذا العنصر»، وقد تغطيه على الجوال. */}
          <button type="button" className="pb-btn pb-details" tabIndex={open ? -1 : undefined} onClick={() => ctx.onDetails(k)}>
            {L("تفاصيل", "Details")} <Go aria-hidden />
          </button>
        </div>
      )}
    </li>
  );
}

/**
 * نافذة «مرتبط بـ» على المحدد: المرتبط من النوع الآخر خيارات (الرمز والاسم مقطوعًا، وكاملًا في title)، كل منها يفتح
 * تفاصيله، ثم فاصل و«تفاصيل هذا العنصر». الأسهم وHome/End تنقل بين الخيارات، وEnter يفتح، وEsc يغلق ويرجع التركيز
 * إلى الصف؛ وTab يخرج منها ويغلقها (لا حبس للتركيز).
 */
export function LinkedMenu({ id, code, kind, links, groupIndex, rowRef, onEnter, onLeave, onClose, onOpen, onDetails }: {
  id: string; code: string; kind: "indicator" | "initiative"; links: Links; groupIndex: number; rowRef: RefObject<HTMLButtonElement>;
  onEnter: () => void; onLeave: () => void; onClose: (refocus: boolean) => void; onOpen: (key: string) => void; onDetails: () => void;
}) {
  const Go = useLang() === "ar" ? LeftOutlined : RightOutlined;
  const Icon = kind === "indicator" ? RocketOutlined : AimOutlined;
  const onKeyDown = (e: ReactKeyboardEvent<HTMLDivElement>) => {
    const opts = [...e.currentTarget.querySelectorAll<HTMLElement>("[data-pb-opt]")];
    const at = opts.indexOf(document.activeElement as HTMLElement);
    const to = e.key === "ArrowDown" ? Math.min(opts.length - 1, at + 1) : e.key === "ArrowUp" ? Math.max(0, at - 1)
      : e.key === "Home" ? 0 : e.key === "End" ? opts.length - 1 : null;
    if (to !== null) {
      e.preventDefault();
      opts[to]?.focus();
    } else if (e.key === "Escape") {
      e.preventDefault();
      e.stopPropagation();
      onClose(true);
    }
  };
  const onBlur = (e: FocusEvent<HTMLDivElement>) => {
    const to = e.relatedTarget as Node | null;
    if (to && (e.currentTarget.contains(to) || to === rowRef.current)) return;
    onClose(false);
  };
  return (
    <div id={id} className="pb-pop" style={tint(groupIndex)} role="menu" aria-label={L(`مرتبط بـ ${code}`, `Linked to ${code}`)}
      onKeyDown={onKeyDown} onBlur={onBlur} onMouseEnter={onEnter} onMouseLeave={onLeave}>
      <p className="pb-pop-title" aria-hidden><LinkOutlined /> {L("مرتبط بـ", "Linked to")} <small>{linkedText(links.kind, links.items.length)}</small></p>
      {links.items.length === 0 ? (
        <p className="pb-pop-none">{L("لا عناصر مرتبطة", "No linked items")}</p>
      ) : links.items.map((x) => (
        <button key={x.key} type="button" role="menuitem" data-pb-opt="" className="pb-pop-opt" title={`${x.code} ${x.name}`} onClick={() => onOpen(x.key)}>
          <Icon aria-hidden className="pb-pop-ico" /><code className="ltr">{x.code}</code><span>{x.name}</span>
        </button>
      ))}
      <div role="separator" className="pb-pop-sep" />
      <button type="button" role="menuitem" data-pb-opt="" className="pb-pop-opt details" onClick={onDetails}>
        <span>{L("تفاصيل هذا العنصر", "Details of this item")}</span><Go aria-hidden />
      </button>
    </div>
  );
}

export const PERF_SHORT: Partial<Record<IndicatorResult["performance"], [string, string]>> = {
  Achieved: ["محقق", "Achieved"],
  Near: ["قريب", "Near"],
  Behind: ["متأخر", "Behind"],
};

/** آخر قياس معتمد للمؤشر إن وجد: القيمة وحكم الأداء بنص قصير وأيقونة (لا باللون وحده). */
export function ResultChip({ i, r }: { i: IndicatorNode; r: IndicatorResult | undefined }) {
  if (!r || r.actual === null) return null;
  const v = i.unit.trim() === "%" ? `${num(r.actual)}%` : num(r.actual);
  const p = r.performance;
  const short = PERF_SHORT[p];
  const text = short ? L(short[0], short[1]) : L(performance[p].text, p);
  const Icon = p === "Achieved" ? CheckCircleFilled : p === "Near" ? MinusCircleFilled : p === "Behind" ? CloseCircleFilled : null;
  return (
    <span className={`pb-chip perf-${p}`} title={L(`آخر قياس معتمد: ${v} — ${performance[p].text}`, `Latest approved: ${v} — ${p}`)}>
      {Icon && <Icon aria-hidden />}
      <b className="ltr-nums">{v}</b>
      <span>{text}</span>
    </span>
  );
}
