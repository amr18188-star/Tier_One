// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DecisionModal } from "./DecisionModal";

// antd in jsdom: the first render loads the whole library, so give each test room.
vi.setConfig({ testTimeout: 30_000 });

// antd reads media queries and pseudo-element styles; jsdom implements neither.
beforeAll(() => {
  const computed = window.getComputedStyle.bind(window);
  window.getComputedStyle = (elt: Element) => computed(elt);
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({ matches: false, media: query, onchange: null, addListener: () => {}, removeListener: () => {}, addEventListener: () => {}, removeEventListener: () => {}, dispatchEvent: () => false }),
  });
});
afterEach(cleanup);

function open(onSubmit: (v: string) => Promise<unknown>, onClose = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <DecisionModal title="إعادة النموذج" explain="يعود النموذج إلى مدخل البيانات." field="note" label="ملاحظة الإعادة" okText="إعادة"
        onSubmit={onSubmit} onClose={onClose} />
    </QueryClientProvider>,
  );
  const field = () => screen.getByLabelText("ملاحظة الإعادة");
  return { onClose, ok: () => screen.getByRole("button", { name: /إعادة/ }), write: (text: string) => fireEvent.change(field(), { target: { value: text } }) };
}

describe("DecisionModal", () => {
  it("asks for a reason of five characters before it submits", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn(async () => ({}));
    const { ok, write } = open(onSubmit);

    await user.click(ok());
    expect(await screen.findByText("هذا الحقل مطلوب")).toBeTruthy();
    write("قصير");
    await user.click(ok());
    await waitFor(() => expect(screen.getByText("هذا الحقل مطلوب")).toBeTruthy());
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("submits the written reason and closes", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn(async () => ({}));
    const { ok, write, onClose } = open(onSubmit);

    write("القيم ناقصة الأدلة");
    await user.click(ok());
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(onSubmit).toHaveBeenCalledWith("القيم ناقصة الأدلة");
  });

  it("keeps the dialog open and shows the server's refusal", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn(async () => { throw new Error("تغيّر النموذج. حدّث الصفحة ثم أعد المحاولة."); });
    const { ok, write, onClose } = open(onSubmit);

    write("سبب كاف للإعادة");
    await user.click(ok());
    expect(await screen.findByText(/تغيّر النموذج/)).toBeTruthy();
    expect(onClose).not.toHaveBeenCalled();
  });
});
