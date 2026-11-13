import { Icon, type IconName } from "./Icon";
import { SectionHeading } from "./SectionHeading";
import { sectionIds } from "./sections";

interface Stat {
  value: string;
  suffix: string;
  label: string;
  icon: IconName;
}

const stats: Stat[] = [
  {
    value: "5",
    suffix: "سنوات",
    label: "مدة الخطة الاستراتيجية",
    icon: "calendar",
  },
  {
    value: "20",
    suffix: "ربعًا",
    label: "دورات قياس ومتابعة ربع سنوية",
    icon: "chart",
  },
  {
    value: "6",
    suffix: "ركائز",
    label: "مناظير التقييم الاستراتيجي",
    icon: "layers",
  },
  {
    value: "92",
    suffix: "مؤشرًا",
    label: "مؤشرات الأداء ومقاييس الرأي",
    icon: "target",
  },
];

export function OverviewSection() {
  return (
    <section id={sectionIds.framework} className="bg-ds-bg px-5 py-24 text-ds-fg sm:px-10 lg:py-28">
      <div className="mx-auto max-w-7xl">
        <SectionHeading
          eyebrow="THE FRAMEWORK"
          title="رؤية موحدة"
          highlight="للأداء المؤسسي"
          description="إطار قياس استراتيجي يترجم التوجهات المؤسسية إلى أهداف قابلة للقياس، ومؤشرات واضحة، ومبادرات يمكن متابعة أثرها على مستوى الجامعة وكلياتها."
        />

        {/* Stats */}
        <div className="grid grid-cols-1 border border-ds-border sm:grid-cols-2 lg:grid-cols-4">
          {stats.map((stat, index) => (
            <div
              key={stat.label}
              className={`group flex flex-col items-center px-6 py-10 text-center transition-colors hover:bg-ds-surface-hover ${
                index !== 3 ? "border-b border-ds-border lg:border-b-0" : ""
              } ${index % 2 === 0 ? "sm:border-l sm:border-ds-border" : ""} ${
                index !== 0 ? "lg:border-r lg:border-ds-border" : ""
              }`}
            >
              <div className="mb-5 flex h-12 w-12 items-center justify-center border border-ds-accent-border bg-ds-tile text-ds-icon transition-colors group-hover:bg-ds-tile-active group-hover:text-ds-tile-active-fg">
                <Icon name={stat.icon} size={23} />
              </div>

              <div className="flex items-baseline gap-2">
                <span className="text-5xl font-bold tracking-tight text-ds-heading">{stat.value}</span>

                <span className="text-sm font-semibold text-ds-eyebrow">{stat.suffix}</span>
              </div>

              <p className="mt-3 text-sm text-ds-body">{stat.label}</p>
            </div>
          ))}
        </div>

        {/* Bottom Info */}
        <div className="mt-8 grid gap-6 border border-ds-border bg-ds-bg-cool p-6 md:grid-cols-2 md:items-center md:p-8">
          <div>
            <span className="text-xs font-bold tracking-wider text-ds-eyebrow">STRATEGIC PLANNING</span>

            <h3 className="mt-3 text-xl font-bold text-ds-heading">من التخطيط إلى قياس الأثر</h3>
          </div>

          <p className="text-sm leading-8 text-ds-body">
            يربط الإطار بين الأهداف الاستراتيجية والمستهدفات السنوية
            والنتائج الفعلية، ليقدم صورة متكاملة عن مستوى الإنجاز
            ويساعد القيادات على تحديد الأولويات واتخاذ القرارات.
          </p>
        </div>
      </div>
    </section>
  );
}
