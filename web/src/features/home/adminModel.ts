import type { Cycle, FormRow } from "../../api/collection";

export type WorkFilter = "all" | "returned" | "review" | "entry";
export type DueSort = "nearest" | "farthest";

/** The cycle summary is scoped by the API. Prefer an open cycle, then the most recent finished one. */
export function featuredCycle(cycles: readonly Cycle[]): Cycle | undefined {
  const ordered = [...cycles].filter((c) => c.status !== "Draft")
    .sort((a, b) => b.targetYear - a.targetYear || (b.quarter ?? 0) - (a.quarter ?? 0) || b.opensOn.localeCompare(a.opensOn));
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
