import { describe, expect, it } from "vitest";

// Node's fs, loaded the way tokens.test.ts loads it so the browser build never sees it.
const { readFileSync, readdirSync } = (await import(/* @vite-ignore */ `node:${"fs"}`)) as {
  readFileSync(path: URL, encoding: "utf8"): string;
  readdirSync(path: URL, options: { recursive: true }): string[];
};
const src = new URL("../", import.meta.url);
const slashes = (f: string) => f.split("\\").join("/");

describe("shared queries (FE-7)", () => {
  it("cycles, units, the strategy tree and the latest results are defined once, in api/queries.ts", () => {
    const inline = /queryKey: \[\s*"(cycles|units|strategy)"\s*\]|queryKey: \["results", undefined, undefined\]/;
    const offenders = readdirSync(src, { recursive: true })
      .map(slashes)
      .filter((f) => /\.(ts|tsx)$/.test(f) && !/\.test\.tsx?$/.test(f) && !f.endsWith("api/queries.ts"))
      .filter((f) => inline.test(readFileSync(new URL(f, src), "utf8")));
    expect(offenders).toEqual([]);
  });

  it("no query key is written inline: every key comes from queryKeys", () => {
    const inline = /queryKey: \[|const \w*[kK]ey = \[|_KEY = \[/;
    const offenders = readdirSync(src, { recursive: true })
      .map(slashes)
      .filter((f) => /\.(ts|tsx)$/.test(f) && !/\.test\.tsx?$/.test(f) && !f.endsWith("api/queries.ts") && !f.endsWith("api/invalidate.ts"))
      .filter((f) => inline.test(readFileSync(new URL(f, src), "utf8")));
    expect(offenders).toEqual([]);
  });
});
