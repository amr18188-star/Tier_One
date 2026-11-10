import { CAPS, type CapLetter, type Catalog, type Feature, type GrantView, type ScopeCode } from "../../api/types";

export type Grants = Record<string, GrantView>;

const EMPTY: GrantView = { caps: "", scope: "A", specials: [] };

/** الحروف بترتيبها الثابت V P E A D. */
export function normalizeCaps(caps: string): string {
  return CAPS.filter((c) => caps.includes(c)).join("");
}

export function grantOf(grants: Grants, feature: string): GrantView {
  return grants[feature] ?? EMPTY;
}

/** يحذف المنح الفارغة ويرتب الحروف والإجراءات، فيقارن المقترح بالحالي بلا فروق شكلية. */
export function clean(grants: Grants): Grants {
  const out: Grants = {};
  for (const [k, g] of Object.entries(grants)) {
    const caps = normalizeCaps(g.caps);
    const specials = [...new Set(g.specials)].sort();
    if (!caps && specials.length === 0) continue;
    out[k] = { caps, scope: g.scope, specials };
  }
  return out;
}

export function same(a: GrantView | undefined, b: GrantView | undefined): boolean {
  const x = a ?? EMPTY;
  const y = b ?? EMPTY;
  const ex = !normalizeCaps(x.caps) && x.specials.length === 0;
  const ey = !normalizeCaps(y.caps) && y.specials.length === 0;
  if (ex && ey) return true;
  return normalizeCaps(x.caps) === normalizeCaps(y.caps) && x.scope === y.scope && [...x.specials].sort().join(",") === [...y.specials].sort().join(",");
}

export function toggleCap(g: GrantView, cap: CapLetter, on: boolean): GrantView {
  const caps = on ? normalizeCaps(g.caps + cap) : g.caps.replace(cap, "");
  return { ...g, caps };
}

export function toggleSpecial(g: GrantView, special: string, on: boolean): GrantView {
  const specials = on ? [...new Set([...g.specials, special])] : g.specials.filter((s) => s !== special);
  return { ...g, specials };
}

export function setScope(g: GrantView, scope: ScopeCode): GrantView {
  return { ...g, scope };
}

/** كل إمكانيات الخاصية، أو لا شيء إن كانت كلها ممنوحة. */
export function toggleRow(g: GrantView, f: Feature): GrantView {
  const all = f.caps.map((c) => c.cap).join("");
  return normalizeCaps(g.caps) === normalizeCaps(all) ? { ...g, caps: "" } : { ...g, caps: normalizeCaps(all) };
}

/** الخصائص التي تغيرت بين الحالي والمقترح. */
export function diffKeys(original: Grants, draft: Grants): string[] {
  const keys = new Set([...Object.keys(original), ...Object.keys(draft)]);
  return [...keys].filter((k) => !same(original[k], draft[k])).sort();
}

export function scopeName(catalog: Catalog | undefined, code: ScopeCode): string {
  return catalog?.scopes.find((s) => s.key === code)?.name ?? code;
}

export function describe(catalog: Catalog | undefined, feature: Feature | undefined, g: GrantView | null | undefined): string {
  if (!g || (!g.caps && g.specials.length === 0)) return "—";
  const specials = g.specials.map((s) => feature?.specials.find((x) => x.key === s)?.name ?? s);
  // الحروف اللاتينية معزولة اتجاهيًا (FSI…PDI) فلا تنقلب مع النص العربي.
  return [g.caps ? `⁨${g.caps}⁩ · ${scopeName(catalog, g.scope)}` : null, ...specials.map((s) => "+ " + s)].filter(Boolean).join("  ");
}
