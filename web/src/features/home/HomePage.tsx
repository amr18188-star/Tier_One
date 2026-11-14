import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { Progress, Tag } from "antd";
import {
  BarChartOutlined, CheckCircleOutlined, ClockCircleOutlined, ClusterOutlined, FileDoneOutlined, FormOutlined, TrophyOutlined, UnorderedListOutlined,
} from "@ant-design/icons";
import { api } from "../../api/client";
import type { FormRow, Results } from "../../api/collection";
import type { Home } from "../../api/tier1";
import { ProgressToTarget, SegmentBar } from "../../shared/charts";
import { formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { dueText, submissionStatus } from "../../shared/labels";
import { StdCode, tint, usePillarMap } from "../../shared/pillars";
import { usePermissions } from "../../shared/session";
import { PlanGlance } from "../strategy/PlanGlance";
import type { Scorecard } from "../../api/scorecard";

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
export default function HomePage() {
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
