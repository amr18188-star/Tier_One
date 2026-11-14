/**
 * The per-field save queue behind autosave: one request in flight, one follow-up with the latest value and the version
 * from the previous response, no request when nothing changed, stale responses ignored, errors stop until a retry.
 */
import { describe, expect, it } from "vitest";
import { SaveQueue, type QueueState } from "./saveQueue";

interface Sent { value: string; version: number | null; resolve: (version: number) => void; reject: (e: unknown) => void }

function setup(initial = "", version: number | null = null) {
  const sent: Sent[] = [];
  const states: QueueState[] = [];
  const saved: string[] = [];
  const q = new SaveQueue<string, number>(initial, version, {
    send: (value, v) => new Promise<number>((resolve, reject) => sent.push({ value, version: v, resolve, reject })),
    versionOf: (r) => r,
    same: (a, b) => a === b,
    ready: (d) => d.length > 0,
    onSaved: (_r, d) => saved.push(d),
    onState: (s) => states.push(s),
  });
  return { q, sent, states, saved };
}

const tick = () => new Promise((r) => setTimeout(r, 0));

describe("save queue", () => {
  it("sends nothing when the value equals the last saved one or is not ready", async () => {
    const { q, sent } = setup("12", 3);
    await q.flush();
    q.set("");
    await q.flush();
    expect(sent).toHaveLength(0);
  });

  it("keeps one request in flight and sends one follow-up with the latest value and the returned version", async () => {
    const { q, sent, states, saved } = setup();
    q.set("1");
    void q.run();
    q.set("12");
    void q.run();
    q.set("123");
    void q.run();
    expect(sent.map((s) => s.value)).toEqual(["1"]);
    sent[0].resolve(7);
    await tick();
    expect(sent.map((s) => [s.value, s.version])).toEqual([["1", null], ["123", 7]]);
    sent[1].resolve(8);
    await tick();
    expect(sent).toHaveLength(2);
    expect(saved).toEqual(["1", "123"]);
    expect(q.version).toBe(8);
    expect(states.at(-1)).toBe("saved");
  });

  it("flush waits for the follow-up too", async () => {
    const { q, sent } = setup();
    q.set("a");
    void q.run();
    q.set("ab");
    let done = false;
    const flushed = q.flush().then(() => { done = true; });
    sent[0].resolve(1);
    await tick();
    expect(done).toBe(false);
    sent[1].resolve(2);
    await flushed;
    expect(sent.map((s) => s.value)).toEqual(["a", "ab"]);
  });

  it("ignores a response that arrives after a reset", async () => {
    const { q, sent, saved } = setup("x", 1);
    q.set("y");
    void q.run();
    q.invalidate();
    sent[0].resolve(2);
    await tick();
    expect(saved).toEqual([]);
    expect(q.version).toBe(1);
  });

  it("stops on an error and resends on retry with the same version", async () => {
    const { q, sent, states } = setup("x", 4);
    q.set("y");
    void q.run();
    sent[0].reject(new Error("boom"));
    await tick();
    expect(states.at(-1)).toBe("error");
    expect(q.unsaved).toBe(true);
    void q.run();
    expect(sent.map((s) => [s.value, s.version])).toEqual([["y", 4], ["y", 4]]);
  });

  it("adopts a newer server value only when nothing is pending, and never an older one", () => {
    const { q } = setup("x", 5);
    expect(q.adopt("old", 4)).toBe(false);
    expect(q.adopt("new", 6)).toBe(true);
    expect([q.latest, q.version]).toEqual(["new", 6]);
    q.set("typing");
    expect(q.adopt("other", 7)).toBe(false);
    expect([q.latest, q.version]).toEqual(["typing", 6]);
  });
});
