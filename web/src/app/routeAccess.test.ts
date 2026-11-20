import { describe, expect, it } from "vitest";
import { canOpen, ROUTE_NEEDS, type RoutePerms } from "./routeAccess";

const perms = (grants: Record<string, string>): RoutePerms => ({
  can: (feature, cap) => `${feature}:${cap}` in grants,
  scopeOf: (feature, cap) => grants[`${feature}:${cap}`],
});

describe("route access (FE-11)", () => {
  it("the scorecard opens only with university-wide results scope, as the server requires", () => {
    expect(canOpen(perms({ "dc.results:V": "A" }), ROUTE_NEEDS["/scorecard"])).toBe(true);
    expect(canOpen(perms({ "dc.results:V": "U" }), ROUTE_NEEDS["/scorecard"])).toBe(false);
  });

  it("college structures need the structure feature, not the units feature", () => {
    expect(canOpen(perms({ "org.units:V": "A" }), ROUTE_NEEDS["/structure"])).toBe(false);
    expect(canOpen(perms({ "org.structure:V": "U" }), ROUTE_NEEDS["/structure"])).toBe(true);
  });

  it("any one need of the list opens the route", () => {
    expect(canOpen(perms({ "adm.logs:V": "A" }), ["iam.users:V", "adm.logs:V"])).toBe(true);
    expect(canOpen(perms({}), ["iam.users:V", "adm.logs:V"])).toBe(false);
  });
});

// Node's fs, loaded the way tokens.test.ts loads it so the browser build never sees it.
const { readFileSync } = (await import(/* @vite-ignore */ `node:${"fs"}`)) as { readFileSync(path: URL, encoding: "utf8"): string };
const source = (file: string) => readFileSync(new URL(file, import.meta.url), "utf8");

describe("one rule per route (FE-11)", () => {
  it("every guarded route reads the rule of its own path", () => {
    const routes = [...source("./App.tsx").matchAll(/<Route path="([^"]+)" element=\{<Guard need=\{([^}]+)\}>/g)];
    expect(routes.length).toBeGreaterThan(15);
    for (const [, path, need] of routes) expect(need).toBe(`ROUTE_NEEDS["${path}"]`);
    expect(Object.keys(ROUTE_NEEDS).sort()).toEqual(routes.map((r) => r[1]).sort());
  });

  it("every menu link is shown by the rule of the page it opens", () => {
    const shell = source("./Shell.tsx");
    const links = [...shell.matchAll(/([^\n]*?) && \{\s*to: "([^"]+)"/g)];
    expect(links.length).toBeGreaterThan(15);
    for (const [, condition, to] of links) expect(condition.trim()).toBe(`canOpen(perms, ROUTE_NEEDS["${to}"])`);
  });
});
