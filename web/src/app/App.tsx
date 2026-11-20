import { shouldRetry } from "../api/client";
import { lazy, Suspense, useEffect, useState, type ComponentType, type ReactNode } from "react";
import { App as AntApp, ConfigProvider, type ThemeConfig } from "antd";
import type { Locale } from "antd/es/locale";
import arEG from "antd/locale/ar_EG";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { onLangChange, useLang, type Lang } from "../shared/i18n";
import { AppLoading } from "../shared/AppLoading";
import { canOpen, ROUTE_NEEDS } from "./routeAccess";
import { ErrorScreen, NoAccess } from "../shared/QueryState";
import { SessionProvider, usePermissions, useSession } from "../shared/session";
import { SessionExpiredDialog } from "../shared/SessionExpiredDialog";
import { AppearanceProvider } from "../styles/design-system/appearance";

/**
 * بعد نشر إصدار جديد تتغير أسماء ملفات الصفحات، فتفشل الصفحة المفتوحة قبله في تحميل صفحة لم تفتحها بعد.
 * تعاد تحميلها مرة واحدة لتأخذ الإصدار الجديد بدل شاشة معطلة؛ وإن تكرر الفشل يظهر الخطأ.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any -- كل صفحة بخصائصها؛ lazy نفسه يعرف النوع من T.
function load<T extends { default: ComponentType<any> }>(importer: () => Promise<T>) {
  return lazy<T["default"]>(async () => {
    try {
      const m = await importer();
      sessionStorage.removeItem("t1-reloaded");
      return m;
    } catch (e) {
      if (!sessionStorage.getItem("t1-reloaded")) {
        sessionStorage.setItem("t1-reloaded", "1");
        window.location.reload();
        return new Promise<T>(() => undefined);
      }
      throw e;
    }
  });
}

const LoginPage = load(() => import("../features/auth/LoginPage").then((m) => ({ default: m.LoginPage })));
const Shell = load(() => import("./Shell").then((m) => ({ default: m.Shell })));
const HomePage = load(() => import("../features/home/HomePage"));
const StrategyPage = load(() => import("../features/strategy/StrategyPage"));
const PlanDocumentPage = load(() => import("../features/strategy/PlanDocumentPage"));
const IndicatorsPage = load(() => import("../features/indicators/IndicatorsPage"));
const IndicatorPage = load(() => import("../features/indicators/IndicatorPage"));
const ScorecardPage = load(() => import("../features/scorecard/ScorecardPage"));
const StructurePage = load(() => import("../features/structure/StructurePage"));
const CollegeStructurePage = load(() => import("../features/structure/CollegeStructurePage"));
const ReviewPage = load(() => import("../features/review/ReviewPage"));
const ProgressPage = load(() => import("../features/progress/ProgressPage"));
const CyclesPage = load(() => import("../features/cycles/CyclesPage"));
const FormsPage = load(() => import("../features/forms/FormsPage"));
const FormPage = load(() => import("../features/forms/FormPage"));
const ResultsPage = load(() => import("../features/results/ResultsPage"));
const UnitsPage = load(() => import("../features/units/UnitsPage"));
const MatrixPage = load(() => import("../features/matrix/MatrixPage"));
const ChangesPage = load(() => import("../features/matrix/ChangesPage"));
const UsersPage = load(() => import("../features/users/UsersPage"));
const LogsPage = load(() => import("../features/logs/LogsPage"));
const SettingsPage = load(() => import("../features/settings/SettingsPage"));
const MailPage = load(() => import("../features/mail/MailPage"));

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: shouldRetry, refetchOnWindowFocus: false } },
});

// ردود الخادم بلغة الطلب: تغيير اللغة يجعل كل ما في الذاكرة قديمًا فيعاد طلبه.
onLangChange(() => void queryClient.invalidateQueries());

/**
 * خطوط antd كما هي. الألوان والزوايا من مظهر المستخدم (styles/design-system/theme.ts وappearance.tsx):
 * يختار لوحة الألوان والزوايا (حادة أو مستديرة أو ناعمة) من زر المظهر أسفل القائمة الجانبية، ويحفظ اختياره في المتصفح،
 * والافتراضي ACTIVE_THEME وCORNERS. يبني AppearanceProvider سمة antd منهما بـ antdTheme، فيتغير كل شيء فورًا بلا إعادة تحميل.
 */
const antdBase: ThemeConfig = {
  token: {
    fontFamily: '"IBM Plex Sans Arabic", "Segoe UI", Tahoma, Arial, sans-serif',
    fontSize: 14,
  },
};

const pageFallback = <AppLoading />;
const screenFallback = <AppLoading fullScreen />;

/** لغة antd: العربية مضمنة، والإنجليزية تحمل عند اختيارها أول مرة. */
function useAntLocale(lang: Lang): Locale {
  const [en, setEn] = useState<Locale | null>(null);
  useEffect(() => {
    if (lang !== "en" || en) return;
    let live = true;
    import("antd/locale/en_US")
      .then((m) => {
        const loaded = m.default as Locale | { default: Locale };
        if (live) setEn("locale" in loaded ? loaded : loaded.default);
      })
      .catch(() => undefined);
    return () => {
      live = false;
    };
  }, [lang, en]);
  return lang === "en" && en ? en : arEG;
}

/**
 * الصفحة لمن يملك إحدى الصلاحيات المذكورة («خاصية:إمكانية»)؛ وإلا شرح «لا تملك صلاحية» بطريق للعودة.
 * للواجهة فقط: الخادم يرفض الطلب نفسه في كل الأحوال.
 */
