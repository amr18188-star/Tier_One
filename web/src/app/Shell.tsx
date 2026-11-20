import { useCallback, useEffect, useId, useRef, useState, type ReactNode } from "react";
import { Button, Drawer, Dropdown, Tooltip } from "antd";
import {
  AimOutlined, ApartmentOutlined, AuditOutlined, BankOutlined, BarChartOutlined, BranchesOutlined, CalendarOutlined, ClusterOutlined, ControlOutlined, DatabaseOutlined, DoubleLeftOutlined, DoubleRightOutlined, FileDoneOutlined, FilePdfOutlined, FormOutlined, HomeOutlined, LogoutOutlined, MailOutlined, MenuOutlined, PartitionOutlined, RiseOutlined, SafetyCertificateOutlined, SettingOutlined, TeamOutlined, TrophyOutlined, UnorderedListOutlined,
} from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { Link, NavLink, matchPath, useLocation } from "react-router-dom";
import { api } from "../api/client";
import type { RbacChange } from "../api/types";
import { DevRoleTabs } from "../shared/DevRoleTabs";
import { L, setLang, useLang } from "../shared/i18n";
import { prefersReducedMotion } from "../shared/entrance";
import { usePermissions, useSession } from "../shared/session";
import { initials } from "../shared/ui";
import { AppearanceButton } from "./AppearanceMenu";
import { NotificationBell } from "../features/notifications/NotificationBell";
import { canOpen, ROUTE_NEEDS } from "./routeAccess";
import logoOnWhite from "../assets/brand/capital-university-logo-light.png";
import strategyBrandOnNavy from "../assets/brand/strategy-performance-dark.png";
import sidebarLogo from "../assets/brand/sidebarLogo.png";
import { GlobalSearch } from "./GlobalSearch";
import { RailTip, SideNav } from "./SideNav";
import { NavGroup, NavItem, RAIL_MS, TipSide, VisibleGroup, readNavOpen, readRail, writeNavOpen, writeRail } from "./navState";
import "../shared/ui.css";
import "../shared/tier1.css";
import "../shared/theme.css";
import "../shared/layout.css";
import "./nav.css";
import "./entrance.css";

/**
 * إطار المنظومة: قائمة جانبية داكنة مقسمة بحسب الصلاحيات (لا يرى المستخدم ما لا يملكه)، وبحث موحد أعلى كل صفحة
 * بكود المؤشر أو اسمه أو اسم الكلية، وشريط تبويب سفلي على الجوال.
 */
