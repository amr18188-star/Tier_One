import { getLang, L } from "../shared/i18n";

/** BE-6: one invalid field of a request, by its JSON name, with the error code and the message in the UI language. */
export interface FieldError {
  field: string;
  code: string;
  message: string;
}

/**
 * خطأ من الخادم برمزه الثابت ورسالته بلغة الطلب. الحالة 0 تعني أن الطلب لم يصل إلى الخادم أصلًا.
 * traceId (FE-12، BE-6): مرجع الطلب في سجلات الخادم، يذكره المستخدم للدعم الفني.
 * errors (BE-6): الحقول التي يعلمها النموذج، وفارغة إن كان الخطأ عن الطلب كله.
 */
export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
    public traceId?: string,
    public errors: FieldError[] = [],
  ) {
    super(message);
  }
}

/** The field errors of an answer, kept only when well formed (a proxy page or an older server sends none). */
function fieldErrors(value: unknown): FieldError[] {
  if (!Array.isArray(value)) return [];
  return value.filter(
    (e): e is FieldError => typeof e?.field === "string" && typeof e?.code === "string" && typeof e?.message === "string",
  );
}

/** FE-12: a 4xx answer will not change on retry (no access, not found, conflict, invalid input); a network or server error may. */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError && error.status >= 400 && error.status < 500) return false;
  return failureCount < 1;
}

/** BE-8 (ADR-0018): how long a write whose answer never came keeps its key for a retry (the server replays for 24 hours). */
export const UNSETTLED_KEY_MS = 12 * 60 * 60 * 1000;

