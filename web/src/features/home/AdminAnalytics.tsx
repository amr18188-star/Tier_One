import { useMemo, useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate } from "react-router-dom";
import {
  AuditOutlined, BankOutlined, BarChartOutlined, CalculatorOutlined, CalendarOutlined, CheckOutlined, ClusterOutlined, CloudServerOutlined,
  ExperimentOutlined, ExportOutlined, FileDoneOutlined, LeftOutlined, PartitionOutlined, ReadOutlined, RightOutlined, SafetyCertificateOutlined,
  TeamOutlined, TrophyOutlined, UnorderedListOutlined, WalletOutlined,
} from "@ant-design/icons";
import { api, query } from "../../api/client";
import type { Cycle, Results } from "../../api/collection";
import type { Scorecard } from "../../api/scorecard";
import type { Home } from "../../api/tier1";
import { L, useLang } from "../../shared/i18n";
import { StdCode, tint, usePillarMap } from "../../shared/pillars";
import { QueryState } from "../../shared/QueryState";
import { CountUp } from "../../shared/useCountUp";
import { usePermissions } from "../../shared/session";
import { PlanCtx, type PlanContext, type ResultMap } from "../strategy/model";
import { DotLegend, StateBar } from "../strategy/PlanMap";
import "../strategy/strategy.css";
import "./analytics.css";
import { approvalState, resultPeriods, shareOf, type ApprovalState } from "./adminModel";
import { useCycles, useStrategyTree, queryKeys } from "../../api/queries";

// ---------------------------------------------------------------- shared parts

/** One header for every section: icon and title at the start, the action at the end. */
function SectionHead({ id, icon, title, sub, action }: { id: string; icon: ReactNode; title: ReactNode; sub?: ReactNode; action?: ReactNode }) {
  return (
    <div className="han-head">
      <div className="han-head-title">
        <span className="han-head-icon" aria-hidden>{icon}</span>
        <h2 id={id}>{title}</h2>
        {sub && <p>{sub}</p>}
      </div>
      {action && <div className="han-head-action">{action}</div>}
    </div>
  );
}

function Card({ id, className, children }: { id: string; className?: string; children: ReactNode }) {
  return <section className={className ? `han-card ${className}` : "han-card"} aria-labelledby={id}>{children}</section>;
}

interface Segment { key: string; label: string; count: number; color: string }

/** One stacked bar and its legend right under it: colour, name, count and share of the segments' own total. */
function StackedBar({ label, segments }: { label: string; segments: Segment[] }) {
  const total = segments.reduce((s, x) => s + x.count, 0);
  return (
    <div className="han-stack">
      <div className="han-stack-track" role="img" aria-label={`${label}: ${segments.map((s) => `${s.label} ${s.count} (${shareOf(s.count, total)}%)`).join("، ")}`}>
        {segments.filter((s) => s.count > 0).map((s) => (
          <span key={s.key} style={{ flexGrow: s.count, background: s.color }} title={`${s.label}: ${s.count} (${shareOf(s.count, total)}%)`} />
        ))}
      </div>
      <ul className="han-legend">
        {segments.map((s) => (
          <li key={s.key} className={s.count === 0 ? "is-zero" : undefined}>
            <i style={{ background: s.color }} aria-hidden />
            <span>{s.label}</span>
            <b className="ltr">{s.count}</b>
            <small className="ltr">({shareOf(s.count, total)}%)</small>
          </li>
        ))}
      </ul>
    </div>
  );
}

const BADGE_TEXT: Record<ApprovalState, () => string> = {
  complete: () => L("معتمد", "Approved"),
  partial: () => L("غير مكتمل", "Incomplete"),
  none: () => L("لم يعتمد", "Not approved"),
};

function StatusBadge({ state }: { state: ApprovalState }) {
  return <span className={`han-badge is-${state}`}>{state === "complete" && <CheckOutlined aria-hidden />}{BADGE_TEXT[state]()}</span>;
}