function Guard({ need, children }: { need: readonly string[]; children: ReactNode }) {
  const perms = usePermissions();
  return canOpen(perms, need) ? <>{children}</> : <NoAccess />;
}

/** صفحة التعريف العامة لزائر لم يسجل دخوله. */
const LandingPage = load(() => import("../features/landing/LandingPage"));

/** صفحة التعريف العامة تتبع المظهر المحفوظ مثل بقية التطبيق. */
const landingRoute = (
  <Suspense fallback={screenFallback}>
    <LandingPage />
  </Suspense>
);

function Gate() {
  const { me, loading, error, retry } = useSession();
  if (loading) return screenFallback;
  // خطأ الخادم أو الشبكة ليس خروجًا: شاشة إعادة محاولة بدل نموذج الدخول.
  if (error) return <ErrorScreen error={error} onRetry={retry} />;
  // غير المسجل على «/» تحديدًا يرى صفحة التعريف؛ وأي عنوان آخر، وأي دخول لم يكتمل، يعرض نموذج الدخول كما كان.
  if (!me || me.mfaPending || me.mustChangePassword) {
    return (
      <Suspense fallback={screenFallback}>
        <Routes>
          {!me && <Route path="/" element={landingRoute} />}
          <Route path="*" element={<LoginPage />} />
        </Routes>
      </Suspense>
    );
  }
  return (
    <Suspense fallback={screenFallback}>
      <Shell>
        <Suspense fallback={pageFallback}>
          <Routes>
            <Route path="/" element={<HomePage />} />
            <Route path="/strategy" element={<Guard need={ROUTE_NEEDS["/strategy"]}><StrategyPage /></Guard>} />
            <Route path="/strategy/document" element={<Guard need={ROUTE_NEEDS["/strategy/document"]}><PlanDocumentPage /></Guard>} />
            <Route path="/indicators" element={<Guard need={ROUTE_NEEDS["/indicators"]}><IndicatorsPage /></Guard>} />
            <Route path="/indicators/:code" element={<Guard need={ROUTE_NEEDS["/indicators/:code"]}><IndicatorPage /></Guard>} />
            {/* سجل التعارضات أخفي بعد اعتماد الإطار الشامل نهائيًا؛ روابطه القديمة تعود إلى الرئيسية. */}
            <Route path="/scorecard" element={<Guard need={ROUTE_NEEDS["/scorecard"]}><ScorecardPage /></Guard>} />
            <Route path="/structure" element={<Guard need={ROUTE_NEEDS["/structure"]}><StructurePage /></Guard>} />
            <Route path="/structure/:id" element={<Guard need={ROUTE_NEEDS["/structure/:id"]}><CollegeStructurePage /></Guard>} />
            <Route path="/review" element={<Guard need={ROUTE_NEEDS["/review"]}><ReviewPage /></Guard>} />
            <Route path="/progress" element={<Guard need={ROUTE_NEEDS["/progress"]}><ProgressPage /></Guard>} />
            <Route path="/cycles" element={<Guard need={ROUTE_NEEDS["/cycles"]}><CyclesPage /></Guard>} />
            <Route path="/forms" element={<Guard need={ROUTE_NEEDS["/forms"]}><FormsPage /></Guard>} />
            <Route path="/forms/:id" element={<Guard need={ROUTE_NEEDS["/forms/:id"]}><FormPage /></Guard>} />
            <Route path="/results" element={<Guard need={ROUTE_NEEDS["/results"]}><ResultsPage /></Guard>} />
            <Route path="/units" element={<Guard need={ROUTE_NEEDS["/units"]}><UnitsPage /></Guard>} />
            <Route path="/matrix" element={<Guard need={ROUTE_NEEDS["/matrix"]}><MatrixPage /></Guard>} />
            <Route path="/matrix/:roleId" element={<Guard need={ROUTE_NEEDS["/matrix/:roleId"]}><MatrixPage /></Guard>} />
            <Route path="/changes" element={<Guard need={ROUTE_NEEDS["/changes"]}><ChangesPage /></Guard>} />
            <Route path="/users" element={<Guard need={ROUTE_NEEDS["/users"]}><UsersPage /></Guard>} />
            <Route path="/logs" element={<Guard need={ROUTE_NEEDS["/logs"]}><LogsPage /></Guard>} />
            <Route path="/settings" element={<Guard need={ROUTE_NEEDS["/settings"]}><SettingsPage /></Guard>} />
            <Route path="/mail" element={<Guard need={ROUTE_NEEDS["/mail"]}><MailPage /></Guard>} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </Suspense>
        <SessionExpiredDialog />
      </Shell>
    </Suspense>
  );
}

export function App() {
  const lang = useLang();
  const locale = useAntLocale(lang);
  return (
    <AppearanceProvider base={antdBase}>
      <ConfigProvider direction={lang === "ar" ? "rtl" : "ltr"} locale={locale}>
        <AntApp>
          <QueryClientProvider client={queryClient}>
            <BrowserRouter>
              {/* إعادة التركيب عند تغيير اللغة تعيد كل النصوص ونتائج الخادم بها. */}
              <SessionProvider key={lang}>
                <Gate />
              </SessionProvider>
            </BrowserRouter>
          </QueryClientProvider>
        </AntApp>
      </ConfigProvider>
    </AppearanceProvider>
  );
}
