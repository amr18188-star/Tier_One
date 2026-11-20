// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import { useCoalesced } from "./useCoalesced";

afterEach(() => vi.useRealTimers());

describe("useCoalesced (FE-4)", () => {
  it("runs once after a burst of calls goes quiet", () => {
    vi.useFakeTimers();
    const run = vi.fn();
    const { result } = renderHook(() => useCoalesced(run, 1500));
    for (let i = 0; i < 10; i++) {
      result.current();
      vi.advanceTimersByTime(500);
    }
    expect(run).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1500);
    expect(run).toHaveBeenCalledTimes(1);
  });

  it("does not run after the page has gone", () => {
    vi.useFakeTimers();
    const run = vi.fn();
    const { result, unmount } = renderHook(() => useCoalesced(run, 1500));
    result.current();
    unmount();
    vi.advanceTimersByTime(5000);
    expect(run).not.toHaveBeenCalled();
  });
});
