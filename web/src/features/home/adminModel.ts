import type { Cycle, FormRow } from "../../api/collection";

export type WorkFilter = "all" | "returned" | "review" | "entry";
export type DueSort = "nearest" | "farthest";

/** The cycle summary is scoped by the API. Prefer an open cycle, then the most recent finished one. */
export function featuredCycle(cycles: readonly Cycle[]): Cycle | undefined {
  const ordered = resultPeriods(cycles);
  return ordered.find((c) => c.status === "Open") ?? ordered[0];
}

export function filterWork(rows: readonly FormRow[], filter: WorkFilter, search: string, sort: DueSort): FormRow[] {
  const q = search.trim().toLocaleLowerCase();
  return rows.filter((row) => {
    const matchesStatus = filter === "all" || (filter === "returned" && row.status === "Returned")
      || (filter === "review" && row.status === "InReview")
      || (filter === "entry" && (row.status === "NotStarted" || row.status === "InProgress"));
    return matchesStatus && (!q || [row.unitName, row.parentName, row.cycleName, row.stepName]
      .some((value) => value?.toLocaleLowerCase().includes(q)));
  }).sort((a, b) => (sort === "nearest" ? 1 : -1) * a.dueOn.localeCompare(b.dueOn)
    || a.unitName.localeCompare(b.unitName));
}

/** Percent of each count within the segments' own total (not another dataset's total). Rounded; 0 when empty. */
export function shareOf(count: number, total: number): number {
  return total > 0 ? Math.round((100 * count) / total) : 0;
}

/** Periods that can carry results, newest first: every cycle except drafts (same rule as the results page). */
export function resultPeriods(cycles: readonly Cycle[]): Cycle[] {
  return [...cycles].filter((c) => c.status !== "Draft")
    .sort((a, b) => b.targetYear - a.targetYear || (b.quarter ?? 0) - (a.quarter ?? 0) || b.opensOn.localeCompare(a.opensOn));
}

export type ApprovalState = "complete" | "partial" | "none";

/** How far a standard's indicator definitions are approved; a display grouping only, not an indicator status. */
export function approvalState(approved: number, indicators: number): ApprovalState {
  return indicators > 0 && approved >= indicators ? "complete" : approved > 0 ? "partial" : "none";
}
