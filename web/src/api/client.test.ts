import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, createApiClient } from "./client";

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
