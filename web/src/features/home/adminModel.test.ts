import { describe, expect, it } from "vitest";
import type { Cycle, FormRow } from "../../api/collection";
import { approvalState, featuredCycle, filterWork, resultPeriods, shareOf } from "./adminModel";

const row = (id: string, status: FormRow["status"], dueOn: string, unitName: string): FormRow => ({
  id, status, dueOn, unitName, cycleId: "c", cycleName: "الربع الأول", unitId: id, unitKind: "College",
  parentName: "جامعة العاصمة", step: "Entry", stepName: "إدخال", overdue: false, completed: 1, required: 3, awaitingMe: true,
});

describe("admin home work list", () => {
  const rows = [row("1", "Returned", "2027-04-10", "كلية الطب"), row("2", "InReview", "2027-04-01", "كلية العلوم"), row("3", "NotStarted", "2027-04-05", "كلية الهندسة")];
  it("filters the user's rows by action and searches their real labels", () => {
    expect(filterWork(rows, "entry", "الهندسة", "nearest").map((r) => r.id)).toEqual(["3"]);
    expect(filterWork(rows, "returned", "جامعة العاصمة", "nearest").map((r) => r.id)).toEqual(["1"]);
    expect(filterWork(rows, "review", "", "nearest").map((r) => r.id)).toEqual(["2"]);
  });
  it("sorts by actual due date without mutating the query result", () => {
    expect(filterWork(rows, "all", "", "nearest").map((r) => r.id)).toEqual(["2", "3", "1"]);
    expect(filterWork(rows, "all", "", "farthest").map((r) => r.id)).toEqual(["1", "3", "2"]);
    expect(rows[0].id).toBe("1");
  });
});

describe("featured cycle", () => {
  const cycle = (id: string, status: Cycle["status"], targetYear: number): Cycle => ({
    id, status, targetYear, name: id, periodLabel: id, opensOn: `${targetYear}-01-01`, dueOn: `${targetYear}-03-30`,
    trial: false, indicators: 0, forms: 10, formsByStatus: {}, overdue: 0, version: 1, quarter: 1,
  });
  it("uses the open cycle even when a newer draft exists", () => {
    expect(featuredCycle([cycle("draft", "Draft", 2028), cycle("open", "Open", 2027), cycle("closed", "Closed", 2026)])?.id).toBe("open");
  });
});

describe("analytics helpers", () => {
  it("computes shares within the segments' own total", () => {
    expect(shareOf(26, 92)).toBe(28);
    expect(shareOf(49, 92)).toBe(53);
    expect(shareOf(0, 0)).toBe(0);
  });
  it("classifies a standard's approval without inventing indicator states", () => {
    expect(approvalState(8, 8)).toBe("complete");
    expect(approvalState(3, 8)).toBe("partial");
    expect(approvalState(0, 8)).toBe("none");
    expect(approvalState(0, 0)).toBe("none");
  });
  it("lists non-draft periods newest first", () => {
    const c = (id: string, status: Cycle["status"], targetYear: number, quarter: number): Cycle => ({
      id, status, targetYear, quarter, name: id, periodLabel: id, opensOn: `${targetYear}-0${quarter}-01`, dueOn: `${targetYear}-12-30`,
      trial: false, indicators: 0, forms: 0, formsByStatus: {}, overdue: 0, version: 1,
    });
    expect(resultPeriods([c("a", "Closed", 2027, 1), c("d", "Draft", 2028, 1), c("b", "Open", 2027, 3)]).map((x) => x.id)).toEqual(["b", "a"]);
  });
});
