import { Link } from "react-router-dom";
import { Icon } from "./Icon";
import { sectionLinks } from "./sections";
// The official light logo for dark backgrounds.
import logo from "../../assets/brand/capital-university-logo-dark.png";

/** Small gold-ruled label above a footer column. */
function ColumnLabel({ id, children }: { id: string; children: string }) {
  return (
    <p id={id} className="mb-5 flex items-center gap-3 text-xs font-bold tracking-wider text-ds-eyebrow">
      <span aria-hidden className="h-px w-6 bg-current" />
      {children}
    </p>
  );
}

/**
 * Footer, redesigned on request (the rest of the page is a pixel-exact port): brand band with the logo,
 * quick links to the page sections, sign-in, and a copyright row. Facts only from the page and the system.
 */
export function LandingFooter() {
  const year = new Date().getFullYear();

  return (
    <footer data-section="dark" className="landing-footer relative z-10 overflow-hidden border-t border-ds-line-soft/10 px-5 text-ds-fg sm:px-10">
      <div aria-hidden="true" className="landing-footer-orbit pointer-events-none absolute" />
      <div className="relative mx-auto max-w-7xl">
        <div className="grid gap-12 py-14 md:grid-cols-12 md:gap-10 lg:py-16">
          {/* Brand */}
          <div className="md:col-span-12 lg:col-span-5">
            <div className="flex items-center gap-5">
              <img src={logo} alt="جامعة العاصمة" className="h-20 w-auto shrink-0 object-contain" />

              <div className="border-s border-ds-border ps-5">
                <p className="text-lg font-bold leading-8 text-ds-heading">جامعة العاصمة</p>
                <p className="text-xs font-semibold tracking-wider text-ds-eyebrow">CAPITAL UNIVERSITY</p>
              </div>
            </div>

            <p className="mt-6 max-w-md text-sm leading-8 text-ds-body">
              منظومة متكاملة لقياس وتطوير الأداء المؤسسي في جامعة العاصمة، تربط الأهداف
              الاستراتيجية بمؤشرات الأداء والمبادرات.
            </p>
          </div>

          {/* Quick links to the page sections */}
          <nav aria-labelledby="landing-footer-sections" className="md:col-span-7 lg:col-span-4">
            <ColumnLabel id="landing-footer-sections">أقسام الصفحة</ColumnLabel>

            <ul className="grid grid-cols-2 gap-x-8 gap-y-3">
              {sectionLinks.map((link) => (
                <li key={link.id} className="flex">
                  <a
                    href={`#${link.id}`}
                    className="group inline-flex items-center gap-3 rounded-ds py-1 text-sm text-ds-body transition-colors hover:text-ds-heading"
                  >
                    <span aria-hidden className="h-px w-3 bg-ds-accent transition-all group-hover:w-5" />
                    {link.label}
                  </a>
                </li>
              ))}
            </ul>
          </nav>

          {/* Sign-in */}
          <div className="md:col-span-5 lg:col-span-3">
            <ColumnLabel id="landing-footer-system">المنظومة</ColumnLabel>

            <p className="text-sm leading-8 text-ds-body">سجّل دخولك بحساب الجامعة للمتابعة.</p>

            <Link
              to="/login"
              className="group mt-5 inline-flex items-center gap-3 rounded-ds border border-ds-btn-ghost-border px-5 py-3 text-sm font-semibold text-ds-btn-ghost-fg transition-all hover:border-ds-btn-ghost-hover-border hover:bg-ds-btn-ghost-hover-bg hover:text-ds-btn-ghost-hover-fg"
            >
              <span>الدخول إلى المنظومة</span>
              <Icon name="arrow" size={17} className="transition-transform rtl:group-hover:-translate-x-1 ltr:group-hover:translate-x-1" />
            </Link>
          </div>
        </div>

        {/* Copyright */}
        <div className="flex flex-col items-center gap-2 border-t border-ds-line-soft/10 py-6 text-center md:flex-row md:justify-between md:text-start">
          <p className="text-xs leading-6 text-ds-body">© {year} جامعة العاصمة</p>

          <p className="text-xs leading-6 text-ds-body">
            جميع حقوق النشر والملكية الفكرية محفوظة لمركز الاتصالات وتكنولوجيا المعلومات
          </p>
        </div>
      </div>
    </footer>
  );
}
