import { useMemo, type CSSProperties, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../api/client";
import type { StrategyTree } from "../api/tier1";
import { usePermissions } from "./session";

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

/** متغيرا اللون على العنصر: --pc للون الركيزة و--pt لدرجتها الفاتحة. */
export function tint(index: number): CSSProperties {
  const p = paletteAt(index);
  return { "--pc": p.c, "--pt": p.t } as CSSProperties;
}

export interface PillarInfo { index: number; id: string; code: string; name: string }

/**
 * رمز المعيار ← ركيزته (بترتيبها للون). من شجرة الخطة لمن يملك الاطلاع عليها؛ وإلا خريطة فارغة فتظهر الرموز بلا لون.
 * الاستعلام نفسه الذي تستخدمه صفحة الخطة، فلا يطلب مرتين.
 */
export function usePillarMap(): { map: Map<string, PillarInfo>; pillars: PillarInfo[] } {
  const perms = usePermissions();
  const tree = useQuery({
    queryKey: ["strategy"],
    queryFn: () => api.get<StrategyTree>("/api/strategy/tree"),
    enabled: perms.can("kpi.strategy", "V"),
    staleTime: 60_000,
  });
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
    <code className="pill-code ltr" style={tint(p ? p.index : -1)} title={title ?? (p ? `${p.code} — ${p.name}` : undefined)}>{stdLabel(code)}</code>
  );
}

/** رأس مجموعة بلون الركيزة: الرقم والاسم وما بعدهما. */
export function PillarBand({ info, children }: { info: PillarInfo | undefined; children?: ReactNode }) {
  return (
    <div className="pill-band" style={tint(info ? info.index : -1)}>
      <span className="pill-band-num ltr">{info ? String(info.index + 1).padStart(2, "0") : "—"}</span>
      <span className="pill-band-name"><small>{info ? info.code : ""}</small><b>{info ? info.name : "معايير بلا ركيزة"}</b></span>
      {children && <span className="pill-band-extra">{children}</span>}
    </div>
  );
}
