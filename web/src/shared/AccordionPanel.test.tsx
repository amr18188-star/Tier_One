import { describe, expect, it } from "vitest";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { AccordionPanel } from "./AccordionPanel";

const base = { id: "t", open: true, onToggle: () => {}, header: "h" };
const q = (o: object) => ({ isLoading: false, isError: false, error: null, data: undefined, refetch: () => {}, ...o });
const render = (query: object | undefined) => renderToStaticMarkup(
  <MemoryRouter><AccordionPanel {...base} query={query ? q(query) : undefined}><b id="real">real</b></AccordionPanel></MemoryRouter>);

describe("AccordionPanel with a query", () => {
  it("shows the skeleton while the first load runs, panel already open", () => {
    const html = render({ isLoading: true });
    expect(html).toContain('data-open="true"');
    expect(html).toContain("ds-skel");
    expect(html).toContain('aria-busy="true"');
    expect(html).not.toContain('id="real"');
  });
  it("shows an inline error with retry when the first load fails", () => {
    const html = render({ isError: true, error: new Error("boom") });
    expect(html).toContain("ds-acc-state");
    expect(html).not.toContain("ds-skel");
    expect(html).not.toContain('id="real"');
  });
  it("shows content when data exists, even if a refetch failed", () => {
    const html = render({ isError: true, data: [1] });
    expect(html).toContain('id="real"');
  });
  it("shows content without a query", () => expect(render(undefined)).toContain('id="real"'));
});
