import { Icon, type IconName } from "./Icon";
import { SectionHeading } from "./SectionHeading";
import { sectionIds } from "./sections";

interface Integration {
  code: string;
  title: string;
  subtitle: string;
  description: string;
  icon: IconName;
}

const integrations: Integration[] = [
  {
    code: "PSS",
    title: "بطاقة أداء القطاع العام",
    subtitle: "Public Sector Scorecard",
    description:
      "مواءمة مؤشرات الأداء مع أبعاد قياس كفاءة وفاعلية المؤسسات العامة، وربط النتائج بالأولويات المؤسسية.",
    icon: "building",
  },
  {
    code: "SDGs",
    title: "أهداف التنمية المستدامة",
    subtitle: "Sustainable Development Goals",
    description:
      "ربط المؤشرات ذات الصلة بأهداف التنمية المستدامة، بما يعكس دور الجامعة في تحقيق التنمية الشاملة.",
    icon: "globe",
  },
  {
    code: "ESG",
    title: "معايير الجودة والاعتماد",
    subtitle: "Quality & Accreditation",
    description:
      "دعم مواءمة عمليات القياس والتقييم مع أطر الجودة والاعتماد الأكاديمي، بما في ذلك ESG وQAA.",
    icon: "shield",
  },
];

const goals = [
  { number: "04", title: "التعليم الجيد" },
  { number: "09", title: "الصناعة والابتكار" },
  { number: "16", title: "السلام والعدل والمؤسسات" },
  { number: "17", title: "عقد الشراكات لتحقيق الأهداف" },
];

export function IntegrationSection() {
  return (
    <section id={sectionIds.integration} data-section="dark" className="relative overflow-hidden bg-ds-bg px-5 py-24 text-ds-fg sm:px-10 lg:py-28">
      <div
        className="pointer-events-none absolute inset-0 opacity-[0.07]"
        style={{ backgroundImage: "var(--ds-texture-dark)", backgroundSize: "48px 48px" }}
      />

      <div className="relative mx-auto max-w-7xl">
        <SectionHeading
          eyebrow="STRATEGIC INTEGRATION"
          title="قياس يتجاوز"
          highlight="حدود المؤسسة"
          description="لا يقتصر إطار الأداء على التقييم الداخلي، بل يربط المؤشرات بالأطر الوطنية والدولية، بما يدعم الحوكمة والشفافية والمواءمة الاستراتيجية."
        />

        <div className="grid gap-px border border-ds-border bg-ds-border lg:grid-cols-3">
          {integrations.map((item) => (
            <article key={item.code} className="bg-ds-surface p-7 transition-colors hover:bg-ds-surface-hover sm:p-8">
              <div className="mb-8 flex items-center justify-between">
                <div className="flex h-12 w-12 items-center justify-center border border-ds-accent-border bg-ds-tile text-ds-icon">
                  <Icon name={item.icon} size={23} />
                </div>

                <span className="text-2xl font-bold tracking-widest text-ds-highlight">{item.code}</span>
              </div>

              <h3 className="text-lg font-bold">{item.title}</h3>

              <p className="mt-2 text-xs text-ds-eyebrow">{item.subtitle}</p>

              <p className="mt-5 text-sm leading-8 text-ds-body">{item.description}</p>
            </article>
          ))}
        </div>

        {/* SDG Goals */}
        <div className="mt-12">
          <div className="mb-6 flex flex-col justify-between gap-3 sm:flex-row sm:items-center">
            <h3 className="text-lg font-bold">أمثلة على مواءمة المؤشرات مع أهداف التنمية المستدامة</h3>

            <span className="text-xs text-ds-body">UNITED NATIONS SDGs</span>
          </div>

          <div className="grid grid-cols-2 gap-px border border-ds-border bg-ds-border lg:grid-cols-4">
            {goals.map((goal) => (
              <div key={goal.number} className="flex items-center gap-4 bg-ds-surface p-5 transition-colors hover:bg-ds-surface-hover">
                <span className="text-3xl font-bold text-ds-highlight">{goal.number}</span>

                <p className="text-xs leading-6 text-ds-body-strong">{goal.title}</p>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
