/** Anchor ids of the landing sections, shared by the sections and the footer's quick links. */
export const sectionIds = {
  framework: "framework",
  pillars: "pillars",
  analytics: "analytics",
  hierarchy: "hierarchy",
  data: "data",
  integration: "integration",
} as const;

/** Footer quick links, in page order. */
export const sectionLinks: { id: (typeof sectionIds)[keyof typeof sectionIds]; label: string }[] = [
  { id: sectionIds.framework, label: "الإطار" },
  { id: sectionIds.pillars, label: "الركائز" },
  { id: sectionIds.analytics, label: "التحليلات" },
  { id: sectionIds.hierarchy, label: "الهيكلية" },
  { id: sectionIds.data, label: "البيانات" },
  { id: sectionIds.integration, label: "التكامل" },
];
