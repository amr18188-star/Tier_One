import {
  useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type FocusEvent, type KeyboardEvent as ReactKeyboardEvent, type MouseEvent,
  type MutableRefObject, type ReactNode, type RefObject,
} from "react";
import { createPortal } from "react-dom";
import { Dropdown, Popover, type MenuProps } from "antd";
import useEmblaCarousel from "embla-carousel-react";
import type { EmblaOptionsType } from "embla-carousel";
import {
  AimOutlined, CheckCircleFilled, CloseCircleFilled, CloseOutlined, DownOutlined, EditOutlined, EllipsisOutlined, FileTextOutlined, InfoCircleOutlined,
  LeftOutlined, LinkOutlined, MinusCircleFilled, PlusOutlined, RightOutlined, RocketOutlined,
} from "@ant-design/icons";
import { useNavigate, useSearchParams } from "react-router-dom";
import type { IndicatorResult } from "../../api/collection";
import type { IndicatorNode, StandardNode, StrategyTree } from "../../api/tier1";
import { L, useLang } from "../../shared/i18n";
import { num, performance } from "../../shared/labels";
import { stdLabel, StdCode, type PillarInfo } from "../../shared/pillars";
import { EmptyNote } from "../../shared/ui";
import { allStandards, tint, usePlan, type ResultMap } from "./model";
import { PillarCarousel, type CarouselSlide } from "./PillarCarousel";
import "./board.css";

/**
 * «الهيكل والعلاقات»: ركيزة واحدة في كل مرة (شريط الركائز نفسه في الخط الزمني)، ومعاييرها أعمدة متجاورة
 * (ثلاثة من 1100px واثنان من 640px، وتسحب بـembla بلا التفاف إن زادت). في كل عمود نوع واحد: المؤشرات أو المبادرات،
 * يبدل بينهما زر مزدوج في رأس العمود (المؤشرات افتراضًا؛ وعمود المبادرة المحددة يفتح على المبادرات).
 * تحديد مؤشر أو مبادرة (نقرة أو Enter/Space) يبرز ما يرتبط به (InitiativeNode.indicatorCodes) ويخفت سائر الركيزة؛
 * النقرة الثانية بالفأرة أو Space أو Esc تلغيه. على المحدد نافذة «مرتبط بـ» (تحويم أو تركيز أو Enter، ولمسة ثانية باللمس):
 * المرتبط خيارات يفتح كل منها تفاصيله، ثم «تفاصيل هذا العنصر». الفتح إضافة إلى السجل (?view=explore&node=…)،
 * فيرجع زر الرجوع إلى اللوحة بركيزتها وتحديدها ظاهرًا. والإضافة والتعديل من مكانهما بحسب صلاحيات tree.actions.
 * دون 640px تصير المعايير لوحات تطوى كمعايير الخط الزمني، بالزر المزدوج والنافذة نفسيهما.
 * الرابط: ?view=board&pillar=<معرف الركيزة أو none>&focus=I:<رمز المؤشر>|M:<معرف المبادرة>؛ الركيزة الناقصة أو غير الصالحة
 * ترجع إلى الأولى، والتحديد الذي ليس في الركيزة يسقط، ويصحح الرابط بالاستبدال؛ وتغيير الركيزة والتحديد يستبدل ولا يضيف.
 * نوع كل عمود حالة محلية لا في الرابط: يكفي التحديد ليفتح العمود على نوعه بعد الرجوع.
 */

/** المبادرات الظاهرة في كل معيار قبل «عرض الكل»؛ والمرتبط بالمحدد يظهر دائمًا. */
const FIRST = 6;
const REDUCE = "(prefers-reduced-motion: reduce)";
/** آخر ضغطة Tab: التركيز بلوحة المفاتيح على المحدد يفتح نافذته، والتركيز البرمجي (بعد الرجوع) لا يفتحها. */
let lastTabAt = 0;

