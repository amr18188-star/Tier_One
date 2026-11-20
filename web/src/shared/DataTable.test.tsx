// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { DataTable } from "./ui";

vi.setConfig({ testTimeout: 30_000 });
beforeAll(() => {
  const computed = window.getComputedStyle.bind(window);
  window.getComputedStyle = (elt: Element) => computed(elt);
  // Wide screen: the table, not the narrow-screen cards.
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({ matches: true, media: query, onchange: null, addListener: () => {}, removeListener: () => {}, addEventListener: () => {}, removeEventListener: () => {}, dispatchEvent: () => false }),
  });
});
afterEach(cleanup);

type Row = { code: string; name: string };
const rows: Row[] = [{ code: "1.01", name: "نسبة الطلاب" }, { code: "1.02", name: "عدد البرامج" }];
const columns = [{ title: "المؤشر", dataIndex: "name" }];

describe("DataTable (A11Y-1)", () => {
  it("gives every openable row a real, named button that opens it", () => {
    const opened: string[] = [];
    render(<DataTable<Row> rowKey={(r) => r.code} data={rows} columns={columns} onRowClick={(r) => opened.push(r.code)} rowLabel={(r) => `فتح المؤشر ${r.code}`} />);
    const second = screen.getByRole("button", { name: "فتح المؤشر 1.02" });
    expect(screen.getByRole("button", { name: "فتح المؤشر 1.01" })).toBeTruthy();
    fireEvent.click(second);
    expect(opened).toEqual(["1.02"]);
  });

  it("keeps plain numbers when rows do not open", () => {
    render(<DataTable<Row> rowKey={(r) => r.code} data={rows} columns={columns} />);
    expect(screen.queryAllByRole("button", { name: /فتح المؤشر/ })).toHaveLength(0);
    expect(screen.getByText("نسبة الطلاب")).toBeTruthy();
  });
});
