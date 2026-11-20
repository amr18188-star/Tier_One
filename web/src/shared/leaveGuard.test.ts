import { describe, expect, it, vi } from "vitest";
import { leaveThen, setLeaveGuard } from "./leaveGuard";

describe("leaveGuard", () => {
  it("goes at once when no page has unsaved work", () => {
    const go = vi.fn();
    leaveThen(go);
    expect(go).toHaveBeenCalledOnce();
  });

  it("lets the page with unsaved work decide, and goes only when it says so", () => {
    let pending: (() => void) | null = null;
    const remove = setLeaveGuard((proceed) => {
      pending = proceed;
      return true;
    });
    const go = vi.fn();
    leaveThen(go);
    expect(go).not.toHaveBeenCalled();
    pending!();
    expect(go).toHaveBeenCalledOnce();
    remove();
  });

  it("a guard that does not take over lets the navigation go", () => {
    const remove = setLeaveGuard(() => false);
    const go = vi.fn();
    leaveThen(go);
    expect(go).toHaveBeenCalledOnce();
    remove();
  });

  it("removing an older guard does not remove the newer one", () => {
    const first = setLeaveGuard(() => true);
    const second = setLeaveGuard(() => true);
    first();
    const go = vi.fn();
    leaveThen(go);
    expect(go).not.toHaveBeenCalled();
    second();
    leaveThen(go);
    expect(go).toHaveBeenCalledOnce();
  });
});
