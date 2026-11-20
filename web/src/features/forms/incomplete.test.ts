import { describe, expect, it } from "vitest";
import type { FormItem } from "../../api/collection";
import { incompleteReason, type Draft } from "./useValueAutosave";

const item = (usesRatio: boolean) => ({ usesRatio, aggregation: "Sum" }) as unknown as FormItem;
const draft = (d: Partial<Draft>): Draft => ({ kind: "Number", value: null, numerator: null, denominator: null, reason: "", explanation: "", ...d });

describe("incompleteReason (UX-3)", () => {
  it("asks for a reason of five characters for 'not available'", () => {
    expect(incompleteReason(item(false), draft({ kind: "NotAvailable", reason: "" }))).toContain("اكتب السبب");
    expect(incompleteReason(item(false), draft({ kind: "NotAvailable", reason: "لا" }))).toContain("5 أحرف");
    expect(incompleteReason(item(false), draft({ kind: "NotApplicable", reason: "لا توجد برامج" }))).toBeNull();
  });

  it("asks for the missing part of a ratio, and nothing for an empty or whole one", () => {
    expect(incompleteReason(item(true), draft({ numerator: 4 }))).toContain("المقام");
    expect(incompleteReason(item(true), draft({ denominator: 5 }))).toContain("البسط");
    expect(incompleteReason(item(true), draft({}))).toBeNull();
    expect(incompleteReason(item(true), draft({ numerator: 4, denominator: 5 }))).toBeNull();
  });
});
