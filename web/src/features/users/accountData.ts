import type { Rule } from "antd/es/form";
import { L } from "../../shared/i18n";

/**
 * ADR-0021: the data every account carries (both names, the national ID, the mobile number), checked here as the server checks it
 * so the form names the mistake before sending. The server refuses a wrong value with its field named anyway.
 */

/** The digits of what was typed, in Latin form: Arabic-Indic and Persian digits are read too, spaces and dashes dropped. */
export function digitsOf(value: string): string {
  return value
    .replace(/[\u{660}-\u{669}]/gu, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(/[\u{6F0}-\u{6F9}]/gu, (d) => String(d.charCodeAt(0) - 0x06f0))
    .replace(/\D/g, "");
}

/** Governorate codes of the national ID; 88 is for those born abroad. */
const GOVERNORATES = new Set(["01", "02", "03", "04", "11", "12", "13", "14", "15", "16", "17", "18", "19",
  "21", "22", "23", "24", "25", "26", "27", "28", "29", "31", "32", "33", "34", "35", "88"]);

/** An Egyptian national ID: 14 digits; the century (2 or 3), a real birth date not after today, and a governorate code. */
export function isNationalId(value: string, today: Date = new Date()): boolean {
  const id = digitsOf(value);
  if (id.length !== 14) return false;
  const century = id[0] === "2" ? 1900 : id[0] === "3" ? 2000 : 0;
  if (!century) return false;
  const year = century + Number(id.slice(1, 3));
  const month = Number(id.slice(3, 5));
  const day = Number(id.slice(5, 7));
  const born = new Date(year, month - 1, day);
  if (born.getFullYear() !== year || born.getMonth() !== month - 1 || born.getDate() !== day) return false;
  return born.getTime() <= today.getTime() && GOVERNORATES.has(id.slice(7, 9));
}

/** An Egyptian mobile number as it is kept (01XXXXXXXXX), however it was typed (+20, 0020, spaces); or null when it is not one. */
export function mobileOf(value: string): string | null {
  let digits = digitsOf(value);
  if (digits.startsWith("0020")) digits = "0" + digits.slice(4);
  else if (digits.length === 12 && digits.startsWith("20")) digits = "0" + digits.slice(2);
  return /^01[0125]\d{8}$/.test(digits) ? digits : null;
}

/** The English name is in Latin letters, with no Arabic ones. */
export function isEnglishName(value: string): boolean {
  return /[A-Za-z]/.test(value) && !/[؀-ۿ]/.test(value);
}

/** What an account lacks, in words, from the field names the server lists. */
export function missingLabels(missing: readonly string[]): string[] {
  const names: Record<string, string> = {
    fullNameEn: L("الاسم بالإنجليزية", "the English name"),
    nationalId: L("الرقم القومي", "the national ID"),
    phone: L("رقم المحمول", "the mobile number"),
  };
  return missing.map((m) => names[m] ?? m);
}

/** Rules are built when the form renders, so their messages follow the interface language. */
export const englishNameRules = (): Rule[] => [
  { required: true, whitespace: true, message: L("اكتب الاسم بالإنجليزية.", "Enter the English name.") },
  { validator: (_, v: string | undefined) => (!v?.trim() || isEnglishName(v) ? Promise.resolve() : Promise.reject(new Error(L("اكتب الاسم بالإنجليزية بحروف لاتينية.", "Write the English name in Latin letters.")))) },
];

/** @param onFile the account already has a national ID: leaving the field empty keeps it. */
export function nationalIdRules(onFile: boolean): Rule[] {
  return [
    ...(onFile ? [] : [{ required: true, whitespace: true, message: L("اكتب الرقم القومي.", "Enter the national ID.") } satisfies Rule]),
    { validator: (_, v: string | undefined) => (!v?.trim() || isNationalId(v) ? Promise.resolve() : Promise.reject(new Error(L("الرقم القومي غير صحيح. اكتبه 14 رقمًا كما في البطاقة.", "The national ID is not valid. Enter its 14 digits as on the card.")))) },
  ];
}

export const phoneRules = (): Rule[] => [
  { required: true, whitespace: true, message: L("اكتب رقم التليفون المحمول.", "Enter the mobile number.") },
  { validator: (_, v: string | undefined) => (!v?.trim() || mobileOf(v) ? Promise.resolve() : Promise.reject(new Error(L("رقم المحمول غير صحيح. اكتبه 11 رقمًا يبدأ بـ 010 أو 011 أو 012 أو 015.", "The mobile number is not valid. Enter its 11 digits starting with 010, 011, 012 or 015.")))) },
];
