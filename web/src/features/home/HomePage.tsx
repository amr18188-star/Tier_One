import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { Progress, Tag } from "antd";
import {
  BarChartOutlined, CheckCircleOutlined, ClockCircleOutlined, ClusterOutlined, FileDoneOutlined, FormOutlined, TrophyOutlined, UnorderedListOutlined,
  SearchOutlined, WarningOutlined, EditOutlined, ArrowLeftOutlined, ArrowRightOutlined, CalendarOutlined,
} from "@ant-design/icons";
import { api } from "../../api/client";
import type { Cycle, FormRow, Results } from "../../api/collection";
import type { Home } from "../../api/tier1";
import { ProgressToTarget, SegmentBar } from "../../shared/charts";
import { formatDate } from "../../shared/format";
import { L, useLang } from "../../shared/i18n";
import { dueText, submissionStatus } from "../../shared/labels";
import { dueLabel, daysLeft } from "../../shared/period";
import { StdCode, tint, usePillarMap } from "../../shared/pillars";
import { usePermissions } from "../../shared/session";
import { CountUp } from "../../shared/useCountUp";
import { PlanGlance } from "../strategy/PlanGlance";
import type { Scorecard } from "../../api/scorecard";
import { filterWork, featuredCycle, type DueSort, type WorkFilter } from "./adminModel";
import { AdminAnalytics } from "./AdminAnalytics";
import "./home.css";

