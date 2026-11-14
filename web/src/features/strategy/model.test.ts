/**
 * Links between the structure board (?view=board) and the single-item details page (?view=explore&node=…).
 * The details page has no tree and no whole-plan view, so an empty or unknown node goes back to the board.
 */
import { describe, expect, it } from "vitest";
import type { IndicatorNode, InitiativeNode, StandardNode, StrategyTree } from "../../api/tier1";
import { boardSearch, detailSearch, find, pillarNum } from "./model";

const indicator = { code: "1.01", nameAr: "مؤشر" } as IndicatorNode;
const initiative = { id: "m1", code: "M1", nameAr: "مبادرة", indicatorCodes: ["1.01"] } as InitiativeNode;
const standard = { id: "s1", code: "1", nameAr: "معيار", pillarId: "p1", indicators: [indicator], initiatives: [initiative] } as StandardNode;
const loose = { id: "s9", code: "9", nameAr: "بلا ركيزة", pillarId: null, indicators: [{ ...indicator, code: "9.01" }], initiatives: [] } as unknown as StandardNode;
const tree = {
  baselineYear: 2024, years: [2025, 2026],
  pillars: [{ id: "p1", code: "P1", nameAr: "ركيزة", nameEn: null, description: null, sortOrder: 1, version: 1, standards: [standard] }],
  unassigned: [loose],
} as unknown as StrategyTree;

const parse = (s: string) => Object.fromEntries(new URLSearchParams(s));

describe("details page links", () => {
  it("numbers pillars 01, 02 … as the board does", () => {
    expect(pillarNum(0)).toBe("01");
    expect(pillarNum(5)).toBe("06");
  });

  it("opens one item's details", () => {
    expect(parse(detailSearch("I:1.01"))).toEqual({ view: "explore", node: "I:1.01" });
  });

  it("goes back to the board on the item's pillar, with indicators and initiatives selected", () => {
    expect(parse(boardSearch(tree, "I:1.01"))).toEqual({ view: "board", pillar: "p1", focus: "I:1.01" });
    expect(parse(boardSearch(tree, "M:m1"))).toEqual({ view: "board", pillar: "p1", focus: "M:m1" });
    expect(parse(boardSearch(tree, "S:s1"))).toEqual({ view: "board", pillar: "p1" });
    expect(parse(boardSearch(tree, "P:p1"))).toEqual({ view: "board", pillar: "p1" });
  });

  it("uses the board's 'none' group for a standard without a pillar", () => {
    expect(parse(boardSearch(tree, "I:9.01"))).toEqual({ view: "board", pillar: "none", focus: "I:9.01" });
  });

  it("sends an empty, root or unknown node to the board, keeping a given pillar", () => {
    expect(find(tree, "root").kind).toBe("root");
    expect(find(tree, "I:404").kind).toBe("root");
    expect(parse(boardSearch(tree, null))).toEqual({ view: "board" });
    expect(parse(boardSearch(tree, "root", "p1"))).toEqual({ view: "board", pillar: "p1" });
    expect(parse(boardSearch(tree, "I:404"))).toEqual({ view: "board" });
  });
});
