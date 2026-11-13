import { Icon, type IconName } from "./Icon";
import { SectionHeading } from "./SectionHeading";

interface Pillar {
  number: string;
  title: string;
  description: string;
  icon: IconName;
}

const pillars: Pillar[] = [
  {
    number: "01",
    title: "المنظور الاستراتيجي",
    description:
      "قياس مدى تحقق التوجهات والأهداف الاستراتيجية، ومواءمة الأداء المؤسسي مع رسالة الجامعة ورؤيتها وأولوياتها.",
    icon: "strategy",
  },
  {
    number: "02",
    title: "المستفيدون وأصحاب المصلحة",
    description:
      "تقييم تجربة الطلاب والخريجين ورضا المستفيدين، وفاعلية العلاقة مع سوق العمل والشركاء والمجتمع.",
    icon: "users",
  },
  {
    number: "03",
    title: "المنظور المالي",
    description:
      "متابعة كفاءة إدارة الموارد المالية، وتحسين الاستدامة، وقياس مدى ارتباط الإنفاق بالأولويات والنتائج المستهدفة.",
    icon: "finance",
  },
  {
    number: "04",
    title: "التميز التشغيلي",
    description:
      "قياس كفاءة العمليات والخدمات الجامعية، وتحسين جودة تقديم الخدمة، ورفع مستوى الفاعلية التشغيلية.",
    icon: "operations",
  },
  {
    number: "05",
    title: "الابتكار والتعلم",
    description:
      "متابعة البحث العلمي والابتكار ونقل المعرفة والدراسات العليا، وتنمية القدرات المؤسسية والتعليمية.",
    icon: "innovation",
  },
  {
    number: "06",
    title: "القيادة والموارد والشراكات",
    description:
      "تقييم فاعلية القيادة والحوكمة وتنمية الموارد البشرية والبنية المؤسسية والشراكات الاستراتيجية.",
    icon: "leadership",
  },
];

export function PillarsSection() {
  return (
    <section data-section="dark" className="relative overflow-hidden bg-ds-bg px-5 py-24 text-ds-fg sm:px-10 lg:py-28">
      {/* Background Pattern */}
      <div
        className="pointer-events-none absolute inset-0 opacity-[0.08]"
        style={{ backgroundImage: "var(--ds-texture-dark)", backgroundSize: "48px 48px" }}
      />

      <div className="relative mx-auto max-w-7xl">
        <SectionHeading
          eyebrow="STRATEGIC PILLARS"
          title="ست ركائز"
          highlight="لمنظومة متكاملة"
          description="ستة مناظير استراتيجية مترابطة تغطي مختلف أبعاد الأداء الجامعي، وتوفر إطارًا متوازنًا لتقييم النتائج والقدرات المؤسسية."
        />

        <div className="grid gap-px border border-ds-border bg-ds-border md:grid-cols-2 lg:grid-cols-3">
          {pillars.map((pillar) => (
            <article
              key={pillar.number}
              className="group relative flex min-h-[290px] flex-col bg-ds-surface p-7 transition-colors hover:bg-ds-surface-hover sm:p-8"
            >
              <div className="mb-8 flex items-center justify-between">
                <div className="flex h-12 w-12 items-center justify-center border border-ds-accent-border bg-ds-tile text-ds-icon transition-colors group-hover:bg-ds-tile-active group-hover:text-ds-tile-active-fg">
                  <Icon name={pillar.icon} size={23} />
                </div>

                <span className="text-3xl font-light text-ds-index">{pillar.number}</span>
              </div>

              <h3 className="text-lg font-bold text-ds-heading">{pillar.title}</h3>

              <p className="mt-4 text-sm leading-8 text-ds-body">{pillar.description}</p>

              <div className="mt-auto flex items-center gap-2 pt-6 text-xs font-semibold text-ds-eyebrow">
                <span className="h-px w-6 bg-ds-accent" />
                منظور استراتيجي
              </div>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}
