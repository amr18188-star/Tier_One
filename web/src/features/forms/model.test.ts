/**
 * The forms page without a UI: plan years and quarters from the calendar (or the cycles when it is missing), which cycle
 * a link opens, the page modes per role, and forms grouped by college. Texts are checked in Arabic, the default language.
 */
import { describe, expect, it } from "vitest";
import type { Cycle, FormRow } from "../../api/collection";
import type { PlanQuarter } from "../../api/scorecard";
import {
  chipLabel, cycleTimeline as buildTimeline, planState, quarterTimeline, resolveCycle as resolveSelection, resolveQuarter, stateLine, stepSlot,
} from "../../shared/period";
import { formEventText, formModes, groupByCollege, skippedSteps, type Access } from "./model";

describe("form history", () => {
  it("labels direct approvals by the college committee and dean", () => {
    expect(formEventText({ action: "Approved", fromStep: "DepartmentReview", toStep: "Dean" })).toBe("اعتماد مباشر");
    expect(formEventText({ action: "Approved", fromStep: "DepartmentReview", toStep: "Quality" })).toBe("اعتماد مباشر");
    expect(formEventText({ action: "Approved", fromStep: "CollegeCommittee", toStep: "Quality" })).toBe("اعتماد مباشر");
  });

  it("keeps ordinary and final approvals labeled as approved", () => {
    expect(formEventText({ action: "Approved", fromStep: "CollegeCommittee", toStep: "Dean" })).toBe("اعتمد");
    expect(formEventText({ action: "Approved", fromStep: "Dean", toStep: "Quality" })).toBe("اعتمد");
    expect(formEventText({ action: "Approved", fromStep: "Quality", toStep: "Done" })).toBe("اعتمد");
  });
});

const calendar: PlanQuarter[] = [2027, 2028, 2029, 2030, 2031].flatMap((year, y) => [1, 2, 3, 4].map((quarter) => ({
  year, yearIndex: y + 1, quarter, index: y * 4 + quarter, start: `${year}-01-01`, end: `${year}-03-31`, label: "", current: false,
})));

function cycle(id: string, year: number, quarter: number | null, status: Cycle["status"], extra: Partial<Cycle> = {}): Cycle {
  return {
    id, name: `دورة ${id}`, periodLabel: "", targetYear: year, opensOn: `${year}-04-01`, dueOn: `${year}-04-30`, status, trial: false,
    indicators: 10, forms: 4, formsByStatus: { Approved: 4 }, overdue: 0, version: 1, quarter, ...extra,
  };
}

const cycles = [
  cycle("q1", 2027, 1, "Closed"),
  cycle("q2", 2027, 2, "Closed", { formsByStatus: { Approved: 3 } }),
  cycle("q3", 2027, 3, "Open"),
  cycle("q4", 2027, 4, "Draft"),
  cycle("trial", 2027, 1, "Closed", { trial: true }),
  cycle("annual", 2026, null, "Closed"),
];

