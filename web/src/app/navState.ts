import { type ReactNode } from "react";

/** The side menu's types, and what it remembers per viewer in localStorage: the open group and the collapsed rail. */
export interface NavItem { to: string; icon: ReactNode; label: string; end?: boolean; badge?: number; badgeText?: string; tab?: boolean }

// icon: أيقونة المجموعة في الشريط المطوي وحده (زر المجموعة)؛ لا تتكرر أيقونة رابط فيها. رؤوس الموسع نص فقط.
export interface NavGroup { key: string; title?: string; icon?: ReactNode; items: (NavItem | false)[] }

export interface VisibleGroup { key: string; title?: string; icon?: ReactNode; items: NavItem[] }

// مجموعة واحدة مفتوحة في القائمة الجانبية (أكورديون)، ومفتاحها يحفظ في المتصفح؛ مجموعة الصفحة الحالية تتقدم عليه.
export const NAV_OPEN_KEY = "t1-nav-open";

export function readNavOpen(): string | null {
  try {
    const raw = localStorage.getItem(NAV_OPEN_KEY);
    const value: unknown = raw ? JSON.parse(raw) : null;
    // القيمة القديمة كانت قائمة مفاتيح؛ يؤخذ أولها.
    const key = Array.isArray(value) ? value[0] : value;
    return typeof key === "string" ? key : null;
  } catch {
    return null;
  }
}

export function writeNavOpen(key: string | null) {
  try {
    localStorage.setItem(NAV_OPEN_KEY, JSON.stringify(key));
  } catch {
    // المتصفح يمنع التخزين؛ تبقى الحالة لهذه الجلسة فقط.
  }
}

// الشريط الجانبي على الحاسوب يطوى إلى عمود أيقونات (72px)؛ الحالة تحفظ في المتصفح «1» مطوي و«0» موسع. درج الجوال لا يتأثر.
export const RAIL_KEY = "t1-side-rail";

// مدة تغير العرض في nav.css (220ms) وهامش صغير: طوالها تبقى النصوص سطرًا واحدًا وتتلاشى ولا تلتف.
export const RAIL_MS = 240;

export function readRail(): boolean {
  try {
    return localStorage.getItem(RAIL_KEY) === "1";
  } catch {
    return false;
  }
}

export function writeRail(on: boolean) {
  try {
    localStorage.setItem(RAIL_KEY, on ? "1" : "0");
  } catch {
    // المتصفح يمنع التخزين؛ تبقى الحالة لهذه الجلسة فقط.
  }
}

export type TipSide = "left" | "right";
