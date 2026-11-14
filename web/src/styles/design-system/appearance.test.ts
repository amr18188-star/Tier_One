/**
 * The runtime appearance store (theme.ts): localStorage "t1.appearance" → <html data-theme / data-corners> at import,
 * validation against the theme registry and the corner list, page pins (the landing), and blocked storage.
 * Node environment: window, document and localStorage are small stand-ins, and theme.ts is re-imported per case.
 */
import { afterEach, describe, expect, it, vi } from "vitest";
import { CORNERS } from "./config";

interface FakeStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

function storage(initial: Record<string, string> = {}, blocked = false) {
  const data = new Map(Object.entries(initial));
  const guard = () => {
    if (blocked) throw new Error("SecurityError: storage is blocked");
  };
  const api: FakeStorage = {
    getItem: (k) => (guard(), data.has(k) ? data.get(k)! : null),
    setItem: (k, v) => (guard(), void data.set(k, String(v))),
    removeItem: (k) => (guard(), void data.delete(k)),
  };
  return { api, data };
}

async function boot(store: FakeStorage) {
  vi.resetModules();
  const html = { dataset: {} as Record<string, string | undefined> };
  vi.stubGlobal("window", { localStorage: store, addEventListener: () => undefined });
  vi.stubGlobal("document", { documentElement: html });
  const mod = await import("./theme");
  return { ...mod, html };
}

afterEach(() => vi.unstubAllGlobals());

describe("parseAppearance", () => {
  it("validates each field against the registry and falls back to the defaults", async () => {
    const { parseAppearance, DEFAULT_APPEARANCE } = await boot(storage().api);
    expect(DEFAULT_APPEARANCE).toEqual({ theme: "navy-gold", corners: CORNERS });
    for (const raw of [null, undefined, "", "{bad json", '"navy-gold"', "[]", "42", '{"theme":1}']) expect(parseAppearance(raw), String(raw)).toEqual(DEFAULT_APPEARANCE);
    expect(parseAppearance('{"theme":"app-soft","corners":"smooth"}')).toEqual({ theme: "app-soft", corners: "smooth" });
    expect(parseAppearance('{"theme":"no-such-theme","corners":"rounded"}')).toEqual({ theme: "navy-gold", corners: "rounded" });
    expect(parseAppearance('{"theme":"app-soft","corners":"bevel"}')).toEqual({ theme: "app-soft", corners: CORNERS });
    expect(parseAppearance('{"theme":"__proto__","corners":"constructor"}')).toEqual(DEFAULT_APPEARANCE);
  });
});

describe("appearance store", () => {
  it("applies the saved choice to <html> when the module loads (before React renders)", async () => {
    const { api } = storage({ "t1.appearance": '{"theme":"app-soft","corners":"rounded"}' });
    const { html, getAppearance, APPEARANCE_KEY } = await boot(api);
    expect(APPEARANCE_KEY).toBe("t1.appearance");
    expect(html.dataset).toEqual({ theme: "app-soft", corners: "rounded" });
    expect(getAppearance()).toEqual({ theme: "app-soft", corners: "rounded" });
  });

  it("no or a bad saved value: the defaults", async () => {
    const cases: Record<string, string>[] = [{}, { "t1.appearance": "{oops" }, { "t1.appearance": '{"theme":"x","corners":"y"}' }];
    for (const initial of cases) {
      const { html } = await boot(storage(initial).api);
      expect(html.dataset).toEqual({ theme: "navy-gold", corners: CORNERS });
    }
  });

  it("set: saves one JSON key, updates <html> and notifies; unchanged values do nothing", async () => {
    const { api, data } = storage();
    const { html, setAppearance, subscribeAppearance, getAppearance } = await boot(api);
    const seen: unknown[] = [];
    const off = subscribeAppearance(() => seen.push(getAppearance()));
    setAppearance({ corners: "smooth" });
    expect(html.dataset).toEqual({ theme: "navy-gold", corners: "smooth" });
    expect(JSON.parse(data.get("t1.appearance")!)).toEqual({ theme: "navy-gold", corners: "smooth" });
    setAppearance({ theme: "app-soft" });
    expect(html.dataset).toEqual({ theme: "app-soft", corners: "smooth" });
    setAppearance({ theme: "app-soft" });
    // @ts-expect-error an unknown value is ignored at run time too
    setAppearance({ theme: "neon" });
    expect(seen).toEqual([{ theme: "navy-gold", corners: "smooth" }, { theme: "app-soft", corners: "smooth" }]);
    off();
  });

  it("reset: removes the key and goes back to ACTIVE_THEME + CORNERS", async () => {
    const { api, data } = storage({ "t1.appearance": '{"theme":"app-soft","corners":"smooth"}' });
    const { html, resetAppearance } = await boot(api);
    resetAppearance();
    expect(data.has("t1.appearance")).toBe(false);
    expect(html.dataset).toEqual({ theme: "navy-gold", corners: CORNERS });
  });

  it("a pin (the landing) shows its own look without touching the saved choice, and releasing restores it", async () => {
    const { api, data } = storage({ "t1.appearance": '{"theme":"app-soft","corners":"smooth"}' });
    const { html, pinAppearance, setAppearance, getAppearance, BRAND_APPEARANCE } = await boot(api);
    expect(BRAND_APPEARANCE).toEqual({ theme: "navy-gold", corners: "square" });
    const release = pinAppearance(BRAND_APPEARANCE);
    expect(html.dataset).toEqual({ theme: "navy-gold", corners: "square" });
    // A change while pinned is saved but not shown until the pin goes (another tab, say).
    setAppearance({ corners: "rounded" });
    expect(html.dataset).toEqual({ theme: "navy-gold", corners: "square" });
    expect(getAppearance()).toEqual({ theme: "app-soft", corners: "rounded" });
    // Two pins (React StrictMode mounts twice): the latest wins, each release removes only its own.
    const inner = pinAppearance({ theme: "app-soft", corners: "square" });
    release();
    expect(html.dataset).toEqual({ theme: "app-soft", corners: "square" });
    inner();
    expect(html.dataset).toEqual({ theme: "app-soft", corners: "rounded" });
    expect(JSON.parse(data.get("t1.appearance")!)).toEqual({ theme: "app-soft", corners: "rounded" });
  });

  it("storage blocked: defaults at load, changes still apply for this tab, nothing throws", async () => {
    const { api } = storage({}, true);
    const { html, setAppearance, resetAppearance, getAppearance } = await boot(api);
    expect(html.dataset).toEqual({ theme: "navy-gold", corners: CORNERS });
    expect(() => setAppearance({ theme: "app-soft", corners: "smooth" })).not.toThrow();
    expect(html.dataset).toEqual({ theme: "app-soft", corners: "smooth" });
    expect(getAppearance()).toEqual({ theme: "app-soft", corners: "smooth" });
    expect(() => resetAppearance()).not.toThrow();
    expect(html.dataset).toEqual({ theme: "navy-gold", corners: CORNERS });
  });

  it("no window at all (server render): defaults, no throw", async () => {
    vi.resetModules();
    const { getAppearance, DEFAULT_APPEARANCE } = await import("./theme");
    expect(getAppearance()).toEqual(DEFAULT_APPEARANCE);
  });
});
