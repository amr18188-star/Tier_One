import { useSyncExternalStore } from "react";

export type Lang = "ar" | "en";

const KEY = "t1-lang";
let current: Lang = read();
const listeners = new Set<() => void>();

function read(): Lang {
  try {
    return localStorage.getItem(KEY) === "en" ? "en" : "ar";
  } catch {
    return "ar";
  }
}

export function getLang(): Lang {
  return current;
}

export function setLang(lang: Lang) {
  current = lang;
  try {
    localStorage.setItem(KEY, lang);
  } catch {
    // المتصفح يمنع التخزين؛ تبقى اللغة لهذه الجلسة فقط.
  }
  document.documentElement.lang = lang;
  document.documentElement.dir = lang === "ar" ? "rtl" : "ltr";
  listeners.forEach((l) => l());
}

/** يستدعى بعد كل تغيير للغة (خارج React)، ويرجع دالة الإلغاء. */
export function onLangChange(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useLang(): Lang {
  return useSyncExternalStore(
    (cb) => {
      listeners.add(cb);
      return () => listeners.delete(cb);
    },
    () => current,
  );
}

/** نص بلغتين يختار حسب لغة الواجهة الحالية. */
export function L(ar: string, en: string): string {
  return current === "en" ? en : ar;
}

/** سهم «من ← إلى» باتجاه القراءة: يشير يسارًا في العربية ويمينًا في الإنجليزية. */
export function dirArrow(): string {
  return current === "en" ? "→" : "←";
}
