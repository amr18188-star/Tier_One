import { App } from "antd";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import { invalidate } from "../../api/invalidate";
import type { Form, FormItem } from "../../api/collection";
import { L } from "../../shared/i18n";
import { useCoalesced } from "../../shared/useCoalesced";
import { queryKeys } from "../../api/queries";

/**
 * One form's data and its writes: the query, the autosave patch, submit, and showing what a decision returned.
 * Every decision refreshes what it changes elsewhere (FE-6).
 */
export function useFormPage(id: string | undefined) {
  const qc = useQueryClient();
  const { message } = App.useApp();
  const key = queryKeys.form(id);
  const form = useQuery({ queryKey: key, queryFn: () => api.get<Form>(`/api/forms/${id}`) });

  const refresh = () => qc.invalidateQueries({ queryKey: key });
  // FE-4: the saved row is patched at once; the form-wide parts (completed count, blocking list, version) refresh once the
  // saves pause, not with a full GET after every autosave. Submit reads the form fresh before sending in any case.
  const refreshSoon = useCoalesced(() => void refresh(), 1500);
  const onSaved = (item: FormItem) => {
    qc.setQueryData<Form>(key, (f) => f && {
      ...f,
      sections: f.sections.map((s) => ({ ...s, items: s.items.map((i) => (i.indicatorId === item.indicatorId ? item : i)) })),
    });
    refreshSoon();
  };

  const submit = useMutation({
    // The autosaves just flushed move the form's version, so send from the version the server holds now (BE-2).
    mutationFn: async () => {
      const fresh = await qc.fetchQuery({ queryKey: key, queryFn: () => api.get<Form>(`/api/forms/${id}`), staleTime: 0 });
      return api.post<Form>(`/api/forms/${id}/submit`, { version: fresh.version });
    },
    onSuccess: (f) => {
      qc.setQueryData(key, f);
      void invalidate.afterFormDecision(qc);
      message.success(L(`أرسل النموذج إلى «${f.path.find((p) => p.state === "current")?.name ?? ""}»`, "Form sent for review"));
    },
  });

  /** A decision (approve, return, reopen, extend) returned the form: show it, and refresh what the decision changes elsewhere. */
  const applyDecision = (next: Form) => {
    qc.setQueryData(key, next);
    void invalidate.afterFormDecision(qc);
  };

  return { form, refresh, onSaved, submit, applyDecision };
}

export type FormSubmit = ReturnType<typeof useFormPage>["submit"];
