import type { QueryClient } from "@tanstack/react-query";

/**
 * FE-6: what each business event changes on screen, in one place. A mutation calls the event it caused instead of
 * listing query keys by hand, so a page added later (or a query given a staleTime) is refreshed with the rest.
 */
const refresh = (qc: QueryClient, prefixes: readonly string[]) =>
  Promise.all(prefixes.map((prefix) => qc.invalidateQueries({ queryKey: [prefix] }))).then(() => undefined);

/** A form moved or its deadline changed: the lists and counters, the results built from approved values, and the bell. */
export const FORM_DECISION = ["forms", "cycles", "home", "results", "progress", "review", "scorecard", "notifications"] as const;

/** University values were transferred from the reviewed cycle: the review rows, the scorecard and what reads from them. */
export const TRANSFER = ["review", "scorecard", "results", "progress", "home"] as const;

export const invalidate = {
  afterFormDecision: (qc: QueryClient) => refresh(qc, FORM_DECISION),
  afterTransfer: (qc: QueryClient) => refresh(qc, TRANSFER),
};
