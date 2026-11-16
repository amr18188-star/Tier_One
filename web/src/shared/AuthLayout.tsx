import type { ReactNode } from "react";
import { Button, Tooltip } from "antd";
import { GlobalOutlined } from "@ant-design/icons";
import { L, setLang, useLang } from "./i18n";
import { useDsTheme } from "../styles/design-system/theme";
import { AuthShowcase } from "./AuthShowcase";
import brandHorizontal from "../assets/brand/brand-horizontal-light.png";
import brandMark from "../assets/brand/brand-mark-color.png";
import brandOnDark from "../assets/brand/strategy-performance-dark.png";
import "./auth-split.css";

/**
 * إطار الدخول بتقسيم نصفين: النموذج على سطح فاتح عند بداية السطر،
 * ولوحة عرض كحلية بحافة منحنية عند نهايته (تنقلب تلقائيًا في الإنجليزية).
 * تحت 1024px تتحول اللوحة إلى شريط علوي صغير.
 */
export function AuthLayout({ title, subtitle, children }: { title?: string; subtitle?: ReactNode; children: ReactNode }) {
  const lang = useLang();
  const theme = useDsTheme();
  const copyright = L(`جميع الحقوق محفوظة — جامعة العاصمة © ${new Date().getFullYear()}`, `© ${new Date().getFullYear()} Capital University`);
  return (
    <div className="lgn-page">
      <main className="lgn-form">
        <div className="lgn-topbar">
          <img
            className="lgn-brand"
            src={theme === "night-gold" ? brandOnDark : brandHorizontal}
            alt={L("الخطة الاستراتيجية وتقييم الأداء — جامعة العاصمة", "Strategy and Performance — Capital University")}
          />
          <Tooltip title={lang === "ar" ? "English" : "العربية"}>
            <Button
              type="text"
              icon={<GlobalOutlined />}
              aria-label={lang === "ar" ? "Switch language to English" : "تغيير اللغة إلى العربية"}
              onClick={() => setLang(lang === "ar" ? "en" : "ar")}
            />
          </Tooltip>
        </div>
        <div className="lgn-card">
          <img className="lgn-mark" src={brandMark} alt="" aria-hidden />
          {title ? (
            <header className="lgn-head">
              <h2>{title}</h2>
              {subtitle && <p>{subtitle}</p>}
            </header>
          ) : null}
          {children}
        </div>
        <p className="lgn-copy-note">{copyright}</p>
      </main>
      <AuthShowcase />
    </div>
  );
}
