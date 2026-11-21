import type { Rule } from "antd/es/form";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../api/client";
import { queryKeys } from "../../api/queries";
import { matchingRoot } from "../../api/security";
import { L } from "../../shared/i18n";

/**
 * ADR-0016: an antd rule for the university email of an account form: the approved active domains or their subdomains, as the
 * server checks it. Until the list is loaded the rule passes (the server refuses a wrong domain with the field named anyway).
 */
export function useEmailDomainRule(): Rule {
  const roots = useQuery({ queryKey: queryKeys.activeEmailDomains, queryFn: () => api.get<string[]>("/api/security/domains/active"), staleTime: 60_000 });
  return {
    validator: (_, value: string | undefined) => {
      if (!value || !roots.data || matchingRoot(value, roots.data)) return Promise.resolve();
      return Promise.reject(new Error(L(`البريد يجب أن يكون بريدًا جامعيًا من نطاق معتمد أو أحد نطاقاته الفرعية: ${roots.data.join("، ")}.`,
        `The email must be a university email at an approved domain or one of its subdomains: ${roots.data.join(", ")}.`)));
    },
  };
}
