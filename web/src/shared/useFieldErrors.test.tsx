// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { Alert, Form, Input } from "antd";
import { ApiError } from "../api/client";
import { namePath, useFieldErrors } from "./useFieldErrors";

vi.setConfig({ testTimeout: 30_000 });

beforeAll(() => {
  const computed = window.getComputedStyle.bind(window);
  window.getComputedStyle = (elt: Element) => computed(elt);
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({ matches: false, media: query, onchange: null, addListener: () => {}, removeListener: () => {}, addEventListener: () => {}, removeEventListener: () => {}, dispatchEvent: () => false }),
  });
});
afterEach(cleanup);

function UserForm({ error }: { error: unknown }) {
  const [form] = Form.useForm();
  const marked = useFieldErrors(form, error);
  return (
    <>
      {error instanceof Error && !marked && <Alert type="error" message={error.message} />}
      <Form form={form}>
        <Form.Item name="email" label="Email"><Input /></Form.Item>
        <Form.Item name="nameAr" label="Name"><Input /></Form.Item>
      </Form>
    </>
  );
}

describe("useFieldErrors", () => {
  it("reads API field names as antd name paths", () => {
    expect(namePath("email")).toEqual(["email"]);
    expect(namePath("items[2].value")).toEqual(["items", 2, "value"]);
  });

  it("marks the refused field under the field, without repeating it in the general alert", async () => {
    const error = new ApiError(400, "invalid_email", "Invalid email address.", "t1", [{ field: "email", code: "invalid_email", message: "Invalid email address." }]);
    render(<UserForm error={error} />);
    expect(await screen.findByText("Invalid email address.")).toBeTruthy();
    expect(screen.getAllByText("Invalid email address.")).toHaveLength(1);
    expect(document.querySelector(".ant-form-item-has-error input")?.getAttribute("id")).toBe("email");
  });

  it("keeps the general alert for an error about a field the form does not have, or about the whole request", async () => {
    const other = new ApiError(400, "invalid_code", "Bad code.", "t2", [{ field: "code", code: "invalid_code", message: "Bad code." }]);
    const { rerender } = render(<UserForm error={other} />);
    expect(screen.getByText("Bad code.")).toBeTruthy();
    expect(document.querySelector(".ant-form-item-has-error")).toBeNull();

    rerender(<UserForm error={new ApiError(422, "cycle_closed", "The cycle is closed.")} />);
    expect(screen.getByText("The cycle is closed.")).toBeTruthy();
  });
});
