import { lazy, Suspense, useEffect, useState, type ComponentType } from "react";
import { App as AntApp, ConfigProvider, Spin } from "antd";
import type { Locale } from "antd/es/locale";
import arEG from "antd/locale/ar_EG";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { onLangChange, useLang, type Lang } from "../shared/i18n";
import { ErrorScreen } from "../shared/QueryState";
import { SessionProvider, useSession } from "../shared/session";

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
const IndicatorsPage = load(() => import("../features/indicators/IndicatorsPage"));
const IndicatorPage = load(() => import("../features/indicators/IndicatorPage"));
const ConflictsPage = load(() => import("../features/conflicts/ConflictsPage"));
const AgendaPage = load(() => import("../features/conflicts/AgendaPage"));
const StructurePage = load(() => import("../features/structure/StructurePage"));
const CollegeStructurePage = load(() => import("../features/structure/CollegeStructurePage"));
const UnitsPage = load(() => import("../features/units/UnitsPage"));
const MatrixPage = load(() => import("../features/matrix/MatrixPage"));
const ChangesPage = load(() => import("../features/matrix/ChangesPage"));
const UsersPage = load(() => import("../features/users/UsersPage"));
const LogsPage = load(() => import("../features/logs/LogsPage"));
const SettingsPage = load(() => import("../features/settings/SettingsPage"));

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
});

// ردود الخادم بلغة الطلب: تغيير اللغة يجعل كل ما في الذاكرة قديمًا فيعاد طلبه.
onLangChange(() => void queryClient.invalidateQueries());

/** ألوان هوية الجامعة: الكحلي لهيكل الواجهة، والتركوازي للإجراءات. */
const theme = {
  token: {
    colorPrimary: "#0A8C93",
    colorInfo: "#0A647F",
    colorInfoBg: "#E8F6F6",
    colorInfoBorder: "#C7E8E9",
    colorLink: "#076B71",
    colorText: "#15202F",
    colorBgLayout: "#F4F6F9",
    borderRadius: 8,
    fontFamily: '"IBM Plex Sans Arabic", "Segoe UI", Tahoma, Arial, sans-serif',
    fontSize: 14,
  },
  components: {
    Table: {
      headerBg: "#0A8C93",
      headerColor: "#FFFFFF",
      headerSortActiveBg: "#087D83",
      headerSortHoverBg: "#087D83",
      fixedHeaderSortActiveBg: "#087D83",
      headerFilterHoverBg: "rgba(255, 255, 255, 0.12)",
      headerSplitColor: "rgba(255, 255, 255, 0.22)",
      rowHoverBg: "#E8F6F6",
      headerBorderRadius: 0,
    },
  },
};

const pageFallback = <div className="center-screen"><Spin /></div>;

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

function Gate() {
  const { me, loading, error, retry } = useSession();
  if (loading) return <div className="center-screen"><Spin size="large" /></div>;
  // خطأ الخادم أو الشبكة ليس خروجًا: شاشة إعادة محاولة بدل نموذج الدخول.
  if (error) return <ErrorScreen error={error} onRetry={retry} />;
  if (!me || me.mfaPending || me.mustChangePassword) return <Suspense fallback={pageFallback}><LoginPage /></Suspense>;
  return (
    <Suspense fallback={<div className="center-screen"><Spin size="large" /></div>}>
      <Shell>
        <Suspense fallback={pageFallback}>
          <Routes>
            <Route path="/" element={<HomePage />} />
            <Route path="/indicators" element={<IndicatorsPage />} />
            <Route path="/indicators/:code" element={<IndicatorPage />} />
            <Route path="/conflicts" element={<ConflictsPage />} />
            <Route path="/conflicts/agenda" element={<AgendaPage />} />
            <Route path="/structure" element={<StructurePage />} />
            <Route path="/structure/:id" element={<CollegeStructurePage />} />
            <Route path="/units" element={<UnitsPage />} />
            <Route path="/matrix" element={<MatrixPage />} />
            <Route path="/matrix/:roleId" element={<MatrixPage />} />
            <Route path="/changes" element={<ChangesPage />} />
            <Route path="/users" element={<UsersPage />} />
            <Route path="/logs" element={<LogsPage />} />
            <Route path="/settings" element={<SettingsPage />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </Suspense>
      </Shell>
    </Suspense>
  );
}

export function App() {
  const lang = useLang();
  const locale = useAntLocale(lang);
  return (
    <ConfigProvider direction={lang === "ar" ? "rtl" : "ltr"} locale={locale} theme={theme}>
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
  );
}
