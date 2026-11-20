// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { api } from "../api/client";
import { SESSION_RESUMED, SessionProvider, useSession } from "./session";
import { SessionExpiredDialog } from "./SessionExpiredDialog";

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

const user = { id: "u1", email: "entry@tier1.local", name: "مدخل البيانات", fullNameAr: "مدخل البيانات", fullNameEn: null, language: "ar", mfaEnabled: false, mfaPending: false, mustChangePassword: false, permissions: { isSuperAdmin: false, roles: [], features: {} } };
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

/** A server whose session can end: while ended, everything but sign-in answers 401. */
function server() {
  const state = { ended: false, logins: [] as unknown[] };
  vi.stubGlobal("fetch", vi.fn(async (url: string, init?: RequestInit) => {
    if (url === "/api/auth/csrf") return json(200, { token: "t" });
    if (url === "/api/auth/login") {
      state.logins.push(JSON.parse(String(init?.body)));
      state.ended = false;
      return json(200, { ok: true });
    }
    if (state.ended) return json(401, { code: "unauthenticated", title: "انتهت الجلسة" });
    if (url === "/api/me") return json(200, user);
    return json(200, { ok: true });
  }));
  return state;
}

/** A page with something typed and not saved, to see that it survives. */
function Page() {
  const { me } = useSession();
  const [typed, setTyped] = useState("");
  const [result, setResult] = useState("");
  if (!me) return <p>login page</p>;
  return (
    <div>
      <p>{me.name}</p>
      <input aria-label="value" value={typed} onChange={(e) => setTyped(e.target.value)} />
      <button onClick={() => api.put("/api/forms/1/values", { value: typed }).then(() => setResult("saved"), () => setResult("failed"))}>save</button>
      <span>{result}</span>
    </div>
  );
}

function mount() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <SessionProvider>
        <Page />
        <SessionExpiredDialog />
      </SessionProvider>
    </QueryClientProvider>,
  );
}

describe("SessionExpiredDialog (UX-1, FE-3)", () => {
  it("keeps the page and its typed value under a sign-in dialog, then resumes", async () => {
    const state = server();
    const resumed = vi.fn();
    window.addEventListener(SESSION_RESUMED, resumed);
    mount();
    expect(await screen.findByText("مدخل البيانات")).toBeTruthy();
    fireEvent.change(screen.getByLabelText("value"), { target: { value: "42" } });

    state.ended = true;
    fireEvent.click(screen.getByText("save"));
    expect(await screen.findByText("انتهت جلستك")).toBeTruthy();
    expect((screen.getByLabelText("value") as HTMLInputElement).value).toBe("42");
    expect(screen.queryByText("login page")).toBeNull();

    fireEvent.change(screen.getByLabelText("كلمة المرور"), { target: { value: "secret-pass" } });
    fireEvent.click(screen.getByRole("button", { name: /تسجيل الدخول والمتابعة/ }));
    await waitFor(() => expect(screen.queryByText("انتهت جلستك")).toBeNull());
    expect(state.logins).toEqual([{ email: "entry@tier1.local", password: "secret-pass" }]);
    expect(resumed).toHaveBeenCalledTimes(1);
    expect((screen.getByLabelText("value") as HTMLInputElement).value).toBe("42");
    window.removeEventListener(SESSION_RESUMED, resumed);
  });

  it("shows the sign-in page, not the dialog, to someone who was never signed in", async () => {
    const state = server();
    state.ended = true;
    mount();
    expect(await screen.findByText("login page")).toBeTruthy();
    expect(screen.queryByText("انتهت جلستك")).toBeNull();
  });
});
