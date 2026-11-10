import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { Link } from "react-router-dom";
import { ClusterOutlined } from "@ant-design/icons";
import { api } from "../../api/client";
import type { CollegeCompleteness } from "../../api/tier1";
import { ProgressToTarget } from "../../shared/charts";
import { L } from "../../shared/i18n";
import { usePermissions } from "../../shared/session";
import { DataTable, EmptyNote, PageHeader, Panel } from "../../shared/ui";

/** هياكل Tier 1 بالكليات في نطاق المستخدم، الأقل اكتمالًا أولًا، مع أول نقص في كل كلية. */
export default function StructurePage() {
  const navigate = useNavigate();
  const perms = usePermissions();
  const list = useQuery({ queryKey: ["structure", "completeness"], queryFn: () => api.get<CollegeCompleteness[]>("/api/structure/completeness") });
  const avg = list.data && list.data.length > 0 ? Math.round(list.data.reduce((s, c) => s + c.percent, 0) / list.data.length) : null;

  return (
    <>
      <PageHeader title={L("هياكل Tier 1 بالكليات", "College Tier 1 structures")}
        subtitle={avg !== null
          ? L(`متوسط الاكتمال ${avg}%. افتح الكلية لترى رسمها التنظيمي وتستكمل المناصب الشاغرة.`, `Average completeness ${avg}%. Open a college to see its chart and fill vacancies.`)
          : L("الرسم التنظيمي لكل كلية بأعضائه وشركائه الصناعيين.", "Each college's chart with its members and industry partners.")} />
      <Panel query={list}>
        {() => list.data!.length === 0 ? (
          <EmptyNote action={perms.can("org.units", "A") && <Link to="/units">{L("إضافة الكليات", "Add colleges")}</Link>}>
            {L("لا توجد كليات في نطاقك بعد.", "No colleges in your scope yet.")}
          </EmptyNote>
        ) : (
          <DataTable<CollegeCompleteness>
            rowKey={(c) => c.collegeId}
            data={list.data!}
            onRowClick={(c) => navigate(`/structure/${c.collegeId}`)}
            columns={[
              { title: L("الكلية أو المعهد", "College"), dataIndex: "nameAr", render: (n: string) => <b><ClusterOutlined aria-hidden /> {n}</b> },
              {
                title: L("اكتمال الهيكل", "Completeness"), dataIndex: "percent", width: 240,
                render: (p: number) => <ProgressToTarget value={p} max={100} label={<><span /><b className="ltr">{p}%</b></>} />,
              },
              { title: L("أول نقص", "First gap"), dataIndex: "firstIssue", render: (t: string | null, c) => t ? <>{t}{c.issueCount > 1 && <span className="muted"> {L(`(+${c.issueCount - 1})`, `(+${c.issueCount - 1})`)}</span>}</> : <span className="muted">{L("مكتمل", "Complete")}</span> },
            ]}
            renderCard={(c) => (
              <>
                <b>{c.nameAr}</b>
                <ProgressToTarget value={c.percent} max={100} label={<><small>{L("الاكتمال", "Complete")}</small><b className="ltr">{c.percent}%</b></>} />
                {c.firstIssue && <small className="muted">{c.firstIssue}</small>}
              </>
            )}
          />
        )}
      </Panel>
    </>
  );
}
