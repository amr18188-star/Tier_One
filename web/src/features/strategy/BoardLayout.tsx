import { useEffect, useId, useMemo, useRef, useState, type CSSProperties, type MutableRefObject, type ReactNode } from "react";
import useEmblaCarousel from "embla-carousel-react";
import type { EmblaOptionsType } from "embla-carousel";
import {
  AimOutlined, CheckCircleFilled, CloseOutlined, DownOutlined, InfoCircleOutlined, LeftOutlined, LinkOutlined, RightOutlined, RocketOutlined,
} from "@ant-design/icons";
import type { StandardNode } from "../../api/tier1";
import { L, useLang } from "../../shared/i18n";
import { StdCode } from "../../shared/pillars";
import { tint } from "./model";
import { StandardBody, StandardMenu, TypeToggle } from "./StandardBody";
import { BoardProps, ColumnCtx, Kind, REDUCE, elsewhereText, findItem, indicatorsText, initiativesText, linkedText } from "./pillarBoard.model";

/** The pillar board's layout: the board, its bar, the standards as columns (or as folding panels under 640px). */
export function useMedia(query: string): boolean {
  const [on, setOn] = useState(() => window.matchMedia(query).matches);
  useEffect(() => {
    const m = window.matchMedia(query);
    const sync = () => setOn(m.matches);
    sync();
    m.addEventListener("change", sync);
    return () => m.removeEventListener("change", sync);
  }, [query]);
  return on;
}

// ----------------------------------------------------------------- النصوص

/** يعرض صف العنصر وسط الشاشة ويضع عليه التركيز (بعد الرسم، فلا ينازعه رجوع المتصفح إلى موضعه القديم). */
export function reveal(root: HTMLElement | null, key: string) {
  requestAnimationFrame(() => requestAnimationFrame(() => {
    const row = root?.querySelector<HTMLElement>(`[data-pb-key="${CSS.escape(key)}"] > .pb-row`);
    if (!row) return;
    const r = row.getBoundingClientRect();
    window.scrollTo({ top: Math.max(0, r.top + window.scrollY - Math.max(96, (window.innerHeight - r.height) / 2)) });
    row.focus({ preventScroll: true });
  }));
}

// ----------------------------------------------------------------- اللوحة

/** يعاد تركيبه مع كل ركيزة (مفتاحه معرفها)، فيعود نوع كل عمود وقوائم «عرض الكل» واللوحات المفتوحة إلى أولها. */
export function Board(props: BoardProps) {
  const { group, focus, wide } = props;
  const focusAt = focus ? findItem(group, focus)?.at ?? -1 : -1;
  // عمود المبادرة المحددة يفتح على المبادرات، فتظهر بعد الرجوع من تفاصيلها؛ وغيره على المؤشرات.
  const [kinds, setKinds] = useState<Map<string, Kind>>(() =>
    new Map(focus?.startsWith("M:") && focusAt >= 0 ? [[group.standards[focusAt].id, "initiatives"]] : []));
  const [open, setOpen] = useState<Set<string>>(() => new Set());
  // التحديد عند التركيب (رابط مباشر أو رجوع من التفاصيل): يعرض صفه ويركز عليه مرة واحدة.
  const restore = useRef(focus);
  const ctx: ColumnCtx = {
    ...props,
    kindOf: (s) => kinds.get(s.id) ?? "indicators",
    setKind: (s, k) => setKinds((was) => new Map(was).set(s.id, k)),
    expanded: (s) => open.has(s.id),
    expand: (s) => setOpen((was) => {
      const next = new Set(was);
      if (next.has(s.id)) next.delete(s.id); else next.add(s.id);
      return next;
    }),
  };

  return (
    <div className="pb-board" style={tint(group.index)}>
      {wide
        ? <Columns ctx={ctx} focusAt={focusAt} restore={restore} />
        : <><BoardBar {...props} /><Panels ctx={ctx} focusAt={focusAt} restore={restore} /></>}
    </div>
  );
}

