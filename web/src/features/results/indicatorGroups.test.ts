import { describe, expect, it } from "vitest";
import type { IndicatorResult, StandardResult } from "../../api/collection";
import { groupResultIndicators } from "./indicatorGroups";

const standard = (code: string, name: string) => ({ code, name } as StandardResult);
const indicator = (code: string, standardCode: string) => ({ indicatorId: code, code, standardCode } as IndicatorResult);

describe("result indicator groups", () => {
  it("keeps plan order, skips empty standards, and retains indicators missing from the standard summary", () => {
    const groups = groupResultIndicators(
      [standard("2", "Second"), standard("1", "First"), standard("3", "Empty")],
      [indicator("1.01", "1"), indicator("2.01", "2"), indicator("1.02", "1"), indicator("9.01", "9")],
    );

    expect(groups.map(({ code, name, indicators }) => ({ code, name, codes: indicators.map((item) => item.code) }))).toEqual([
      { code: "2", name: "Second", codes: ["2.01"] },
      { code: "1", name: "First", codes: ["1.01", "1.02"] },
      { code: "9", name: "9", codes: ["9.01"] },
    ]);
  });
});
