import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Button, Empty, Grid, Select, Skeleton, Table } from "antd";
import type { ColumnsType, TableProps } from "antd/es/table";
import {
  ApartmentOutlined, ArrowLeftOutlined, ArrowRightOutlined, AuditOutlined, BarChartOutlined, BranchesOutlined, CalendarOutlined, CheckOutlined, ClusterOutlined, FileDoneOutlined, FormOutlined,
  LeftOutlined, RightOutlined, RiseOutlined, SafetyCertificateOutlined, SettingOutlined, TeamOutlined, UnorderedListOutlined, WarningOutlined,
} from "@ant-design/icons";
import { Link, useLocation } from "react-router-dom";
import { L, useLang } from "./i18n";
import { QueryState, type QueryLike } from "./QueryState";
import "./ui.css";

/** القسم الذي تتبعه الصفحة (كما في القائمة الجانبية) وأيقونته، من مسارها. */
const SECTIONS: { prefix: string; ar: string; en: string; icon: ReactNode }[] = [
  { prefix: "/forms", ar: "جمع البيانات والنتائج", en: "Data and results", icon: <FormOutlined /> },
  { prefix: "/results", ar: "جمع البيانات والنتائج", en: "Data and results", icon: <BarChartOutlined /> },
  { prefix: "/review", ar: "جمع البيانات والنتائج", en: "Data and results", icon: <FileDoneOutlined /> },
  { prefix: "/progress", ar: "جمع البيانات والنتائج", en: "Data and results", icon: <RiseOutlined /> },
  { prefix: "/cycles", ar: "جمع البيانات والنتائج", en: "Data and results", icon: <CalendarOutlined /> },
  { prefix: "/indicators", ar: "الخطة والمؤشرات", en: "Plan and indicators", icon: <UnorderedListOutlined /> },
  { prefix: "/conflicts", ar: "الخطة والمؤشرات", en: "Plan and indicators", icon: <WarningOutlined /> },
  { prefix: "/structure", ar: "الجهات والهياكل", en: "Units and structures", icon: <ClusterOutlined /> },
  { prefix: "/units", ar: "الجهات والهياكل", en: "Units and structures", icon: <ApartmentOutlined /> },
  { prefix: "/matrix", ar: "الإدارة", en: "Administration", icon: <SafetyCertificateOutlined /> },
  { prefix: "/changes", ar: "الإدارة", en: "Administration", icon: <BranchesOutlined /> },
  { prefix: "/users", ar: "الإدارة", en: "Administration", icon: <TeamOutlined /> },
  { prefix: "/logs", ar: "الإدارة", en: "Administration", icon: <AuditOutlined /> },
  { prefix: "/settings", ar: "الإدارة", en: "Administration", icon: <SettingOutlined /> },
];

export interface HeadStat { label: string; value: ReactNode; tone?: "ok" | "warn" | "bad" }

/**
 * عنوان الصفحة شريطًا داكنًا بهوية المنظومة (مثل الخطة الاستراتيجية): القسم وأيقونته من المسار، والعنوان ووصفه،
 * وأرقام الصفحة الأهم (stats) إن وجدت، والإجراء الرئيسي في الطرف الآخر، ورابط رجوع اختياري.
 */
export function PageHeader({ title, subtitle, extra, back, stats, kicker }: {
  title: ReactNode; subtitle?: ReactNode; extra?: ReactNode; back?: { to: string; label: string }; stats?: HeadStat[]; kicker?: ReactNode;
}) {
  const lang = useLang();
  const { pathname } = useLocation();
  const Back = lang === "ar" ? ArrowRightOutlined : ArrowLeftOutlined;
  const section = SECTIONS.find((s) => pathname.startsWith(s.prefix));
  return (
    <header className="lcp-head">
      {section && <span className="lcp-head-mark" aria-hidden>{section.icon}</span>}
      <div className="lcp-head-main">
        {back && <Link to={back.to} className="lcp-back"><Back aria-hidden /> {back.label}</Link>}
        {(kicker ?? section) && <small className="lcp-head-kicker">{kicker ?? L(section!.ar, section!.en)}</small>}
        <h1>{title}</h1>
        {subtitle && <p>{subtitle}</p>}
      </div>
      {stats && stats.length > 0 && (
        <dl className="lcp-head-stats">
          {stats.map((s) => <div key={s.label} className={s.tone ? `tone-${s.tone}` : undefined}><dt>{s.label}</dt><dd>{s.value}</dd></div>)}
        </dl>
      )}
      {extra && <div className="lcp-head-extra">{extra}</div>}
    </header>
  );
}

