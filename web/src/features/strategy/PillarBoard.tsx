import { useEffect, useMemo, useRef, useState } from "react";
import { EditOutlined, LeftOutlined, PlusOutlined, RightOutlined } from "@ant-design/icons";
import { useNavigate, useSearchParams } from "react-router-dom";
import { L, useLang } from "../../shared/i18n";
import { type PillarInfo } from "../../shared/pillars";
import { EmptyNote } from "../../shared/ui";
import { tint, usePlan } from "./model";
import { PillarCarousel, type CarouselSlide } from "./PillarCarousel";
import { Board, useMedia } from "./BoardLayout";
import { Group, elsewhereText, findItem, keyboard, linkedText, linksOf } from "./pillarBoard.model";
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
    const onKey = (e: KeyboardEvent) => { if (e.key === "Tab") keyboard.lastTabAt = Date.now(); };
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
