import type { Level, Rag } from "../../api/scorecard";
import { num } from "../../shared/labels";

/** Shared pieces of the scorecard views: tier and RAG colours and texts, number formats, the ring and bar. */
export const TIER_COLORS = ["var(--ds-tier-1)", "var(--ds-tier-2)", "var(--ds-tier-3)", "var(--ds-tier-4)", "var(--ds-tier-5)"];

export const RAG_TEXT: Record<Rag, { ar: string; en: string }> = {
  Strong: { ar: "قوي / على المسار", en: "Strong" },
  Developing: { ar: "قيد التطوير", en: "Developing" },
  AtRisk: { ar: "معرض للخطر", en: "At risk" },
};

export const LEVEL_TEXT: Record<Level, { ar: string; color: string }> = {
  InLevel: { ar: "في المستوى", color: "success" },
  Below: { ar: "دون المستوى", color: "error" },
  NoData: { ar: "لا قيمة بعد", color: "default" },
};

export const pct = (v: number | null | undefined) => (v === null || v === undefined ? "—" : `${num(v)}%`);

export const fmt = (v: number | null | undefined, unit: string) => (v === null || v === undefined ? "—" : unit.trim() === "%" ? `${num(v)}%` : num(v));

export const ragOf = (v: number): Rag => (v >= 85 ? "Strong" : v >= 70 ? "Developing" : "AtRisk");

export function Ring({ value, size = 76, color, label }: { value: number; size?: number; color: string; label?: string }) {
  const r = size / 2 - 7, C = 2 * Math.PI * r;
  return (
    <svg viewBox={`0 0 ${size} ${size}`} width={size} height={size} role="img" aria-label={label ?? `${num(value)}%`} className="sc-ring">
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--ds-chart-ring-track)" strokeWidth="8" />
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke={color} strokeWidth="8" strokeLinecap="round"
        strokeDasharray={`${((C * Math.min(100, Math.max(0, value))) / 100).toFixed(1)} ${C.toFixed(1)}`} transform={`rotate(-90 ${size / 2} ${size / 2})`} />
      <text x={size / 2} y={size / 2 + 5} textAnchor="middle">{num(value)}</text>
    </svg>
  );
}

export function Bar({ value, color }: { value: number | null; color?: string }) {
  return (
    <span className="sc-bar" aria-hidden>
      <i style={{ width: `${Math.min(100, Math.max(0, value ?? 0))}%`, background: color ?? (value === null ? "transparent" : value >= 100 ? "var(--ds-status-ok)" : value >= 90 ? "var(--ds-score-high-bar)" : value >= 70 ? "var(--ds-status-near)" : "var(--ds-status-late)") }} />
    </span>
  );
}

/** لون خلية نسبة الإنجاز كما في الملف: 100% فأكثر، 90%، 70%، وما دونها. */
export function cellTone(v: number | null) {
  return v === null ? "" : v >= 100 ? "c100" : v >= 90 ? "c90" : v >= 70 ? "c70" : "clow";
}

// ----------------------------------------------------------------- حاسبة التصنيف
