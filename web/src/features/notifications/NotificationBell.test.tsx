// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { NotificationBell, type Inbox } from "./NotificationBell";

vi.setConfig({ testTimeout: 30_000 });

beforeAll(() => {
  const computed = window.getComputedStyle.bind(window);
  window.getComputedStyle = (elt: Element) => computed(elt);
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({ matches: false, media: query, onchange: null, addListener: () => {}, removeListener: () => {}, addEventListener: () => {}, removeEventListener: () => {}, dispatchEvent: () => false }),
  });
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const json = (status: number, body: unknown) => new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

/** An inbox of two items, one unread; reading marks it read on the server. */
function server() {
  const inbox: Inbox = {
    unread: 1,
    items: [
      { id: "n1", kind: "dc.form", title: "نموذج ينتظر مراجعتك", body: "عند خطوة لجنة الكلية", link: "/forms/f1", createdAt: "2026-11-20T08:00:00Z", readAt: null },
      { id: "n2", kind: "dc.form", title: "اعتمد نموذج", body: "شكرًا لكم", link: "/forms/f2", createdAt: "2026-11-19T08:00:00Z", readAt: "2026-11-19T09:00:00Z" },
    ],
  };
  const calls: { url: string; method: string; passive: boolean }[] = [];
  vi.stubGlobal("fetch", vi.fn(async (url: string, init?: RequestInit) => {
    const headers = new Headers(init?.headers);
    calls.push({ url, method: init?.method ?? "GET", passive: headers.get("X-Passive") === "1" });
    if (url === "/api/auth/csrf") return json(200, { token: "t" });
    if (url === "/api/notifications" && (init?.method ?? "GET") === "GET") return json(200, inbox);
    if (url === "/api/notifications/n1/read") {
      inbox.items[0].readAt = "2026-11-20T09:00:00Z";
      inbox.unread = 0;
      return json(204, null);
    }
    return json(404, { code: "not_found" });
  }));
  return calls;
}

function renderBell() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={["/"]}>
        <NotificationBell />
        <Routes>
          <Route path="/" element={<p>home</p>} />
          <Route path="/forms/:id" element={<p>form page</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("NotificationBell", () => {
  it("announces the unread count, and opening an item marks it read and goes to its page", async () => {
    const calls = server();
    renderBell();
    const bell = await screen.findByRole("button", { name: /1 غير مقروء/ });
    expect(calls.find((c) => c.url === "/api/notifications")?.passive).toBe(true);

    fireEvent.click(bell);
    fireEvent.click(await screen.findByRole("button", { name: /نموذج ينتظر مراجعتك/ }));

    await screen.findByText("form page");
    expect(calls.some((c) => c.url === "/api/notifications/n1/read" && c.method === "POST")).toBe(true);
    await waitFor(() => expect(screen.getByRole("button", { name: "الإشعارات" })).toBeTruthy());
  });

  it("opening an item already read does not post again", async () => {
    const calls = server();
    renderBell();
    fireEvent.click(await screen.findByRole("button", { name: /الإشعارات/ }));
    fireEvent.click(await screen.findByRole("button", { name: /اعتمد نموذج/ }));
    await screen.findByText("form page");
    expect(calls.some((c) => c.method === "POST")).toBe(false);
  });
});
