import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { CheckCircleOutlined, ClusterOutlined, FileDoneOutlined, UnorderedListOutlined, WarningOutlined } from "@ant-design/icons";
import { api } from "../../api/client";
import type { Home } from "../../api/tier1";
import { ProgressToTarget, SegmentBar } from "../../shared/charts";
import { L } from "../../shared/i18n";
import { firstName, greeting, Hero, Panel, StatTile } from "../../shared/ui";

/**
 * الصفحة الأولى: «مطلوب منك» أولًا (كل مهمة تفتح مكان تنفيذها مباشرة)، ثم الصورة العامة للقاموس والتعارضات والهياكل.
 */
export default function HomePage() {
  const home = useQuery({ queryKey: ["home"], queryFn: () => api.get<Home>("/api/home") });
  const d = home.data;
  const approved = d?.definitionStatus.find((s) => s.key === "Approved")?.count ?? 0;
  const total = d?.definitionStatus.reduce((s, x) => s + x.count, 0) ?? 0;

  return (
    <>
      <Hero kicker={greeting()} title={`${L("أهلًا", "Welcome")} ${firstName(d?.displayName)}`}>
        {d
          ? L(`اعتُمد ${approved} من ${total} مؤشرًا، وبقي ${d.openConflicts} تعارضًا مفتوحًا أمام اللجنة العليا.`,
            `${approved} of ${total} indicators approved; ${d.openConflicts} conflicts are open for the higher committee.`)
          : L("جار تحميل الصورة العامة…", "Loading the overview…")}
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
        <Panel title={L("المعايير الاثنا عشر", "The twelve standards")} icon={<UnorderedListOutlined aria-hidden />}>
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
  );
}
