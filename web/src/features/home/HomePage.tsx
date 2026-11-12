import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { Progress, Tag } from "antd";
import {
  BarChartOutlined, CheckCircleOutlined, ClockCircleOutlined, ClusterOutlined, FileDoneOutlined, FormOutlined, UnorderedListOutlined, WarningOutlined,
} from "@ant-design/icons";
import { api } from "../../api/client";
import type { FormRow, Results } from "../../api/collection";
import type { Home } from "../../api/tier1";
import { ProgressToTarget, SegmentBar } from "../../shared/charts";
import { formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { dueText, submissionStatus } from "../../shared/labels";
import { usePermissions } from "../../shared/session";
import { EmptyNote, firstName, greeting, Hero, Panel, StatTile } from "../../shared/ui";

/**
 * الصفحة الأولى بحسب الدور، والأهم أولًا:
 * «مطلوب منك» لكل الأدوار؛ ثم نماذج المستخدم بمواعيدها لمدخل البيانات والمراجع؛ ثم أداء آخر دورة لمن يرى النتائج (القيادة والعميد)؛
 * ثم صورة القاموس والتعارضات لمن يعد الخطة أو يعتمدها فقط، فلا يرى مدخل البيانات ما لا يخص عمله.
 */
export default function HomePage() {
  const perms = usePermissions();
  const home = useQuery({ queryKey: ["home"], queryFn: () => api.get<Home>("/api/home") });
  const planning = perms.can("kpi.dictionary", "E") || perms.can("kpi.dictionary", "approve") || perms.can("kpi.conflicts", "E") || perms.can("kpi.conflicts", "decide");
  const seesForms = perms.can("dc.forms", "V");
  const seesResults = perms.can("dc.results", "V");
  const mine = useQuery({ queryKey: ["forms", "mine"], queryFn: () => api.get<FormRow[]>("/api/forms?awaitingMe=true"), enabled: seesForms });
  const results = useQuery({ queryKey: ["results", undefined, undefined], queryFn: () => api.get<Results | null>("/api/results"), enabled: seesResults });
  const d = home.data;
  const approved = d?.definitionStatus.find((s) => s.key === "Approved")?.count ?? 0;
  const total = d?.definitionStatus.reduce((s, x) => s + x.count, 0) ?? 0;
  const forms = [...(mine.data ?? [])].sort((a, b) => Number(b.status === "Returned") - Number(a.status === "Returned") || a.dueOn.localeCompare(b.dueOn));
  const r = results.data;

  // سطر الترحيب يلخص ما يهم هذا المستخدم الآن.
  const summary = forms.length > 0
    ? L(forms.length === 1 ? `لديك نموذج واحد ينتظرك، موعده ${dueText(forms[0].dueOn)}.`
      : `لديك ${forms.length === 2 ? "نموذجان ينتظرانك" : `${forms.length} نماذج تنتظرك`}، أقربها موعدًا ${dueText(forms[0].dueOn)}.`, `${forms.length} forms are waiting for you.`)
    : planning && d
      ? L(`اعتُمد ${approved} من ${total} مؤشرًا، وبقي ${d.openConflicts} تعارضًا مفتوحًا أمام اللجنة العليا.`, `${approved} of ${total} indicators approved.`)
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
                { key: "a", label: L("محقق", "Achieved"), count: r.indicators.filter((i) => i.performance === "Achieved").length, color: "#2e9e63" },
                { key: "n", label: L("قريب من المستهدف", "Near"), count: r.indicators.filter((i) => i.performance === "Near").length, color: "#d9a227" },
                { key: "b", label: L("متأخر", "Behind"), count: r.indicators.filter((i) => i.performance === "Behind").length, color: "#b42318" },
                { key: "o", label: L("بلا بيانات أو مستهدف أو جزئي", "Not judged"), count: r.indicators.filter((i) => !["Achieved", "Near", "Behind"].includes(i.performance)).length, color: "#c4ccd8" },
              ]} />
              <p className="t1-help mt">{L(`${r.unit.kind === "University" ? "الجامعة" : r.unit.name} · محسوبة من ${r.formsApproved} نموذجًا معتمدًا نهائيًا من ${r.formsTotal}.`,
                `From ${r.formsApproved} of ${r.formsTotal} approved forms.`)}</p>
            </>
          )}
        </Panel>
      )}

      {planning && (
        <>
          <div className="lcp-stats">
            <StatTile icon={<UnorderedListOutlined />} label={L("مؤشرات معتمدة", "Approved indicators")} value={`${approved} / ${total}`} to="/indicators?status=Approved" loading={home.isLoading} />
            <StatTile icon={<FileDoneOutlined />} tone="navy" label={L("تنتظر الاعتماد", "Awaiting approval")}
              value={d?.definitionStatus.find((s) => s.key === "UnderReview")?.count ?? 0} to="/indicators?status=UnderReview" loading={home.isLoading} />
            <StatTile icon={<WarningOutlined />} tone="rose" label={L("تعارضات مفتوحة", "Open conflicts")} value={d?.openConflicts ?? 0}
              hint={d ? L(`${d.resolvedConflicts} محسوم`, `${d.resolvedConflicts} resolved`) : undefined} to="/conflicts" loading={home.isLoading} />
            {d?.structureAveragePercent !== null && d?.structureAveragePercent !== undefined && (
              <StatTile icon={<ClusterOutlined />} tone="gold" label={L("اكتمال هياكل الكليات", "College structures complete")} value={`${d.structureAveragePercent}%`}
                hint={L(`${d.collegeCount} كلية ومعهد`, `${d.collegeCount} colleges`)} to="/structure" loading={home.isLoading} />
            )}
          </div>

          <div className="lcp-grid-2">
            <Panel title={L("حالة تعريفات المؤشرات", "Indicator definitions")} query={home}>
              {() => (
                <SegmentBar label={L("حالة التعريفات", "Definition status")} segments={[
                  { key: "Approved", label: L("معتمد", "Approved"), count: approved, color: "#1c6a43" },
                  { key: "UnderReview", label: L("قيد الاعتماد", "Under review"), count: d!.definitionStatus.find((s) => s.key === "UnderReview")?.count ?? 0, color: "#0a8c93" },
                  { key: "Draft", label: L("مسودة", "Draft"), count: d!.definitionStatus.find((s) => s.key === "Draft")?.count ?? 0, color: "#aab6c6" },
                ]} />
              )}
            </Panel>
            <Panel title={L("التعارضات المفتوحة حسب الخطورة", "Open conflicts by severity")} query={home}>
              {() => (
                <SegmentBar label={L("التعارضات", "Conflicts")} segments={d!.conflictSeverity.map((s) => ({
                  key: s.key, label: s.labelAr, count: s.count, color: s.key === "High" ? "#b42318" : s.key === "Medium" ? "#c99a2e" : "#aab6c6",
                }))} />
              )}
            </Panel>
          </div>

          {d && d.standards.length > 0 && (
            <Panel title={L("اعتماد المؤشرات بحسب المعيار", "Approval by standard")} icon={<UnorderedListOutlined aria-hidden />}>
              {d.standards.map((s) => (
                <Link key={s.code} to={`/indicators?standard=${s.code}`} className="t1-std">
                  <code>{s.code}</code>
                  <span>
                    {s.nameAr}
                    {s.openConflicts > 0 && <small> · {L(`${s.openConflicts} تعارض مفتوح`, `${s.openConflicts} open conflicts`)}</small>}
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
