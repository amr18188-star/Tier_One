import { useMemo, useState, type ReactNode } from "react";
import { AutoComplete, Button, Drawer, Dropdown, Input } from "antd";
import {
  ApartmentOutlined, AuditOutlined, BarChartOutlined, BranchesOutlined, CalendarOutlined, ClusterOutlined, FormOutlined, GlobalOutlined, HomeOutlined, LogoutOutlined, MenuOutlined,
  PartitionOutlined, SafetyCertificateOutlined, SearchOutlined, SettingOutlined, TeamOutlined, UnorderedListOutlined, WarningOutlined,
} from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { Link, NavLink, useNavigate } from "react-router-dom";
import { api } from "../api/client";
import type { RbacChange } from "../api/types";
import type { IndicatorRow, OrgUnit } from "../api/tier1";
import type { FormRow } from "../api/collection";
import { L, setLang, useLang } from "../shared/i18n";
import { usePermissions, useSession } from "../shared/session";
import { initials } from "../shared/ui";
import mark from "../assets/brand/tier1-mark.svg";
import "../shared/ui.css";
import "../shared/tier1.css";

interface NavItem { to: string; icon: ReactNode; label: string; end?: boolean; badge?: number; badgeText?: string; tab?: boolean }
interface NavGroup { key: string; title?: string; items: (NavItem | false)[] }

/**
 * إطار المنظومة: قائمة جانبية داكنة مقسمة بحسب الصلاحيات (لا يرى المستخدم ما لا يملكه)، وبحث موحد أعلى كل صفحة
 * بكود المؤشر أو اسمه أو اسم الكلية، وشريط تبويب سفلي على الجوال.
 */
export function Shell({ children }: { children: ReactNode }) {
  const { me } = useSession();
  const perms = usePermissions();
  const lang = useLang();
  const [drawer, setDrawer] = useState(false);

  const canMatrix = perms.can("adm.roles", "V");
  const pending = useQuery({
    queryKey: ["changes", "Pending"],
    queryFn: () => api.get<RbacChange[]>("/api/iam/changes?status=Pending"),
    enabled: canMatrix,
    refetchInterval: 60_000,
  });
  const waiting = pending.data?.filter((c) => c.canDecide).length ?? 0;
  const mine = useQuery({
    queryKey: ["forms", "mine"],
    queryFn: () => api.get<FormRow[]>("/api/forms?awaitingMe=true"),
    enabled: perms.can("dc.forms", "V"),
    refetchInterval: 120_000,
  });
  const awaiting = mine.data?.length ?? 0;
  const anyLog = ["log.audit", "log.security", "log.access", "log.tech"].some((f) => perms.can(f, "V"));

  // الأيقونات زخرفية؛ اسم الرابط نصه.
  const groups: NavGroup[] = [
    { key: "home", items: [{ to: "/", icon: <HomeOutlined aria-hidden />, label: L("الرئيسية", "Home"), end: true, tab: true }] },
    {
      key: "data", title: L("جمع البيانات والنتائج", "Data and results"), items: [
        perms.can("dc.forms", "V") && {
          to: "/forms", icon: <FormOutlined aria-hidden />, label: L("نماذج الجهات", "Unit forms"), tab: true,
          badge: awaiting, badgeText: L(`${awaiting} نموذج ينتظرك`, `${awaiting} forms waiting for you`),
        },
        perms.can("dc.results", "V") && { to: "/results", icon: <BarChartOutlined aria-hidden />, label: L("النتائج والمقارنات", "Results"), tab: true },
        perms.can("dc.cycles", "E") && { to: "/cycles", icon: <CalendarOutlined aria-hidden />, label: L("دورات جمع البيانات", "Collection cycles") },
      ],
    },
    {
      key: "plan", title: L("الخطة والمؤشرات", "Plan and indicators"), items: [
        perms.can("kpi.strategy", "V") && { to: "/strategy", icon: <PartitionOutlined aria-hidden />, label: L("الخطة الاستراتيجية", "Strategic plan") },
        perms.can("kpi.dictionary", "V") && { to: "/indicators", icon: <UnorderedListOutlined aria-hidden />, label: L("قاموس المؤشرات", "Indicators"), tab: true },
        perms.can("kpi.conflicts", "V") && { to: "/conflicts", icon: <WarningOutlined aria-hidden />, label: L("سجل التعارضات", "Conflicts"), tab: true },
      ],
    },
    {
      key: "org", title: L("الجهات والهياكل", "Units and structures"), items: [
        perms.can("org.structure", "V") && { to: "/structure", icon: <ClusterOutlined aria-hidden />, label: L("هياكل Tier 1 بالكليات", "College structures"), tab: true },
        perms.can("org.units", "V") && { to: "/units", icon: <ApartmentOutlined aria-hidden />, label: L("الجهات", "Units") },
      ],
    },
    {
      key: "admin", title: L("الإدارة", "Administration"), items: [
        canMatrix && { to: "/matrix", icon: <SafetyCertificateOutlined aria-hidden />, label: L("مصفوفة الصلاحيات", "Permission matrix") },
        canMatrix && {
          to: "/changes", icon: <BranchesOutlined aria-hidden />, label: L("طلبات التعديل", "Change requests"),
          badge: waiting, badgeText: L(`${waiting} ينتظر قرارك`, `${waiting} waiting for your decision`),
        },
        (perms.can("adm.users", "V") || perms.can("adm.grants", "V")) && { to: "/users", icon: <TeamOutlined aria-hidden />, label: L("الحسابات والأدوار", "Accounts and roles") },
        anyLog && { to: "/logs", icon: <AuditOutlined aria-hidden />, label: L("السجلات", "Logs") },
        perms.can("adm.settings", "V") && { to: "/settings", icon: <SettingOutlined aria-hidden />, label: L("إعدادات النظام", "System settings") },
      ],
    },
  ];
  const visible = groups
    .map((g) => ({ ...g, items: g.items.filter((i): i is NavItem => !!i) }))
    .filter((g) => g.items.length > 0);

  async function logout() {
    try {
      await api.post("/api/auth/logout");
    } finally {
      // تحميل جديد يفرغ ما خزنته الواجهة من بيانات الجلسة.
      window.location.replace("/");
    }
  }
  const toggleLang = () => setLang(lang === "ar" ? "en" : "ar");

  const side = (
    <>
      <Link to="/" className="lcp-brand" onClick={() => setDrawer(false)}>
        <img src={mark} alt="" aria-hidden />
        <span>
          <b>{L("منظومة Tier 1", "Tier 1 system")}</b>
          <small>{L("جامعة العاصمة · الأداء الاستراتيجي", "Capital University · Strategic performance")}</small>
        </span>
      </Link>
      <nav className="lcp-nav" aria-label={L("القوائم الرئيسية", "Main menu")}>
        {visible.map((g) => (
          <div key={g.key} className="lcp-nav-sec" role="group" aria-label={g.title}>
            {g.title && <div className="lcp-nav-group" aria-hidden>{g.title}</div>}
            {g.items.map((n) => (
              <NavLink key={n.to} to={n.to} end={n.end} onClick={() => setDrawer(false)}>
                {n.icon}<span>{n.label}</span>
                {!!n.badge && <><span className="lcp-badge" aria-hidden>{n.badge}</span><span className="sr-only">{n.badgeText}</span></>}
              </NavLink>
            ))}
          </div>
        ))}
      </nav>
      <div className="lcp-side-foot">
        <button type="button" onClick={toggleLang}><GlobalOutlined aria-hidden /> {lang === "ar" ? "English" : "العربية"}</button>
        <button type="button" onClick={logout}><LogoutOutlined aria-hidden /> {L("تسجيل الخروج", "Sign out")}</button>
      </div>
    </>
  );

  const tabs = visible.flatMap((g) => g.items).filter((n) => n.tab).slice(0, 5);

  return (
    <div className="lcp lcp-staff">
      <aside className="lcp-side">{side}</aside>
      <div className="lcp-main">
        <header className="lcp-top">
          <Button className="lcp-burger" type="text" icon={<MenuOutlined aria-hidden />} onClick={() => setDrawer(true)} aria-label={L("القائمة", "Menu")} />
          <Link to="/" className="lcp-top-brand">
            <img src={mark} alt="" aria-hidden />
            <b>Tier 1</b>
          </Link>
          <GlobalSearch />
          <span className="lcp-top-space" />
          <Button type="text" className="lcp-lang" onClick={toggleLang}>{lang === "ar" ? "EN" : "ع"}</Button>
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
            <button type="button" className="lcp-user" aria-label={L("حسابي", "My account")}>
              <span className="lcp-avatar" aria-hidden>{initials(me?.name)}</span>
              <span className="lcp-user-name">{me?.name}</span>
            </button>
          </Dropdown>
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
        {side}
      </Drawer>
    </div>
  );
}