export function BoardBar({ group, focus, links, map, onClear, children }: BoardProps & { children?: ReactNode }) {
  const item = focus ? findItem(group, focus) : null;
  return (
    <div className="pb-bar">
      <div className="pb-status">
        {item ? (
          <>
            <span className="pb-status-sel">
              <CheckCircleFilled aria-hidden className="pb-status-ico" />
              <span className="sr-only">{L("المحدد:", "Selected:")} </span>
              <StdCode code={item.code} standard={item.std} map={map} />
              <b title={item.name}>{item.name}</b>
            </span>
            {/* على الجوال سطر واحد: العدد رقمًا والنص كاملًا لقارئ الشاشة، وزر الإلغاء أيقونة باسمه. */}
            <span className="pb-status-links">
              <LinkOutlined aria-hidden />
              <span className="pb-count-full">
                {linkedText(links.kind, links.linked.size)}
                {links.elsewhere > 0 && <> · {elsewhereText(links.elsewhere)}</>}
              </span>
              <span className="pb-count-num ltr-nums" aria-hidden>{links.linked.size}</span>
            </span>
            <button type="button" className="pb-btn pb-clear" aria-label={L("إلغاء التحديد", "Clear selection")} title={L("إلغاء التحديد", "Clear selection")} onClick={onClear}>
              <CloseOutlined aria-hidden /><span className="pb-clear-text">{L("إلغاء التحديد", "Clear selection")}</span>
            </button>
          </>
        ) : (
          <span className="pb-hint">
            <InfoCircleOutlined aria-hidden /> {L("اختر مؤشرًا أو مبادرة لترى ما يرتبط بها، وانقر مرتين لفتح تفاصيلها.", "Select an indicator or initiative to see its links. Double-click to open its details.")}
          </span>
        )}
      </div>
      {children}
    </div>
  );
}

// ----------------------------------------------------------------- الأعمدة (640px فأكثر)

/**
 * المعايير أعمدة بارتفاع واحد (الشريحة flex والعمود يملؤها)، والقوائم الطويلة تتمرر مع الصفحة لا داخل العمود.
 * إن زادت عن العرض تسحب بـembla (الاتجاه من لغة الواجهة، بلا التفاف) وبزري السابق والتالي في الشريط أعلاه،
 * وعلى الزر شارة «+n» بعدد المرتبط في الأعمدة الخارجة من العرض، ونقره يعرض أقرب عمود منها.
 */
