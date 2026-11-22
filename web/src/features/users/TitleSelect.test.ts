import { describe, expect, it } from "vitest";
import type { TitleOption } from "../../api/types";
import { titleGroups } from "./TitleSelect";

/** ADR-0019: the title picker of the account form. */
describe("titleGroups", () => {
  const served: TitleOption[] = [
    { title: "أ.د.", label: "أ.د.", meaning: "أستاذ دكتور", group: "Faculty" },
    { title: "السيد", label: "السيد", meaning: "موظف", group: "Staff" },
    { title: "السيدة", label: "السيدة", meaning: "موظفة", group: "Staff" },
    { title: "عقيد", label: "عقيد", meaning: "عقيد", group: "Officer" },
  ];

  it("groups the titles as faculty, staff and military ranks, in that order", () => {
    const groups = titleGroups(served);
    expect(groups.map((g) => g.label)).toEqual(["أعضاء هيئة التدريس", "الموظفون", "الرتب العسكرية"]);
    expect(groups[1].options.map((o) => o.value)).toEqual(["السيد", "السيدة"]);
  });

  it("shows a title's meaning beside it only when it says more than the title", () => {
    const groups = titleGroups(served);
    expect(groups[0].options[0]).toEqual({ value: "أ.د.", label: "أ.د. — أستاذ دكتور" });
    expect(groups[2].options[0]).toEqual({ value: "عقيد", label: "عقيد" });
  });

  it("sends the Arabic title whatever the interface language shows", () => {
    const english: TitleOption[] = [{ title: "أ.د.", label: "Prof.", meaning: "Professor", group: "Faculty" }];
    expect(titleGroups(english)[0].options[0]).toEqual({ value: "أ.د.", label: "Prof. — Professor" });
  });

  it("leaves out a group with no titles", () => {
    expect(titleGroups(served.filter((t) => t.group !== "Officer")).map((g) => g.label)).toEqual(["أعضاء هيئة التدريس", "الموظفون"]);
  });
});
