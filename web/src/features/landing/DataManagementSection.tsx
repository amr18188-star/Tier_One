import { Icon, type IconName } from "./Icon";
import { SectionHeading } from "./SectionHeading";
import { sectionIds } from "./sections";

interface Feature {
  number: string;
  title: string;
  subtitle: string;
  description: string;
  icon: IconName;
}

const features: Feature[] = [
  {
    number: "01",
    title: "مستهدفات مركزية",
    subtitle: "Centralized Targets",
    description:
      "تُحدد المستهدفات على مستوى الخطة السنوية، بما يضمن توحيد المرجعية وعدم تكرار المستهدفات بشكل مستقل لكل كلية.",
    icon: "target",
  },
  {
    number: "02",
    title: "إدخال ربع سنوي للبيانات",
    subtitle: "Quarterly Reporting",
    description:
      "تُسجل النتائج الفعلية في كل دورة ربعية، وفق نطاق إدخال محدد لكل مؤشر على مستوى الجامعة أو الكلية أو كليهما.",
    icon: "calendar",
  },
  {
    number: "03",
    title: "احتساب المؤشرات النسبية",
    subtitle: "Automatic Projection",
    description:
      "تعتمد المؤشرات النسبية على إدخال البسط والمقام بشكل منفصل، ثم احتساب النسبة تلقائيًا لضمان دقة النتائج.",
    icon: "chart",
  },
  {
    number: "04",
    title: "متابعة تراكمية أو ربعية",
    subtitle: "Cumulative & Quarterly",
    description:
      "تدعم المنظومة المؤشرات التراكمية التي تجمع قيم الأرباع، والمؤشرات غير التراكمية التي تقيس أداء كل ربع بصورة مستقلة.",
    icon: "database",
  },
];

export function DataManagementSection() {
  return (
    <section id={sectionIds.data} className="bg-ds-bg px-5 py-24 sm:px-10 lg:py-28">
      <div className="mx-auto max-w-7xl">
        <SectionHeading
          eyebrow="DATA GOVERNANCE"
          title="بيانات منظمة"
          highlight="وقياس أكثر دقة"
          description="قواعد واضحة لإدارة المستهدفات وتسجيل النتائج واحتساب المؤشرات، بما يعزز موثوقية البيانات وقابلية المقارنة بين مستويات الجامعة."
        />

        <div className="grid gap-6 lg:grid-cols-2">
          {/* Features */}
          <div className="grid gap-4">
            {features.map((feature) => (
              <article
                key={feature.number}
                className="group flex gap-5 rounded-ds-lg border border-ds-border p-5 transition-colors hover:border-ds-accent hover:bg-ds-surface-hover-soft sm:p-6"
              >
                <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-ds-md border border-ds-accent-border bg-ds-tile text-ds-icon">
                  <Icon name={feature.icon} size={22} />
                </div>

                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <h3 className="text-base font-bold text-ds-heading">{feature.title}</h3>

                    <span className="text-[10px] text-ds-eyebrow">{feature.subtitle}</span>
                  </div>

                  <p className="mt-3 text-xs leading-7 text-ds-body sm:text-sm">{feature.description}</p>
                </div>

                <span className="text-sm font-semibold text-ds-index">{feature.number}</span>
              </article>
            ))}
          </div>

          {/* Ratio Calculation Card */}
          <div data-section="dark" className="flex flex-col justify-between rounded-ds-lg border border-ds-border bg-ds-bg p-6 text-ds-fg sm:p-8">
            <div>
              <div className="flex items-center justify-between gap-4">
                <div>
                  <span className="text-xs font-bold tracking-wider text-ds-eyebrow">INDICATOR CALCULATION</span>

                  <h3 className="mt-3 text-xl font-bold">احتساب المؤشرات النسبية</h3>
                </div>

                <div className="flex h-12 w-12 items-center justify-center rounded-ds-md border border-ds-accent-border bg-ds-tile text-ds-icon">
                  <Icon name="chart" size={23} />
                </div>
              </div>

              <p className="mt-4 text-sm leading-7 text-ds-body">
                يتم إدخال مكونات المؤشر بصورة منفصلة، ثم تتولى المنظومة
                احتساب النتيجة النهائية تلقائيًا.
              </p>
            </div>

            {/* Formula */}
            <div className="my-10 rounded-ds-md border border-ds-border bg-ds-surface p-5 sm:p-7">
              <div className="flex flex-col items-center gap-5">
                <div className="w-full rounded-ds-sm border border-ds-border bg-ds-bg p-4 text-center">
                  <span className="text-xs text-ds-body">البسط</span>

                  <p className="mt-2 text-lg font-bold text-ds-heading">Numerator</p>
                </div>

                <div className="h-px w-16 bg-ds-accent" />

                <div className="w-full rounded-ds-sm border border-ds-border bg-ds-bg p-4 text-center">
                  <span className="text-xs text-ds-body">المقام</span>

                  <p className="mt-2 text-lg font-bold text-ds-heading">Denominator</p>
                </div>

                <div className="flex w-full items-center justify-center gap-3 rounded-ds-sm border border-ds-accent/40 bg-ds-accent/10 px-4 py-4 text-ds-highlight">
                  <Icon name="check" size={20} />

                  <span className="text-sm font-bold">النسبة المحسوبة تلقائيًا</span>
                </div>
              </div>
            </div>

            <div className="flex items-start gap-3 border-r-2 border-ds-accent pr-4">
              <Icon name="shield" size={19} className="mt-1 shrink-0 text-ds-icon" />

              <p className="text-xs leading-7 text-ds-body">
                توحيد قواعد الاحتساب يحد من اختلاف طرق القياس، ويعزز
                اتساق النتائج وموثوقية التقارير المؤسسية.
              </p>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
