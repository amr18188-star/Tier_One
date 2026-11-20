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

/**
 * FE-11: the access rule of every guarded route, by its path. The route guard (App.tsx), the side menu and global search (Shell.tsx)
 * read the same entry, so a link is shown exactly when its page opens.
 */
export const ROUTE_NEEDS = {
  "/strategy": ["kpi.strategy:V"],
  "/strategy/document": ["kpi.strategy:V"],
  "/indicators": ["kpi.dictionary:V"],
  "/indicators/:code": ["kpi.dictionary:V"],
  // The university scorecard: the server answers it for university-wide results scope only.
  "/scorecard": ["dc.results:V:A"],
  // College structures, also the target of a college in global search.
  "/structure": ["org.structure:V"],
  "/structure/:id": ["org.structure:V"],
  "/review": ["dc.university:V"],
  "/progress": ["dc.results:V"],
  "/cycles": ["dc.cycles:E"],
  "/forms": ["dc.forms:V"],
  "/forms/:id": ["dc.forms:V"],
  "/results": ["dc.results:V"],
  "/units": ["org.units:V"],
  "/matrix": ["adm.roles:V"],
  "/matrix/:roleId": ["adm.roles:V"],
  "/changes": ["adm.roles:V"],
  "/users": ["adm.users:V", "adm.grants:V"],
  "/logs": ["log.audit:V", "log.security:V", "log.access:V", "log.tech:V"],
  "/settings": ["adm.settings:V", "adm.security:V"],
  "/mail": ["adm.mail:V"],
} as const satisfies Record<string, readonly string[]>;