describe("year and quarter timeline", () => {
  const tl = buildTimeline(cycles, calendar);

  it("takes the plan years from the calendar and keeps a cycle outside it reachable", () => {
    expect(tl.years).toEqual([2026, 2027, 2028, 2029, 2030, 2031]);
  });

  it("falls back to the years of non-draft cycles without a calendar", () => {
    expect(buildTimeline(cycles, undefined).years).toEqual([2026, 2027]);
  });

  it("gives every year four quarters, then its trial and annual cycles", () => {
    const y27 = tl.slots.filter((s) => s.year === 2027);
    expect(y27.map((s) => [s.cycle?.id ?? null, s.state])).toEqual([
      ["q1", "closed"], ["q2", "closed"], ["q3", "open"], ["q4", "future"], ["trial", "closed"],
    ]);
    expect(y27.map(chipLabel)).toEqual(["ر1", "ر2", "ر3", "ر4", "بروفة ر1"]);
    const y26 = tl.slots.filter((s) => s.year === 2026);
    expect(y26.map((s) => s.state)).toEqual(["none", "none", "none", "none", "closed"]);
    expect(chipLabel(y26[4])).toBe("سنوية");
  });

  it("describes each state", () => {
    const [q1, q2, , q4] = tl.slots.filter((s) => s.year === 2027);
    expect(stateLine(q1)).toBe("مغلق · اعتُمد بالكامل");
    expect(stateLine(q2)).toBe("مغلق · 3/4 معتمد");
    expect(stateLine(q4)).toMatch(/^لم يُفتح بعد · يفتح /);
    expect(stateLine(tl.slots.find((s) => s.year === 2028)!)).toBe("لا دورة");
  });

  it("opens the cycle in the link, closed ones too, with its year", () => {
    const sel = resolveSelection(tl, "q1", 2030);
    expect([sel.year, sel.slot?.cycle?.id]).toEqual([2027, "q1"]);
  });

  it("otherwise opens the year in the link, then the open cycle's year, then the first plan year", () => {
    expect(resolveSelection(tl, null, 2026).slot?.cycle?.id).toBe("annual");
    expect(resolveSelection(tl, null, 2029)).toEqual({ year: 2029, slot: null });
    expect(resolveSelection(tl, "unknown", null).slot?.cycle?.id).toBe("q3");
    const closedOnly = buildTimeline([cycle("a", 2028, 2, "Closed")], calendar);
    expect(resolveSelection(closedOnly, null, null)).toMatchObject({ year: 2027, slot: null });
  });

  it("steps to the previous or next quarter that has a cycle, across years", () => {
    const at = (id: string) => tl.slots.find((s) => s.cycle?.id === id)!;
    expect(stepSlot(tl, at("q1"), 2027, -1)?.cycle?.id).toBe("annual");
    expect(stepSlot(tl, at("q3"), 2027, 1)?.cycle?.id).toBe("q4");
    expect(stepSlot(tl, at("trial"), 2027, 1)).toBeNull();
    expect(stepSlot(tl, null, 2029, -1)?.cycle?.id).toBe("trial");
  });
});

describe("plan quarters (PeriodNav quarter mode)", () => {
  const plan = (year: number, quarter: number, start: string, end: string, current = false): PlanQuarter =>
    ({ year, yearIndex: 1, quarter, index: quarter, start, end, label: "", current });

  it("takes each quarter's state from its dates", () => {
    expect(planState(plan(2027, 1, "2027-01-01", "2027-03-31"), "2027-05-10")).toBe("closed");
    expect(planState(plan(2027, 2, "2027-04-01", "2027-06-30"), "2027-05-10")).toBe("open");
    expect(planState(plan(2027, 3, "2027-07-01", "2027-09-30"), "2027-05-10")).toBe("future");
  });

  it("disables quarters on request, with the reason in the state line", () => {
    const cal = [1, 2, 3, 4].map((q) => plan(2090, q, `2090-0${q * 2}-01`, `2090-0${q * 2}-28`));
    const tl = quarterTimeline(cal, { disabledQuarters: (_y, q) => (q === 2 ? "له دورة" : false) });
    expect(tl.slots.map((s) => [s.state, s.disabled])).toEqual([["future", false], ["future", true], ["future", false], ["future", false]]);
    expect(stateLine(tl.slots[1])).toMatch(/ · له دورة$/);
    expect(stepSlot(tl, tl.slots[0], 2090, 1)?.quarter).toBe(3);
    expect(resolveQuarter(tl, 2090, 2).slot?.quarter).toBe(2);
    expect(resolveQuarter(tl, null, null)).toMatchObject({ year: 2090, slot: { quarter: 1 } });
  });
});

function access(grants: Record<string, string[]>, scope: "A" | "U" = "U", isSuperAdmin = false): Access {
  const forms = grants["dc.forms"] ?? [];
  return {
    isSuperAdmin,
    can: (feature, cap) => feature === "dc.forms" && forms.includes(cap),
    scopeOf: (feature, cap) => (feature === "dc.forms" && forms.includes(cap) ? scope : undefined),
  };
}