/** A new Idempotency-Key: a random UUID where the browser has one (always on https and localhost). */
function newIdempotencyKey(): string {
  if (typeof globalThis.crypto?.randomUUID === "function") return globalThis.crypto.randomUUID();
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}-${Math.random().toString(36).slice(2)}`;
}

/**
 * BE-8 (ADR-0018): whether an error settles a write. The server answered with a refusal (4xx), so nothing was done and the next
 * identical write is a new one. A network error, a server error, or "the first one is still running" leaves the outcome unknown,
 * and a retry must reuse the key so the server returns the first answer instead of doing the write twice.
 */
function settles(error: unknown): boolean {
  return error instanceof ApiError && error.status >= 400 && error.status < 500 && error.code !== "request_in_progress";
}

/** FE-12: the message when the answer is not the API's JSON (a proxy's 502 or 504 page, or an empty body). */
function fallbackTitle(status: number): string {
  return status >= 500
    ? L("الخادم غير متاح الآن. أعد المحاولة بعد قليل.", "The server is not available right now. Try again shortly.")
    : L(`تعذر تنفيذ الطلب (رمز ${status}).`, `The request could not be completed (code ${status}).`);
}

/** fetch يرمي TypeError حين تنقطع الشبكة أو يتوقف الخادم؛ يتحول إلى خطأ مفهوم بلغة الواجهة. */
async function send(path: string, init: RequestInit): Promise<Response> {
  try {
    return await fetch(path, { ...init, credentials: "same-origin" });
  } catch (e) {
    if (e instanceof TypeError)
      throw new ApiError(0, "network", L("تعذر الاتصال بالخادم. تحقق من الشبكة وأعد المحاولة.", "Could not reach the server. Check your connection and try again."));
    throw e;
  }
}

async function readJson(r: Response): Promise<{ code?: string; title?: string; token?: string; traceId?: string; errors?: unknown } | null> {
  if (!r.headers.get("content-type")?.includes("json")) return null;
  // FE-12: a JSON content type with an empty or broken body is "no answer", not a SyntaxError shown to the user.
  const text = await r.text();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

export interface ApiClient {
  /** FE-12: `signal` (react-query passes one) cancels a request whose answer is no longer wanted. */
  get: <T>(path: string, options?: { signal?: AbortSignal }) => Promise<T>;
  /** A background poll (FE-2): the server reads the session without extending its idle window. */
  poll: <T>(path: string) => Promise<T>;
  post: <T>(path: string, body?: unknown) => Promise<T>;
  put: <T>(path: string, body?: unknown) => Promise<T>;
  del: <T>(path: string) => Promise<T>;
  /** رفع ملف واحد (multipart) برأس CSRF؛ الخادم يتحقق من النوع والحجم. */
  upload: <T>(path: string, file: File, fields?: Record<string, string>) => Promise<T>;
  /** رمز CSRF جديد بعد الدخول أو الخروج، لأنه مرتبط بالهوية. */
  refreshCsrf: () => Promise<void>;
}

/**
 * عميل الواجهة: رمز CSRF من csrfUrl مرتبط بالجلسة، وكوكي الجلسة HttpOnly يرسله المتصفح وحده. لا رموز في التخزين المحلي.
 * الكتابات الأولى المتزامنة تنتظر طلب رمز واحدًا، ورفض الرمز يجدده مرة واحدة ثم يعاد الطلب.
 */
export function createApiClient({ csrfUrl, onUnauthorized, onAddressRefused }: {
  csrfUrl: string;
  /** UX-1: a 401 from any call except the session check and sign-in itself means the session ended under the user. */
  onUnauthorized?: (path: string) => void;
  /** ADR-0016: a signed-in request refused because of the address it came from (403 ip_not_allowed), with the server's message. */
  onAddressRefused?: (message: string) => void;
}): ApiClient {
  let token: string | null = null;
  let pending: Promise<string> | null = null;

  function loadToken(): Promise<string> {
    const p: Promise<string> = (async () => {
      const r = await send(csrfUrl, { headers: { "Accept-Language": getLang() } });
      const data = await readJson(r);
      if (!r.ok || typeof data?.token !== "string" || !data.token)
        throw new ApiError(r.status, data?.code ?? "csrf", data?.title ?? fallbackTitle(r.status), data?.traceId ?? r.headers.get("X-Trace-Id") ?? undefined);
      return data.token;
    })().then(
      (t) => {
        // طلب أقدم سبقه تجديد لا يكتب فوق الرمز الأحدث.
        if (pending === p) {
          token = t;
          pending = null;
        }
        return t;
      },
      (e: unknown) => {
        if (pending === p) pending = null;
        throw e;
      },
    );
    return p;
  }

  function csrf(): Promise<string> {
    if (token) return Promise.resolve(token);
    return (pending ??= loadToken());
  }

  async function refreshCsrf(): Promise<void> {
    token = null;
    pending = loadToken();
    await pending;
  }

  async function finish<T>(path: string, r: Response, retry: boolean, again: () => Promise<T>): Promise<T> {
    // 204 بلا جسم: null لا undefined، لأن react-query يرفض undefined بيانات استعلام (مثل /api/results قبل أول دورة).
    if (r.status === 204) return null as T;
    const data = await readJson(r);
    if (!r.ok) {
      // رمز CSRF مرتبط بالجلسة؛ يجدد مرة واحدة ثم يعاد الطلب.
      if (r.status === 400 && data?.code === "csrf" && retry) {
        await refreshCsrf();
        return again();
      }
      if (r.status === 401 && path !== "/api/me" && !path.startsWith("/api/auth/")) onUnauthorized?.(path);
      // The sign-in form shows its own refusal; elsewhere the whole application is closed from this address.
      if (r.status === 403 && data?.code === "ip_not_allowed" && path !== "/api/auth/login") onAddressRefused?.(data.title ?? "");
      throw new ApiError(r.status, data?.code ?? (r.status >= 500 ? "server_unavailable" : "error"), data?.title ?? fallbackTitle(r.status),
        data?.traceId ?? r.headers.get("X-Trace-Id") ?? undefined, fieldErrors(data?.errors));
    }
    return data as T;
  }

  /**
   * BE-8: a write identical to one still in flight (same method, path and body: a double click, or a second click while the first
   * is slow) does not go out again; both callers get the first one's answer.
   */
  const inFlight = new Map<string, Promise<unknown>>();
  function once<T>(key: string, send: () => Promise<T>): Promise<T> {
    const running = inFlight.get(key);
    if (running) return running as Promise<T>;
    const p = send().finally(() => inFlight.delete(key));
    inFlight.set(key, p);
    return p;
  }

  /**
   * BE-8 (ADR-0018): every POST carries an Idempotency-Key. The key of a write whose outcome is unknown (the answer was lost, or
   * the server failed) is kept, so sending the same write again reuses it: the server then returns the first answer instead of
   * creating the member, cycle or decision twice. Any answer that settles the write frees the key, so a later identical write
   * (adding the same name again on purpose) is new.
   */
  const unsettled = new Map<string, { key: string; at: number }>();
  async function idempotent<T>(fingerprint: string, send: (key: string) => Promise<T>): Promise<T> {
    const now = Date.now();
    for (const [f, k] of unsettled) if (now - k.at >= UNSETTLED_KEY_MS) unsettled.delete(f);
    const key = unsettled.get(fingerprint)?.key ?? newIdempotencyKey();
    unsettled.set(fingerprint, { key, at: unsettled.get(fingerprint)?.at ?? now });
    try {
      const result = await send(key);
      unsettled.delete(fingerprint);
      return result;
    } catch (e) {
      if (settles(e)) unsettled.delete(fingerprint);
      throw e;
    }
  }

  function write<T>(method: string, path: string, body?: unknown): Promise<T> {
    const fingerprint = `${method} ${path} ${JSON.stringify(body ?? null)}`;
    return once(fingerprint, () => method === "POST"
      ? idempotent(fingerprint, (key) => request<T>(method, path, body, true, false, undefined, key))
      : request<T>(method, path, body));
  }

  async function request<T>(method: string, path: string, body?: unknown, retry = true, passive = false, signal?: AbortSignal, idempotencyKey?: string): Promise<T> {
    const headers: Record<string, string> = { "Accept-Language": getLang() };
    if (passive) headers["X-Passive"] = "1";
    if (body !== undefined) headers["Content-Type"] = "application/json";
    if (method !== "GET") headers["X-XSRF-TOKEN"] = await csrf();
    if (idempotencyKey) headers["Idempotency-Key"] = idempotencyKey;
    const r = await send(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal });
    return finish<T>(path, r, retry, () => request<T>(method, path, body, false, passive, signal, idempotencyKey));
  }

  async function upload<T>(path: string, file: File, fields: Record<string, string> | undefined, idempotencyKey: string, retry = true): Promise<T> {
    const body = new FormData();
    body.append("file", file, file.name);
    for (const [k, v] of Object.entries(fields ?? {})) body.append(k, v);
    const headers = { "Accept-Language": getLang(), "X-XSRF-TOKEN": await csrf(), "Idempotency-Key": idempotencyKey };
    const r = await send(path, { method: "POST", headers, body });
    return finish<T>(path, r, retry, () => upload<T>(path, file, fields, idempotencyKey, false));
  }

  return {
    get: <T>(path: string, options?: { signal?: AbortSignal }) => request<T>("GET", path, undefined, true, false, options?.signal),
    poll: <T>(path: string) => request<T>("GET", path, undefined, true, true),
    post: <T>(path: string, body: unknown = {}) => write<T>("POST", path, body),
    put: <T>(path: string, body: unknown = {}) => write<T>("PUT", path, body),
    del: <T>(path: string) => write<T>("DELETE", path),
    upload: <T>(path: string, file: File, fields?: Record<string, string>) => {
      const fingerprint = `UPLOAD ${path} ${file.name} ${file.size} ${file.lastModified} ${JSON.stringify(fields ?? {})}`;
      return once(fingerprint, () => idempotent(fingerprint, (key) => upload<T>(path, file, fields, key)));
    },
    refreshCsrf,
  };
}

/** UX-1: who hears that the session ended mid-task (the session provider registers itself). */
let sessionEnded: ((path: string) => void) | null = null;
export function onSessionEnded(handler: ((path: string) => void) | null) {
  sessionEnded = handler;
}

/** ADR-0016: who hears that the network address is refused mid-session (the dialog that explains it registers itself). */
let addressRefused: ((message: string) => void) | null = null;
export function onAddressRefused(handler: ((message: string) => void) | null) {
  addressRefused = handler;
}

const staff = createApiClient({ csrfUrl: "/api/auth/csrf", onUnauthorized: (path) => sessionEnded?.(path), onAddressRefused: (m) => addressRefused?.(m) });

export const api = { get: staff.get, poll: staff.poll, post: staff.post, put: staff.put, del: staff.del };
export const uploadFile = staff.upload;
export const refreshCsrf = staff.refreshCsrf;

/** يبني سلسلة استعلام من القيم غير الفارغة فقط. */
export function query(params: Record<string, string | number | null | undefined>): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== "") q.set(k, String(v));
  const s = q.toString();
  return s ? `?${s}` : "";
}