/** الدرجة الكلية والفئة ودرجة كل ركيزة، بربط إلى بطاقة الأداء. */
function ScoreGlance() {
  const card = useQuery({ queryKey: ["scorecard"], queryFn: () => api.get<Scorecard>("/api/scorecard") });
  const c = card.data;
  return (
    <Panel title={L("بطاقة أداء الجامعة وفئتها", "University scorecard")} icon={<TrophyOutlined aria-hidden />} query={card}
      extra={<Link to="/scorecard">{L("حاسبة التصنيف والتفاصيل", "Details")}</Link>}>
      {() => (
        <div className="t1-score">
          <div className="t1-score-main">
            <b className="ltr">{c!.overall.score}</b>
            <span>{L(`الفئة ${c!.overall.tier.finalTier}: ${c!.tiers.find((t) => t.tier === c!.overall.tier.finalTier)?.nameAr ?? ""}`, `Tier ${c!.overall.tier.finalTier}`)}</span>
            <small>{c!.lastReported ? L(`حتى ${c!.lastReported.label}`, `Up to ${c!.lastReported.label}`) : L("لم تعتمد قيم أي ربع بعد", "No approved quarter yet")}</small>
          </div>
          <ul className="t1-score-pillars">
            {c!.pillars.map((p, index) => (
              <li key={p.id} style={tint(index)}>
                <span>{p.name}</span>
                <span className="t1-score-bar"><i style={{ width: `${Math.min(100, p.score)}%` }} /></span>
                <b className="ltr">{p.score}</b>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Panel>
  );
}
import { EmptyNote, firstName, greeting, Hero, Panel, StatTile } from "../../shared/ui";

/**
 * الصفحة الأولى بحسب الدور، والأهم أولًا:
 * «مطلوب منك» لكل الأدوار؛ ثم نماذج المستخدم بمواعيدها لمدخل البيانات والمراجع؛ ثم أداء آخر دورة لمن يرى النتائج (القيادة والعميد)؛
 * ثم صورة القاموس والتعارضات لمن يعد الخطة أو يعتمدها فقط، فلا يرى مدخل البيانات ما لا يخص عمله.
 */
function GeneralHome() {
  const perms = usePermissions();
  const home = useQuery({ queryKey: ["home"], queryFn: () => api.get<Home>("/api/home") });
  const planning = perms.can("kpi.dictionary", "E") || perms.can("kpi.dictionary", "approve");
  const seesForms = perms.can("dc.forms", "V");
  const seesResults = perms.can("dc.results", "V");
  const mine = useQuery({ queryKey: ["forms", "mine"], queryFn: () => api.get<FormRow[]>("/api/forms?awaitingMe=true"), enabled: seesForms });
  const results = useQuery({ queryKey: ["results", undefined, undefined], queryFn: () => api.get<Results | null>("/api/results"), enabled: seesResults });
  const d = home.data;
  const pillarMap = usePillarMap();
  const approved = d?.definitionStatus.find((s) => s.key === "Approved")?.count ?? 0;
  const total = d?.definitionStatus.reduce((s, x) => s + x.count, 0) ?? 0;
  const forms = [...(mine.data ?? [])].sort((a, b) => Number(b.status === "Returned") - Number(a.status === "Returned") || a.dueOn.localeCompare(b.dueOn));
  const r = results.data;

  // سطر الترحيب يلخص ما يهم هذا المستخدم الآن.
  const summary = forms.length > 0
    ? L(forms.length === 1 ? `لديك نموذج واحد ينتظرك، موعده ${dueText(forms[0].dueOn)}.`
      : `لديك ${forms.length === 2 ? "نموذجان ينتظرانك" : `${forms.length} نماذج تنتظرك`}، أقربها موعدًا ${dueText(forms[0].dueOn)}.`, `${forms.length} forms are waiting for you.`)
    : planning && d
      ? L(`${total} مؤشرًا في الإطار الشامل لتقييم أداء الجامعات، اعتُمد منها ${approved}.`, `${approved} of ${total} indicators approved.`)
      : r
        ? L(`${r.cycle.name}: اعتمد ${r.formsApproved} من ${r.formsTotal} نموذجًا حتى الآن.`, `${r.cycle.name}: ${r.formsApproved} of ${r.formsTotal} forms approved.`)
        : L("لا شيء ينتظرك الآن.", "Nothing is waiting for you right now.");

  return (
    <>
      <Hero kicker={greeting()} title={`${L("أهلًا", "Welcome")} ${firstName(d?.displayName)}`}>
        {home.isLoading ? L("جار تحميل ما يخصك…", "Loading…") : summary}
      </Hero>

      <Panel title={L("مطلوب منك", "Your tasks")} icon={<CheckCircleOutlined aria-hidden />} query={home}>
        {() => d!.tasks.length === 0 ? (
          <div className="t1-done"><CheckCircleOutlined aria-hidden /> {L("لا توجد مهام تنتظرك الآن.", "Nothing is waiting for you right now.")}</div>
        ) : (
          <div className="t1-tasks">
            {d!.tasks.map((t) => (
              <Link key={t.key} to={t.link} className={`t1-task tone-${t.tone}`}>
                <b className="ltr">{t.count}</b>
                <span><strong>{t.titleAr}</strong><small>{t.hintAr}</small></span>
              </Link>
            ))}
          </div>
        )}
      </Panel>

      {seesForms && forms.length > 0 && (
        <Panel title={L("نماذج تنتظرك", "Forms waiting for you")} icon={<FormOutlined aria-hidden />} extra={<Link to="/forms">{L("كل النماذج", "All forms")}</Link>}>
          <ul className="t1-myforms">
            {forms.slice(0, 6).map((f) => (
              <li key={f.id}>
                <Link to={`/forms/${f.id}`}>
                  <span className="t1-myforms-name">
                    <b>{f.unitName}</b>
                    <small className="muted">{f.cycleName} · {f.stepName}</small>
                  </span>
                  <Tag color={submissionStatus[f.status].color}>{submissionStatus[f.status].text}</Tag>
                  <Progress percent={f.required ? Math.round((100 * f.completed) / f.required) : 0} size="small" className="t1-myforms-progress"
                    format={() => <span className="ltr">{f.completed}/{f.required}</span>} />
                  <span className={`t1-due${f.overdue ? " late" : ""}`}><ClockCircleOutlined aria-hidden /> {dueText(f.dueOn)} <small className="muted ltr t1-due-date">{formatDate(f.dueOn)}</small></span>
                </Link>
              </li>
            ))}
          </ul>
        </Panel>
      )}

      {seesResults && r && (
        <Panel title={L(`الأداء — ${r.cycle.name}`, `Performance — ${r.cycle.name}`)} icon={<BarChartOutlined aria-hidden />}
          extra={<Link to="/results">{L("النتائج والمقارنات", "Results")}</Link>}>
          {r.formsApproved === 0 ? (
            <EmptyNote>{L(`لم يعتمد نهائيًا أي نموذج ${r.unit.kind === "University" ? "في الجامعة" : `في ${r.unit.name}`} بعد؛ تظهر النتائج بعد اعتماد الجودة المركزية.`, "No approved forms yet.")}</EmptyNote>
          ) : (
            <>
              <SegmentBar label={L("حالة المؤشرات", "Indicator status")} segments={[
                { key: "a", label: L("محقق", "Achieved"), count: r.indicators.filter((i) => i.performance === "Achieved").length, color: "var(--ds-status-ok)" },
                { key: "n", label: L("قريب من المستهدف", "Near"), count: r.indicators.filter((i) => i.performance === "Near").length, color: "var(--ds-status-near)" },
                { key: "b", label: L("متأخر", "Behind"), count: r.indicators.filter((i) => i.performance === "Behind").length, color: "var(--ds-status-late-ink)" },
                { key: "o", label: L("بلا بيانات أو مستهدف أو جزئي", "Not judged"), count: r.indicators.filter((i) => !["Achieved", "Near", "Behind"].includes(i.performance)).length, color: "var(--ds-status-none)" },
              ]} />
              <p className="t1-help mt">{L(`${r.unit.kind === "University" ? "الجامعة" : r.unit.name} · محسوبة من ${r.formsApproved} نموذجًا معتمدًا نهائيًا من ${r.formsTotal}.`,
                `From ${r.formsApproved} of ${r.formsTotal} approved forms.`)}</p>
            </>
          )}
        </Panel>
      )}

      {perms.scopeOf("dc.results", "V") === "A" && <ScoreGlance />}

      <PlanGlance />

      {planning && (
        <>
          <div className="lcp-stats">
            <StatTile icon={<UnorderedListOutlined />} label={L("مؤشرات معتمدة", "Approved indicators")} value={`${approved} / ${total}`} to="/indicators?status=Approved" loading={home.isLoading} />
            <StatTile icon={<FileDoneOutlined />} tone="navy" label={L("تنتظر الاعتماد", "Awaiting approval")}
              value={d?.definitionStatus.find((s) => s.key === "UnderReview")?.count ?? 0} to="/indicators?status=UnderReview" loading={home.isLoading} />
            {d?.structureAveragePercent !== null && d?.structureAveragePercent !== undefined && (
              <StatTile icon={<ClusterOutlined />} tone="gold" label={L("اكتمال هياكل الكليات", "College structures complete")} value={`${d.structureAveragePercent}%`}
                hint={L(`${d.collegeCount} كلية ومعهد`, `${d.collegeCount} colleges`)} to="/structure" loading={home.isLoading} />
            )}
          </div>

          <div className="lcp-grid-2">
            <Panel title={L("حالة تعريفات المؤشرات", "Indicator definitions")} query={home}>
              {() => (
                <SegmentBar label={L("حالة التعريفات", "Definition status")} segments={[
                  { key: "Approved", label: L("معتمد", "Approved"), count: approved, color: "var(--ds-status-ok-ink)" },
                  { key: "UnderReview", label: L("قيد الاعتماد", "Under review"), count: d!.definitionStatus.find((s) => s.key === "UnderReview")?.count ?? 0, color: "var(--ds-status-review)" },
                  { key: "Draft", label: L("مسودة", "Draft"), count: d!.definitionStatus.find((s) => s.key === "Draft")?.count ?? 0, color: "var(--ds-status-draft)" },
                ]} />
              )}
            </Panel>
          </div>

          {d && d.standards.length > 0 && (
            <Panel title={L("اعتماد المؤشرات بحسب المعيار", "Approval by standard")} icon={<UnorderedListOutlined aria-hidden />}>
              {d.standards.map((s) => (
                <Link key={s.code} to={`/indicators?standard=${s.code}`} className="t1-std">
                  <StdCode code={s.code} map={pillarMap.map} />
                  <span>
                    {s.nameAr}
                  </span>
                  <ProgressToTarget value={s.approved} max={s.indicators}
                    label={<><small>{L("معتمد", "Approved")}</small><small className="ltr">{s.approved}/{s.indicators}</small></>} />
                </Link>
              ))}
            </Panel>
          )}
        </>
      )}
    </>
  );
}

const PAGE_SIZE = 8;

/** The administrator's work counts come from /api/home; cycle totals come only from /api/cycles. */
function AdminHome() {
  const lang = useLang();
  const Forward = lang === "ar" ? ArrowLeftOutlined : ArrowRightOutlined;
  const home = useQuery({ queryKey: ["home"], queryFn: () => api.get<Home>("/api/home") });
  const mine = useQuery({ queryKey: ["forms", "mine"], queryFn: () => api.get<FormRow[]>("/api/forms?awaitingMe=true") });
  const cycles = useQuery({ queryKey: ["cycles"], queryFn: () => api.get<Cycle[]>("/api/cycles") });
  const [filter, setFilter] = useState<WorkFilter>("all");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<DueSort>("nearest");
  const [page, setPage] = useState(1);
  const rows = mine.data ?? [];
  const shown = useMemo(() => filterWork(rows, filter, search, sort), [rows, filter, search, sort]);
  const pageCount = Math.max(1, Math.ceil(shown.length / PAGE_SIZE));
  const currentPage = Math.min(page, pageCount);
  const pageRows = shown.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);
  const cycle = featuredCycle(cycles.data ?? []);
  const task = (key: string) => home.data?.tasks.find((t) => t.key === key);
  const cards = [
    { key: "forms_returned", title: L("نماذج تتطلب تصحيحًا", "Forms to correct"), hint: L("أُعيدت لك بملاحظات المراجع", "Returned with review notes"), icon: <WarningOutlined />, tone: "bad", link: "/forms?view=Returned" },
    { key: "forms_review", title: L("نماذج بانتظار المراجعة", "Forms to review"), hint: L("تنتظر قرارك في مسار الاعتماد", "Waiting for your review"), icon: <ClockCircleOutlined />, tone: "review", link: "/forms" },
    { key: "forms_fill", title: L("نماذج بانتظار إدخال البيانات", "Forms to complete"), hint: L("ابدأ الإدخال أو استكمل القيم", "Enter or complete values"), icon: <EditOutlined />, tone: "entry", link: "/forms" },
    { key: "structure_incomplete", title: L("هياكل غير مكتملة", "Incomplete structures"), hint: L("كليات تحتاج استكمال هيكلها", "Colleges needing structure updates"), icon: <ClusterOutlined />, tone: "structure", link: "/structure" },
  ];
  const filters: { key: WorkFilter; text: string; count: number }[] = [
    { key: "all", text: L("الكل", "All"), count: rows.length },
    { key: "returned", text: L("تحتاج تصحيحًا", "To correct"), count: rows.filter((r) => r.status === "Returned").length },
    { key: "review", text: L("بانتظار المراجعة", "To review"), count: rows.filter((r) => r.status === "InReview").length },
    { key: "entry", text: L("بانتظار إدخال البيانات", "To complete"), count: rows.filter((r) => r.status === "NotStarted" || r.status === "InProgress").length },
  ];
  const changeFilter = (next: WorkFilter) => { setFilter(next); setPage(1); };
  const changeSearch = (next: string) => { setSearch(next); setPage(1); };
  const changeSort = (next: DueSort) => { setSort(next); setPage(1); };

  return (
    <div className="home-admin">
      <header className="home-admin-hero" data-section="dark">
        <div className="home-admin-welcome">
          <span className="home-admin-eyebrow">{greeting()}</span>
          <h1>{L("أهلًا", "Welcome")} {firstName(home.data?.displayName)}</h1>
          <p>{cycle ? L(`متابعة ${cycle.periodLabel || cycle.name}`, `Overview of ${cycle.periodLabel || cycle.name}`)
            : cycles.isLoading ? L("جار تحميل الدورة…", "Loading cycle…")
              : cycles.isError ? L("تعذر تحميل ملخص الدورات.", "Could not load cycle summary.")
                : L("لا توجد دورة جمع بيانات متاحة بعد.", "No collection cycle is available yet.")}</p>
        </div>
        <dl className="home-admin-hero-stats" aria-label={L("ملخص الدورة", "Cycle summary")}>
          <div><dt>{L("نماذج الدورة", "Cycle forms")}</dt><dd>{cycle ? <CountUp value={cycle.forms} /> : "—"}</dd></div>
          <div><dt>{L("متأخر عن الموعد", "Overdue")}</dt><dd className={cycle && cycle.overdue > 0 ? "is-late" : undefined}>{cycle ? <CountUp value={cycle.overdue} /> : "—"}</dd></div>
          <div><dt>{L("نماذج معتمدة", "Approved forms")}</dt><dd>{cycle ? <span className="ltr"><CountUp value={cycle.formsByStatus.Approved ?? 0} />/{cycle.forms}</span> : "—"}</dd></div>
          <div><dt>{L("قيد المراجعة", "In review")}</dt><dd>{cycle ? <CountUp value={cycle.formsByStatus.InReview ?? 0} /> : "—"}</dd></div>
        </dl>
      </header>

      <section className="home-admin-priorities" aria-label={L("أولوياتك", "Your priorities")}>
        {cards.map((card) => {
          const item = task(card.key);
          return <Link key={card.key} to={item?.link ?? card.link} className={`home-admin-kpi tone-${card.tone}`}>
            <span className="home-admin-kpi-icon" aria-hidden>{card.icon}</span>
            <strong>{card.title}</strong>
            <b className="ltr">{home.isLoading || home.isError ? "—" : <CountUp value={item?.count ?? 0} />}</b>
            <small>{card.hint}</small>
            <Forward className="home-admin-kpi-arrow" aria-hidden />
          </Link>;
        })}
      </section>
      {home.isError && <p className="home-admin-error" role="alert">{L("تعذر تحميل مؤشرات المهام.", "Could not load task counts.")} <button type="button" onClick={() => void home.refetch()}>{L("إعادة المحاولة", "Retry")}</button></p>}

      <section className="home-admin-work" aria-labelledby="home-work-title">
        <div className="home-admin-work-head">
          <div><span className="home-admin-section-icon" aria-hidden><FileDoneOutlined /></span><h2 id="home-work-title">{L("نماذج تنتظرك", "Forms waiting for you")}</h2><p>{L("النماذج التي تتطلب إجراءً منك الآن", "Forms that need your action now")}</p></div>
          <Link to="/forms">{L("كل النماذج", "All forms")} <Forward aria-hidden /></Link>
        </div>
        <div className="home-admin-toolbar">
          <div className="home-admin-filters" role="group" aria-label={L("تصفية حسب الإجراء", "Filter by action")}>
            {filters.map((option) => <button type="button" key={option.key} className={filter === option.key ? "is-active" : undefined}
              aria-pressed={filter === option.key} onClick={() => changeFilter(option.key)}>{option.text} <span className="ltr">{mine.isSuccess ? option.count : "—"}</span></button>)}
          </div>
          <div className="home-admin-tools">
            <label className="home-admin-search"><SearchOutlined aria-hidden /><span className="sr-only">{L("ابحث في النماذج", "Search forms")}</span>
              <input type="search" value={search} onChange={(event) => changeSearch(event.target.value)} placeholder={L("ابحث عن جهة أو دورة…", "Search unit or cycle…")} /></label>
            <label className="home-admin-sort"><span className="sr-only">{L("ترتيب النماذج", "Sort forms")}</span>
              <select value={sort} onChange={(event) => changeSort(event.target.value as DueSort)}>
                <option value="nearest">{L("الأقرب موعدًا", "Nearest due")}</option><option value="farthest">{L("الأبعد موعدًا", "Farthest due")}</option>
              </select></label>
          </div>
        </div>
        {mine.isLoading ? <div className="home-admin-state" role="status">{L("جار تحميل النماذج…", "Loading forms…")}</div>
          : mine.isError ? <div className="home-admin-state" role="alert">{L("تعذر تحميل النماذج التي تنتظرك.", "Could not load forms waiting for you.")} <button type="button" onClick={() => void mine.refetch()}>{L("إعادة المحاولة", "Retry")}</button></div>
            : rows.length === 0 ? <div className="home-admin-state"><CheckCircleOutlined aria-hidden /> {L("لا توجد نماذج تنتظر إجراءك الآن.", "No forms need your action now.")}</div>
              : shown.length === 0 ? <div className="home-admin-state">{L("لا توجد نماذج تطابق البحث أو التصفية.", "No forms match your search or filter.")} <button type="button" onClick={() => { changeFilter("all"); changeSearch(""); }}>{L("مسح التصفية", "Clear filters")}</button></div>
                : <>
                  <div className="home-admin-table-head" aria-hidden>
                    <span>{L("الجهة", "Unit")}</span><span>{L("الدورة", "Cycle")}</span><span>{L("الحالة", "Status")}</span><span>{L("التقدم", "Progress")}</span><span>{L("الموعد", "Due")}</span><span>{L("الإجراء", "Action")}</span>
                  </div>
                  <ul className="home-admin-rows">
                    {pageRows.map((row) => {
                      const action = row.status === "Returned" ? L("تصحيح", "Correct") : row.status === "InReview" ? L("مراجعة", "Review") : row.status === "NotStarted" ? L("بدء الإدخال", "Start entry") : L("استكمال الإدخال", "Continue entry");
                      const days = daysLeft(row.dueOn);
                      return <li key={row.id}>
                        <div className="home-admin-unit"><b title={row.unitName}>{row.unitName}</b><small title={row.parentName ?? undefined}>{row.parentName || L("جهة مركزية", "Central unit")}</small></div>
                        <div className="home-admin-cycle"><CalendarOutlined aria-hidden /><span>{row.cycleName}</span></div>
                        <div className="home-admin-status"><Tag color={submissionStatus[row.status].color}>{submissionStatus[row.status].text}</Tag></div>
                        <div className="home-admin-progress"><span className="ltr">{row.completed}/{row.required}</span><Progress percent={row.required ? Math.min(100, Math.round(100 * row.completed / row.required)) : 0} showInfo={false} size="small" /></div>
                        <div className={`home-admin-due${row.overdue ? " is-late" : days <= 3 ? " is-near" : ""}`}><ClockCircleOutlined aria-hidden /><span>{dueLabel(row.dueOn)}<small className="ltr">{formatDate(row.dueOn)}</small></span></div>
                        <Link className="home-admin-action" to={`/forms/${row.id}`}>{action}<Forward aria-hidden /></Link>
                      </li>;
                    })}
                  </ul>
                  {pageCount > 1 && <nav className="home-admin-pager" aria-label={L("صفحات النماذج", "Form pages")}>
                    <button type="button" disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}>{L("السابق", "Previous")}</button>
                    <span>{L(`صفحة ${currentPage} من ${pageCount}`, `Page ${currentPage} of ${pageCount}`)}</span>
                    <button type="button" disabled={currentPage === pageCount} onClick={() => setPage(currentPage + 1)}>{L("التالي", "Next")}</button>
                  </nav>}
                </>}
      </section>
      {home.data?.tasks.some((item) => !cards.some((card) => card.key === item.key)) && (
        <Panel title={L("مهام أخرى", "Other tasks")} icon={<CheckCircleOutlined aria-hidden />}>
          <div className="t1-tasks">
            {home.data.tasks.filter((item) => !cards.some((card) => card.key === item.key)).map((item) => (
              <Link key={item.key} to={item.link} className={`t1-task tone-${item.tone}`}>
                <b className="ltr">{item.count}</b><span><strong>{item.titleAr}</strong><small>{item.hintAr}</small></span>
              </Link>
            ))}
          </div>
        </Panel>
      )}
      <AdminAnalytics />
    </div>
  );
}

export default function HomePage() {
  const perms = usePermissions();
  return perms.isSuperAdmin ? <AdminHome /> : <GeneralHome />;
}
