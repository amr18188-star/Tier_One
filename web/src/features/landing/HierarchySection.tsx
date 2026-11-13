import { Icon, type IconName } from "./Icon";
import { SectionHeading } from "./SectionHeading";

interface Level {
  number: string;
  title: string;
  subtitle: string;
  description: string;
  icon: IconName;
  /** Card width as a share of the column: the stack narrows from plan to indicators. */
  width: string;
}

const levels: Level[] = [
  {
    number: "01",
    title: "الخطة الاستراتيجية",
    subtitle: "Performance Plan",
    description: "الإطار السنوي الذي يحدد المستهدفات والأولويات الاستراتيجية.",
    icon: "target",
    width: "100%",
  },
  {
    number: "02",
    title: "دورات المتابعة",
    subtitle: "Seasons",
    description: "عشرون دورة قياس ومتابعة موزعة على الخطة الخمسية.",
    icon: "calendar",
    width: "90%",
  },
  {
    number: "03",
    title: "الركائز الاستراتيجية",
    subtitle: "Pillars",
    description: "ستة مناظير رئيسية تغطي مختلف أبعاد الأداء الجامعي.",
    icon: "layers",
    width: "80%",
  },
  {
    number: "04",
    title: "المعايير الرئيسية",
    subtitle: "Criteria",
    description: "اثنا عشر معيارًا لترجمة الركائز إلى مجالات تقييم محددة.",
    icon: "chart",
    width: "70%",
  },
  {
    number: "05",
    title: "المؤشرات والمبادرات",
    subtitle: "Indicators & Initiatives",
    description: "مقاييس ومبادرات مرتبطة بالأهداف، لمتابعة الإنجاز وقياس الأثر.",
    icon: "database",
    width: "60%",
  },
];

export function HierarchySection() {
  return (
    <section className="bg-ds-bg-warm px-5 py-24 sm:px-10 lg:py-28">
      <div className="mx-auto max-w-7xl">
        <SectionHeading
          eyebrow="FRAMEWORK HIERARCHY"
          title="هيكلية مترابطة"
          highlight="من الهدف إلى الأثر"
          description="تسلسل مؤسسي واضح ينظم التخطيط والقياس والمتابعة، ويضمن ارتباط كل نتيجة بمستواها الاستراتيجي داخل الخطة."
        />

        <div className="grid items-center gap-14 lg:grid-cols-2">
          {/* Hierarchy */}
          <div className="flex flex-col items-center">
            {levels.map((level, index) => (
              <div key={level.number} className="w-full" style={{ maxWidth: level.width }}>
                <div className="group flex items-center gap-4 border border-ds-accent/50 bg-ds-surface px-5 py-5 shadow-ds-sm transition-all hover:border-ds-accent-strong hover:shadow-ds-md sm:px-6">
                  <div className="flex h-12 w-12 shrink-0 items-center justify-center bg-ds-tile-solid text-ds-tile-solid-fg">
                    <Icon name={level.icon} size={22} />
                  </div>

                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <h3 className="text-sm font-bold text-ds-heading sm:text-base">{level.title}</h3>

                      <span className="text-[10px] text-ds-eyebrow">{level.subtitle}</span>
                    </div>

                    <p className="mt-2 text-xs leading-6 text-ds-body">{level.description}</p>
                  </div>

                  <span className="text-xl font-light text-ds-accent">{level.number}</span>
                </div>

                {index !== levels.length - 1 && (
                  <div className="flex h-7 items-center justify-center">
                    <div className="h-full w-px bg-ds-accent-strong" />
                  </div>
                )}
              </div>
            ))}
          </div>

          {/* Explanation */}
          <div>
            <div className="mb-8 border-r-4 border-ds-accent-strong bg-ds-surface p-6 sm:p-8">
              <span className="text-xs font-bold tracking-wider text-ds-eyebrow">STRUCTURED PLANNING</span>

              <h3 className="mt-4 text-2xl font-bold leading-relaxed text-ds-heading">
                كل مؤشر له سياق،
                <br />
                وكل نتيجة لها هدف.
              </h3>

              <p className="mt-5 text-sm leading-8 text-ds-body">
                يضمن هذا التسلسل وضوح العلاقة بين الأهداف الاستراتيجية
                والمستهدفات السنوية ودورات المتابعة ومجالات التقييم،
                وصولًا إلى المؤشرات والمبادرات التي تقيس النتائج
                وتدعم تحسين الأداء.
              </p>
            </div>

            <div className="grid grid-cols-2 gap-px border border-ds-accent/50 bg-ds-accent/50">
              <div className="bg-ds-surface-warm p-5">
                <p className="text-3xl font-bold text-ds-heading">12</p>

                <p className="mt-2 text-xs text-ds-body">معيارًا رئيسيًا</p>
              </div>

              <div className="bg-ds-surface-warm p-5">
                <p className="text-3xl font-bold text-ds-heading">92</p>

                <p className="mt-2 text-xs text-ds-body">مؤشرًا ومقياسًا</p>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
