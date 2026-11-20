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
export function createApiClient({ csrfUrl, onUnauthorized }: {
  csrfUrl: string;
  /** UX-1: a 401 from any call except the session check and sign-in itself means the session ended under the user. */
  onUnauthorized?: (path: string) => void;
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

  function write<T>(method: string, path: string, body?: unknown): Promise<T> {
    return once(`${method} ${path} ${JSON.stringify(body ?? null)}`, () => request<T>(method, path, body));
  }

  async function request<T>(method: string, path: string, body?: unknown, retry = true, passive = false, signal?: AbortSignal): Promise<T> {
    const headers: Record<string, string> = { "Accept-Language": getLang() };
    if (passive) headers["X-Passive"] = "1";
    if (body !== undefined) headers["Content-Type"] = "application/json";
    if (method !== "GET") headers["X-XSRF-TOKEN"] = await csrf();
    const r = await send(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal });
    return finish<T>(path, r, retry, () => request<T>(method, path, body, false, passive, signal));
  }

  async function upload<T>(path: string, file: File, fields?: Record<string, string>, retry = true): Promise<T> {
    const body = new FormData();
    body.append("file", file, file.name);
    for (const [k, v] of Object.entries(fields ?? {})) body.append(k, v);
    const r = await send(path, { method: "POST", headers: { "Accept-Language": getLang(), "X-XSRF-TOKEN": await csrf() }, body });
    return finish<T>(path, r, retry, () => upload<T>(path, file, fields, false));
  }

  return {
    get: <T>(path: string, options?: { signal?: AbortSignal }) => request<T>("GET", path, undefined, true, false, options?.signal),
    poll: <T>(path: string) => request<T>("GET", path, undefined, true, true),
    post: <T>(path: string, body: unknown = {}) => write<T>("POST", path, body),
    put: <T>(path: string, body: unknown = {}) => write<T>("PUT", path, body),
    del: <T>(path: string) => write<T>("DELETE", path),
    upload: <T>(path: string, file: File, fields?: Record<string, string>) =>
      once(`UPLOAD ${path} ${file.name} ${file.size} ${file.lastModified} ${JSON.stringify(fields ?? {})}`, () => upload<T>(path, file, fields)),
    refreshCsrf,
  };
}

/** UX-1: who hears that the session ended mid-task (the session provider registers itself). */
let sessionEnded: ((path: string) => void) | null = null;
export function onSessionEnded(handler: ((path: string) => void) | null) {
  sessionEnded = handler;
}

const staff = createApiClient({ csrfUrl: "/api/auth/csrf", onUnauthorized: (path) => sessionEnded?.(path) });

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