describe("page modes per role (permission-catalog.json defaults)", () => {
  it("entry and department representatives get tasks only", () => {
    expect(formModes(access({ "dc.forms": ["V", "P", "E"] }), false)).toEqual({ modes: ["tasks"], initial: "tasks", readOnly: false });
    expect(formModes(access({ "dc.forms": ["V", "P", "E", "review_department"] }), false).modes).toEqual(["tasks"]);
  });

  it("deans, college committees, quality and super admins get both, monitor first", () => {
    for (const a of [
      access({ "dc.forms": ["V", "P", "approve_college"] }),
      access({ "dc.forms": ["V", "P", "E", "review_college"] }),
      access({ "dc.forms": ["V", "P", "approve_final"] }, "A"),
      access({ "dc.forms": ["V", "P", "E"] }, "A", true),
    ]) expect(formModes(a, false)).toEqual({ modes: ["monitor", "tasks"], initial: "monitor", readOnly: false });
  });

  it("the plan manager and viewers monitor read-only", () => {
    expect(formModes(access({ "dc.forms": ["V", "P"] }, "A"), false)).toEqual({ modes: ["monitor"], initial: "monitor", readOnly: true });
    expect(formModes(access({ "dc.forms": ["V"] }), false)).toEqual({ modes: ["monitor"], initial: "monitor", readOnly: true });
  });

  it("a form actually waiting for the user adds the tasks tab", () => {
    expect(formModes(access({ "dc.forms": ["V", "P"] }, "A"), true).modes).toEqual(["monitor", "tasks"]);
  });
});

function row(id: string, unitName: string, unitKind: FormRow["unitKind"], parentName: string | null, extra: Partial<FormRow> = {}): FormRow {
  return {
    id, cycleId: "q3", cycleName: "", unitId: id, unitName, unitKind, parentName, step: "Entry", stepName: "إدخال البيانات",
    status: "InProgress", dueOn: "2027-04-30", overdue: false, completed: 1, required: 4, awaitingMe: false, ...extra,
  };
}

describe("forms grouped by college", () => {
  const rows = [
    row("civ", "قسم المدنية", "Department", "كلية الهندسة", { status: "Returned" }),
    row("eng", "كلية الهندسة", "College", "جامعة العاصمة"),
    row("ele", "قسم الكهربية", "Department", "كلية الهندسة", { status: "Approved" }),
    row("qac", "وحدة الجودة", "Center", "جامعة العاصمة", { overdue: true }),
    row("cs", "كلية الحاسبات", "College", "جامعة العاصمة", { status: "Approved" }),
    row("inst", "معهد البحوث", "Institute", "جامعة العاصمة"),
  ];

  it("puts departments under their college, the college form first, central units last", () => {
    const groups = groupByCollege(rows, rows);
    expect(groups.map((g) => g.name)).toEqual(["كلية الحاسبات", "كلية الهندسة", "معهد البحوث", "الوحدات المركزية"]);
    const eng = groups[1];
    expect(eng.shown.map((r) => r.id)).toEqual(["eng", "civ", "ele"]);
    expect([eng.total, eng.approved, eng.returned, eng.overdue]).toEqual([3, 1, 1, 0]);
    expect(groups[3]).toMatchObject({ central: true, overdue: 1 });
  });

  it("counts every form of the college but shows only the matching ones", () => {
    const groups = groupByCollege(rows, rows.filter((r) => r.id === "civ"));
    expect(groups).toHaveLength(1);
    expect(groups[0]).toMatchObject({ name: "كلية الهندسة", total: 3 });
    expect(groups[0].shown.map((r) => r.id)).toEqual(["civ"]);
  });
});

describe("direct approval skips (D2)", () => {
  const path = (current: number) => ["الإدخال", "ممثل القسم", "لجنة الكلية", "العميد", "الجودة"].map((name, i) => ({
    name, state: i < current ? "done" : i === current ? "current" : "pending",
  }));
  it("lists the steps between the current one and the approver's own step (matches ApprovalRules.SkippedBy)", () => {
    // The dean forwards to quality from the department step: department review and college committee are skipped.
    expect(skippedSteps({ path: path(1) }, "الجودة")).toEqual(["ممثل القسم", "لجنة الكلية"]);
    // The dean forwards to quality from the committee step: only the committee is skipped.
    expect(skippedSteps({ path: path(2) }, "الجودة")).toEqual(["لجنة الكلية"]);
    // The college committee forwards to the dean from the department step: only department review is skipped.
    expect(skippedSteps({ path: path(1) }, "العميد")).toEqual(["ممثل القسم"]);
  });
  it("skips nothing without a target ahead of the current step", () => {
    expect(skippedSteps({ path: path(2) }, null)).toEqual([]);
    expect(skippedSteps({ path: path(3) }, "لجنة الكلية")).toEqual([]);
  });
});
