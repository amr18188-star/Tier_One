import { getLang, L } from "../shared/i18n";

/** خطأ من الخادم برمزه الثابت ورسالته بلغة الطلب. الحالة 0 تعني أن الطلب لم يصل إلى الخادم أصلًا. */
export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
  ) {
    super(message);
  }
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

async function readJson(r: Response): Promise<{ code?: string; title?: string; token?: string } | null> {
  return r.headers.get("content-type")?.includes("json") ? await r.json() : null;
}

export interface ApiClient {
  get: <T>(path: string) => Promise<T>;
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
export function createApiClient({ csrfUrl }: { csrfUrl: string }): ApiClient {
  let token: string | null = null;
  let pending: Promise<string> | null = null;

  function loadToken(): Promise<string> {
    const p: Promise<string> = (async () => {
      const r = await send(csrfUrl, { headers: { "Accept-Language": getLang() } });
      const data = await readJson(r);
      if (!r.ok || typeof data?.token !== "string" || !data.token) throw new ApiError(r.status, data?.code ?? "csrf", data?.title ?? `HTTP ${r.status}`);
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

  async function finish<T>(r: Response, retry: boolean, again: () => Promise<T>): Promise<T> {
    if (r.status === 204) return undefined as T;
    const data = await readJson(r);
    if (!r.ok) {
      // رمز CSRF مرتبط بالجلسة؛ يجدد مرة واحدة ثم يعاد الطلب.
      if (r.status === 400 && data?.code === "csrf" && retry) {
        await refreshCsrf();
        return again();
      }
      throw new ApiError(r.status, data?.code ?? "error", data?.title ?? `HTTP ${r.status}`);
    }
    return data as T;
  }

  async function request<T>(method: string, path: string, body?: unknown, retry = true): Promise<T> {
    const headers: Record<string, string> = { "Accept-Language": getLang() };
    if (body !== undefined) headers["Content-Type"] = "application/json";
    if (method !== "GET") headers["X-XSRF-TOKEN"] = await csrf();
    const r = await send(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    return finish<T>(r, retry, () => request<T>(method, path, body, false));
  }

  async function upload<T>(path: string, file: File, fields?: Record<string, string>, retry = true): Promise<T> {
    const body = new FormData();
    body.append("file", file, file.name);
    for (const [k, v] of Object.entries(fields ?? {})) body.append(k, v);
    const r = await send(path, { method: "POST", headers: { "Accept-Language": getLang(), "X-XSRF-TOKEN": await csrf() }, body });
    return finish<T>(r, retry, () => upload<T>(path, file, fields, false));
  }

  return {
    get: <T>(path: string) => request<T>("GET", path),
    post: <T>(path: string, body: unknown = {}) => request<T>("POST", path, body),
    put: <T>(path: string, body: unknown = {}) => request<T>("PUT", path, body),
    del: <T>(path: string) => request<T>("DELETE", path),
    upload: <T>(path: string, file: File, fields?: Record<string, string>) => upload<T>(path, file, fields),
    refreshCsrf,
  };
}

const staff = createApiClient({ csrfUrl: "/api/auth/csrf" });

export const api = { get: staff.get, post: staff.post, put: staff.put, del: staff.del };
export const uploadFile = staff.upload;
export const refreshCsrf = staff.refreshCsrf;

/** يبني سلسلة استعلام من القيم غير الفارغة فقط. */
export function query(params: Record<string, string | number | null | undefined>): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== "") q.set(k, String(v));
  const s = q.toString();
  return s ? `?${s}` : "";
}
