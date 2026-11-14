import { cloneElement, useCallback, useEffect, useId, useMemo, useRef, useState, type MouseEvent as ReactMouseEvent, type ReactElement, type ReactNode } from "react";
import { AutoComplete, Button, Drawer, Dropdown, Input, Tooltip } from "antd";
import {
  AimOutlined, ApartmentOutlined, AuditOutlined, BankOutlined, BarChartOutlined, BranchesOutlined, CalendarOutlined, ClusterOutlined, ControlOutlined, DatabaseOutlined, DoubleLeftOutlined, DoubleRightOutlined, DownOutlined, FileDoneOutlined, FilePdfOutlined, FormOutlined, HomeOutlined, LogoutOutlined, MenuOutlined,
  PartitionOutlined, RiseOutlined, SafetyCertificateOutlined, SearchOutlined, SettingOutlined, TeamOutlined, TrophyOutlined, UnorderedListOutlined,
} from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { Link, NavLink, matchPath, useLocation, useNavigate } from "react-router-dom";
import { api } from "../api/client";
import type { RbacChange } from "../api/types";
import type { IndicatorRow, OrgUnit } from "../api/tier1";
import { L, setLang, useLang } from "../shared/i18n";
import { usePermissions, useSession } from "../shared/session";
import { initials } from "../shared/ui";
import { AppearanceButton } from "./AppearanceMenu";
// شعار الجامعة الرسمي. أسماء الملفات بلون الخلفية: «-dark» حروفه بيضاء للقائمة الكحلية، و«-light» حروفه كحلية للشريط الأبيض.
import logoOnNavy from "../assets/brand/capital-university-logo-dark.png";
import logoOnWhite from "../assets/brand/capital-university-logo-light.png";
import "../shared/ui.css";
import "../shared/tier1.css";
import "../shared/theme.css";
import "../shared/layout.css";
import "./nav.css";

interface NavItem { to: string; icon: ReactNode; label: string; end?: boolean; badge?: number; badgeText?: string; tab?: boolean }
// icon: أيقونة المجموعة في الشريط المطوي وحده (زر المجموعة)؛ لا تتكرر أيقونة رابط فيها. رؤوس الموسع نص فقط.
interface NavGroup { key: string; title?: string; icon?: ReactNode; items: (NavItem | false)[] }
interface VisibleGroup { key: string; title?: string; icon?: ReactNode; items: NavItem[] }

// مجموعة واحدة مفتوحة في القائمة الجانبية (أكورديون)، ومفتاحها يحفظ في المتصفح؛ مجموعة الصفحة الحالية تتقدم عليه.
const NAV_OPEN_KEY = "t1-nav-open";

function readNavOpen(): string | null {
  try {
    const raw = localStorage.getItem(NAV_OPEN_KEY);
    const value: unknown = raw ? JSON.parse(raw) : null;
    // القيمة القديمة كانت قائمة مفاتيح؛ يؤخذ أولها.
    const key = Array.isArray(value) ? value[0] : value;
    return typeof key === "string" ? key : null;
  } catch {
    return null;
  }
}

function writeNavOpen(key: string | null) {
  try {
    localStorage.setItem(NAV_OPEN_KEY, JSON.stringify(key));
  } catch {
    // المتصفح يمنع التخزين؛ تبقى الحالة لهذه الجلسة فقط.
  }
}

// الشريط الجانبي على الحاسوب يطوى إلى عمود أيقونات (72px)؛ الحالة تحفظ في المتصفح «1» مطوي و«0» موسع. درج الجوال لا يتأثر.
const RAIL_KEY = "t1-side-rail";
// مدة تغير العرض في nav.css (220ms) وهامش صغير: طوالها تبقى النصوص سطرًا واحدًا وتتلاشى ولا تلتف.
const RAIL_MS = 240;

function readRail(): boolean {
  try {
    return localStorage.getItem(RAIL_KEY) === "1";
  } catch {
    return false;
  }
}

