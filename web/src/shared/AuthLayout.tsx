import type { ReactNode } from "react";
import { Button } from "antd";
import { CheckOutlined } from "@ant-design/icons";
import { DevRoleTabs } from "./DevRoleTabs";
import { L, setLang, useLang } from "./i18n";
// شعار الجامعة الرسمي؛ «-dark» حروفه بيضاء للوحة الكحلية. شعار واحد في الشاشة، فلا علامة للمنظومة فوق النموذج.
import uniLogo from "../assets/brand/capital-university-logo-dark.png";
import "./auth-logo.css";

/** أعمدة متصاعدة بخط رفيع، زخرفة خلف اللوحة الداكنة. */
function GrowthDecor() {
  return (
    <svg className="auth-decor" viewBox="0 0 600 600" aria-hidden focusable="false">
      <path d="M90 480V360h70v120zM220 480V280h70v200zM350 480V200h70v280zM480 480V120h70v360z" />
      <path d="M80 300 L250 190 L360 240 L540 90" fill="none" />
    </svg>
  );
}

/**
 * إطار الدخول: لوحة الهوية (الجامعة والمنظومة وما تقدمه) ونموذج الدخول.
 * على الشاشات الضيقة تصغر اللوحة إلى شريط علوي وتختفي القائمة.
 */
export function AuthLayout({ wide = false, title, subtitle, children }: { wide?: boolean; title?: string; subtitle?: ReactNode; children: ReactNode }) {
  const lang = useLang();
  const features = [
    L("قاموس المؤشرات ومستهدفات 2027–2031", "Indicator dictionary and 2027–2031 targets"),
    L("توزيع المستهدفات على الكليات", "Targets distributed to colleges"),
    L("قرارات اللجنة العليا في التعارضات", "Higher committee decisions on conflicts"),
    L("هيكل Tier 1 بكل كلية وأعضاؤه", "Each college's Tier 1 structure and members"),
  ];
  const copyright = L(`جميع الحقوق محفوظة — جامعة العاصمة © ${new Date().getFullYear()}`, `© ${new Date().getFullYear()} Capital University`);
  return (
    <div className={wide ? "auth-page auth-page-wide" : "auth-page"}>
      <aside className="auth-side">
        <GrowthDecor />
        <div className="auth-side-inner">
          <div className="auth-uni">
            <img src={uniLogo} alt={L("جامعة العاصمة", "Capital University")} />
            <span>
              <b>{L("جامعة العاصمة", "Capital University")}</b>
              <small>{L("نموذج الجامعات المصرية الرائدة", "Leading Egyptian Universities model")}</small>
            </span>
          </div>
          <div className="auth-pitch">
            <h1>{L("منظومة الأداء الاستراتيجي Tier 1", "Tier 1 strategic performance system")}</h1>
            <p>{L("من المؤشر إلى القرار: تعريفات معتمدة، ومستهدفات موزعة، ومتابعة واضحة لكل جهة.",
              "From indicator to decision: approved definitions, distributed targets and clear follow-up for every unit.")}</p>
          </div>
          <ul className="auth-features">
            {features.map((f) => <li key={f}><CheckOutlined aria-hidden />{f}</li>)}
          </ul>
        </div>
        <p className="auth-copy">{copyright}</p>
      </aside>
      <main className="auth-main">
        <div className="auth-topbar">
          <span><DevRoleTabs /></span>
          <Button type="link" onClick={() => setLang(lang === "ar" ? "en" : "ar")}>{lang === "ar" ? "English" : "العربية"}</Button>
        </div>
        <div className="auth-card">
          {title ? (
            <header className="auth-head">
              <div>
                <h2>{title}</h2>
                {subtitle && <p>{subtitle}</p>}
              </div>
            </header>
          ) : null}
          {children}
        </div>
        <p className="auth-copy auth-copy-main">{copyright}</p>
      </main>
    </div>
  );
}
