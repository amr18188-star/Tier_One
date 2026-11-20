import { useMemo, type CSSProperties, type ReactNode } from "react";
import { usePermissions } from "./session";
import { useStrategyTree } from "../api/queries";

/**
 * لون كل ركيزة في كل الشاشات (الخطة، والقاموس، والنتائج، والنماذج): المعيار يأخذ لون ركيزته،
 * فيعرف القارئ من أي محور في الخطة جاء المؤشر دون أن يقرأ.
 * الألوان كلها بتباين 4.5 على الأقل مع النص الأبيض، ومعها درجة فاتحة للخلفيات؛ المعيار بلا ركيزة رمادي.
 */
const PALETTE = [
  { c: "#0A7F86", t: "#E6F4F4" },
  { c: "#2B5797", t: "#EAF0F9" },
  { c: "#6A4C9C", t: "#F1EDF8" },
  { c: "#9A6216", t: "#FBF2E4" },
  { c: "#2F7D4F", t: "#E8F4EC" },
  { c: "#A63D56", t: "#FAEBEF" },
];
const NEUTRAL = { c: "#5B6576", t: "#EEF1F5" };

export function paletteAt(index: number) {
  return index < 0 ? NEUTRAL : PALETTE[index % PALETTE.length];
}

/**
 * متغيرا اللون على العنصر: --pc للون الركيزة و--pt لدرجتها الفاتحة. اللونان بيانات ثابتة، ولقاؤهما بلوحة الألوان من رموزها
 * (--ds-pillar-*، styles/design-system/themes): في اللوحات الفاتحة 100% فيبقيان كما هما بالضبط، وفي الداكنة يفتح اللون نحو
 * الأبيض قليلًا وتصبح الدرجة لون الركيزة ممزوجًا بلون البطاقة الداكن، فلا تظهر رقع فاتحة على صفحة داكنة.
 * و--pf لون الركيزة خلفيةً تحت نص أبيض: هو --pc نفسه في اللوحات الفاتحة، ويغمق في الداكنة نحو --ds-shade بنسبة
 * --ds-pillar-fill-mix، فيبقى النص الأبيض عليه 4.5:1 على الأقل.
 */
export function tint(index: number): CSSProperties {
  const p = paletteAt(index);
  return {
    "--pc": `color-mix(in srgb, ${p.c} var(--ds-pillar-keep, 100%), var(--ds-pillar-lift, transparent))`,
    "--pt": `color-mix(in srgb, ${p.t} var(--ds-pillar-tint-keep, 100%), color-mix(in srgb, var(--pc) var(--ds-pillar-tint-mix, 0%), var(--ds-surface, transparent)))`,
    "--pf": `color-mix(in srgb, var(--pc) calc(100% - var(--ds-pillar-fill-mix, 0%)), var(--ds-shade, transparent))`,
  } as CSSProperties;
}

export interface PillarInfo { index: number; id: string; code: string; name: string }

/**
 * رمز المعيار ← ركيزته (بترتيبها للون). من شجرة الخطة لمن يملك الاطلاع عليها؛ وإلا خريطة فارغة فتظهر الرموز بلا لون.
 * الاستعلام نفسه الذي تستخدمه صفحة الخطة، فلا يطلب مرتين.
 */
export function usePillarMap(): { map: Map<string, PillarInfo>; pillars: PillarInfo[] } {
  const perms = usePermissions();
  const tree = useStrategyTree({ enabled: perms.can("kpi.strategy", "V"), staleTime: 60_000 });
  return useMemo(() => {
    const map = new Map<string, PillarInfo>();
    const pillars: PillarInfo[] = [];
    tree.data?.pillars.forEach((p, index) => {
      const info = { index, id: p.id, code: p.code, name: p.nameAr };
      pillars.push(info);
      p.standards.forEach((s) => map.set(s.code, info));
    });
    return { map, pillars };
  }, [tree.data]);
}

/** رمز المعيار للعرض: المعايير مرقمة 1–12 في الإطار فتعرض «م1»…«م12»؛ وغيرها كما هو. */
export function stdLabel(code: string): string {
  return /^\d+$/.test(code) ? `م${code}` : code;
}

/** رمز معيار أو مؤشر في رقعة بلون ركيزته؛ standard رمز المعيار الذي يحدد اللون (وإلا ما قبل النقطة في كود المؤشر 1.01). */
export function StdCode({ code, standard, map, title }: { code: string; standard?: string; map: Map<string, PillarInfo>; title?: string }) {
  const p = map.get(standard ?? code.split(".")[0]);
  return (
    <code className="pill-code ltr" style={tint(p ? p.index : -1)} title={title ?? (p ? `${String(p.index + 1).padStart(2, "0")} — ${p.name}` : undefined)}>{stdLabel(code)}</code>
  );
}

/** رأس مجموعة بلون الركيزة: الرقم والاسم وما بعدهما. */
export function PillarBand({ info, children }: { info: PillarInfo | undefined; children?: ReactNode }) {
  return (
    <div className="pill-band" style={tint(info ? info.index : -1)}>
      <span className="pill-band-num ltr">{info ? String(info.index + 1).padStart(2, "0") : "—"}</span>
      <span className="pill-band-name"><b>{info ? info.name : "معايير بلا ركيزة"}</b></span>
      {children && <span className="pill-band-extra">{children}</span>}
    </div>
  );
}
