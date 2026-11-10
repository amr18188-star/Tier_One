import { describe, expect, it } from "vitest";
import type { Feature } from "../../api/types";
import { clean, diffKeys, normalizeCaps, same, toggleCap, toggleRow, toggleSpecial } from "./grants";

const feature: Feature = {
  key: "req.files",
  module: "req",
  name: "المرفقات",
  caps: (["V", "P", "E", "A", "D"] as const).map((cap) => ({ cap, meaning: "" })),
  specials: [],
};

describe("grants", () => {
  it("يرتب الحروف بترتيبها الثابت", () => {
    expect(normalizeCaps("DAV")).toBe("VAD");
  });

  it("يضيف الإمكانية ويزيلها", () => {
    const g = toggleCap({ caps: "VP", scope: "U", specials: [] }, "E", true);
    expect(g.caps).toBe("VPE");
    expect(toggleCap(g, "P", false).caps).toBe("VE");
  });

  it("يبدل الصف كله", () => {
    expect(toggleRow({ caps: "V", scope: "A", specials: [] }, feature).caps).toBe("VPEAD");
    expect(toggleRow({ caps: "VPEAD", scope: "A", specials: [] }, feature).caps).toBe("");
  });

  it("المنح الفارغ يساوي غياب المنح", () => {
    expect(same({ caps: "", scope: "U", specials: [] }, undefined)).toBe(true);
    expect(same({ caps: "V", scope: "U", specials: [] }, { caps: "V", scope: "A", specials: [] })).toBe(false);
  });

  it("يحذف المنح الفارغة ويحسب الفروق", () => {
    const original = { "req.files": { caps: "VPEA", scope: "U" as const, specials: [] }, "req.inq": { caps: "V", scope: "U" as const, specials: [] } };
    const draft = { ...original, "req.files": { caps: "AEPV", scope: "U" as const, specials: [] }, "req.inq": { caps: "", scope: "U" as const, specials: [] } };
    expect(Object.keys(clean(draft))).toEqual(["req.files"]);
    expect(diffKeys(original, draft)).toEqual(["req.inq"]);
  });

  it("الإجراءات الخاصة بلا تكرار", () => {
    const g = toggleSpecial(toggleSpecial({ caps: "", scope: "A", specials: [] }, "export", true), "export", true);
    expect(g.specials).toEqual(["export"]);
  });
});
