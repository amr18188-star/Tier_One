import { DonutChart, PerformanceBarChart, PerformanceLineChart } from "./charts";
import { Icon } from "./Icon";
import { SectionHeading } from "./SectionHeading";

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
    label: "دورات المتابعة",
    value: "20",
    note: "على مدار خمس سنوات",
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
    <section className="bg-ds-bg-cool px-5 py-24 sm:px-10 lg:py-28">
      <div className="mx-auto max-w-7xl">
        <SectionHeading
          eyebrow="PERFORMANCE ANALYTICS"
          title="حوّل البيانات إلى"
          highlight="رؤية واضحة"
          description="لوحات متابعة تفاعلية تعرض مستويات الإنجاز وتساعد على تحليل الفجوات ومقارنة الأداء الفعلي بالمستهدفات، لدعم القرارات المبنية على البيانات."
        />

        {/* Dashboard */}
        <div className="border border-ds-border bg-ds-surface shadow-ds-sm">
          {/* Dashboard Header */}
          <div className="flex flex-col justify-between gap-4 border-b border-ds-border px-6 py-5 sm:flex-row sm:items-center sm:px-8">
            <div>
              <div className="flex items-center gap-2">
                <span className="h-2 w-2 bg-ds-live" />

                <span className="text-xs font-semibold text-ds-heading">PERFORMANCE OVERVIEW</span>
              </div>

              <h3 className="mt-2 text-lg font-bold text-ds-heading">لوحة متابعة الأداء المؤسسي</h3>
            </div>

            <div className="flex items-center gap-2 border border-ds-border px-3 py-2 text-xs text-ds-body">
              <Icon name="calendar" size={15} />
              <span>الخطة الاستراتيجية الحالية</span>
            </div>
          </div>

          {/* KPI Summary */}
          <div className="grid grid-cols-2 border-b border-ds-border lg:grid-cols-4">
            {kpis.map((item, index) => (
              <div
                key={item.label}
                className={`border-ds-border px-5 py-6 ${index % 2 === 0 ? "border-l" : ""} ${
                  index < 2 ? "border-b lg:border-b-0" : ""
                }`}
              >
                <p className="text-xs text-ds-body">{item.label}</p>

                <p className="mt-3 text-3xl font-bold text-ds-heading">{item.value}</p>

                <p className="mt-1 text-[11px] text-ds-eyebrow">{item.note}</p>
              </div>
            ))}
          </div>

          {/* Charts */}
          <div className="grid lg:grid-cols-3">
            {/* Main Chart */}
            <div className="border-b border-ds-border p-6 lg:col-span-2 lg:border-b-0 lg:border-l lg:p-8">
              <div className="mb-8">
                <h4 className="text-base font-bold text-ds-heading">تطور الأداء عبر دورات المتابعة</h4>

                <p className="mt-2 text-xs text-ds-faint">مقارنة الأداء الفعلي بالمستهدفات الدورية</p>
              </div>

              <PerformanceLineChart />

              <p className="mt-6 border-r-2 border-ds-accent bg-ds-callout px-4 py-3 text-[11px] leading-6 text-ds-body">
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
                      <span className="h-2 w-2 shrink-0" style={{ backgroundColor: color }} />

                      <span className="text-[10px] text-ds-body">{label}</span>
                    </div>
                  ))}
                </div>
              </div>

              <div className="flex-1 p-6">
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
