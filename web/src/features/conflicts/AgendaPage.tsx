import { Button, Tag } from "antd";
import { PrinterOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { api } from "../../api/client";
import type { Conflict } from "../../api/tier1";
import { L } from "../../shared/i18n";
import { conflictSeverity, conflictStatus } from "../../shared/labels";
import { EmptyNote, PageHeader, Panel } from "../../shared/ui";

/**
 * جدول أعمال جلسة اللجنة العليا: الحالات الجاهزة والمؤجلة، الأخطر أولًا، بالقيمتين المتعارضتين والمعالجة المقترحة.
 * يطبع للجلسة، ومن كل بند يفتح نموذج تسجيل القرار.
 */
export default function AgendaPage() {
  const agenda = useQuery({ queryKey: ["conflicts", "agenda"], queryFn: () => api.get<Conflict[]>("/api/kpi/conflicts/agenda") });
  const today = new Intl.DateTimeFormat("ar-EG", { dateStyle: "long", timeZone: "Africa/Cairo" }).format(new Date());

  return (
    <>
      <PageHeader back={{ to: "/conflicts", label: L("سجل التعارضات", "Conflict register") }}
        title={L("جدول أعمال اللجنة العليا لـ Tier 1", "Tier 1 higher committee agenda")}
        subtitle={L(`أعد في ${today} — البنود الجاهزة والمؤجلة مرتبة بالخطورة.`, `Prepared ${today} — ready and deferred items by severity.`)}
        extra={<Button icon={<PrinterOutlined />} onClick={() => window.print()}>{L("طباعة", "Print")}</Button>} />
      <Panel query={agenda}>
        {() => agenda.data!.length === 0 ? (
          <EmptyNote action={<Link to="/conflicts?status=Open">{L("التعارضات المفتوحة", "Open conflicts")}</Link>}>
            {L("لا بنود جاهزة. جهّز التعارضات المفتوحة بكتابة المعالجة المقترحة لتدخل جدول الأعمال.", "Nothing ready. Prepare open conflicts to add them to the agenda.")}
          </EmptyNote>
        ) : (
          <ol className="lcp-list">
            {agenda.data!.map((c, i) => (
              <li key={c.code} className="lcp-list-item">
                <h3 className="t1-form-sec">
                  {i + 1}. <code>{c.code}</code> — {c.type}{" "}
                  <Tag color={conflictSeverity[c.severity].color}>{conflictSeverity[c.severity].text}</Tag>
                  {c.status === "Deferred" && <Tag color={conflictStatus.Deferred.color}>{conflictStatus.Deferred.text}</Tag>}
                </h3>
                <p><b>{c.description}</b></p>
                <p>{L("أ:", "A:")} {c.valueA}</p>
                <p>{L("ب:", "B:")} {c.valueB}</p>
                <p><b>{L("المقترح:", "Proposal:")}</b> {c.proposal}</p>
                {c.preparationNote && <p className="t1-help">{c.preparationNote}</p>}
                <Link className="no-print" to={`/conflicts?open=${c.code}`}>{L("تسجيل القرار", "Record the decision")}</Link>
              </li>
            ))}
          </ol>
        )}
      </Panel>
    </>
  );
}
