import { ApiError } from "../api/client";
import { getLang, L } from "./i18n";

/**
 * لغة العرض العربية: أسماء الأشهر والأيام بالعربية، والأرقام لاتينية (0-9) دائمًا.
 * الامتداد `-u-nu-latn` يمنع Intl من استعمال الأرقام العربية الهندية (U+0660 إلى U+0669). أي `Intl.*` أو `toLocale*` عربي يمر من هنا.
 */
export const AR_LOCALE = "ar-EG-u-nu-latn";

/** لغة Intl للواجهة الحالية: العربية بأرقام لاتينية، أو en-GB للإنجليزية. */
export function uiLocale(): string {
  return getLang() === "ar" ? AR_LOCALE : "en-GB";
}

/**
 * ar-EG يضع علامة اتجاه (U+200F) حول كل شرطة مائلة؛ مع الأرقام اللاتينية داخل عنصر `.ltr` تنقلب الأجزاء فيظهر 07/11/2027 هكذا: 072027/11/.
 * نزيل العلامات فيبقى التاريخ كما يكتب: يوم/شهر/سنة بأرقام لاتينية في كل سياق.
 */
const BIDI_MARKS = /[‎‏]/g;

/** التاريخ والوقت بتوقيت القاهرة؛ التخزين UTC دائمًا. */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  return new Intl.DateTimeFormat(uiLocale(), {
    timeZone: "Africa/Cairo",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  })
    .format(new Date(iso))
    .replace(BIDI_MARKS, "");
}

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return "—";
  return new Intl.DateTimeFormat(uiLocale(), { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" })
    .format(new Date(iso))
    .replace(BIDI_MARKS, "");
}

export function errorText(e: unknown): string {
  // FE-12: a server error names its reference, which the support team finds in the logs.
  if (e instanceof ApiError && e.status >= 500 && e.traceId) return `${e.message} ${L(`(المرجع: ${e.traceId})`, `(Reference: ${e.traceId})`)}`;
  return e instanceof Error ? e.message : String(e);
}