function KpiCard({ icon, tone, value, label, hint, to, loading }: {
  icon: ReactNode; tone: "ok" | "review" | "gold"; value: ReactNode; label: string; hint?: ReactNode; to?: string; loading?: boolean;
}) {
  const body = (
    <>
      <span className="han-kpi-icon" aria-hidden>{icon}</span>
      <span className="han-kpi-text">
        <b className="ltr">{loading ? "…" : value}</b>
        <span>{label}</span>
        {hint && <small>{hint}</small>}
      </span>
    </>
  );
  return to ? <Link to={to} className={`han-kpi tone-${tone}`}>{body}</Link> : <div className={`han-kpi tone-${tone}`}>{body}</div>;
}

const Chevron = () => (useLang() === "ar" ? <LeftOutlined aria-hidden /> : <RightOutlined aria-hidden />);

/** Plan pillars keep their order; an icon per position, a neutral one beyond the six. */
const PILLAR_ICONS = [ReadOutlined, ExperimentOutlined, TeamOutlined, BankOutlined, WalletOutlined, CloudServerOutlined];
const pillarIcon = (index: number) => { const Icon = PILLAR_ICONS[index] ?? PartitionOutlined; return <Icon aria-hidden />; };
const num2 = (index: number) => String(index + 1).padStart(2, "0");
const periodText = (c: { periodLabel: string; name: string; trial: boolean }) => `${c.periodLabel || c.name}${c.trial ? L(" (تجريبي)", " (trial)") : ""}`;

// ---------------------------------------------------------------- 1. results

function ResultsCard({ results, periods, period, onPeriod }: {
  results: ReturnType<typeof useResults>; periods: Cycle[]; period: string | undefined; onPeriod: (id: string) => void;
}) {
  const r = results.data;
  const selected = period ?? r?.cycle.id ?? "";
  const count = (p: string) => r?.indicators.filter((i) => i.performance === p).length ?? 0;
  const judged = ["Achieved", "Near", "Behind"];
  return (
    <Card id="han-results" className={results.isFetching && !results.isLoading ? "is-refreshing" : undefined}>
      <SectionHead id="han-results" icon={<BarChartOutlined />} title={L("النتائج والمقارنات", "Results & comparisons")}
        action={<>
          <label className="han-period">
            <CalendarOutlined aria-hidden />
            <span className="sr-only">{L("الفترة", "Period")}</span>
            <select value={selected} disabled={periods.length === 0} onChange={(e) => onPeriod(e.target.value)}>
              {!periods.some((c) => c.id === selected) && r && <option value={r.cycle.id}>{periodText(r.cycle)}</option>}
              {periods.map((c) => <option key={c.id} value={c.id}>{periodText(c)}</option>)}
            </select>
          </label>
          <Link to={r ? `/results?cycle=${r.cycle.id}` : "/results"} className="han-link">{L("التفاصيل", "Details")} <Chevron /></Link>
        </>} />
      <QueryState query={results}>
        {() => !r ? (
          <p className="han-empty">{L("لا توجد دورة لها نتائج بعد.", "No cycle has results yet.")}</p>
        ) : r.formsApproved === 0 ? (
          <p className="han-empty">{L(`لم يعتمد نهائيًا أي نموذج في ${r.cycle.periodLabel || r.cycle.name} بعد؛ تظهر النتائج بعد اعتماد الجودة المركزية.`, "No approved forms in this period yet.")}</p>
        ) : (
          <div className="han-results">
            <div className="han-summary">
              <b className="ltr"><CountUp value={r.formsTotal} /></b>
              <span>{L("إجمالي النماذج", "Total forms")}</span>
              <small>{L(`${r.formsApproved} معتمد نهائيًا`, `${r.formsApproved} approved`)}</small>
            </div>
            <div className="han-results-main">
              <StackedBar label={L("حالة المؤشرات", "Indicator status")} segments={[
                { key: "a", label: L("محقق", "Achieved"), count: count("Achieved"), color: "var(--ds-status-ok)" },
                { key: "n", label: L("قريب من المستهدف", "Near target"), count: count("Near"), color: "var(--ds-status-near)" },
                { key: "b", label: L("متأخر", "Behind"), count: count("Behind"), color: "var(--ds-status-late-ink)" },
                { key: "o", label: L("بلا بيانات أو مستهدف أو أجل", "No data, target or due"), count: r.indicators.filter((i) => !judged.includes(i.performance)).length, color: "var(--ds-status-none)" },
              ]} />
              <p className="han-note">{L(`${r.unit.kind === "University" ? "الجامعة" : r.unit.name} · النسب من ${r.indicators.length} مؤشرًا، محسوبة من ${r.formsApproved} نموذجًا معتمدًا نهائيًا من ${r.formsTotal}.`,
                `Shares of ${r.indicators.length} indicators, from ${r.formsApproved} of ${r.formsTotal} approved forms.`)}</p>
            </div>
          </div>
        )}
      </QueryState>
    </Card>
  );
}

