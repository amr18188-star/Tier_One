import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { App } from "antd";
import { SaveQueue, type QueueState } from "./saveQueue";
import { api, ApiError } from "../../api/client";
import type { Form, FormItem, SaveValue, ValueKind } from "../../api/collection";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import { SESSION_RESUMED } from "../../shared/session";

// ----- المسودة -----

export interface Draft { kind: ValueKind; value: number | null; numerator: number | null; denominator: number | null; reason: string; explanation: string }

export function draftOf(item: FormItem): Draft {
  const v = item.value;
  return {
    kind: v?.kind ?? "Number", value: v?.value ?? null, numerator: v?.numerator ?? null, denominator: v?.denominator ?? null,
    reason: v?.reason ?? "", explanation: v?.explanation ?? "",
  };
}

export function sameDraft(a: Draft, b: Draft) {
  return a.kind === b.kind && a.value === b.value && a.numerator === b.numerator && a.denominator === b.denominator
    && a.reason.trim() === b.reason.trim() && a.explanation.trim() === b.explanation.trim();
}

function emptyDraft(d: Draft) {
  return d.kind === "Number" && d.value === null && d.numerator === null && d.denominator === null;
}

/** طلب الحفظ نفسه الذي كان عند مغادرة الصف: PUT /api/forms/{id}/values. */
export function saveBody(item: FormItem, d: Draft, version: number | null): SaveValue {
  const weighted = item.aggregation === "WeightedAverage";
  return {
    indicatorId: item.indicatorId, kind: d.kind,
    value: d.kind === "Number" && !item.usesRatio ? d.value : null,
    numerator: d.kind === "Number" && item.usesRatio ? d.numerator : null,
    denominator: d.kind === "Number" && (item.usesRatio || weighted) ? d.denominator : null,
    reason: d.kind === "Number" ? null : d.reason || null, explanation: d.explanation || null, version,
  };
}

/**
 * UX-3: why a draft is not sent yet, in the user's words; null when it is complete (or untouched).
 * The same rules as complete() below.
 */
export function incompleteReason(item: FormItem, d: Draft): string | null {
  if (d.kind !== "Number") {
    const n = d.reason.trim().length;
    if (n >= 5) return null;
    return n === 0 ? L("اكتب السبب لحفظه.", "Write the reason to save it.") : L("اكتب سببًا من 5 أحرف على الأقل لحفظه.", "Write a reason of at least 5 characters to save it.");
  }
  if (item.usesRatio && (d.numerator === null) !== (d.denominator === null))
    return d.numerator === null
      ? L("أدخل البسط لحساب النسبة وحفظها.", "Enter the numerator to compute and save the ratio.")
      : L("أدخل المقام لحساب النسبة وحفظها.", "Enter the denominator to compute and save the ratio.");
  return null;
}

/** يحفظ فقط ما يقبله الخادم كاملًا (القاعدة نفسها قبل الحفظ التلقائي): النسبة ببسطها ومقامها، وغير المتاح بسبب من 5 أحرف. */
function complete(item: FormItem, d: Draft, saved: Draft) {
  if (emptyDraft(d) && emptyDraft(saved)) return false;
  if (item.usesRatio && d.kind === "Number" && (d.numerator === null || d.denominator === null)) return false;
  if (d.kind !== "Number" && d.reason.trim().length < 5) return false;
  return true;
}

// ----- سجل الصفوف في الصفحة: الحفظ المعلق قبل التنقل أو الإرسال، ومنع مغادرة الصفحة بقيم لم تحفظ -----

/** unsaved: a complete change not saved yet; incomplete: a change the server would refuse (UX-3), never sent. */
export interface RowStatus { saving: boolean; unsaved: boolean; incomplete?: boolean }
interface Registry {
  register: (id: string, flush: () => Promise<void>) => () => void;
  report: (id: string, status: RowStatus) => void;
}

export const AutosaveContext = createContext<Registry | null>(null);

/**
 * سجل الحفظ في صفحة النموذج: flushAll يحفظ كل ما لم يحفظ (قبل الانتقال بين الركائز وقبل الإرسال)،
 * وsaving/unsaved لشريط الحالة؛ وما دام شيء لم يحفظ يطلب المتصفح تأكيد مغادرة الصفحة.
 */