/** لوحة بيضاء بعنوان وأيقونة وإجراء جانبي. مع query تعرض هيكل التحميل أو خطأه بزر إعادة المحاولة بدل المحتوى. */
export function Panel({ title, icon, extra, children, className, loading, query, id }: {
  title?: ReactNode; icon?: ReactNode; extra?: ReactNode; children: ReactNode | (() => ReactNode); className?: string; loading?: boolean; query?: QueryLike | QueryLike[]; id?: string;
}) {
  const body = typeof children === "function" && !query ? children() : children;
  return (
    <section id={id} className={className ? `lcp-panel ${className}` : "lcp-panel"}>
      {(title || extra) && (
        <div className="lcp-panel-head">
          {title && <h2>{icon}{title}</h2>}
          {extra}
        </div>
      )}
      {loading ? <Skeleton active paragraph={{ rows: 3 }} /> : query ? <QueryState query={query}>{children}</QueryState> : (body as ReactNode)}
    </section>
  );
}

/** قيمة من الرابط تقبل فقط إن كانت من القيم المعروفة؛ وإلا undefined. */
export function oneOf<T extends string>(value: string | null, allowed: readonly T[]): T | undefined {
  return allowed.find((a) => a === value);
}

export type Tone = "teal" | "gold" | "navy" | "green" | "rose";

/** مؤشر رقمي يفتح القائمة المعنية. hint سطر صغير تحت الاسم (نسبة أو تفصيل). */
export function StatTile({ icon, label, value, tone = "teal", to, loading, hint }: {
  icon: ReactNode; label: string; value: ReactNode; tone?: Tone; to?: string; loading?: boolean; hint?: ReactNode;
}) {
  const body = (
    <>
      <span className="lcp-stat-icon" aria-hidden>{icon}</span>
      <span className="lcp-stat-text">
        <b>{loading ? "…" : value}</b>
        <span>{label}</span>
        {hint && <small className="lcp-stat-hint">{hint}</small>}
      </span>
    </>
  );
  return to ? <Link to={to} className={`lcp-stat tone-${tone}`}>{body}</Link> : <div className={`lcp-stat tone-${tone}`}>{body}</div>;
}

/**
 * طرف شريط المرشحات: عدد النتائج (ومن كم)، وزر «إعادة ضبط المرشحات» حين يكون مرشح نشط؛ نفسه في كل الجداول.
 */
export function FilterMeta({ shown, total, noun, active, onReset }: { shown: number; total?: number; noun: string; active: boolean; onReset: () => void }) {
  return (
    <span className="lcp-filter-meta" aria-live="polite">
      {total !== undefined && total !== shown ? L(`${shown} من ${total} ${noun}`, `${shown} of ${total}`) : L(`${shown} ${noun}`, `${shown}`)}
      {active && <Button size="small" type="link" onClick={onReset}>{L("إعادة ضبط المرشحات", "Reset filters")}</Button>}
    </span>
  );
}

export function EmptyNote({ children, action }: { children: ReactNode; action?: ReactNode }) {
  return <Empty className="lcp-empty" image={Empty.PRESENTED_IMAGE_SIMPLE} description={<>{children}{action && <div className="mt">{action}</div>}</>} />;
}

export interface Step { title: string; note?: ReactNode }

