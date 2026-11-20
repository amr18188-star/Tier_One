import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { Cycle, Results } from "./collection";
import type { OrgUnit, StrategyTree } from "./tier1";

/**
 * FE-7: every query key of the app, in one place. The queries several screens share also have one fetcher each (below), so two
 * call sites can no longer share a cache entry with different data. A key with arguments is a function; the bare key of a
 * family (`users`, `conflicts`...) is its prefix, and invalidating it refreshes every member. Keys are not written inline
 * elsewhere (api/queries.test.ts).
 */
export const queryKeys = {
  // Shared by several screens, with the fetchers below.
  cycles: ["cycles"] as const,
  units: ["units"] as const,
  strategy: ["strategy"] as const,
  /** The default results view (latest cycle, whole scope); other results views extend the "results" prefix. */
  latestResults: ["results", undefined, undefined] as const,

  // Session and home.
  me: ["me"] as const,
  home: ["home"] as const,
  scorecard: ["scorecard"] as const,
  planCalendar: ["plan", "calendar"] as const,
  planDocuments: ["plan-documents"] as const,
  notifications: ["notifications"] as const,

  // Collection: cycles, forms, results, review, progress.
  cyclePreview: (cycleId: string) => ["cycles", cycleId, "preview"] as const,
  formsMine: ["forms", "mine"] as const,
  forms: (cycleId: string | undefined) => ["forms", cycleId] as const,
  form: (id: string | undefined) => ["form", id] as const,
  results: (cycleId: string | undefined, unitId: string | undefined) => ["results", cycleId, unitId] as const,
  resultsIndicator: (cycleId: string | undefined, indicatorId: string, unitId: string | undefined) =>
    ["results", cycleId, "indicator", indicatorId, unitId] as const,
  review: (cycleId: string | undefined) => ["review", cycleId] as const,
  progress: (unitId: string | undefined) => ["progress", unitId] as const,

  // Indicators, standards, targets and conflicts.
  indicators: ["indicators"] as const,
  indicatorsAll: ["indicators", "all"] as const,
  indicatorsList: (standard: unknown, status: unknown, q: unknown, withConflicts: unknown) => ["indicators", standard, status, q, withConflicts] as const,
  indicator: (code: string | undefined) => ["indicator", code] as const,
  standards: ["standards"] as const,
  targets: (code: string) => ["targets", code] as const,
  distribution: (code: string) => ["distribution", code] as const,
  distributionYear: (code: string, year: unknown) => ["distribution", code, year] as const,
  conflicts: ["conflicts"] as const,
  conflictsAll: ["conflicts", "all"] as const,
  conflictsAgenda: ["conflicts", "agenda"] as const,
  conflictsList: (status: unknown, severity: unknown, q: unknown) => ["conflicts", status, severity, q] as const,

  // Units and college structures.
  unitsList: (showClosed: boolean) => ["units", showClosed] as const,
  unitIndicators: (unitId: string) => ["unit-indicators", unitId] as const,
  structure: ["structure"] as const,
  structureCompleteness: ["structure", "completeness"] as const,
  structureCollege: (collegeId: string | undefined, asOf: unknown) => ["structure", "college", collegeId, asOf] as const,
  structureDocs: (collegeId: string) => ["structure", "docs", collegeId] as const,

  // Accounts, roles and permissions.
  users: ["users"] as const,
  usersList: (search: string) => ["users", search] as const,
  user: (userId: string) => ["user", userId] as const,
  effective: (userId: string) => ["effective", userId] as const,
  roles: ["roles"] as const,
  role: (roleId: string | undefined) => ["role", roleId] as const,
  catalog: ["catalog"] as const,
  changes: ["changes"] as const,
  changesList: (kind: "Pending" | "all") => ["changes", kind] as const,

  // Administration.
  logs: (collection: unknown, params: unknown, page: number) => ["logs", collection, params, page] as const,
  logsStatus: ["logs-status"] as const,
  adminSettings: ["admin-settings"] as const,
  adminMail: (filter: unknown) => ["admin-mail", filter] as const,
};

/** Per call site: whether the user may load it, and how long a cached copy is fresh. */
export interface SharedQueryOptions {
  enabled?: boolean;
  staleTime?: number;
}

export function useCycles(options: SharedQueryOptions = {}) {
  return useQuery({ queryKey: queryKeys.cycles, queryFn: ({ signal }) => api.get<Cycle[]>("/api/cycles", { signal }), ...options });
}

export function useUnits(options: SharedQueryOptions = {}) {
  return useQuery({ queryKey: queryKeys.units, queryFn: ({ signal }) => api.get<OrgUnit[]>("/api/org/units", { signal }), ...options });
}

export function useStrategyTree(options: SharedQueryOptions = {}) {
  return useQuery({ queryKey: queryKeys.strategy, queryFn: ({ signal }) => api.get<StrategyTree>("/api/strategy/tree", { signal }), ...options });
}

export function useLatestResults(options: SharedQueryOptions = {}) {
  return useQuery({ queryKey: queryKeys.latestResults, queryFn: ({ signal }) => api.get<Results | null>("/api/results", { signal }), ...options });
}
