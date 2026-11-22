import { Select } from "antd";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../api/client";
import { queryKeys } from "../../api/queries";
import type { TitleGroup, TitleOption } from "../../api/types";
import { L } from "../../shared/i18n";

const GROUPS: { key: TitleGroup; label: () => string }[] = [
  { key: "Faculty", label: () => L("أعضاء هيئة التدريس", "Faculty") },
  { key: "Staff", label: () => L("الموظفون", "Staff") },
  { key: "Officer", label: () => L("الرتب العسكرية", "Military ranks") },
];

/** Group the served titles for the picker: faculty, staff, military ranks; a title's meaning beside it when it differs. */
export function titleGroups(titles: TitleOption[]) {
  return GROUPS.map((g) => ({
    label: g.label(),
    // The value is always the Arabic title the server stores; only the label follows the interface language.
    options: titles.filter((t) => t.group === g.key).map((t) => ({ value: t.title, label: t.meaning === t.label ? t.label : `${t.label} — ${t.meaning}` })),
  })).filter((g) => g.options.length > 0);
}

/**
 * اختيار لقب صاحب الحساب من القائمة المغلقة (ADR-0019): أ.د. وأخواتها لهيئة التدريس، والسيد أو السيدة للموظف بحسب النوع،
 * والرتب العسكرية. اللقب يسبق الاسم في كل الشاشات، ويعرض الاسم بعده ثنائيًا.
 */
export function TitleSelect({ value, onChange, allowClear }: { value?: string | null; onChange?: (v: string | null) => void; allowClear?: boolean }) {
  const titles = useQuery({ queryKey: queryKeys.titles, queryFn: () => api.get<TitleOption[]>("/api/iam/users/titles"), staleTime: Infinity });
  return (
    <Select
      value={value ?? undefined}
      onChange={(v) => onChange?.(v ?? null)}
      allowClear={allowClear}
      loading={titles.isLoading}
      showSearch
      optionFilterProp="label"
      placeholder={L("اختر اللقب", "Choose a title")}
      options={titleGroups(titles.data ?? [])}
      aria-label={L("اللقب", "Title")}
    />
  );
}
