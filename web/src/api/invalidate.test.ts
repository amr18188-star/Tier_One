import { describe, expect, it } from "vitest";
import { QueryClient } from "@tanstack/react-query";
import { FORM_DECISION, invalidate } from "./invalidate";

/** Seeds one cached query per key and reports which ones a call marked stale. */
async function staleAfter(call: (qc: QueryClient) => Promise<void>, keys: string[]) {
  const qc = new QueryClient();
  for (const key of keys) qc.setQueryData([key, "x"], 1);
  await call(qc);
  return keys.filter((key) => qc.getQueryState([key, "x"])?.isInvalidated);
}

describe("invalidate (FE-6)", () => {
  it("a form decision refreshes every screen built from forms, and nothing else", async () => {
    const stale = await staleAfter(invalidate.afterFormDecision, [...FORM_DECISION, "indicators", "users"]);
    expect(stale).toEqual([...FORM_DECISION]);
  });

  it("a transfer refreshes the review, the scorecard, results, progress and home", async () => {
    const stale = await staleAfter(invalidate.afterTransfer, ["review", "scorecard", "results", "progress", "home", "forms"]);
    expect(stale).toEqual(["review", "scorecard", "results", "progress", "home"]);
  });
});
