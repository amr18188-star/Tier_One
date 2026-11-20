// @vitest-environment jsdom
/** The pillar board's pure rules, now testable on their own (split out of PillarBoard.tsx). */
import { describe, expect, it } from "vitest";
import type { IndicatorNode, InitiativeNode, StandardNode, StrategyTree } from "../../api/tier1";
import { setLang } from "../../shared/i18n";
import { findItem, indicatorsText, linkedText, linksOf, type Group } from "./pillarBoard.model";

const ind = (code: string) => ({ code, nameAr: `مؤشر ${code}` }) as IndicatorNode;
const ini = (id: string, codes: string[]) => ({ id, code: id.toUpperCase(), nameAr: `مبادرة ${id}`, indicatorCodes: codes }) as InitiativeNode;
const std = (id: string, indicators: IndicatorNode[], initiatives: InitiativeNode[]) =>
  ({ id, code: id, nameAr: `معيار ${id}`, indicators, initiatives }) as unknown as StandardNode;

const s1 = std("s1", [ind("1.01"), ind("1.02")], [ini("m1", ["1.01", "2.01"])]);
const s2 = std("s2", [ind("1.03")], [ini("m2", ["1.01"])]);
const other = std("s3", [ind("2.01")], [ini("m3", ["1.01"])]);
const tree = {
  pillars: [{ id: "p1", standards: [s1, s2] }, { id: "p2", standards: [other] }],
  unassigned: [],
} as unknown as StrategyTree;
const group: Group = { id: "p1", name: "ركيزة", index: 0, standards: [s1, s2] };

describe("pillar board model", () => {
  it("finds an indicator or an initiative and its column, or nothing outside the pillar", () => {
    expect(findItem(group, "I:1.03")).toMatchObject({ at: 1, code: "1.03", std: "s2" });
    expect(findItem(group, "M:m1")).toMatchObject({ at: 0, code: "M1" });
    expect(findItem(group, "I:2.01")).toBeNull();
  });

  it("an indicator links to the initiatives that name it, counted per column and elsewhere", () => {
    const links = linksOf(tree, group, "I:1.01");
    expect(links.kind).toBe("indicator");
    expect([...links.linked].sort()).toEqual(["M:m1", "M:m2"]);
    expect(links.perStandard.get("s1")).toBe(1);
    expect(links.perStandard.get("s2")).toBe(1);
    expect(links.elsewhere).toBe(1);
    expect(links.items.map((i) => i.key)).toEqual(["M:m1", "M:m2", "M:m3"]);
  });

  it("an initiative links to the indicators it names, wherever they are", () => {
    const links = linksOf(tree, group, "M:m1");
    expect(links.kind).toBe("initiative");
    expect([...links.linked]).toEqual(["I:1.01"]);
    expect(links.elsewhere).toBe(1);
  });

  it("nothing is linked without a selection", () => {
    expect(linksOf(tree, group, null).kind).toBeNull();
  });

  it("counts read naturally in Arabic", () => {
    setLang("ar");
    expect(indicatorsText(2)).toBe("مؤشران");
    expect(indicatorsText(11)).toBe("11 مؤشرًا");
    expect(linkedText("indicator", 1)).toBe("مبادرة واحدة مرتبطة");
    expect(linkedText("initiative", 3)).toBe("3 مؤشرات مرتبطة");
  });
});
