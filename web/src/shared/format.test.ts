/**
 * Every number and date the UI prints uses Western digits (0-9), also in Arabic; Arabic month names stay Arabic.
 * Arabic-Indic digits (U+0660-0669) and Eastern Arabic-Indic digits (U+06F0-06F9) must never reach the screen.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AR_LOCALE, formatDate, formatDateTime, uiLocale } from "./format";
import { setLang } from "./i18n";
import { dueText, fileSize, num, withUnit } from "./labels";

const NON_WESTERN_DIGITS = /[٠-٩۰-۹]/;
const ISO = "2026-11-13T18:05:09Z"; // 20:05 Cairo time
const instant = new Date(ISO);

function useLang(lang: "ar" | "en") {
  // i18n.setLang writes <html lang/dir>; the node test environment has no DOM, so give it a stub.
  vi.stubGlobal("document", { documentElement: {} });
  vi.stubGlobal("localStorage", { getItem: () => lang, setItem: () => undefined });
  setLang(lang);
}

beforeEach(() => useLang("ar"));
afterEach(() => vi.unstubAllGlobals());

describe("Western digits", () => {
  it("the check itself can fail: a bare ar-EG locale prints Arabic-Indic digits", () => {
    expect(new Intl.DateTimeFormat("ar-EG").format(instant)).toMatch(NON_WESTERN_DIGITS);
    expect(new Intl.NumberFormat("ar-EG").format(1234.5)).toMatch(NON_WESTERN_DIGITS);
  });

  it("uiLocale is the Latin-digit Arabic locale in Arabic and en-GB in English", () => {
    expect(uiLocale()).toBe(AR_LOCALE);
    expect(AR_LOCALE).toContain("-u-nu-latn");
    useLang("en");
    expect(uiLocale()).toBe("en-GB");
  });

  it("formatDate and formatDateTime print Latin digits in Arabic", () => {
    const date = formatDate(ISO);
    const dateTime = formatDateTime(ISO);
    expect(date).not.toMatch(NON_WESTERN_DIGITS);
    expect(dateTime).not.toMatch(NON_WESTERN_DIGITS);
    expect(date).toBe("13/11/2026");
    expect(dateTime).toMatch(/^13\/11\/2026/);
    expect(dateTime).toMatch(/05.*09/); // minutes and seconds
  });

  it("emits no bidi marks: ar-EG puts U+200F around the slashes, which scrambles Latin digits inside an .ltr element", () => {
    expect(formatDate(ISO)).not.toMatch(/[‎‏]/);
    expect(formatDateTime(ISO)).not.toMatch(/[‎‏]/);
    expect(new Intl.DateTimeFormat(AR_LOCALE, { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(instant)).toMatch(/‏/); // the raw Intl output has them
  });

  it("a long date keeps the Arabic month name with Latin digits", () => {
    const long = new Intl.DateTimeFormat(uiLocale(), { dateStyle: "long", timeZone: "Africa/Cairo" }).format(instant);
    expect(long).not.toMatch(NON_WESTERN_DIGITS);
    expect(long).toMatch(/[ء-ي]/); // Arabic letters remain (the month name)
    expect(long).toContain("2026");
  });

  it("numbers, units, due dates and file sizes", () => {
    for (const text of [num(1234567.891), num(0), withUnit(82.5, "%"), withUnit(1800, "عدد"), dueText("2030-01-01"), dueText("2020-01-01"), fileSize(2048), fileSize(5 * 1024 * 1024)]) {
      expect(text).not.toMatch(NON_WESTERN_DIGITS);
    }
    expect(num(1234567.891)).toBe("1,234,567.89");
  });

  it("English mode is unaffected", () => {
    useLang("en");
    expect(formatDate(ISO)).toBe("13/11/2026");
    expect(formatDate(ISO)).not.toMatch(NON_WESTERN_DIGITS);
  });
});

describe("source guard", () => {
  // Raw source of every non-test file; Vite resolves this glob at transform time, so the project needs no Node types.
  const files = import.meta.glob(["/src/**/*.{ts,tsx}", "!/src/**/*.test.ts"], { query: "?raw", import: "default", eager: true }) as Record<string, string>;
  const entries = Object.entries(files);

  it("scans the source tree", () => {
    expect(entries.length).toBeGreaterThan(50);
  });

  it("has no hard-coded Arabic-Indic digits", () => {
    const hits = entries.filter(([, text]) => NON_WESTERN_DIGITS.test(text)).map(([file]) => file);
    expect(hits).toEqual([]);
  });

  it("names a regional Arabic locale (ar-EG, ar-SA…) only in shared/format.ts", () => {
    const hits = entries.filter(([file, text]) => !file.endsWith("/shared/format.ts") && /["'`]ar-[A-Za-z]{2}(-u-[\w-]+)?["'`]/.test(text)).map(([file]) => file);
    expect(hits).toEqual([]);
  });

  it("does not call toLocale*String outside the shadcn components/ui folder", () => {
    const hits = entries.filter(([file, text]) => !file.includes("/components/ui/") && /\.toLocale(Date|Time)?String\(/.test(text)).map(([file]) => file);
    expect(hits).toEqual([]);
  });
});
