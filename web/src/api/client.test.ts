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
