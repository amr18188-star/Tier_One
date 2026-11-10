import { getLang } from "./i18n";

/** التاريخ والوقت بتوقيت القاهرة؛ التخزين UTC دائمًا. */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  return new Intl.DateTimeFormat(getLang() === "ar" ? "ar-EG" : "en-GB", {
    timeZone: "Africa/Cairo",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  }).format(new Date(iso));
}

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return "—";
  return new Intl.DateTimeFormat(getLang() === "ar" ? "ar-EG" : "en-GB", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(iso));
}

export function errorText(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}
