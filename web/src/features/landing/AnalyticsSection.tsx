import { DonutChart, PerformanceBarChart, PerformanceLineChart } from "./charts";
import { Icon } from "./Icon";
import { SectionHeading } from "./SectionHeading";
import { sectionIds } from "./sections";

const kpis = [
  {
    label: "إجمالي المؤشرات",
    value: "92",
    note: "مؤشرات ومقاييس رأي",
  },
  {
    label: "الركائز الاستراتيجية",
    value: "06",
    note: "مناظير تقييم",
  },
  {
    label: "المعايير الرئيسية",
    value: "12",
    note: "معيارًا مؤسسيًا",
  },
  {
    label: "الدورات الربعية",
    value: "20",
    note: "ربعًا في الخطة الخمسية",
  },
];

/** Legend of the donut chart: label and series colour. */
const donutLegend: [label: string, color: string][] = [
  ["ركائز استراتيجية", "var(--ds-chart-1)"],
  ["مستفيدون", "var(--ds-chart-2)"],
  ["تشغيلية", "var(--ds-chart-3)"],
  ["أخرى", "var(--ds-chart-4)"],
];

export function AnalyticsSection() {
  return (
    <section id={sectionIds.analytics} className="bg-ds-bg-cool px-5 py-24 sm:px-10 lg:py-28">
      <div className="mx-auto max-w-7xl">
        <SectionHeading
          eyebrow="PERFORMANCE ANALYTICS"
          title="حوّل البيانات إلى"
          highlight="رؤية واضحة"
          description="لوحات متابعة تفاعلية تعرض مستويات الإنجاز وتساعد على تحليل الفجوات ومقارنة الأداء الفعلي بالمستهدفات، لدعم القرارات المبنية على البيانات."
        />

        {/* Dashboard */}
        <div className="rounded-ds-lg border border-ds-border bg-ds-surface shadow-ds-sm">
          {/* Dashboard Header */}
          <div className="flex flex-col justify-between gap-4 border-b border-ds-border px-6 py-5 sm:flex-row sm:items-center sm:px-8">
            <div>
              <div className="flex items-center gap-2">
                <span className="h-2 w-2 rounded-ds-pill bg-ds-live" />

                <span className="text-xs font-semibold text-ds-heading">PERFORMANCE OVERVIEW</span>
              </div>

              <h3 className="mt-2 text-lg font-bold text-ds-heading">لوحة متابعة الأداء المؤسسي</h3>
            </div>

            <div className="flex items-center gap-2 rounded-ds-pill border border-ds-border px-3 py-2 text-xs text-ds-body">
              <Icon name="calendar" size={15} />
              <span>الخطة الاستراتيجية الحالية</span>
            </div>
          </div>

          {/* KPI Summary: compact rows (label and note at the start, number at the end), hairline grid */}
          <div className="grid grid-cols-2 gap-px border-b border-ds-border bg-ds-border lg:grid-cols-4">
            {kpis.map((item) => (
              <div key={item.label} className="flex items-center justify-between gap-3 bg-ds-surface px-4 py-4 sm:px-5">
                <div className="min-w-0 text-start">
                  <p className="text-xs text-ds-body">{item.label}</p>

                  <p className="mt-1 text-[11px] text-ds-eyebrow">{item.note}</p>
                </div>

                <p className="shrink-0 text-end text-2xl font-bold text-ds-heading sm:text-3xl">{item.value}</p>
              </div>
            ))}
          </div>

          {/* Charts */}
          <div className="grid lg:grid-cols-3">
            {/* Main Chart */}
            <div className="border-b border-ds-border p-6 lg:col-span-2 lg:border-b-0 lg:border-e lg:p-8">
              <div className="mb-8">
                <h4 className="text-base font-bold text-ds-heading">تطور الأداء عبر الدورات الربعية</h4>

                <p className="mt-2 text-xs text-ds-faint">مقارنة القيم الربعية الفعلية بالمستهدفات</p>
              </div>

              <PerformanceLineChart />

              <p className="mt-6 rounded-ds-sm border-s-2 border-ds-accent bg-ds-callout px-4 py-3 text-[11px] leading-6 text-ds-body">
                رسم توضيحي لشكل التحليل الزمني. تُعرض النتائج الفعلية
                عند ربط المنظومة ببيانات الأداء المعتمدة.
              </p>
            </div>

            {/* Side Charts */}
            <div className="flex flex-col">
              <div className="border-b border-ds-border p-6">
                <h4 className="text-sm font-bold text-ds-heading">توزيع المؤشرات</h4>

                <p className="mt-2 text-xs text-ds-faint">توزيع توضيحي لفئات المؤشرات</p>

                <div className="mt-5">
                  <DonutChart />
                </div>

                <div className="mt-5 grid grid-cols-2 gap-3">
                  {donutLegend.map(([label, color]) => (
                    <div key={label} className="flex items-center gap-2">
                      <span className="h-2 w-2 shrink-0 rounded-ds-xs" style={{ backgroundColor: color }} />

                      <span className="text-[10px] text-ds-body">{label}</span>
                    </div>
                  ))}
                </div>
              </div>

              {/* A flex column, so the bars fill only the space under the heading (h-full overflowed it by the heading's height). */}
              <div className="flex flex-1 flex-col p-6">
                <h4 className="text-sm font-bold text-ds-heading">الأداء حسب الركائز</h4>

                <p className="mb-6 mt-2 text-xs text-ds-faint">نموذج لعرض مستويات الأداء</p>

                <PerformanceBarChart />
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