function writeRail(on: boolean) {
  try {
    localStorage.setItem(RAIL_KEY, on ? "1" : "0");
  } catch {
    // المتصفح يمنع التخزين؛ تبقى الحالة لهذه الجلسة فقط.
  }
}

type TipSide = "left" | "right";

/**
 * تلميح الشريط المطوي بالمرور والتركيز، نحو المحتوى (antd لا يقلب يمين ويسار مع الاتجاه، والشريط يمين الشاشة في العربية).
 * يغلق عند الضغط: فتح مجموعة يطوي غيرها فيتحرك الزر، ولا يبقى التلميح معلقًا في المكان القديم.
 * بلا عنوان يعرض العنصر وحده، فالقائمة الموسعة والدرج كما هما.
 */
function RailTip({ title, side, children }: {
  title?: string;
  side: TipSide;
  children: ReactElement<{ onClick?: (event: ReactMouseEvent<HTMLElement>) => void }>;
}) {
  const [open, setOpen] = useState(false);
  useEffect(() => {
    if (!title) setOpen(false);
  }, [title]);
  if (!title) return children;
  const child = cloneElement(children, {
    onClick: (event: ReactMouseEvent<HTMLElement>) => {
      setOpen(false);
      children.props.onClick?.(event);
    },
  });
  return (
    <Tooltip title={title} placement={side} trigger={["hover", "focus"]} mouseEnterDelay={0.15} open={open} onOpenChange={setOpen}>
      {child}
    </Tooltip>
  );
}

/**
 * إطار المنظومة: قائمة جانبية داكنة مقسمة بحسب الصلاحيات (لا يرى المستخدم ما لا يملكه)، وبحث موحد أعلى كل صفحة
 * بكود المؤشر أو اسمه أو اسم الكلية، وشريط تبويب سفلي على الجوال.
 */
