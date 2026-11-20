import { describe, expect, it } from "vitest";

// Node's fs, loaded the way tokens.test.ts loads it so the browser build never sees it.
const { readFileSync } = (await import(/* @vite-ignore */ `node:${"fs"}`)) as { readFileSync(path: URL, encoding: "utf8"): string };

// A variable path: the script is plain JavaScript run by Node, not part of the typed sources.
const generator = "../../scripts/gen-api.mjs";

describe("generated API types (BE-22)", () => {
  it("src/api/schema.d.ts matches the contract in docs/api/openapi.json; run `npm run gen:api` after the contract changes", async () => {
    const { generate, target } = (await import(/* @vite-ignore */ generator)) as { generate(): Promise<string>; target: URL };
    expect(readFileSync(target, "utf8").replace(/\r\n/g, "\n")).toBe((await generate()).replace(/\r\n/g, "\n"));
  }, 60_000);
});
