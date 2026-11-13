import { Link } from "react-router-dom";
import { Icon } from "./Icon";

export function CtaSection() {
  return (
    <section className="bg-ds-bg-warm px-5 py-24 sm:px-10 lg:py-28">
      <div className="mx-auto max-w-5xl border border-ds-accent/50 bg-ds-surface px-6 py-14 text-center sm:px-12 sm:py-20">
        <div className="mx-auto mb-6 flex h-14 w-14 items-center justify-center border border-ds-accent/50 bg-ds-tile text-ds-icon">
          <Icon name="target" size={27} />
        </div>

        <span className="text-xs font-bold tracking-widest text-ds-eyebrow">UNIVERSITY PERFORMANCE FRAMEWORK</span>

        <h2 className="mx-auto mt-5 max-w-3xl text-3xl font-bold leading-[1.5] text-ds-heading sm:text-4xl lg:text-5xl">
          نحو منظومة جامعية
          <span className="block text-ds-highlight-display">
            أكثر كفاءة وشفافية
          </span>
        </h2>

        <p className="mx-auto mt-5 max-w-2xl text-sm leading-8 text-ds-body">
          ابدأ رحلة إدارة الأداء المؤسسي من خلال إطار موحد يربط
          التخطيط بالقياس، ويحول البيانات إلى رؤى تدعم التطوير
          المستمر والتميز المؤسسي.
        </p>

        <Link
          to="/login"
          className="group mt-8 inline-flex items-center gap-4 border border-ds-btn-primary-border bg-ds-btn-primary-bg px-8 py-4 text-sm font-bold text-ds-btn-primary-fg transition-all hover:border-ds-btn-primary-hover-border hover:bg-ds-btn-primary-hover-bg hover:text-ds-btn-primary-hover-fg"
        >
          <span>الدخول إلى المنظومة</span>

          <Icon name="arrow" size={19} className="transition-transform rtl:group-hover:-translate-x-1 ltr:group-hover:translate-x-1" />
        </Link>
      </div>
    </section>
  );
}