export function Shell({ children }: { children: ReactNode }) {
  const { me } = useSession();
  const perms = usePermissions();
  const lang = useLang();
  const [drawer, setDrawer] = useState(false);

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

  const canMatrix = perms.can("adm.roles", "V");
  const pending = useQuery({
    queryKey: ["changes", "Pending"],
    queryFn: () => api.get<RbacChange[]>("/api/iam/changes?status=Pending"),
    enabled: canMatrix,
    refetchInterval: 60_000,
  });
  const waiting = pending.data?.filter((c) => c.canDecide).length ?? 0;
  const anyLog = ["log.audit", "log.security", "log.access", "log.tech"].some((f) => perms.can(f, "V"));

  // الأيقونات زخرفية؛ اسم الرابط نصه.
  const groups: NavGroup[] = [
    { key: "home", items: [{ to: "/", icon: <HomeOutlined aria-hidden />, label: L("الرئيسية", "Home"), end: true, tab: true }] },
    {
      key: "data", title: L("جمع البيانات والنتائج", "Data and results"), icon: <DatabaseOutlined aria-hidden />, items: [
        // بلا عداد في القائمة الجانبية (طلب المستخدم)؛ عدد النماذج المنتظرة يظهر في الرئيسية وصفحة النماذج.
        perms.can("dc.forms", "V") && { to: "/forms", icon: <FormOutlined aria-hidden />, label: L("نماذج الجهات", "Unit forms"), tab: true },
        // بطاقة الجامعة كاملة لمن نطاقه الجامعة؛ مستخدم الكلية أو الإدارة يرى تقدم جهته.
        perms.scopeOf("dc.results", "V") === "A" && { to: "/scorecard", icon: <TrophyOutlined aria-hidden />, label: L("بطاقة الأداء والتصنيف", "Scorecard and tier"), tab: true },
        perms.can("dc.university", "V") && { to: "/review", icon: <FileDoneOutlined aria-hidden />, label: L("مراجعة مؤشرات الكليات", "Review college indicators"), tab: true },
        perms.can("dc.results", "V") && { to: "/progress", icon: <RiseOutlined aria-hidden />, label: L("تقدم الكلية", "College progress") },
        perms.can("dc.results", "V") && { to: "/results", icon: <BarChartOutlined aria-hidden />, label: L("النتائج والمقارنات", "Results") },
        perms.can("dc.cycles", "E") && { to: "/cycles", icon: <CalendarOutlined aria-hidden />, label: L("دورات جمع البيانات", "Collection cycles") },
      ],
    },
    {
      key: "plan", title: L("الخطة والمؤشرات", "Plan and indicators"), icon: <AimOutlined aria-hidden />, items: [
        perms.can("kpi.strategy", "V") && { to: "/strategy", icon: <PartitionOutlined aria-hidden />, label: L("الخطة الاستراتيجية", "Strategic plan"), end: true },
        perms.can("kpi.strategy", "V") && { to: "/strategy/document", icon: <FilePdfOutlined aria-hidden />, label: L("وثيقة الخطة الاستراتيجية", "Strategic plan document") },
        perms.can("kpi.dictionary", "V") && { to: "/indicators", icon: <UnorderedListOutlined aria-hidden />, label: L("قاموس المؤشرات", "Indicators"), tab: true },
      ],
    },
    {
      key: "org", title: L("الجهات والهياكل", "Units and structures"), icon: <BankOutlined aria-hidden />, items: [
        perms.can("org.structure", "V") && { to: "/structure", icon: <ClusterOutlined aria-hidden />, label: L("هياكل Tier 1 بالكليات", "College structures"), tab: true },
        perms.can("org.units", "V") && { to: "/units", icon: <ApartmentOutlined aria-hidden />, label: L("الجهات", "Units") },
      ],
    },
    {
      key: "admin", title: L("الإدارة", "Administration"), icon: <ControlOutlined aria-hidden />, items: [
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
      window.location.replace("/");
    }
  }
  const toggleLang = () => setLang(lang === "ar" ? "en" : "ar");

  const tipSide: TipSide = lang === "ar" ? "left" : "right";
  const logoutText = L("تسجيل الخروج", "Sign out");
  // في الشريط المطوي يبقى نص الشعار في الصفحة مخفيًا بصريًا (nav.css)، فاسم الرابط لقارئ الشاشة لا يتغير.
  const brand = (
    <Link to="/" className="lcp-brand" onClick={() => setDrawer(false)}>
      <img src={logoOnNavy} alt={L("جامعة العاصمة", "Capital University")} />
      <span className="lcp-brand-text">
        <b>{L("منظومة Tier 1", "Tier 1 system")}</b>
        <small>{L("جامعة العاصمة · الأداء الاستراتيجي", "Capital University · Strategic performance")}</small>
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
    <div className={["lcp lcp-staff", rail && "is-rail", railing && "is-railing"].filter(Boolean).join(" ")}>
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
          <Link to="/" className="lcp-top-brand">
            <img src={logoOnWhite} alt={L("جامعة العاصمة", "Capital University")} />
            <b>Tier 1</b>
          </Link>
          <GlobalSearch />
          <span className="lcp-top-space" />
          <Button type="text" className="lcp-lang" onClick={toggleLang}>{lang === "ar" ? "EN" : "ع"}</Button>
          {/* المظهر (لوحة الألوان والزوايا) بجانب اللغة والحساب. */}
          <AppearanceButton tooltip={false} />
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
        {brand}
        {sideBody(false)}
      </Drawer>
    </div>
  );
}

/**
 * روابط القائمة الجانبية. كل مجموعة بعنوان لوحة تطوى: الرأس زر يفتحها ويطويها، ويعرض وهي مطوية مجموع شارات روابطها.
 * تعرض القائمة مرتين (الشريط الجانبي ودرج الجوال)، فمعرفات الرؤوس واللوحات من useId لكل نسخة، والحالة المفتوحة مشتركة من Shell.
 * rail (الشريط المطوي، الحاسوب وحده): الأكورديون نفسه والحالة المفتوحة نفسها، لكن الرأس زر بأيقونة المجموعة واسمها في تلميح،
 * والروابط أيقونات تحته. active مفتاح مجموعة الصفحة الحالية: شريط ذهبي على زرها ولو كانت مطوية.
 */
function SideNav({ groups, open, active, onToggle, onNavigate, rail }: {
  groups: VisibleGroup[];
  open: string | null;
  active?: string;
  onToggle: (key: string) => void;
  onNavigate: () => void;
  rail: boolean;
}) {
  const uid = useId();
  const tipSide: TipSide = useLang() === "ar" ? "left" : "right";
  return (
    <nav className="lcp-nav" aria-label={L("القوائم الرئيسية", "Main menu")}>
      {groups.map((g) => {
        // اسم الرابط نصه (مخفي بصريًا في الشريط المطوي)؛ العدد نقطة على الأيقونة ورقم في التلميح.
        const links = g.items.map((n) => (
          <RailTip key={n.to} title={rail ? (n.badge ? `${n.label} · ${n.badge}` : n.label) : undefined} side={tipSide}>
            <NavLink to={n.to} end={n.end} onClick={onNavigate}>
              {n.icon}<span className="lcp-nav-label">{n.label}</span>
              {!!n.badge && (
                <>
                  {rail ? <span className="lcp-rail-dot" aria-hidden /> : <span className="lcp-badge" aria-hidden>{n.badge}</span>}
                  <span className="sr-only">{n.badgeText}</span>
                </>
              )}
            </NavLink>
          </RailTip>
        ));
        if (!g.title) return <div key={g.key} className="lcp-nav-sec" role="group">{links}</div>;

        const isOpen = open === g.key;
        const headId = `${uid}-${g.key}-head`;
        const panelId = `${uid}-${g.key}-panel`;
        const badged = g.items.filter((n) => !!n.badge);
        const total = badged.reduce((sum, n) => sum + (n.badge ?? 0), 0);
        const sep = L("، ", ", ");
        const badgeTexts = badged.map((n) => n.badgeText).filter(Boolean).join(sep);
        // الشريط المطوي: الاسم عنوان المجموعة، والعدد (والمجموعة مطوية) نقطة على الأيقونة ووصف لقارئ الشاشة ورقم في التلميح.
        const badgeId = `${headId}-badges`;
        const head = rail ? (
          <RailTip title={total > 0 ? `${g.title} · ${total}` : g.title} side={tipSide}>
            <button type="button" id={headId} className="lcp-nav-cat" aria-expanded={isOpen} aria-controls={panelId} aria-label={g.title}
              aria-describedby={!isOpen && total > 0 ? badgeId : undefined} onClick={() => onToggle(g.key)}>
              {g.icon}
              {!isOpen && total > 0 && <><span className="lcp-rail-dot" aria-hidden /><span id={badgeId} className="sr-only">{badgeTexts}</span></>}
            </button>
          </RailTip>
        ) : (
          <button type="button" id={headId} className="lcp-nav-group lcp-nav-head" aria-expanded={isOpen} aria-controls={panelId} onClick={() => onToggle(g.key)}>
            <span className="lcp-nav-title">{g.title}</span>
            {!isOpen && total > 0 && (
              <>
                <span className="lcp-badge" aria-hidden>{total}</span>
                <span className="sr-only">{sep}{badgeTexts}</span>
              </>
            )}
            <DownOutlined className="lcp-nav-chev" aria-hidden />
          </button>
        );
        return (
          <div key={g.key} className="lcp-nav-sec lcp-nav-panel" data-open={isOpen ? "true" : "false"}
            data-active={rail ? (active === g.key ? "true" : "false") : undefined}>
            {head}
            <div id={panelId} className="lcp-nav-body" role="group" aria-labelledby={headId}>
              <div className="lcp-nav-inner">{links}</div>
            </div>
          </div>
        );
      })}
    </nav>
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