// ---------------------------------------------------------------- 2. score + calculator

function ScoreRow() {
  const card = useQuery({ queryKey: queryKeys.scorecard, queryFn: () => api.get<Scorecard>("/api/scorecard") });
  const c = card.data;
  return (
    <div className="han-pair">
      <Card id="han-score">
        <SectionHead id="han-score" icon={<TrophyOutlined />} title={L("بطاقة أداء الجامعة وفئتها", "University scorecard")} />
        <QueryState query={card}>
          {() => (
            <div className="han-score">
              <div className="han-score-main">
                <b className="ltr"><CountUp value={c!.overall.score} /></b>
                <span>{L(`الفئة ${c!.overall.tier.finalTier}: ${c!.tiers.find((t) => t.tier === c!.overall.tier.finalTier)?.nameAr ?? ""}`, `Tier ${c!.overall.tier.finalTier}`)}</span>
                <small>{c!.lastReported ? L(`حتى ${c!.lastReported.label}`, `Up to ${c!.lastReported.label}`) : L("لم تعتمد قيم أي ربع بعد", "No approved quarter yet")}</small>
              </div>
              <ol className="han-score-pillars">
                {c!.pillars.map((p, index) => (
                  <li key={p.id} style={tint(index)}><i aria-hidden /><span className="ltr-nums">{num2(index)}</span>{p.name}</li>
                ))}
              </ol>
            </div>
          )}
        </QueryState>
      </Card>
      <Card id="han-calc">
        <SectionHead id="han-calc" icon={<CalculatorOutlined />} title={L("حاسبة التصنيف والتفاصيل", "Classification calculator")}
          action={<Link to="/scorecard" className="han-link">{L("التفاصيل", "Details")} <Chevron /></Link>} />
        <QueryState query={card}>
          {() => (
            <ul className="han-calc">
              {c!.pillars.map((p, index) => (
                <li key={p.id} style={tint(index)}>
                  <span className="han-calc-name" title={p.name}><span className="ltr-nums">{num2(index)}</span>{p.name}</span>
                  <span className="han-calc-bar" role="img" aria-label={`${p.name}: ${p.score}`}><i style={{ width: `${Math.max(0, Math.min(100, p.score))}%` }} /></span>
                  <b className="ltr">{p.score}</b>
                </li>
              ))}
            </ul>
          )}
        </QueryState>
      </Card>
    </div>
  );
}

// ---------------------------------------------------------------- 3. strategic plan

