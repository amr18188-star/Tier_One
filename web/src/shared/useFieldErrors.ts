import type { FormInstance } from "antd";
import { useEffect } from "react";
import { ApiError, type FieldError } from "../api/client";

/** A field name from the API ("items[2].value") as an antd name path (["items", 2, "value"]). */
export function namePath(field: string): (string | number)[] {
  return field
    .split(/[.[\]]/)
    .filter((part) => part.length > 0)
    .map((part) => (/^\d+$/.test(part) ? Number(part) : part));
}

/** The field errors of `error` that name a field registered on `form`. */
export function errorsOnForm(form: FormInstance, error: unknown): FieldError[] {
  if (!(error instanceof ApiError) || error.errors.length === 0) return [];
  const registered = new Set(form.getFieldsError().map((f) => namePath(f.name.join(".")).join(".")));
  return error.errors.filter((e) => registered.has(namePath(e.field).join(".")));
}

/**
 * For a form submitted in a try/catch: marks the refused fields at once and returns true when any was marked; otherwise the
 * caller shows the error as before.
 */
export function markFieldErrors(form: FormInstance, error: unknown): boolean {
  const found = errorsOnForm(form, error);
  if (found.length > 0) form.setFields(found.map((e) => ({ name: namePath(e.field), errors: [e.message] })));
  return found.length > 0;
}

/**
 * BE-6: the server names the fields it refused; mark each under its own field in the antd form, as the form's own rules do.
 * Returns true when a field of this form was marked, so the caller can leave out the general alert that would repeat it.
 * The mark goes when the user edits the field (antd validates it again).
 */
export function useFieldErrors(form: FormInstance, error: unknown): boolean {
  const marked = errorsOnForm(form, error);
  useEffect(() => {
    markFieldErrors(form, error);
  }, [form, error]);
  return marked.length > 0;
}
