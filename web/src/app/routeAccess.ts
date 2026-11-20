import type { CapLetter } from "../api/types";

/** What the guard and the menu need from the current permissions. */
export interface RoutePerms {
  can: (feature: string, cap: CapLetter | string) => boolean;
  scopeOf: (feature: string, cap: CapLetter) => string | undefined;
}

/**
 * FE-11: one access rule per route, read by the route guard, the side menu and global search, so they cannot disagree.
 * A need is "feature:cap" (or a special), or "feature:cap:A" when the page needs university-wide scope (the server refuses
 * a narrower scope). Any one need of the list opens the route.
 */
export function canOpen(perms: RoutePerms, need: readonly string[]): boolean {
  return need.some((n) => {
    const [feature, cap, scope] = n.split(":");
    return scope ? perms.scopeOf(feature, cap as CapLetter) === scope : perms.can(feature, cap);
  });
}

export const ROUTE_NEEDS = {
  /** The university scorecard: the server answers it for university-wide results scope only. */
  scorecard: ["dc.results:V:A"],
  /** College structures, also the target of a college in global search. */
  structure: ["org.structure:V"],
} as const satisfies Record<string, readonly string[]>;
