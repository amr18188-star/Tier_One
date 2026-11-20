import type { StandardNode, StrategyTree } from "../../api/tier1";
import { L } from "../../shared/i18n";
import { type PillarInfo } from "../../shared/pillars";
import { allStandards, type ResultMap } from "./model";

/** The pillar board's types and pure rules: which item is where, what an item links to, and the counted texts (no React). */
/** المبادرات الظاهرة في كل معيار قبل «عرض الكل»؛ والمرتبط بالمحدد يظهر دائمًا. */
export const FIRST = 6;

export const REDUCE = "(prefers-reduced-motion: reduce)";

/** آخر ضغطة Tab: التركيز بلوحة المفاتيح على المحدد يفتح نافذته، والتركيز البرمجي (بعد الرجوع) لا يفتحها. */
export const keyboard = { lastTabAt: 0 };

export interface Group { id: string; name: string; index: number; standards: StandardNode[] }

export type Kind = "indicators" | "initiatives";

export type ItemState = "idle" | "selected" | "linked" | "dim";

export interface LinkedItem { key: string; code: string; name: string }

export interface Links {
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

export interface Found { at: number; code: string; std: string; name: string }

export function indicatorsText(n: number): string {
  return L(n === 0 ? "لا مؤشرات" : n === 1 ? "مؤشر واحد" : n === 2 ? "مؤشران" : n <= 10 ? `${n} مؤشرات` : `${n} مؤشرًا`, n === 1 ? "1 indicator" : `${n} indicators`);
}

export function initiativesText(n: number): string {
  return L(n === 0 ? "لا مبادرات" : n === 1 ? "مبادرة واحدة" : n === 2 ? "مبادرتان" : n <= 10 ? `${n} مبادرات` : `${n} مبادرة`, n === 1 ? "1 initiative" : `${n} initiatives`);
}

/** «3 مؤشرات مرتبطة» لمبادرة محددة، و«4 مبادرات مرتبطة» لمؤشر محدد. */
export function linkedText(kind: Links["kind"], n: number): string {
  if (kind === "initiative")
    return L(n === 0 ? "لا مؤشرات مرتبطة" : n === 1 ? "مؤشر واحد مرتبط" : n === 2 ? "مؤشران مرتبطان" : n <= 10 ? `${n} مؤشرات مرتبطة` : `${n} مؤشرًا مرتبطًا`,
      n === 0 ? "No linked indicators" : n === 1 ? "1 linked indicator" : `${n} linked indicators`);
  return L(n === 0 ? "لا مبادرات مرتبطة" : n === 1 ? "مبادرة واحدة مرتبطة" : n === 2 ? "مبادرتان مرتبطتان" : n <= 10 ? `${n} مبادرات مرتبطة` : `${n} مبادرة مرتبطة`,
    n === 0 ? "No linked initiatives" : n === 1 ? "1 linked initiative" : `${n} linked initiatives`);
}

export function elsewhereText(n: number): string {
  return L(`+${n} في ركائز أخرى`, `+${n} in other pillars`);
}

// ----------------------------------------------------------------- الروابط

/** موضع العنصر (I:رمز أو M:معرف) في الركيزة، أو null إن لم يكن فيها. */
export function findItem(group: Group, key: string): Found | null {
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
export function linksOf(tree: StrategyTree, group: Group | undefined, key: string | null): Links {
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

export interface BoardProps {
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

export interface ColumnCtx extends BoardProps {
  kindOf: (s: StandardNode) => Kind;
  setKind: (s: StandardNode, k: Kind) => void;
  expanded: (s: StandardNode) => boolean;
  expand: (s: StandardNode) => void;
}
