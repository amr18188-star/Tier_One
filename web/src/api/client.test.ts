import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, createApiClient, shouldRetry } from "./client";

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("createApiClient", () => {
  it("shares one CSRF request between concurrent first writes", async () => {
    const fetchMock = vi.fn(async (url: string, _init?: RequestInit) => (url === "/csrf" ? json(200, { token: "t1" }) : json(200, { ok: true })));
    vi.stubGlobal("fetch", fetchMock);
    const c = createApiClient({ csrfUrl: "/csrf" });
    await Promise.all([c.post("/a"), c.post("/b"), c.put("/c")]);
    expect(fetchMock.mock.calls.filter(([url]) => url === "/csrf")).toHaveLength(1);
    for (const [url, init] of fetchMock.mock.calls) {
      if (url !== "/csrf") expect(init?.headers).toMatchObject({ "X-XSRF-TOKEN": "t1" });
    }
  });

  it("refreshes a rejected CSRF token once and retries", async () => {
    let tokens = 0;
    let writes = 0;
    vi.stubGlobal("fetch", vi.fn(async (url: string) => {
      if (url === "/csrf") return json(200, { token: `t${++tokens}` });
      return ++writes === 1 ? json(400, { code: "csrf", title: "bad token" }) : json(200, { done: true });
    }));
    const c = createApiClient({ csrfUrl: "/csrf" });
    await expect(c.post("/a")).resolves.toEqual({ done: true });
    expect(tokens).toBe(2);
  });

  it("fails clearly when the CSRF endpoint fails", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => json(503, { code: "unavailable", title: "down" })));
    const c = createApiClient({ csrfUrl: "/csrf" });
    await expect(c.post("/a")).rejects.toMatchObject({ status: 503, code: "unavailable" });
  });

  it("maps network failures to ApiError(0, network)", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => {
      throw new TypeError("Failed to fetch");
    }));
    const c = createApiClient({ csrfUrl: "/csrf" });
    const e = await c.get("/x").catch((err: unknown) => err);
    expect(e).toBeInstanceOf(ApiError);
    expect(e).toMatchObject({ status: 0, code: "network" });
  });
});

describe("createApiClient 204", () => {
  it("resolves a 204 to null so react-query accepts it as data", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 204 })));
    const c = createApiClient({ csrfUrl: "/csrf" });
    await expect(c.get("/api/results")).resolves.toBeNull();
  });

  it("marks a poll as passive so it does not keep the session alive (FE-2)", async () => {
    const fetchMock = vi.fn(async (_url: string, _init?: RequestInit) => json(200, { ok: true }));
    vi.stubGlobal("fetch", fetchMock);
    const c = createApiClient({ csrfUrl: "/csrf" });
    await c.poll("/api/me");
    await c.get("/api/forms");
    expect(fetchMock.mock.calls[0][1]?.headers).toMatchObject({ "X-Passive": "1" });
    expect(fetchMock.mock.calls[1][1]?.headers).not.toHaveProperty("X-Passive");
  });

  it("reports a 401 as an ended session, except for the session check and sign-in (UX-1)", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => json(401, { code: "unauthenticated", title: "ended" })));
    const ended = vi.fn();
    const c = createApiClient({ csrfUrl: "/csrf", onUnauthorized: ended });
    await expect(c.get("/api/forms/1")).rejects.toMatchObject({ status: 401 });
    await expect(c.get("/api/me")).rejects.toMatchObject({ status: 401 });
    await expect(c.get("/api/auth/csrf")).rejects.toMatchObject({ status: 401 });
    expect(ended.mock.calls).toEqual([["/api/forms/1"]]);
  });
});

describe("API errors (FE-12)", () => {
  it("keeps the trace id from the body, or from the header when the body has none", async () => {
    vi.stubGlobal("fetch", vi.fn(async (url: string) => url === "/a"
      ? json(500, { status: 500, code: "server_error", title: "boom", traceId: "abc123" })
      : new Response("<html>bad gateway</html>", { status: 502, headers: { "content-type": "text/html", "X-Trace-Id": "def456" } })));
    const c = createApiClient({ csrfUrl: "/csrf" });
    await expect(c.get("/a")).rejects.toMatchObject({ status: 500, code: "server_error", traceId: "abc123" });
    await expect(c.get("/b")).rejects.toMatchObject({ status: 502, code: "server_unavailable", traceId: "def456" });
  });

  it("explains a proxy page or an empty JSON body instead of showing HTTP codes or a SyntaxError", async () => {
    vi.stubGlobal("fetch", vi.fn(async (url: string) => url === "/a"
      ? new Response("<html>gateway timeout</html>", { status: 504, headers: { "content-type": "text/html" } })
      : new Response("", { status: 400, headers: { "content-type": "application/json" } })));
    const c = createApiClient({ csrfUrl: "/csrf" });
    const proxy = await c.get("/a").catch((e: unknown) => e);
    expect(proxy).toBeInstanceOf(ApiError);
    expect((proxy as ApiError).message).not.toMatch(/HTTP/);
    const empty = await c.get("/b").catch((e: unknown) => e);
    expect(empty).toBeInstanceOf(ApiError);
    expect(empty).toMatchObject({ status: 400 });
  });

  it("does not retry an answer that will not change (4xx), and retries a network or server error once", () => {
    for (const status of [400, 401, 403, 404, 409, 422]) expect(shouldRetry(0, new ApiError(status, "x", "x"))).toBe(false);
    expect(shouldRetry(0, new ApiError(0, "network", "x"))).toBe(true);
    expect(shouldRetry(0, new ApiError(503, "x", "x"))).toBe(true);
    expect(shouldRetry(1, new ApiError(503, "x", "x"))).toBe(false);
  });
});

describe("duplicate writes (BE-8)", () => {
  it("sends an identical write once while it is in flight, and again after it finished", async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((r) => { release = r; });
    const writes: string[] = [];
    vi.stubGlobal("fetch", vi.fn(async (url: string, init?: RequestInit) => {
      if (url === "/csrf") return json(200, { token: "t" });
      writes.push(`${init?.method} ${url} ${String(init?.body)}`);
      await gate;
      return json(200, { ok: writes.length });
    }));
    const c = createApiClient({ csrfUrl: "/csrf" });
    const first = c.post("/api/members", { name: "أ" });
    const second = c.post("/api/members", { name: "أ" });
    const other = c.post("/api/members", { name: "ب" });
    await vi.waitFor(() => expect(writes).toHaveLength(2));
    release();
    const [a, b] = await Promise.all([first, second, other]);
    expect(a).toEqual(b);
    expect(writes).toHaveLength(2);
    await c.post("/api/members", { name: "أ" });
    expect(writes).toHaveLength(3);
  });
});