function PlanCard({ results }: { results: Results | null | undefined }) {
  const navigate = useNavigate();
  const tree = useStrategyTree();
  const map: ResultMap = useMemo(() => new Map((results?.indicators ?? []).map((i) => [i.code, i])), [results]);
  const t = tree.data;
  const ctx: PlanContext | null = t ? {
    tree: t, results: map, resultsFrom: results ? `${results.unit.name} · ${periodText(results.cycle)}` : null, resultsYear: results?.cycle.targetYear ?? null,
    select: (k) => navigate(k === "root" ? "/strategy?view=explore" : `/strategy?view=explore&node=${k}`),
    edit: () => undefined,
  } : null;
  const years = t ? `${t.years[0]}–${t.years[t.years.length - 1]}` : "";
  return (
    <Card id="han-plan">
      <SectionHead id="han-plan" icon={<PartitionOutlined />} title={L(`الخطة الاستراتيجية ${years}`, `Strategic plan ${years}`)}
        action={<Link to="/strategy" className="han-btn"><ExportOutlined aria-hidden /> {L("افتح الخطة", "Open the plan")}</Link>} />
      <QueryState query={tree}>
        {() => ctx && (
          <PlanCtx.Provider value={ctx}>
            <div className="han-plan-legend"><DotLegend list={t!.pillars.flatMap((p) => p.standards.flatMap((s) => s.indicators))} /></div>
            {t!.pillars.length === 0 ? <p className="han-empty">{L("لا توجد ركائز في الخطة بعد.", "The plan has no pillars yet.")}</p> : (
              <div className="han-axes">
                {t!.pillars.map((p, index) => {
                  const list = p.standards.flatMap((s) => s.indicators);
                  return (
                    <button key={p.id} type="button" className="han-axis" style={tint(index)} onClick={() => ctx.select(`P:${p.id}`)}>
                      <span className="han-axis-top">
                        <span className="han-axis-num ltr-nums">{num2(index)}</span>
                        <span className="han-axis-icon" aria-hidden>{pillarIcon(index)}</span>
                      </span>
                      <b className="han-axis-name">{p.nameAr}</b>
                      <span className="han-axis-meta">
                        <span>{L(`${p.standards.length} معيار · ${list.length} مؤشر`, `${p.standards.length} standards · ${list.length} indicators`)}</span>
                        <span className="han-axis-go"><Chevron /></span>
                      </span>
                      <StateBar list={list} />
                    </button>
                  );
                })}
              </div>
            )}
          </PlanCtx.Provider>
        )}
      </QueryState>
    </Card>
  );
}

// ---------------------------------------------------------------- 4–6. definitions

const STD_PAGE = 8;