/** شريط مراحل أفقي: المنجز بعلامة صح، والحالي بإطار، والقادم رمادي. */
export function Stepper({ steps, current }: { steps: Step[]; current: number }) {
  return (
    <ol className="lcp-steps" style={{ gridTemplateColumns: `repeat(${steps.length}, minmax(0, 1fr))` }}>
      {steps.map((s, i) => {
        const state = i < current ? "done" : i === current ? "current" : "";
        return (
          <li key={s.title} className={`lcp-step ${state}`} aria-current={i === current ? "step" : undefined}>
            <span className="lcp-step-dot">{i < current ? <CheckOutlined /> : i + 1}</span>
            <b>{s.title}</b>
            {s.note && <small>{s.note}</small>}
          </li>
        );
      })}
    </ol>
  );
}

// ----- الجدول -----

/** ترقيم من الخادم: الصفحة والعدد الكلي يأتيان مع البيانات، فلا يقطع الجدول القائمة بنفسه. */
export interface ServerPaging { page: number; size: number; total: number; onPage: (page: number) => void }

/**
 * الجدول الموحد: رأس تركوازي وعمود ترقيم، وتذييل «عدد العناصر لكل صفحة» و«1-10 من N».
 * على الشاشات الضيقة يعرض البطاقات بدل الصفوف إن أعطيت renderCard.
 */
export function DataTable<T extends object>({
  rowKey, columns, data, loading, onRowClick, empty, renderCard, pageSizes = [10, 20, 50], resetKey, minWidth = 880, expandable, paging, rowClassName,
}: {
  rowKey: (row: T) => string;
  columns: ColumnsType<T>;
  data: T[];
  loading?: boolean;
  onRowClick?: (row: T) => void;
  empty?: ReactNode;
  renderCard?: (row: T) => ReactNode;
  pageSizes?: number[];
  /** يتغير مع المرشحات فيعود الجدول إلى الصفحة الأولى. */
  resetKey?: string;
  /** أقل عرض للجدول؛ تحته يمرر أفقيًا، وفوقه تلتف النصوص الطويلة في أعمدتها. */
  minWidth?: number;
  /** صف يفتح تحته تفاصيله. */
  expandable?: TableProps<T>["expandable"];
  paging?: ServerPaging;
  rowClassName?: (row: T) => string;
}) {
  const lang = useLang();
  const screens = Grid.useBreakpoint();
  const [page, setPage] = useState(1);
  const [chosenSize, setSize] = useState(pageSizes[0]);
  useEffect(() => setPage(1), [resetKey]);
  const size = paging ? paging.size : chosenSize;
  const total = paging ? paging.total : data.length;
  const pages = Math.max(1, Math.ceil(total / size));
  const current = paging ? paging.page : Math.min(page, pages);
  const rows = useMemo(() => (paging ? data : data.slice((current - 1) * size, current * size)), [data, current, size, paging]);
  const go = (p: number) => (paging ? paging.onPage(p) : setPage(p));
  const from = total === 0 ? 0 : (current - 1) * size + 1;
  const to = Math.min((current - 1) * size + rows.length, total);
  const Prev = lang === "ar" ? RightOutlined : LeftOutlined;
  const Next = lang === "ar" ? LeftOutlined : RightOutlined;
  const cards = !!renderCard && screens.md === false;

  const indexed: ColumnsType<T> = [
    { title: "#", key: "__idx", width: 56, align: "center", render: (_, __, i) => <span className="dt-idx">{(current - 1) * size + i + 1}</span> },
    ...columns,
  ];

  return (
    <div className="dt">
      {cards ? (
        loading ? <div className="dt-pad"><Skeleton active /></div> : rows.length === 0 ? <div className="dt-pad">{empty ?? <EmptyNote>{L("لا بيانات.", "No data.")}</EmptyNote>}</div> : (
          <div className="dt-cards">
            {rows.map((r) => (
              <div key={rowKey(r)} className={onRowClick ? "dt-card dt-card-click" : "dt-card"} role={onRowClick ? "button" : undefined} tabIndex={onRowClick ? 0 : undefined}
                onClick={onRowClick ? () => onRowClick(r) : undefined}
                onKeyDown={onRowClick ? (e) => {
                  // Enter أو المسافة على البطاقة نفسها كزر؛ مفاتيح الروابط داخلها لا تفتحها مرة ثانية.
                  if (e.target !== e.currentTarget || (e.key !== "Enter" && e.key !== " ")) return;
                  e.preventDefault();
                  onRowClick(r);
                } : undefined}>
                {renderCard!(r)}
              </div>
            ))}
          </div>
        )
      ) : (
        <Table<T> rowKey={rowKey} columns={indexed} dataSource={rows} loading={loading} pagination={false} scroll={{ x: minWidth }}
          expandable={expandable} rowClassName={rowClassName}
          locale={{ emptyText: empty ?? <EmptyNote>{L("لا بيانات.", "No data.")}</EmptyNote> }}
          onRow={onRowClick ? (r) => ({ onClick: () => onRowClick(r), className: "dt-click" }) : undefined} />
      )}
      {total > 0 && (
        <div className="dt-foot">
          {!paging && (
            <span className="dt-size">
              {L("عدد العناصر لكل صفحة", "Rows per page")}
              <Select size="small" variant="borderless" value={size} onChange={(v) => { setSize(v); setPage(1); }}
                options={pageSizes.map((n) => ({ value: n, label: n }))} aria-label={L("عدد العناصر لكل صفحة", "Rows per page")} />
            </span>
          )}
          <bdi className="dt-range">{from}-{to} {L("من", "of")} {total}</bdi>
          <span className="dt-nav">
            <Button type="text" shape="circle" icon={<Prev />} disabled={current <= 1} onClick={() => go(current - 1)} aria-label={L("السابق", "Previous")} />
            <Button type="text" shape="circle" icon={<Next />} disabled={current >= pages} onClick={() => go(current + 1)} aria-label={L("التالي", "Next")} />
          </span>
        </div>
      )}
    </div>
  );
}