export function Columns({ ctx, focusAt, restore }: { ctx: ColumnCtx; focusAt: number; restore: MutableRefObject<string | null> }) {
  const { group, links } = ctx;
  const n = group.standards.length;
  const rtl = useLang() === "ar";
  const dir = rtl ? "rtl" : "ltr";
  const reduced = useMedia(REDUCE);
  const perView = useMedia("(min-width: 1100px)") ? 3 : 2;
  const active = n > perView;
  const snaps = active ? n - perView + 1 : 1;
  const snapOf = (col: number) => Math.max(0, Math.min(col, snaps - 1));
  const [start] = useState(() => snapOf(focusAt));
  const options = useMemo<EmblaOptionsType>(
    () => ({ direction: dir, align: "start", containScroll: "trimSnaps", loop: false, active, startIndex: start }),
    [dir, active, start],
  );
  const [viewportRef, api] = useEmblaCarousel(options);
  const rootRef = useRef<HTMLDivElement>(null);
  const trackId = useId();
  const [index, setIndex] = useState(start);

  useEffect(() => {
    if (!api) return;
    const sync = () => setIndex(api.selectedScrollSnap());
    sync();
    api.on("select", sync).on("reInit", sync);
    return () => {
      api.off("select", sync).off("reInit", sync);
    };
  }, [api]);

  // أثناء السحب: مؤشر القبضة ولا تحديد للنص؛ ومع تقليل الحركة يستقر على موضعه فورًا بعد الإفلات.
  useEffect(() => {
    if (!api || !active) return;
    const root = api.rootNode();
    const down = () => root.setAttribute("data-dragging", "");
    const up = () => {
      root.removeAttribute("data-dragging");
      if (reduced) api.scrollTo(api.selectedScrollSnap(), true);
    };
    api.on("pointerDown", down).on("pointerUp", up);
    return () => {
      api.off("pointerDown", down).off("pointerUp", up);
    };
  }, [api, active, reduced]);

  // لوحة اللمس والعجلة الأفقية: خطوة لكل إيماءة، والتمرير العمودي يبقى للصفحة (كما في PillarCarousel).
  useEffect(() => {
    if (!api || !active) return;
    const root = api.rootNode();
    let sum = 0;
    let locked = false;
    let timer = 0;
    const onWheel = (e: WheelEvent) => {
      const unit = e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? root.clientWidth : 1;
      const dx = e.deltaX * unit;
      if (Math.abs(dx) <= Math.abs(e.deltaY * unit)) return;
      e.preventDefault();
      window.clearTimeout(timer);
      timer = window.setTimeout(() => { sum = 0; locked = false; }, 200);
      if (locked) return;
      sum += dx;
      if (Math.abs(sum) < 40) return;
      locked = true;
      if (sum > 0 !== rtl) api.scrollNext(reduced); else api.scrollPrev(reduced);
    };
    root.addEventListener("wheel", onWheel, { passive: false });
    return () => {
      root.removeEventListener("wheel", onWheel);
      window.clearTimeout(timer);
    };
  }, [api, active, rtl, reduced]);

  useEffect(() => {
    const key = restore.current;
    if (!key || (active && !api)) return;
    restore.current = null;
    if (api && active) api.scrollTo(snapOf(focusAt), true);
    reveal(rootRef.current, key);
  }, [api, active]);

  const first = active ? Math.min(index, snaps - 1) : 0;
  const lastCol = first + Math.min(perView, n) - 1;
  let before = 0;
  let after = 0;
  group.standards.forEach((s, i) => {
    const c = links.perStandard.get(s.id) ?? 0;
    if (i < first) before += c;
    else if (i > lastCol) after += c;
  });
  const has = (i: number) => (links.perStandard.get(group.standards[i].id) ?? 0) > 0;
  const go = (to: number) => api?.scrollTo(snapOf(to), reduced);
  const prev = () => {
    if (first <= 0) return;
    if (before > 0) for (let i = first - 1; i >= 0; i--) if (has(i)) return go(i);
    go(first - 1);
  };
  const next = () => {
    if (first >= snaps - 1) return;
    if (after > 0) for (let i = lastCol + 1; i < n; i++) if (has(i)) return go(i - perView + 1);
    go(first + 1);
  };
  const linkedNote = (c: number) => (c > 0 ? L(`، عناصر مرتبطة: ${c}`, `, ${c} linked`) : "");
  const PrevIcon = rtl ? RightOutlined : LeftOutlined;
  const NextIcon = rtl ? LeftOutlined : RightOutlined;

  const nav = active ? (
    <div className="pb-nav" role="group" aria-label={L("التنقل بين المعايير", "Move between standards")}>
      <span className="pb-nav-pos">{L(`المعايير ${first + 1}–${lastCol + 1} من ${n}`, `Standards ${first + 1}–${lastCol + 1} of ${n}`)}</span>
      <button type="button" className="pb-btn pb-nav-btn" aria-controls={trackId} aria-disabled={first <= 0}
        aria-label={L("المعايير السابقة", "Previous standards") + linkedNote(before)} onClick={prev}>
        <PrevIcon aria-hidden />
        {before > 0 && <span className="pb-badge" aria-hidden>+{before}</span>}
      </button>
      <button type="button" className="pb-btn pb-nav-btn" aria-controls={trackId} aria-disabled={first >= snaps - 1}
        aria-label={L("المعايير التالية", "Next standards") + linkedNote(after)} onClick={next}>
        <NextIcon aria-hidden />
        {after > 0 && <span className="pb-badge" aria-hidden>+{after}</span>}
      </button>
    </div>
  ) : null;

  return (
    <>
      <BoardBar {...ctx}>{nav}</BoardBar>
      <div ref={rootRef} className="pb-cols" dir={dir}
        {...(active ? { role: "region", "aria-roledescription": L("عرض متتابع", "carousel") } : { role: "group" })}
        aria-label={L(`معايير الركيزة: ${group.name}`, `Standards: ${group.name}`)}>
        <div className="pb-viewport" ref={viewportRef} dir={dir} data-scrollable={active ? "" : undefined}>
          <div className="pb-track" id={trackId} style={{ "--pb-n": n } as CSSProperties}>
            {group.standards.map((s) => <Column key={s.id} s={s} ctx={ctx} slide={active} />)}
          </div>
        </div>
      </div>
    </>
  );
}

export function Column({ s, ctx, slide }: { s: StandardNode; ctx: ColumnCtx; slide: boolean }) {
  const titleId = useId();
  const single = ctx.group.standards.length === 1;
  return (
    <div className="pb-slide" role="group" aria-roledescription={slide ? L("شريحة", "slide") : undefined} aria-labelledby={titleId}>
      <div className={`pb-col${single ? " single" : ""}`}>
        <header className="pb-col-head">
          <div className="pb-col-top">
            <h3 id={titleId} className="pb-col-title"><StdCode code={s.code} map={ctx.map} /><span>{s.nameAr}</span></h3>
            <StandardMenu s={s} />
          </div>
          <TypeToggle s={s} ctx={ctx} />
        </header>
        {single ? <SplitBody s={s} ctx={ctx} /> : <StandardBody s={s} ctx={ctx} />}
      </div>
    </div>
  );
}

/**
 * ركيزة بمعيار واحد: لوحتان متجاورتان للمؤشرات والمبادرات (المؤشرات في البداية)، والتحديد والإبراز يعبران بينهما.
 * يحدث ذلك حين تتسع اللوحة (900px من عرض .pb-board، استعلام حاوية في board.css لا عرض الشاشة)؛ وإلا يختفي العنوانان
 * ويظهر النوع المختار بالزر المزدوج كسائر الأعمدة. اللوحتان في الصفحة دائمًا، والمخفية منهما display: none.
 */