function DefinitionSections({ home }: { home: ReturnType<typeof useHome> }) {
  const d = home.data;
  const pillarMap = usePillarMap();
  const [all, setAll] = useState(false);
  const status = (key: string) => d?.definitionStatus.find((s) => s.key === key)?.count ?? 0;
  const approved = status("Approved");
  const total = d?.definitionStatus.reduce((s, x) => s + x.count, 0) ?? 0;
  const standards = d?.standards ?? [];
  const incomplete = standards.filter((s) => approvalState(s.approved, s.indicators) !== "complete").length;
  const shown = all ? standards : standards.slice(0, STD_PAGE);

  return (
    <>
      <div className="han-kpis">
        <KpiCard icon={<UnorderedListOutlined />} tone="ok" label={L("مؤشرات معتمدة", "Approved indicators")} value={<><CountUp value={approved} /> / {total}</>}
          hint={L("تعريفات معتمدة في القاموس", "Approved definitions")} to="/indicators?status=Approved" loading={home.isLoading} />
        <KpiCard icon={<FileDoneOutlined />} tone="review" label={L("تنتظر الاعتماد", "Awaiting approval")} value={<CountUp value={status("UnderReview")} />}
          hint={L("تعريفات قيد الاعتماد", "Definitions under review")} to="/indicators?status=UnderReview" loading={home.isLoading} />
        {d?.structureAveragePercent !== null && d?.structureAveragePercent !== undefined && (
          <KpiCard icon={<ClusterOutlined />} tone="gold" label={L("اكتمال هياكل الكليات", "College structures complete")} value={<CountUp value={d.structureAveragePercent} suffix="%" />}
            hint={L(`${d.collegeCount} كلية ومعهد`, `${d.collegeCount} colleges`)} to="/structure" loading={home.isLoading} />
        )}
      </div>

      <Card id="han-defs">
        <SectionHead id="han-defs" icon={<SafetyCertificateOutlined />} title={L("حالة تعريفات المؤشرات", "Indicator definition status")}
          sub={L("اعتماد تعريف المؤشر في القاموس، وليس أداءه الفعلي", "Approval of the definition, not performance")}
          action={d && <span className="han-meta ltr-nums">{L(`${total} مؤشرًا`, `${total} indicators`)}</span>} />
        <QueryState query={home}>
          {() => total === 0 ? <p className="han-empty">{L("لا توجد تعريفات مؤشرات بعد.", "No indicator definitions yet.")}</p> : (
            <StackedBar label={L("حالة التعريفات", "Definition status")} segments={[
              { key: "Approved", label: L("معتمد", "Approved"), count: approved, color: "var(--ds-status-ok-ink)" },
              { key: "UnderReview", label: L("قيد الاعتماد", "Under review"), count: status("UnderReview"), color: "var(--ds-status-review)" },
              { key: "Draft", label: L("مسودة", "Draft"), count: status("Draft"), color: "var(--ds-status-draft)" },
            ]} />
          )}
        </QueryState>
      </Card>

      <Card id="han-stds">
        <SectionHead id="han-stds" icon={<AuditOutlined />} title={L("اعتماد المؤشرات بحسب المعيار", "Approval by standard")}
          action={d && standards.length > 0 && (
            <span className={`han-meta${incomplete > 0 ? " is-warn" : ""}`}>
              {incomplete === 0 ? L("كل المعايير مكتملة", "All standards complete") : L(`${incomplete} من ${standards.length} غير مكتمل`, `${incomplete} of ${standards.length} incomplete`)}
            </span>
          )} />
        <QueryState query={home}>
          {() => standards.length === 0 ? <p className="han-empty">{L("لا توجد معايير بعد.", "No standards yet.")}</p> : (
            <>
              <div className="han-std-head" aria-hidden>
                <span>{L("المعيار", "Standard")}</span><span>{L("الحالة", "Status")}</span><span>{L("التقدم", "Progress")}</span><span>{L("المعتمد", "Approved")}</span>
              </div>
              <ul className="han-stds">
                {shown.map((s) => {
                  const state = approvalState(s.approved, s.indicators);
                  return (
                    <li key={s.code}>
                      <Link to={`/indicators?standard=${s.code}`} className={`han-std is-${state}`}>
                        <StdCode code={s.code} map={pillarMap.map} />
                        <span className="han-std-name" title={s.nameAr}>{s.nameAr}</span>
                        <StatusBadge state={state} />
                        <span className="han-std-bar" role="img" aria-label={L(`${s.approved} من ${s.indicators} معتمد`, `${s.approved} of ${s.indicators} approved`)}>
                          <i style={{ width: `${shareOf(s.approved, s.indicators)}%` }} />
                        </span>
                        <b className="han-std-count ltr">{s.approved}/{s.indicators}</b>
                      </Link>
                    </li>
                  );
                })}
              </ul>
              {standards.length > STD_PAGE && (
                <button type="button" className="han-more" aria-expanded={all} onClick={() => setAll(!all)}>
                  {all ? L("عرض أقل", "Show less") : L(`عرض كل المعايير (${standards.length})`, `Show all standards (${standards.length})`)}
                </button>
              )}
            </>
          )}
        </QueryState>
      </Card>
    </>
  );
}

// ---------------------------------------------------------------- page section

const useHome = () => useQuery({ queryKey: queryKeys.home, queryFn: () => api.get<Home>("/api/home") });
function useResults(cycleId: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.results(cycleId, undefined),
    queryFn: () => api.get<Results | null>(`/api/results${query({ cycleId })}`),
    enabled, placeholderData: (prev) => prev,
  });
}

/**
 * The administrator's analytics & performance block, under the work list. Same queries, permissions and
 * calculations as the general home; the period chosen in the results card also drives the plan's status bars.
 */
export function AdminAnalytics() {
  const perms = usePermissions();
  const seesResults = perms.can("dc.results", "V");
  const seesPlan = perms.can("kpi.strategy", "V");
  const planning = perms.can("kpi.dictionary", "E") || perms.can("kpi.dictionary", "approve");
  const [period, setPeriod] = useState<string | undefined>(undefined);
  const home = useHome();
  const cycles = useCycles({ enabled: seesResults });
  const results = useResults(period, seesResults);
  const periods = useMemo(() => resultPeriods(cycles.data ?? []), [cycles.data]);

  return (
    <div className="han">
      {seesResults && <ResultsCard results={results} periods={periods} period={period} onPeriod={setPeriod} />}
      {perms.scopeOf("dc.results", "V") === "A" && <ScoreRow />}
      {seesPlan && <PlanCard results={seesResults ? results.data : null} />}
      {planning && <DefinitionSections home={home} />}
    </div>
  );
}