/** تحية بحسب الساعة في القاهرة. */
export function greeting(): string {
  const h = Number(new Intl.DateTimeFormat("en-GB", { timeZone: "Africa/Cairo", hour: "numeric", hour12: false }).format(new Date()));
  return h < 12 ? L("صباح الخير", "Good morning") : L("مساء الخير", "Good evening");
}

/** شريط الترحيب الداكن أعلى الرئيسية: سطر صغير، والعنوان، والملخص، والإجراءات. */
export function Hero({ kicker, title, children, actions }: { kicker?: ReactNode; title: ReactNode; children?: ReactNode; actions?: ReactNode }) {
  return (
    <section className="lcp-hero">
      <svg className="lcp-hero-decor" viewBox="0 0 600 600" aria-hidden focusable="false">
        <path d="M120 70h250a90 90 0 0 1 90 90v120a90 90 0 0 1-90 90H230l-90 80v-80h-20a90 90 0 0 1-90-90V160a90 90 0 0 1 90-90z" />
        <path d="M330 300h170a70 70 0 0 1 70 70v80a70 70 0 0 1-70 70h-20v60l-70-60H330a70 70 0 0 1-70-70v-80a70 70 0 0 1 70-70z" />
      </svg>
      {kicker && <small>{kicker}</small>}
      <h1>{title}</h1>
      {children && <p>{children}</p>}
      {actions && <div className="lcp-hero-actions">{actions}</div>}
    </section>
  );
}

/** أول اسم من الاسم الكامل للتحية. */
export function firstName(full: string | null | undefined): string {
  return (full ?? "").trim().split(/\s+/)[0] ?? "";
}

export function initials(full: string | null | undefined): string {
  const parts = (full ?? "").trim().split(/\s+/).filter(Boolean);
  return parts.slice(0, 2).map((p) => p[0]).join("") || "؟";
}