export function useAutosaveRegistry() {
  const flushers = useRef(new Map<string, () => Promise<void>>());
  const [status, setStatus] = useState<Record<string, RowStatus>>({});
  const registry = useMemo<Registry>(() => ({
    register: (id, flush) => {
      flushers.current.set(id, flush);
      return () => {
        if (flushers.current.get(id) === flush) flushers.current.delete(id);
      };
    },
    report: (id, s) => setStatus((prev) => {
      const p = prev[id];
      const any = s.saving || s.unsaved || !!s.incomplete;
      if (p ? p.saving === s.saving && p.unsaved === s.unsaved && !!p.incomplete === !!s.incomplete : !any) return prev;
      const next = { ...prev };
      if (any) next[id] = s;
      else delete next[id];
      return next;
    }),
  }), []);
  const flushAll = useCallback(async () => {
    await Promise.all([...flushers.current.values()].map((f) => f()));
  }, []);
  const list = Object.values(status);
  const saving = list.filter((s) => s.saving).length;
  const unsaved = list.filter((s) => s.unsaved).length;
  const incomplete = list.filter((s) => s.incomplete).length;
  const busy = saving + unsaved + incomplete > 0;
  const leftRef = useRef(0);
  leftRef.current = unsaved + incomplete;
  const navigate = useNavigate();
  const { modal } = App.useApp();

  // UX-3: leaving by a link inside the app (sidebar, back link) saves what can be saved first; what cannot (an incomplete
  // reason or ratio) is not dropped silently: the user is asked whether to stay.
  useEffect(() => {
    if (!busy) return;
    const onClick = (e: MouseEvent) => {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      const a = (e.target as Element | null)?.closest?.("a[href]") as HTMLAnchorElement | null;
      if (!a || a.target === "_blank" || a.hasAttribute("download")) return;
      const url = new URL(a.href, window.location.href);
      if (url.origin !== window.location.origin || url.pathname.startsWith("/api/")) return;
      const to = url.pathname + url.search + url.hash;
      if (to === window.location.pathname + window.location.search + window.location.hash) return;
      e.preventDefault();
      e.stopPropagation();
      void (async () => {
        await flushAll();
        await new Promise((r) => setTimeout(r, 0));
        if (leftRef.current === 0) return navigate(to);
        modal.confirm({
          title: L("قيم لم تحفظ", "Unsaved values"),
          content: L(`في النموذج ${leftRef.current} قيمة لم تحفظ (سبب أقصر من 5 أحرف، أو نسبة بلا بسط أو مقام، أو حفظ لم ينجح). إن غادرت ضاعت.`,
            `${leftRef.current} values are not saved (a short reason, a ratio missing a part, or a failed save). Leaving loses them.`),
          okText: L("غادر دون حفظها", "Leave without them"), cancelText: L("ابق وأكملها", "Stay and complete them"), okButtonProps: { danger: true },
          onOk: () => navigate(to),
        });
      })();
    };
    document.addEventListener("click", onClick, true);
    return () => document.removeEventListener("click", onClick, true);
  }, [busy, flushAll, navigate, modal]);
  useEffect(() => {
    if (!busy) return;
    const warn = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = "";
    };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [busy]);
  return { registry, flushAll, saving, unsaved, incomplete, active: status };
}

// ----- الحفظ التلقائي لصف -----

export type SaveState = QueueState;
export interface SaveError { text: string; conflict: boolean }

function saveError(e: unknown): SaveError {
  const conflict = e instanceof ApiError && e.status === 409;
  return {
    conflict,
    text: e instanceof ApiError && e.status === 401
      ? L("انتهت الجلسة قبل الحفظ؛ القيمة باقية وتحفظ بعد تسجيل الدخول.", "The session ended before saving; the value stays and is saved after you sign in.")
      : conflict
      ? L("تغيرت هذه القيمة من مكان آخر (مستخدم آخر أو استيراد) بعد أن فتحت النموذج، فلم تحفظ قيمتك.",
        "This value changed elsewhere (another user or an import) after you opened the form, so yours was not saved.")
      : errorText(e),
  };
}

/**
 * الحفظ أثناء الكتابة لصف مؤشر (الطابور في saveQueue.ts):
 * - بعد 800ms من آخر تغيير، وفورًا عند مغادرة الصف أو Enter أو flush (التنقل بين الركائز والإرسال).
 * - طلب واحد في الطريق لكل صف؛ ما يكتب أثناءه يرسل بعده مرة واحدة بآخر قيمة وبالإصدار العائد من الطلب السابق.
 * - لا حفظ إن كانت القيمة كآخر قيمة محفوظة. الرد بعد إعادة الضبط يتجاهل برقم تسلسل.
 * - قيمة الخادم الأقدم من آخر إصدار معروف (تحديث متأخر) تتجاهل؛ والأحدث تحل محل المسودة فقط إن لم يكن فيها تعديل،
 *   وإلا يبقى الإصدار القديم فيرد الحفظ بتعارض بدل الكتابة فوق تغيير غيره صامتًا.
 */
