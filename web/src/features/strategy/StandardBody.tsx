import { useId, type ReactNode } from "react";
import { Dropdown, type MenuProps } from "antd";
import { CheckCircleFilled, EditOutlined, EllipsisOutlined, FileTextOutlined, LinkOutlined, PlusOutlined } from "@ant-design/icons";
import type { StandardNode } from "../../api/tier1";
import { L } from "../../shared/i18n";
import { stdLabel } from "../../shared/pillars";
import { usePlan } from "./model";
import { Item, ResultChip } from "./BoardItem";
import { ColumnCtx, FIRST, ItemState, Kind } from "./pillarBoard.model";

/** One standard in the pillar board: its type toggle, menu and list of indicators or initiatives. */
/**
 * «المؤشرات (n) | المبادرات (n)»: زران بحالة aria-pressed. على الخيار الذي فيه مرتبط بالمحدد شارة بعدده
 * (وعلامة صح إن كان المحدد نفسه فيه وهو مخفي)، فيعرف القارئ أن التبديل يظهر المرتبط مبرزًا.
 */
export function TypeToggle({ s, ctx }: { s: StandardNode; ctx: ColumnCtx }) {
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
export function StandardMenu({ s }: { s: StandardNode }) {
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

export function StandardBody({ s, ctx, kind: fixed }: { s: StandardNode; ctx: ColumnCtx; kind?: Kind }) {
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
