// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render } from "@testing-library/react";
import { CollegeTarget, shownOf, UniTarget } from "./CollegeTarget";

// antd in jsdom: the first render loads the whole library, so give each test room.
vi.setConfig({ testTimeout: 30_000 });

beforeAll(() => {
  const computed = window.getComputedStyle.bind(window);
  window.getComputedStyle = (elt: Element) => computed(elt);
});
afterEach(cleanup);

/** DEC-5 (ADR-0017): the "college target" column beside an indicator's value. */
describe("CollegeTarget", () => {
  it("shows the college's own target plainly", () => {
    const { container } = render(<CollegeTarget target={{ value: 82, text: null, source: "EachCollege" }} unit="%" />);
    expect(container.textContent).toBe("82%");
    expect(container.querySelector("u")).toBeNull();
  });

  it("underlines the university's target shown in place of a missing college target", () => {
    const { container } = render(<CollegeTarget target={{ value: 175000, text: null, source: "University" }} unit="عدد" />);
    const mark = container.querySelector("u.t1-uni-target");
    expect(mark).not.toBeNull();
    expect(mark!.textContent).toContain("175");
    expect(mark!.getAttribute("aria-label")).toBe("مستهدف الجامعة");
  });

  it("shows a descriptive college target as written, without an underline", () => {
    const { container } = render(<CollegeTarget target={{ value: null, text: "مشروعين لكل كلية", source: "EachCollege" }} unit="%" />);
    expect(container.textContent).toBe("مشروعين لكل كلية");
    expect(container.querySelector("u")).toBeNull();
  });

  it("says there is no target when neither the college nor the university has one", () => {
    const { container } = render(<CollegeTarget target={{ value: null, text: null, source: "None" }} unit="%" />);
    expect(container.textContent).toBe("لا مستهدف");
  });

  it("drops a non-percent unit in a compact cell", () => {
    const { container } = render(<CollegeTarget compact target={{ value: 2, text: null, source: "EachCollege" }} unit="عدد" />);
    expect(container.textContent).toBe("2");
  });

  it("underlines the target of the university's own row", () => {
    const { container } = render(<CollegeTarget university target={{ value: 85, text: null, source: "Unit" }} unit="%" />);
    expect(container.querySelector("u.t1-uni-target")?.textContent).toBe("85%");
  });

  it("underlines a university target wherever it is shown, but not the dash of a missing one", () => {
    expect(render(<UniTarget>175,000</UniTarget>).container.querySelector("u.t1-uni-target")?.textContent).toBe("175,000");
    const none = render(<UniTarget>—</UniTarget>).container;
    expect(none.querySelector("u")).toBeNull();
    expect(none.textContent).toBe("—");
  });

  it("falls back to the measured target for a row from an older API", () => {
    expect(shownOf({ target: 80, targetSource: "Unit" })).toEqual({ value: 80, text: null, source: "Unit" });
    expect(shownOf({ target: null, targetSource: "Unit" }).source).toBe("None");
    const shown = { value: 5, text: null, source: "University" as const };
    expect(shownOf({ shownTarget: shown, target: null, targetSource: "None" })).toBe(shown);
  });
});
