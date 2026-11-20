import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { Cycle, Results } from "./collection";
import type { OrgUnit, StrategyTree } from "./tier1";

/**
 * FE-7: the queries several screens share, each with one key and one fetcher, so two call sites can no longer share a cache
 * entry with different data. Invalidate by these keys (or by their first element as a prefix).
 */
export const queryKeys = {
  cycles: ["cycles"] as const,
  units: ["units"] as const,
  strategy: ["strategy"] as const,
  /** The default results view (latest cycle, whole scope); other results views extend the "results" prefix. */
  latestResults: ["results", undefined, undefined] as const,
};

/** Per call site: whether the user may load it, and how long a cached copy is fresh. */
export interface SharedQueryOptions {
  enabled?: boolean;
  staleTime?: number;
}

export function useCycles(options: SharedQueryOptions = {}) {
  return useQuery({ queryKey: queryKeys.cycles, queryFn: () => api.get<Cycle[]>("/api/cycles"), ...options });
}

export function useUnits(options: SharedQueryOptions = {}) {
  return useQuery({ queryKey: queryKeys.units, queryFn: () => api.get<OrgUnit[]>("/api/org/units"), ...options });
}

export function useStrategyTree(options: SharedQueryOptions = {}) {
  return useQuery({ queryKey: queryKeys.strategy, queryFn: () => api.get<StrategyTree>("/api/strategy/tree"), ...options });
}

export function useLatestResults(options: SharedQueryOptions = {}) {
  return useQuery({ queryKey: queryKeys.latestResults, queryFn: () => api.get<Results | null>("/api/results"), ...options });
}