export function useValueAutosave({ formId, item, editable, onSaved, delay = 800 }: {
  formId: string; item: FormItem; editable: boolean; onSaved: (item: FormItem) => void; delay?: number;
}) {
  const registry = useContext(AutosaveContext);
  const [draft, setDraft] = useState<Draft>(() => draftOf(item));
  const [state, setState] = useState<SaveState>("idle");
  const [error, setError] = useState<SaveError | null>(null);
  const id = item.indicatorId;

  // ما تقرؤه دوال الحفظ من مراجع، فلا تقرأ دالة سجلت منذ أول رسم قيمة قديمة.
  const itemRef = useRef(item);
  itemRef.current = item;
  const formIdRef = useRef(formId);
  formIdRef.current = formId;
  const onSavedRef = useRef(onSaved);
  onSavedRef.current = onSaved;
  const editableRef = useRef(editable);
  editableRef.current = editable;
  const registryRef = useRef(registry);
  registryRef.current = registry;
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const fade = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const alive = useRef(true);

  const show = (s: SaveState, e: SaveError | null = null) => {
    if (!alive.current) return;
    clearTimeout(fade.current);
    setState(s);
    setError(e);
    if (s === "saved") fade.current = setTimeout(() => alive.current && setState((x) => (x === "saved" ? "idle" : x)), 2000);
  };

  const queue = useRef<SaveQueue<Draft, FormItem> | null>(null);
  if (!queue.current) {
    const q: SaveQueue<Draft, FormItem> = new SaveQueue<Draft, FormItem>(draftOf(item), item.value?.version ?? null, {
      send: (d, version) => api.put<FormItem>(`/api/forms/${formIdRef.current}/values`, saveBody(itemRef.current, d, version)),
      versionOf: (r) => r.value?.version ?? null,
      same: sameDraft,
      ready: (d, saved) => editableRef.current && complete(itemRef.current, d, saved),
      onSaved: (r) => onSavedRef.current(r),
      onState: (s, e) => show(s, s === "error" ? saveError(e) : null),
      onBusy: () => registryRef.current?.report(id, { saving: q.saving, unsaved: q.unsaved, incomplete: q.dirty && !q.unsaved && !q.saving }),
    });
    queue.current = q;
  }
  const q = queue.current;

  const flush = useCallback(async () => {
    clearTimeout(timer.current);
    timer.current = undefined;
    await q.flush();
  }, [q]);

  /** تغيير من المستخدم: يظهر فورًا ويحفظ بعد توقف الكتابة. */
  const change = (d: Draft) => {
    setDraft(d);
    q.set(d);
    clearTimeout(timer.current);
    if (!editable) return;
    // حقل رقمي فُرّغ لا يرسل أثناء الكتابة (يرد الخادم «أدخل القيمة»)؛ يرسل كما كان عند مغادرة الصف أو Enter.
    timer.current = setTimeout(() => {
      timer.current = undefined;
      if (!emptyDraft(q.latest)) void q.run();
    }, delay);
  };

  /** بعد تعارض الإصدار: يقرأ القيمة الأحدث، ثم يحفظ قيمة المستخدم فوقها (keep) أو يعود إليها (discard). */
  const resolve = async (keep: boolean) => {
    const form = await api.get<Form>(`/api/forms/${formId}`);
    const fresh = form.sections.flatMap((s) => s.items).find((i) => i.indicatorId === id);
    if (!fresh) return;
    q.version = fresh.value?.version ?? null;
    q.saved = draftOf(fresh);
    onSavedRef.current(fresh);
    if (keep) return void q.run();
    q.invalidate();
    q.set(q.saved);
    setDraft(q.saved);
    show("idle");
  };
  const fail = (e: unknown) => show("error", { ...saveError(e), conflict: true });
  const retry = () => (error?.conflict ? void resolve(true).catch(fail) : void q.run());
  const discard = () => void resolve(false).catch(fail);

  // قيمة جديدة من الخادم (حفظ هذا الصف، أو استيراد، أو تحديث الصفحة).
  const serverVersion = item.value?.version ?? null;
  useEffect(() => {
    const server = draftOf(item);
    const before = q.latest;
    if (q.adopt(server, serverVersion) && !sameDraft(before, server)) setDraft(server);
  }, [serverVersion, item.value?.editedAt]);

  // UX-1: back from an ended session, a row whose save failed sends it again.
  useEffect(() => {
    const resumed = () => {
      if (q.unsaved && !q.saving) void q.run();
    };
    window.addEventListener(SESSION_RESUMED, resumed);
    return () => window.removeEventListener(SESSION_RESUMED, resumed);
  }, [q]);

  useEffect(() => {
    alive.current = true;
    const unregister = registry?.register(id, flush);
    return () => {
      alive.current = false;
      clearTimeout(fade.current);
      clearTimeout(timer.current);
      timer.current = undefined;
      // الصف يختفي (مرشح أو ركيزة أخرى) وفيه ما لم يحفظ: يكمل الحفظ في الخلفية، ويبقى مسجلًا حتى ينتهي
      // فينتظره flushAll قبل الإرسال.
      if (q.saving || q.unsaved) void q.flush().finally(() => unregister?.());
      else {
        unregister?.();
        registry?.report(id, { saving: false, unsaved: false, incomplete: false });
      }
    };
  }, []);

  return { draft, change, flush, state, error, retry, discard };
}
