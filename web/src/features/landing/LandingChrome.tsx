import { Link } from "react-router-dom";
import { Icon } from "./Icon";
import logo from "./assets/capital-university-logo.png";

export function LandingHeader() {
  return (
    <header
      data-section="dark"
      className="relative z-30 shrink-0 border-b border-ds-line-soft/10 bg-ds-chrome/95 px-5 py-3 backdrop-blur-md sm:px-10"
    >
      <div className="mx-auto flex max-w-7xl items-center justify-between gap-6">
        <img src={logo} alt="Capital University" className="h-14 w-auto object-contain sm:h-16" />

        <Link
          to="/login"
          className="inline-flex items-center gap-3 border border-ds-btn-ghost-border px-6 py-3 text-sm font-semibold text-ds-btn-ghost-fg transition-all hover:border-ds-btn-ghost-hover-border hover:bg-ds-btn-ghost-hover-bg hover:text-ds-btn-ghost-hover-fg"
        >
          <span>تسجيل الدخول</span>
          <Icon name="arrow" size={17} />
        </Link>
      </div>
    </header>
  );
}

export function LandingFooter() {
  return (
    <footer data-section="dark" className="relative z-10 border-t border-ds-line-soft/10 bg-ds-chrome px-5 py-6 sm:px-10">
      <div className="mx-auto flex max-w-7xl flex-col items-center justify-between gap-3 text-center md:flex-row md:text-right">
        <p className="text-xs leading-6 text-ds-body">
          جميع حقوق النشر والملكية الفكرية محفوظة لكلية الحاسبات والذكاء
          الاصطناعي ومركز الاتصالات وتكنولوجيا المعلومات
        </p>

        <span className="text-xs font-semibold tracking-wider text-ds-eyebrow">
          CAPITAL UNIVERSITY
        </span>
      </div>
    </footer>
  );
}
