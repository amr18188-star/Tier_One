import { describe, expect, it } from "vitest";

// Node's fs, loaded the way tokens.test.ts loads it so the browser build never sees it.
const { readFileSync } = (await import(/* @vite-ignore */ `node:${"fs"}`)) as { readFileSync(path: URL, encoding: "utf8"): string };
const read = (rel: string) => readFileSync(new URL(rel, import.meta.url), "utf8");

/** The stylesheets a module imports itself (not those another chunk may have loaded earlier in the session). */
function importedCss(rel: string): string {
  const source = read(rel);
  return [...source.matchAll(/^import "([^"]+\.css)";/gm)].map((m) => read(new URL(m[1], new URL(rel, import.meta.url)).href)).join("\n");
}

describe("progress page styles (FE-5)", () => {
  it("defines the achievement bar and its tones in the stylesheets the page imports itself", () => {
    const css = importedCss("./ProgressPage.tsx");
    for (const selector of [".sc-bar ", ".sc-bar i", ".c100", ".c90", ".c70", ".clow"]) expect(css).toContain(selector);
  });

  it("the scorecard keeps the same bar from the shared stylesheet", () => {
    expect(importedCss("../scorecard/ScorecardPage.tsx")).toContain(".sc-bar ");
  });
});