export function SplitBody({ s, ctx }: { s: StandardNode; ctx: ColumnCtx }) {
  const baseId = useId();
  const n = ctx.links.perStandard.get(s.id) ?? 0;
  const linkedKind: Kind | null = ctx.links.kind === "indicator" ? "initiatives" : ctx.links.kind === "initiative" ? "indicators" : null;
  const halves: { key: Kind; icon: ReactNode; label: string }[] = [
    { key: "indicators", icon: <AimOutlined aria-hidden />, label: L(`المؤشرات (${s.indicators.length})`, `Indicators (${s.indicators.length})`) },
    { key: "initiatives", icon: <RocketOutlined aria-hidden />, label: L(`المبادرات (${s.initiatives.length})`, `Initiatives (${s.initiatives.length})`) },
  ];
  return (
    <div className="pb-split" data-kind={ctx.kindOf(s)}>
      {halves.map((h) => (
        <div key={h.key} className="pb-half" data-half={h.key} role="group" aria-labelledby={`${baseId}-${h.key}`}>
          <h4 id={`${baseId}-${h.key}`} className="pb-half-head">
            {h.icon} {h.label}
            {linkedKind === h.key && n > 0 && (
              <span className="pb-seg-badge" title={L(`${n} مرتبطة بالمحدد`, `${n} linked to the selection`)}>
                <LinkOutlined aria-hidden /><span className="ltr-nums">{n}</span><span className="sr-only">{L(" مرتبطة", " linked")}</span>
              </span>
            )}
          </h4>
          <StandardBody s={s} ctx={ctx} kind={h.key} />
        </div>
      ))}
    </div>
  );
}

// ----------------------------------------------------------------- اللوحات (دون 640px)

/**
 * على الجوال المعايير لوحات تطوى كمعايير الخط الزمني: رأس كل معيار زر يفتح محتواه، وتفتح عدة لوحات معًا.
 * تبدأ مطوية إلا معيارًا وحيدًا أو معيار التحديد، وعلى رأس اللوحة عدد المرتبط فيها إن وجد. والزر المزدوج أول المحتوى.
 */
export function Panels({ ctx, focusAt, restore }: { ctx: ColumnCtx; focusAt: number; restore: MutableRefObject<string | null> }) {
  const { group, links, map } = ctx;
  const [open, setOpen] = useState<Set<string>>(() => new Set(
    group.standards.length === 1 ? [group.standards[0].id] : focusAt >= 0 ? [group.standards[focusAt].id] : [],
  ));
  const rootRef = useRef<HTMLDivElement>(null);
  const baseId = useId();
  useEffect(() => {
    const key = restore.current;
    if (!key) return;
    restore.current = null;
    reveal(rootRef.current, key);
  }, []);
  const toggle = (id: string) => setOpen((was) => {
    const next = new Set(was);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });

  return (
    <div ref={rootRef} className="pb-panels">
      {group.standards.map((s, k) => {
        const isOpen = open.has(s.id);
        const bodyId = `${baseId}-${k}`;
        const linked = links.perStandard.get(s.id) ?? 0;
        return (
          <div key={s.id} className="pb-panel" data-open={isOpen}>
            <div className="pb-panel-head">
              <h3 className="pb-panel-h">
                <button type="button" className="pb-panel-toggle" aria-expanded={isOpen} aria-controls={bodyId} onClick={() => toggle(s.id)}>
                  <DownOutlined aria-hidden className="pb-chev" />
                  <span className="pb-panel-title">
                    <span className="pb-panel-name"><StdCode code={s.code} map={map} /> <b>{s.nameAr}</b></span>
                    <small>{indicatorsText(s.indicators.length)} · {initiativesText(s.initiatives.length)}</small>
                  </span>
                  {linked > 0 ? (
                    <span className="pb-col-linked"><LinkOutlined aria-hidden /> <span className="ltr-nums">{linked}</span><span className="sr-only">{L(" مرتبطة بالمحدد", " linked")}</span></span>
                  ) : <span aria-hidden />}
                </button>
              </h3>
              <StandardMenu s={s} />
            </div>
            <div className="pb-panel-body" id={bodyId}>
              <div className="pb-panel-inner">
                <div className="pb-panel-toolbar"><TypeToggle s={s} ctx={ctx} /></div>
                <StandardBody s={s} ctx={ctx} />
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}

// ----------------------------------------------------------------- رأس المعيار: الزر المزدوج والقائمة
