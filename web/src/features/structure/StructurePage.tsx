import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { ArrowLeftOutlined, ArrowRightOutlined, CheckCircleOutlined, ClusterOutlined, WarningOutlined } from "@ant-design/icons";
import { api } from "../../api/client";
import type { CollegeCompleteness } from "../../api/tier1";
import { L, useLang } from "../../shared/i18n";
import { usePermissions } from "../../shared/session";
import { EmptyNote, PageHeader, Panel } from "../../shared/ui";

/**
 * هياكل Tier 1 بالكليات في نطاق المستخدم: بطاقة لكل كلية بحلقة اكتمال هيكلها وأول نقص فيه، الأقل اكتمالًا أولًا،
 * والنقر يفتح رسمها التنظيمي.
 */
export default function StructurePage() {
  const perms = usePermissions();
  const Go = useLang() === "ar" ? ArrowLeftOutlined : ArrowRightOutlined;
  const list = useQuery({ queryKey: ["structure", "completeness"], queryFn: () => api.get<CollegeCompleteness[]>("/api/structure/completeness") });
  const data = list.data ?? [];
  const avg = data.length > 0 ? Math.round(data.reduce((s, c) => s + c.percent, 0) / data.length) : null;
  const complete = data.filter((c) => c.percent >= 100).length;
  const issues = data.reduce((n, c) => n + c.issueCount, 0);

  return (
    <>
      <PageHeader title={L("هياكل Tier 1 بالكليات", "College Tier 1 structures")}
        subtitle={L("لكل كلية رسم تنظيمي لفريق Tier 1 بأعضائه وشركائه الصناعيين. افتح الكلية لترى رسمها وتستكمل المناصب الشاغرة.",
          "Each college's Tier 1 chart with its members and industry partners. Open a college to fill vacancies.")}
        stats={list.data ? [
          { label: L("كليات ومعاهد", "Colleges"), value: data.length },
          { label: L("متوسط الاكتمال", "Average"), value: <span className="ltr">{avg ?? 0}%</span>, tone: (avg ?? 0) >= 90 ? "ok" : "warn" },
          { label: L("هياكل مكتملة", "Complete"), value: complete, tone: "ok" },
          { label: L("نواقص مفتوحة", "Open gaps"), value: issues, tone: issues > 0 ? "bad" : undefined },
        ] : undefined} />
      <Panel query={list}>
        {() => data.length === 0 ? (
          <EmptyNote action={perms.can("org.units", "A") && <Link to="/units">{L("إضافة الكليات", "Add colleges")}</Link>}>
            {L("لا توجد كليات في نطاقك بعد.", "No colleges in your scope yet.")}
          </EmptyNote>
        ) : (
          <div className="st-grid">
            {[...data].sort((a, b) => a.percent - b.percent).map((c) => {
              const R = 30, C = 2 * Math.PI * R;
              const tone = c.percent >= 90 ? "ok" : c.percent >= 70 ? "near" : "bad";
              return (
                <Link key={c.collegeId} to={`/structure/${c.collegeId}`} className={`st-card tone-${tone}`}>
                  <span className="st-ring" aria-hidden>
                    <svg viewBox="0 0 76 76" width="76" height="76">
                      <circle cx="38" cy="38" r={R} className="st-ring-track" />
                      <circle cx="38" cy="38" r={R} className="st-ring-fill" strokeDasharray={`${((C * c.percent) / 100).toFixed(1)} ${C.toFixed(1)}`} transform="rotate(-90 38 38)" />
                      <text x="38" y="43" textAnchor="middle">{c.percent}%</text>
                    </svg>
                  </span>
                  <span className="st-text">
                    <small><ClusterOutlined aria-hidden /> <code className="ltr">{c.code}</code></small>
                    <b>{c.nameAr}</b>
                    {c.firstIssue ? (
                      <span className="st-issue"><WarningOutlined aria-hidden /> {c.firstIssue}{c.issueCount > 1 && <em> {L(`و${c.issueCount - 1} نقص آخر`, `+${c.issueCount - 1} more`)}</em>}</span>
                    ) : <span className="st-ok"><CheckCircleOutlined aria-hidden /> {L("الهيكل مكتمل", "Structure complete")}</span>}
                  </span>
                  <span className="st-go">{L("الرسم التنظيمي", "Org chart")} <Go aria-hidden /></span>
                </Link>
              );
            })}
          </div>
        )}
      </Panel>
    </>
  );
}
