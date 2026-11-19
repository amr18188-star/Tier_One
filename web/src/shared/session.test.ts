import { describe, expect, it } from "vitest";
import { QueryClient } from "@tanstack/react-query";
import { refreshIdentity } from "./session";

/** انحدار: بعد انتهاء الجلسة ودخول مستخدم آخر في التبويب نفسه كانت بيانات السابق تبقى في الذاكرة وتعرض. */
describe("refreshIdentity", () => {
  const me = (id: string) => ({ id });

  it("drops every cached query when another user signs in", async () => {
    const qc = new QueryClient();
    qc.setQueryData(["me"], null); // انتهت جلسة المستخدم السابق
    qc.setQueryData(["pillar-map"], { secret: "previous user's data" });
    await refreshIdentity(qc, async () => qc.setQueryData(["me"], me("b")));
    expect(qc.getQueryData(["pillar-map"])).toBeUndefined();
    expect(qc.getQueryData(["me"])).toEqual(me("b"));
  });

  it("drops cached queries when the session ends", async () => {
    const qc = new QueryClient();
    qc.setQueryData(["me"], me("a"));
    qc.setQueryData(["cycles"], [1]);
    await refreshIdentity(qc, async () => qc.setQueryData(["me"], null));
    expect(qc.getQueryData(["cycles"])).toBeUndefined();
  });

  it("keeps data but marks it stale when the same user re-verifies", async () => {
    const qc = new QueryClient();
    qc.setQueryData(["me"], me("a"));
    qc.setQueryData(["cycles"], [1]);
    await refreshIdentity(qc, async () => qc.setQueryData(["me"], me("a")));
    expect(qc.getQueryData(["cycles"])).toEqual([1]);
    expect(qc.getQueryState(["cycles"])?.isInvalidated).toBe(true);
  });
});