interface Group { id: string; name: string; index: number; standards: StandardNode[] }
type Kind = "indicators" | "initiatives";
type ItemState = "idle" | "selected" | "linked" | "dim";
interface LinkedItem { key: string; code: string; name: string }
interface Links {
  /** نوع المحدد: مؤشر (فالمرتبط مبادرات) أو مبادرة (فالمرتبط مؤشرات). */
  kind: "indicator" | "initiative" | null;
  linked: Set<string>;
  /** عدد المرتبط في كل معيار من الركيزة (لشارات الأعمدة والزر المزدوج). */
  perStandard: Map<string, number>;
  /** مرتبط في ركائز أخرى. */
  elsewhere: number;
  /** المرتبط كله لنافذة المحدد: ما في الركيزة أولًا. */
  items: LinkedItem[];
}
interface Found { at: number; code: string; std: string; name: string }

function useMedia(query: string): boolean {
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

function indicatorsText(n: number): string {
  return L(n === 0 ? "لا مؤشرات" : n === 1 ? "مؤشر واحد" : n === 2 ? "مؤشران" : n <= 10 ? `${n} مؤشرات` : `${n} مؤشرًا`, n === 1 ? "1 indicator" : `${n} indicators`);
}

function initiativesText(n: number): string {
  return L(n === 0 ? "لا مبادرات" : n === 1 ? "مبادرة واحدة" : n === 2 ? "مبادرتان" : n <= 10 ? `${n} مبادرات` : `${n} مبادرة`, n === 1 ? "1 initiative" : `${n} initiatives`);
}

/** «3 مؤشرات مرتبطة» لمبادرة محددة، و«4 مبادرات مرتبطة» لمؤشر محدد. */
function linkedText(kind: Links["kind"], n: number): string {
  if (kind === "initiative")
    return L(n === 0 ? "لا مؤشرات مرتبطة" : n === 1 ? "مؤشر واحد مرتبط" : n === 2 ? "مؤشران مرتبطان" : n <= 10 ? `${n} مؤشرات مرتبطة` : `${n} مؤشرًا مرتبطًا`,
      n === 0 ? "No linked indicators" : n === 1 ? "1 linked indicator" : `${n} linked indicators`);
  return L(n === 0 ? "لا مبادرات مرتبطة" : n === 1 ? "مبادرة واحدة مرتبطة" : n === 2 ? "مبادرتان مرتبطتان" : n <= 10 ? `${n} مبادرات مرتبطة` : `${n} مبادرة مرتبطة`,
    n === 0 ? "No linked initiatives" : n === 1 ? "1 linked initiative" : `${n} linked initiatives`);
}

function elsewhereText(n: number): string {
  return L(`+${n} في ركائز أخرى`, `+${n} in other pillars`);
}

// ----------------------------------------------------------------- الروابط

/** موضع العنصر (I:رمز أو M:معرف) في الركيزة، أو null إن لم يكن فيها. */
function findItem(group: Group, key: string): Found | null {
  const id = key.slice(2);
  const kind = key.slice(0, 2);
  for (let at = 0; at < group.standards.length; at++) {
    const s = group.standards[at];
    if (kind === "I:") {
      const i = s.indicators.find((x) => x.code === id);
      if (i) return { at, code: i.code, std: s.code, name: i.nameAr };
    } else if (kind === "M:") {
      const m = s.initiatives.find((x) => x.id === id);
      if (m) return { at, code: m.code, std: s.code, name: m.nameAr };
    }
  }
  return null;
}

/** ما يرتبط بالعنصر المحدد: المبادرات التي تذكر رمز المؤشر، أو المؤشرات التي تذكرها المبادرة. */
function linksOf(tree: StrategyTree, group: Group | undefined, key: string | null): Links {
  const out: Links = { kind: null, linked: new Set(), perStandard: new Map(), elsewhere: 0, items: [] };
  if (!group || !key) return out;
  const id = key.slice(2);
  const own = new Set(group.standards.map((s) => s.id));
  const outside: LinkedItem[] = [];
  const add = (s: StandardNode, item: LinkedItem) => {
    if (!own.has(s.id)) {
      out.elsewhere++;
      outside.push(item);
      return;
    }
    out.items.push(item);
    out.linked.add(item.key);
    out.perStandard.set(s.id, (out.perStandard.get(s.id) ?? 0) + 1);
  };
  if (key.startsWith("I:")) {
    out.kind = "indicator";
    for (const s of allStandards(tree))
      for (const m of s.initiatives) if (m.indicatorCodes.includes(id)) add(s, { key: `M:${m.id}`, code: m.code, name: m.nameAr });
  } else {
    const m = group.standards.flatMap((s) => s.initiatives).find((x) => x.id === id);
    if (!m) return out;
    out.kind = "initiative";
    const codes = new Set(m.indicatorCodes);
    for (const s of allStandards(tree)) for (const i of s.indicators) if (codes.has(i.code)) add(s, { key: `I:${i.code}`, code: i.code, name: i.nameAr });
  }
  out.items.push(...outside);
  return out;
}

/** يعرض صف العنصر وسط الشاشة ويضع عليه التركيز (بعد الرسم، فلا ينازعه رجوع المتصفح إلى موضعه القديم). */
function reveal(root: HTMLElement | null, key: string) {
  requestAnimationFrame(() => requestAnimationFrame(() => {
    const row = root?.querySelector<HTMLElement>(`[data-pb-key="${CSS.escape(key)}"] > .pb-row`);
    if (!row) return;
    const r = row.getBoundingClientRect();
    window.scrollTo({ top: Math.max(0, r.top + window.scrollY - Math.max(96, (window.innerHeight - r.height) / 2)) });
    row.focus({ preventScroll: true });
  }));
}

// ----------------------------------------------------------------- اللوحة

export function PillarBoard() {
  const { tree, results, select, edit } = usePlan();
  const lang = useLang();
  const [params, setParams] = useSearchParams();
  const navigate = useNavigate();
  const wide = useMedia("(min-width: 640px)");
  const a = tree.actions;
  const Go = lang === "ar" ? LeftOutlined : RightOutlined;

  const groups: Group[] = useMemo(() => [
    ...tree.pillars.map((p, index) => ({ id: p.id, name: p.nameAr, index, standards: p.standards })),
    ...(tree.unassigned.length > 0 ? [{ id: "none", name: L("معايير بلا ركيزة", "No pillar"), index: -1, standards: tree.unassigned }] : []),
  ], [tree, lang]);
  const pillarMap = useMemo(() => {
    const map = new Map<string, PillarInfo>();
    tree.pillars.forEach((p, index) => p.standards.forEach((s) => map.set(s.code, { index, id: p.id, code: p.code, name: p.nameAr })));
    return map;
  }, [tree]);

  const rawPillar = params.get("pillar");
  const rawFocus = params.get("focus");
  const found = groups.findIndex((g) => g.id === rawPillar);
  const at = found < 0 ? 0 : found;
  const group: Group | undefined = groups[at];
  const pillar = group && group.index >= 0 ? tree.pillars[group.index] : undefined;
  const item = group && rawFocus ? findItem(group, rawFocus) : null;
  const focus = item ? rawFocus : null;
  const links = useMemo(() => linksOf(tree, group, focus), [tree, group, focus]);

  // الركيزة الناقصة أو غير الصالحة ترجع إلى الأولى، والتحديد الذي ليس فيها يسقط؛ بالاستبدال فلا يزيد السجل.
  useEffect(() => {
    if (!group || (rawPillar === group.id && rawFocus === focus)) return;
    setParams((prev) => {
      const p = new URLSearchParams(prev);
      p.set("pillar", group.id);
      if (focus) p.set("focus", focus); else p.delete("focus");
      return p;
    }, { replace: true });
  }, [group?.id, rawPillar, rawFocus, focus]); // setParams يتجدد مع كل رابط ولا يغير شيئًا هنا

  const setFocus = (key: string | null) => setParams((prev) => {
    const p = new URLSearchParams(prev);
    if (key) p.set("focus", key); else p.delete("focus");
    return p;
  }, { replace: true });
  const toggle = (key: string) => setFocus(key === focus ? null : key);
  const changePillar = (id: string) => {
    if (id === group?.id) return;
    setParams((prev) => {
      const p = new URLSearchParams(prev);
      p.set("pillar", id);
      p.delete("focus");
      return p;
    }, { replace: true });
  };
  // التفاصيل تضاف إلى السجل؛ وقبلها يثبت التحديد في رابط اللوحة (النقر المزدوج قد يلغيه بنقرته الأولى)، فيرجع إليه الرجوع.
  const openDetails = (key: string) => {
    if (key !== focus) {
      const p = new URLSearchParams(params);
      p.set("focus", key);
      navigate({ search: `?${p.toString()}` }, { replace: true });
    }
    select(key);
  };

  // Esc يلغي التحديد، إلا في حقل أو نافذة منبثقة (ونافذة «مرتبط بـ» تغلق نفسها أولًا وتمنع الحدث).
  useEffect(() => {
    if (!focus) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape" || e.defaultPrevented) return;
      const t = e.target instanceof Element ? e.target : null;
      if (t?.closest("input, textarea, select, [contenteditable='true'], .ant-modal-root, .ant-drawer, .ant-select-dropdown, .ant-dropdown")) return;
      setFocus(null);
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [focus, setParams]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Tab") lastTabAt = Date.now(); };
    document.addEventListener("keydown", onKey, true);
    return () => document.removeEventListener("keydown", onKey, true);
  }, []);

  // منطقة إعلان واحدة: عدد المرتبط عند كل تحديد، و«ألغي التحديد» عند إلغائه في الركيزة نفسها.
  const [said, setSaid] = useState("");
  const last = useRef<{ group?: string; focus: string | null }>({ group: group?.id, focus: null });
  useEffect(() => {
    const was = last.current;
    last.current = { group: group?.id, focus };
    if (focus && item) setSaid(`${item.code}: ${linkedText(links.kind, links.linked.size)}${links.elsewhere > 0 ? `، ${elsewhereText(links.elsewhere)}` : ""}`);
    else if (was.focus && was.group === group?.id) setSaid(L("ألغي التحديد", "Selection cleared"));
    else setSaid("");
  }, [focus, group?.id]);

  const slides: CarouselSlide[] = groups.map((g) => {
    const indicators = g.standards.reduce((n, s) => n + s.indicators.length, 0);
    const initiatives = g.standards.reduce((n, s) => n + s.initiatives.length, 0);
    return {
      key: g.id,
      name: g.name,
      node: (
        <article className={`sp-pillar sp-road-pick${g.index < 0 ? " loose" : ""}`} style={tint(g.index)}>
          <header>
            <div className="sp-pillar-head">
              {g.index >= 0 && <span className="sp-pillar-num ltr-nums">{String(g.index + 1).padStart(2, "0")}</span>}
              <span className="sp-pillar-name"><b title={g.name}>{g.name}</b></span>
            </div>
            <dl className="sp-pillar-stats">
              <div><dt>{L("معيار", "standards")}</dt><dd>{g.standards.length}</dd></div>
              <div><dt>{L("مؤشر", "indicators")}</dt><dd>{indicators}</dd></div>
              <div><dt>{L("مبادرة", "initiatives")}</dt><dd>{initiatives}</dd></div>
            </dl>
          </header>
        </article>
      ),
    };
  });

  // إجراءات الركيزة تحت شريطها لا داخل بطاقاته: زر في بطاقة خارج العرض يسحب الشريط إليها حين يركز عليه فتتغير الركيزة.
  const pillarBar = (pillar || a.canAdd) && (
    <div className="pb-pillar-bar" role="group" aria-label={L("إجراءات الركيزة", "Pillar actions")}>
      {pillar && (
        <>
          <button type="button" className="pb-btn" onClick={() => select(`P:${pillar.id}`)}>{L("تفاصيل الركيزة", "Pillar details")} <Go aria-hidden /></button>
          {a.canEdit && <button type="button" className="pb-btn" onClick={() => edit({ kind: "pillar", pillar })}><EditOutlined aria-hidden /> {L("تعديل الركيزة", "Edit pillar")}</button>}
          {a.canAdd && <button type="button" className="pb-btn" onClick={() => edit({ kind: "standard", pillarId: pillar.id })}><PlusOutlined aria-hidden /> {L("معيار جديد", "New standard")}</button>}
        </>
      )}
      {a.canAdd && <button type="button" className="pb-btn primary pb-new-pillar" onClick={() => edit({ kind: "pillar" })}><PlusOutlined aria-hidden /> {L("ركيزة جديدة", "New pillar")}</button>}
    </div>
  );

  return (
    <section className="lcp-panel pb" aria-label={L("الهيكل والعلاقات", "Structure & links")}>
      {groups.length > 0 && (
        <PillarCarousel single label={L("الركيزة المعروضة", "Pillar shown")} slides={slides} selected={at}
          onSelect={(i) => groups[i] && changePillar(groups[i].id)} />
      )}
      {pillarBar}
      {!group || group.standards.length === 0 ? (
        <EmptyNote>{L("لا معايير في هذه الركيزة.", "No standards in this pillar.")}</EmptyNote>
      ) : (
        <Board key={group.id} group={group} focus={focus} links={links} wide={wide} map={pillarMap} results={results}
          onToggle={toggle} onDetails={openDetails} onOpen={select} onClear={() => setFocus(null)} />
      )}
      <p className="sr-only" aria-live="polite">{said}</p>
    </section>
  );
}

interface BoardProps {
  group: Group;
  focus: string | null;
  links: Links;
  wide: boolean;
  map: Map<string, PillarInfo>;
  results: ResultMap;
  onToggle: (key: string) => void;
  /** تفاصيل العنصر نفسه (يثبت التحديد في الرابط أولًا). */
  onDetails: (key: string) => void;
  /** تفاصيل عنصر مرتبط (التحديد في الرابط باق). */
  onOpen: (key: string) => void;
  onClear: () => void;
}

interface ColumnCtx extends BoardProps {
  kindOf: (s: StandardNode) => Kind;
  setKind: (s: StandardNode, k: Kind) => void;
  expanded: (s: StandardNode) => boolean;
  expand: (s: StandardNode) => void;
}

/** يعاد تركيبه مع كل ركيزة (مفتاحه معرفها)، فيعود نوع كل عمود وقوائم «عرض الكل» واللوحات المفتوحة إلى أولها. */
function Board(props: BoardProps) {
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

function BoardBar({ group, focus, links, map, onClear, children }: BoardProps & { children?: ReactNode }) {
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
function Columns({ ctx, focusAt, restore }: { ctx: ColumnCtx; focusAt: number; restore: MutableRefObject<string | null> }) {
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

function Column({ s, ctx, slide }: { s: StandardNode; ctx: ColumnCtx; slide: boolean }) {
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
function SplitBody({ s, ctx }: { s: StandardNode; ctx: ColumnCtx }) {
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
function Panels({ ctx, focusAt, restore }: { ctx: ColumnCtx; focusAt: number; restore: MutableRefObject<string | null> }) {
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

/**
 * «المؤشرات (n) | المبادرات (n)»: زران بحالة aria-pressed. على الخيار الذي فيه مرتبط بالمحدد شارة بعدده
 * (وعلامة صح إن كان المحدد نفسه فيه وهو مخفي)، فيعرف القارئ أن التبديل يظهر المرتبط مبرزًا.
 */
function TypeToggle({ s, ctx }: { s: StandardNode; ctx: ColumnCtx }) {
  const kind = ctx.kindOf(s);
  const n = ctx.links.perStandard.get(s.id) ?? 0;
  const linkedKind: Kind | null = ctx.links.kind === "indicator" ? "initiatives" : ctx.links.kind === "initiative" ? "indicators" : null;
  const selectedKind: Kind | null = !ctx.focus ? null
    : ctx.focus.startsWith("I:") ? (s.indicators.some((i) => `I:${i.code}` === ctx.focus) ? "indicators" : null)
      : (s.initiatives.some((m) => `M:${m.id}` === ctx.focus) ? "initiatives" : null);
  const options: { key: Kind; label: string; count: number }[] = [
    { key: "indicators", label: L(`المؤشرات (${s.indicators.length})`, `Indicators (${s.indicators.length})`), count: s.indicators.length },
    { key: "initiatives", label: L(`المبادرات (${s.initiatives.length})`, `Initiatives (${s.initiatives.length})`), count: s.initiatives.length },
  ];
  return (
    <div className="pb-seg" role="group" aria-label={L(`نوع العناصر في ${stdLabel(s.code)}`, `Item type in ${s.code}`)}>
      {options.map((o) => (
        <button key={o.key} type="button" className="pb-seg-btn" aria-pressed={kind === o.key} onClick={() => ctx.setKind(s, o.key)}>
          <span className="pb-seg-label">{o.label}</span>
          {selectedKind === o.key && kind !== o.key && (
            <CheckCircleFilled className="pb-seg-sel" aria-label={L("فيه المحدد", "has the selection")} />
          )}
          {linkedKind === o.key && n > 0 && (
            <span className="pb-seg-badge" title={L(`${n} مرتبطة بالمحدد`, `${n} linked to the selection`)}>
              <LinkOutlined aria-hidden /><span className="ltr-nums">{n}</span><span className="sr-only">{L(" مرتبطة", " linked")}</span>
            </span>
          )}
        </button>
      ))}
    </div>
  );
}

/** قائمة ⋯ في رأس المعيار: تعديله (بصلاحية canEdit) وشرحه. تفتح بالنقر أو Enter، وتتنقل بالأسهم، ويغلقها Esc. */
function StandardMenu({ s }: { s: StandardNode }) {
  const { tree, select, edit } = usePlan();
  const items: MenuProps["items"] = [
    ...(tree.actions.canEdit ? [{ key: "edit", icon: <EditOutlined />, label: L("تعديل المعيار", "Edit standard") }] : []),
    { key: "details", icon: <FileTextOutlined />, label: L("شرح المعيار", "Standard details") },
  ];
  return (
    <Dropdown trigger={["click"]} autoFocus
      menu={{ items, onClick: ({ key }) => (key === "edit" ? edit({ kind: "standard", standard: s }) : select(`S:${s.id}`)) }}>
      <button type="button" className="pb-icon-btn" aria-label={L(`إجراءات المعيار ${stdLabel(s.code)}`, `Standard ${s.code} actions`)}>
        <EllipsisOutlined aria-hidden />
      </button>
    </Dropdown>
  );
}

// ----------------------------------------------------------------- محتوى المعيار وصفوفه

function StandardBody({ s, ctx, kind: fixed }: { s: StandardNode; ctx: ColumnCtx; kind?: Kind }) {
  const { tree, edit } = usePlan();
  const { focus, links, results } = ctx;
  const kind = fixed ?? ctx.kindOf(s);
  const expanded = ctx.expanded(s);
  const listId = useId();
  const stateOf = (key: string): ItemState => (!focus ? "idle" : key === focus ? "selected" : links.linked.has(key) ? "linked" : "dim");

  if (kind === "indicators") {
    return (
      <div className="pb-body">
        {s.indicators.length === 0 ? <p className="pb-none">{L("لا مؤشرات لهذا المعيار بعد.", "No indicators yet.")}</p> : (
          <ul className="pb-items" aria-label={L(`مؤشرات ${stdLabel(s.code)}`, `Indicators of ${s.code}`)}>
            {s.indicators.map((i) => {
              const key = `I:${i.code}`;
              return <Item key={key} k={key} state={stateOf(key)} kind="indicator" code={i.code} std={s.code} name={i.nameAr} ctx={ctx}
                chip={<ResultChip i={i} r={results.get(i.code)} />} />;
            })}
          </ul>
        )}
        {tree.actions.canAddIndicator && (
          <button type="button" className="pb-btn pb-add" onClick={() => edit({ kind: "indicator", standardCode: s.code })}>
            <PlusOutlined aria-hidden /> {L("مؤشر جديد", "New indicator")}
          </button>
        )}
      </div>
    );
  }

  const inits = s.initiatives;
  const folds = inits.length > FIRST;
  const shown = inits
    .map((m, k) => ({ m, k, key: `M:${m.id}` }))
    .filter(({ k, key }) => expanded || !folds || k < FIRST || stateOf(key) === "selected" || stateOf(key) === "linked");
  // بين المبادرات الظاهرة (الست الأولى والمرتبط بالمحدد) علامة بعدد المخفي، فيعرف القارئ أن القائمة ليست متصلة.
  const rows: ReactNode[] = [];
  let prevK = -1;
  for (const { m, k, key } of shown) {
    if (k > prevK + 1) rows.push(<li key={`gap-${k}`} className="pb-gap" aria-hidden>{L(`⋯ ${k - prevK - 1} أخرى`, `⋯ ${k - prevK - 1} more`)}</li>);
    rows.push(<Item key={key} k={key} state={stateOf(key)} kind="initiative" code={m.code} std={s.code} name={m.nameAr} ctx={ctx} />);
    prevK = k;
  }
  return (
    <div className="pb-body">
      {inits.length === 0 ? <p className="pb-none">{L("لا مبادرات لهذا المعيار بعد.", "No initiatives yet.")}</p> : (
        <>
          <ul id={listId} className="pb-items" aria-label={L(`مبادرات ${stdLabel(s.code)}`, `Initiatives of ${s.code}`)}>{rows}</ul>
          {folds && (
            <button type="button" className="pb-btn pb-more" aria-expanded={expanded} aria-controls={listId} onClick={() => ctx.expand(s)}>
              {expanded ? L("عرض أقل", "Show fewer") : L(`عرض الكل (${inits.length})`, `Show all (${inits.length})`)}
            </button>
          )}
        </>
      )}
      {tree.actions.canAdd && (
        <button type="button" className="pb-btn pb-add" onClick={() => edit({ kind: "initiative", standardId: s.id })}>
          <PlusOutlined aria-hidden /> {L("مبادرة جديدة", "New initiative")}
        </button>
      )}
    </div>
  );
}

/**
 * صف مؤشر أو مبادرة: زر بحالة aria-pressed (المحدد)، وأيقونة تتبع الحالة فلا يكون اللون وحده العلامة
 * (علامة صح للمحدد، وسلسلة للمرتبط، وأيقونة النوع لغيرهما). تحت المحدد زر «تفاصيل» وعدد المرتبط في ركائز أخرى.
 * النقرة تحدد؛ وعلى المحدد: نقرة الفأرة أو Space تلغي، وEnter يفتح نافذة «مرتبط بـ» وينقل التركيز إليها، واللمسة تفتحها.
 * وتفتح أيضًا بتحويم الفأرة على المحدد وبالتركيز عليه بـTab. النقر المزدوج يفتح التفاصيل (النقرة الثانية منه لا تلغي التحديد).
 * دون 640px تعرض النافذة لوحة سفلية ثابتة (في body) بين الشريط العلوي الثابت وشريط التبويبات السفلي، يقاسان عند الفتح،
 * وبأقصى ارتفاع يتمرر داخله؛ فلا تخرج من الشاشة في أي موضع تمرير. الخيارات ولوحة المفاتيح كما في النافذة.
 */
function Item({ k, state, kind, code, std, name, chip, ctx }: {
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
  const onFocus = () => { if (selected && Date.now() - lastTabAt < 600) schedule(true, 0); };
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
function LinkedMenu({ id, code, kind, links, groupIndex, rowRef, onEnter, onLeave, onClose, onOpen, onDetails }: {
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

const PERF_SHORT: Partial<Record<IndicatorResult["performance"], [string, string]>> = {
  Achieved: ["محقق", "Achieved"],
  Near: ["قريب", "Near"],
  Behind: ["متأخر", "Behind"],
};

/** آخر قياس معتمد للمؤشر إن وجد: القيمة وحكم الأداء بنص قصير وأيقونة (لا باللون وحده). */
function ResultChip({ i, r }: { i: IndicatorNode; r: IndicatorResult | undefined }) {
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