/** بحث موحد: مؤشر بكوده أو اسمه، أو كلية باسمها، ثم الانتقال إليه مباشرة. */
function GlobalSearch() {
  const perms = usePermissions();
  const navigate = useNavigate();
  const [text, setText] = useState("");
  const indicators = useQuery({ queryKey: ["indicators", "all"], queryFn: () => api.get<IndicatorRow[]>("/api/kpi/indicators"), enabled: perms.can("kpi.dictionary", "V"), staleTime: 300_000 });
  const units = useQuery({ queryKey: ["units"], queryFn: () => api.get<OrgUnit[]>("/api/org/units"), enabled: perms.can("org.units", "V"), staleTime: 300_000 });

  const options = useMemo(() => {
    const t = text.trim();
    if (t.length < 2) return [];
    const low = t.toLowerCase();
    const ind = (indicators.data ?? []).filter((i) => i.code.toLowerCase().includes(low) || i.nameAr.includes(t)).slice(0, 8)
      .map((i) => ({ value: `/indicators/${i.code}`, label: <span><code>{i.code}</code> {i.nameAr}</span> }));
    const col = (units.data ?? []).filter((u) => (u.kind === "College" || u.kind === "Institute") && (u.nameAr.includes(t) || u.code.toLowerCase().includes(low))).slice(0, 5)
      .map((u) => ({ value: `/structure/${u.id}`, label: <span><ClusterOutlined aria-hidden /> {u.nameAr}</span> }));
    return [
      ...(ind.length ? [{ label: L("المؤشرات", "Indicators"), options: ind }] : []),
      ...(col.length ? [{ label: L("الكليات", "Colleges"), options: col }] : []),
    ];
  }, [text, indicators.data, units.data]);

  return (
    <AutoComplete className="t1-search" options={options} value={text} onChange={setText}
      onSelect={(to: string) => { setText(""); navigate(to); }} popupMatchSelectWidth={420} style={{ width: "min(420px, 46vw)" }}>
      <Input prefix={<SearchOutlined aria-hidden />} allowClear placeholder={L("ابحث بكود المؤشر أو اسمه أو اسم الكلية", "Search an indicator code or name, or a college")}
        aria-label={L("بحث", "Search")} />
    </AutoComplete>
  );
}