export function Shell({ children }: { children: ReactNode }) {
  const { me } = useSession();
  const perms = usePermissions();
  const lang = useLang();
  const [drawer, setDrawer] = useState(false);

  // حركة الشريطين الجانبي والعلوي: عند تحميل الصفحة كاملة أو بعد الدخول (Shell يركب حينها فقط)، للمسؤول على «/»؛ لا تعاد عند التنقل الداخلي.
  // محتوى الصفحة الرئيسية يتحرك عند كل تركيب بنفسه (entrance.css).
  const [entering, setEntering] = useState(() => perms.isSuperAdmin && !prefersReducedMotion() && ["/", "/login"].includes(window.location.pathname)); // «/login» يحول إلى «/» فور التركيب
  useEffect(() => {
    if (!entering) return;
    const id = window.setTimeout(() => setEntering(false), 2400);
    return () => window.clearTimeout(id);
  }, [entering]);

  // الشريط المطوي. «railing» طوال تغير العرض فقط: النصوص فيه سطر واحد يتلاشى (nav.css)؛ بلا حركة (تقليل الحركة) لا حاجة إليه.
  const sideId = useId();
  const [rail, setRail] = useState(readRail);
  const [railing, setRailing] = useState(false);
  const [railTip, setRailTip] = useState(false);
  const railTimer = useRef<number | undefined>(undefined);
  useEffect(() => writeRail(rail), [rail]);
  useEffect(() => () => window.clearTimeout(railTimer.current), []);
  function toggleRail() {
    // الزر ينتقل مكانه بعد الضغط؛ يغلق تلميحه كي لا يبقى معلقًا في المكان القديم.
    setRailTip(false);
    setRail((on) => !on);
    if (window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) return;
    setRailing(true);
    window.clearTimeout(railTimer.current);
    railTimer.current = window.setTimeout(() => setRailing(false), RAIL_MS);
  }

  const canMatrix = canOpen(perms, ROUTE_NEEDS["/matrix"]);
  const pending = useQuery({
    queryKey: ["changes", "Pending"],
    queryFn: () => api.poll<RbacChange[]>("/api/iam/changes?status=Pending"),
    enabled: canMatrix,
    refetchInterval: 60_000,
  });
  const waiting = pending.data?.filter((c) => c.canDecide).length ?? 0;

  // الأيقونات زخرفية؛ اسم الرابط نصه.
  const groups: NavGroup[] = [
    { key: "home", items: [{ to: "/", icon: <HomeOutlined aria-hidden />, label: L("الرئيسية", "Home"), end: true, tab: true }] },
    {
      key: "data", title: L("جمع البيانات والنتائج", "Data and results"), icon: <DatabaseOutlined aria-hidden />, items: [
        // بلا عداد في القائمة الجانبية (طلب المستخدم)؛ عدد النماذج المنتظرة يظهر في الرئيسية وصفحة النماذج.
        canOpen(perms, ROUTE_NEEDS["/forms"]) && { to: "/forms", icon: <FormOutlined aria-hidden />, label: L("نماذج الجهات", "Unit forms"), tab: true },
        // بطاقة الجامعة كاملة لمن نطاقه الجامعة؛ مستخدم الكلية أو الإدارة يرى تقدم جهته.
        canOpen(perms, ROUTE_NEEDS["/scorecard"]) && { to: "/scorecard", icon: <TrophyOutlined aria-hidden />, label: L("بطاقة الأداء والتصنيف", "Scorecard and tier"), tab: true },
        canOpen(perms, ROUTE_NEEDS["/review"]) && { to: "/review", icon: <FileDoneOutlined aria-hidden />, label: L("مراجعة مؤشرات الكليات", "Review college indicators"), tab: true },
        canOpen(perms, ROUTE_NEEDS["/progress"]) && { to: "/progress", icon: <RiseOutlined aria-hidden />, label: L("تقدم الكلية", "College progress") },
        canOpen(perms, ROUTE_NEEDS["/results"]) && { to: "/results", icon: <BarChartOutlined aria-hidden />, label: L("النتائج والمقارنات", "Results") },
        canOpen(perms, ROUTE_NEEDS["/cycles"]) && { to: "/cycles", icon: <CalendarOutlined aria-hidden />, label: L("دورات جمع البيانات", "Collection cycles") },
      ],
    },
    {
      key: "plan", title: L("الخطة والمؤشرات", "Plan and indicators"), icon: <AimOutlined aria-hidden />, items: [
        canOpen(perms, ROUTE_NEEDS["/strategy"]) && { to: "/strategy", icon: <PartitionOutlined aria-hidden />, label: L("الخطة الاستراتيجية", "Strategic plan"), end: true },
        canOpen(perms, ROUTE_NEEDS["/strategy/document"]) && { to: "/strategy/document", icon: <FilePdfOutlined aria-hidden />, label: L("وثيقة الخطة الاستراتيجية", "Strategic plan document") },
        canOpen(perms, ROUTE_NEEDS["/indicators"]) && { to: "/indicators", icon: <UnorderedListOutlined aria-hidden />, label: L("قاموس المؤشرات", "Indicators"), tab: true },
      ],
    },
    {
      key: "org", title: L("الجهات والهياكل", "Units and structures"), icon: <BankOutlined aria-hidden />, items: [
        canOpen(perms, ROUTE_NEEDS["/structure"]) && { to: "/structure", icon: <ClusterOutlined aria-hidden />, label: L("هياكل Tier 1 بالكليات", "College structures"), tab: true },
        canOpen(perms, ROUTE_NEEDS["/units"]) && { to: "/units", icon: <ApartmentOutlined aria-hidden />, label: L("الجهات", "Units") },
      ],
    },
    {
      key: "admin", title: L("الإدارة", "Administration"), icon: <ControlOutlined aria-hidden />, items: [
        canOpen(perms, ROUTE_NEEDS["/matrix"]) && { to: "/matrix", icon: <SafetyCertificateOutlined aria-hidden />, label: L("مصفوفة الصلاحيات", "Permission matrix") },
        canOpen(perms, ROUTE_NEEDS["/changes"]) && {
          to: "/changes", icon: <BranchesOutlined aria-hidden />, label: L("طلبات التعديل", "Change requests"),
          badge: waiting, badgeText: L(`${waiting} ينتظر قرارك`, `${waiting} waiting for your decision`),
        },
        canOpen(perms, ROUTE_NEEDS["/users"]) && { to: "/users", icon: <TeamOutlined aria-hidden />, label: L("الحسابات والأدوار", "Accounts and roles") },
        canOpen(perms, ROUTE_NEEDS["/logs"]) && { to: "/logs", icon: <AuditOutlined aria-hidden />, label: L("السجلات", "Logs") },
        canOpen(perms, ROUTE_NEEDS["/settings"]) && { to: "/settings", icon: <SettingOutlined aria-hidden />, label: L("إعدادات النظام", "System settings") },
        canOpen(perms, ROUTE_NEEDS["/mail"]) && { to: "/mail", icon: <MailOutlined aria-hidden />, label: L("البريد الصادر", "Outgoing mail") },
      ],
    },
  ];
  const visible: VisibleGroup[] = groups
    .map((g) => ({ ...g, items: g.items.filter((i): i is NavItem => !!i) }))
    .filter((g) => g.items.length > 0);

  // أكورديون: مجموعة واحدة مفتوحة. مجموعة الصفحة الحالية هي المفتوحة عند الدخول وعند الانتقال إليها؛
  // فتح غيرها يطوي ما سواها، والضغط على رأس المفتوحة يطويها.
  const { pathname } = useLocation();
  const activeGroup = visible.find((g) => g.title && g.items.some((n) => matchPath({ path: n.to, end: !!n.end }, pathname)))?.key;
  const [openGroup, setOpenGroup] = useState<string | null>(() => activeGroup ?? readNavOpen());
  useEffect(() => {
    if (activeGroup) setOpenGroup(activeGroup);
  }, [activeGroup]);
  useEffect(() => writeNavOpen(openGroup), [openGroup]);
  const toggleGroup = useCallback((key: string) => setOpenGroup((prev) => (prev === key ? null : key)), []);

  async function logout() {
    try {
      await api.post("/api/auth/logout");
    } finally {
      // تحميل جديد يفرغ ما خزنته الواجهة من بيانات الجلسة.
      window.location.replace("/login");
    }
  }
  const toggleLang = () => setLang(lang === "ar" ? "en" : "ar");

  const tipSide: TipSide = lang === "ar" ? "left" : "right";
  const logoutText = L("تسجيل الخروج", "Sign out");
  // شعار الخطة وحده في القائمة؛ علامته المختصرة تظهر في الشريط المطوي.
  const brand = (
    <Link to="/" className="lcp-brand" onClick={() => setDrawer(false)}
      aria-label={L("الرئيسية — الخطة الاستراتيجية وتقييم الأداء", "Home — Strategy and Performance")}>
      <span className="lcp-brand-wordmark">
        <img src={sidebarLogo} alt="" />
      </span>
      <span className="lcp-brand-mark" aria-hidden="true">
        <img src={strategyBrandOnNavy} alt="" />
      </span>
    </Link>
  );
  // asRail للشريط الجانبي على الحاسوب وحده؛ الدرج يمرر false دائمًا.
  // اللغة والمظهر في الشريط العلوي وحده (زر اللغة وزر المظهر)؛ تسجيل الخروج هنا أسفل القائمة، وفي قائمة الحساب أيضًا.
  const sideBody = (asRail: boolean) => (
    <>
      <SideNav groups={visible} open={openGroup} active={activeGroup} onToggle={toggleGroup} onNavigate={() => setDrawer(false)} rail={asRail} />
      <div className="lcp-side-foot">
        <RailTip title={asRail ? logoutText : undefined} side={tipSide}>
          <button type="button" onClick={logout} aria-label={asRail ? logoutText : undefined}>
            <LogoutOutlined aria-hidden /><span className="lcp-side-foot-label">{logoutText}</span>
          </button>
        </RailTip>
      </div>
    </>
  );
  const railText = rail ? L("توسيع القائمة", "Expand menu") : L("طي القائمة", "Collapse menu");
  // السهم نحو حافة الشريط وهو موسع (يسار في الإنجليزية، يمين في العربية)، ونحو الداخل وهو مطوي.
  const RailIcon = (lang === "ar") === rail ? DoubleLeftOutlined : DoubleRightOutlined;

  const tabs = visible.flatMap((g) => g.items).filter((n) => n.tab).slice(0, 5);

  return (
    <div className={["lcp lcp-staff", rail && "is-rail", railing && "is-railing", entering && "is-enter"].filter(Boolean).join(" ")}>
      <aside id={sideId} className="lcp-side">
        <div className="lcp-side-head">
          {brand}
          <Tooltip title={railText} placement={tipSide} trigger={["hover", "focus"]} mouseEnterDelay={0.15} open={railTip} onOpenChange={setRailTip}>
            <button type="button" className="lcp-rail-toggle" aria-expanded={!rail} aria-controls={sideId} aria-label={railText} onClick={toggleRail}>
              <RailIcon aria-hidden />
            </button>
          </Tooltip>
        </div>
        {sideBody(rail)}
      </aside>
      <div className="lcp-main">
        <header className="lcp-top">
          <Button className="lcp-burger" type="text" icon={<MenuOutlined aria-hidden />} onClick={() => setDrawer(true)} aria-label={L("القائمة", "Menu")} />
          <GlobalSearch />
          <span className="lcp-top-space" />
          <DevRoleTabs />
          <Button type="text" className="lcp-lang" onClick={toggleLang}>{lang === "ar" ? "EN" : "ع"}</Button>
          {/* المظهر (لوحة الألوان والزوايا) بجانب اللغة والحساب. */}
          <AppearanceButton tooltip={false} />
          <NotificationBell />
          <Dropdown trigger={["click"]} placement={lang === "ar" ? "bottomLeft" : "bottomRight"} menu={{
            items: [
              {
                key: "me", disabled: true,
                label: (
                  <span className="lcp-user-menu">
                    <b>{me?.name}</b>
                    <small className="ltr">{me?.email}</small>
                    {perms.roles.length > 0 && <small>{L("الأدوار:", "Roles:")} <span className="ltr">{perms.roles.join(" · ")}</span></small>}
                  </span>
                ),
              },
              { type: "divider" },
              { key: "logout", icon: <LogoutOutlined aria-hidden />, label: L("تسجيل الخروج", "Sign out"), onClick: logout },
            ],
          }}>
            <button type="button" className="lcp-user" aria-label={`${L("حسابي", "My account")}: ${me?.name ?? ""}`} title={me?.name}>
              <span className="lcp-avatar" aria-hidden>{initials(me?.name)}</span>
              <span className="lcp-user-name">{me?.name || L("حسابي", "My account")}</span>
            </button>
          </Dropdown>
          <Link to="/" className="lcp-top-university" aria-label={L("جامعة العاصمة — الرئيسية", "Capital University — Home")}>
            <img src={logoOnWhite} alt="" />
          </Link>
        </header>
        <main className="lcp-content">
          <div className="lcp-page">{children}</div>
        </main>
      </div>
      {tabs.length > 1 && (
        <nav className="lcp-tabbar" aria-label={L("التنقل السريع", "Quick navigation")}>
          {tabs.map((n) => <NavLink key={n.to} to={n.to} end={n.end}>{n.icon}<span>{n.label}</span></NavLink>)}
        </nav>
      )}
      <Drawer open={drawer} onClose={() => setDrawer(false)} placement={lang === "ar" ? "right" : "left"} width={280} className="lcp-drawer" closable={false}>
        {brand}
        {sideBody(false)}
      </Drawer>
    </div>
  );
}
