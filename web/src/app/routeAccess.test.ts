import { describe, expect, it } from "vitest";
import { canOpen, ROUTE_NEEDS, type RoutePerms } from "./routeAccess";

const perms = (grants: Record<string, string>): RoutePerms => ({
  can: (feature, cap) => `${feature}:${cap}` in grants,
  scopeOf: (feature, cap) => grants[`${feature}:${cap}`],
});

describe("route access (FE-11)", () => {
  it("the scorecard opens only with university-wide results scope, as the server requires", () => {
    expect(canOpen(perms({ "dc.results:V": "A" }), ROUTE_NEEDS.scorecard)).toBe(true);
    expect(canOpen(perms({ "dc.results:V": "U" }), ROUTE_NEEDS.scorecard)).toBe(false);
  });

  it("college structures need the structure feature, not the units feature", () => {
    expect(canOpen(perms({ "org.units:V": "A" }), ROUTE_NEEDS.structure)).toBe(false);
    expect(canOpen(perms({ "org.structure:V": "U" }), ROUTE_NEEDS.structure)).toBe(true);
  });

  it("any one need of the list opens the route", () => {
    expect(canOpen(perms({ "adm.logs:V": "A" }), ["iam.users:V", "adm.logs:V"])).toBe(true);
    expect(canOpen(perms({}), ["iam.users:V", "adm.logs:V"])).toBe(false);
  });
});
