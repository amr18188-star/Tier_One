import { useState } from "react";
import { Alert, App, Button, Tag, Timeline, Tooltip } from "antd";
import { CheckOutlined, EditOutlined, FileTextOutlined, HistoryOutlined, PartitionOutlined, PrinterOutlined, RollbackOutlined, SendOutlined, WarningOutlined } from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";
import { api } from "../../api/client";
import type { ConflictList, IndicatorDetail, OrgUnit, Version } from "../../api/tier1";
import { howCalculated } from "../../shared/calc";
import { DecisionModal } from "../../shared/DecisionModal";
import { formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { aggregation, conflictSeverity, conflictStatus, definitionStatus, direction, frequency, level } from "../../shared/labels";
import { PageHeader, Panel } from "../../shared/ui";
import { QueryState } from "../../shared/QueryState";
import { DefinitionDrawer } from "./DefinitionDrawer";
import { DistributionPanel } from "./DistributionPanel";
import { TargetsPanel } from "./TargetsPanel";

/**
 * صفحة المؤشر الواحدة: الحالة والخطوة التالية مكتوبتان أعلاها، ثم التعريف، والمستهدفات، والتوزيع على الكليات،
 * والتعارضات المرتبطة، وسجل الإصدارات. الأزرار المتاحة وحدها تظهر بحسب صلاحية المستخدم وحالة التعريف.
 */
export default function IndicatorPage() {
  const { code = "" } = useParams();
  const qc = useQueryClient();
  const { message } = App.useApp();
  // «مؤشر جديد» يفتح الصفحة ومحرر التعريف مباشرة لإكماله.
  const [editing, setEditing] = useState(() => new URLSearchParams(window.location.search).get("edit") === "1");
  const [decision, setDecision] = useState<"approve" | "return" | null>(null);
  const detail = useQuery({ queryKey: ["indicator", code], queryFn: () => api.get<IndicatorDetail>(`/api/kpi/indicators/${code}`) });
  const units = useQuery({ queryKey: ["units"], queryFn: () => api.get<OrgUnit[]>("/api/org/units") });
  const university = units.data?.find((u) => u.kind === "University");
  const d = detail.data;

  const refresh = async (updated?: IndicatorDetail) => {
    if (updated) qc.setQueryData(["indicator", code], updated);
    await qc.invalidateQueries({ queryKey: ["indicators"] });
    await qc.invalidateQueries({ queryKey: ["home"] });
  };
  const submit = useMutation({
    mutationFn: () => api.post<IndicatorDetail>(`/api/kpi/indicators/${code}/submit`),
    onSuccess: async (u) => { await refresh(u); message.success(L("أُرسل التعريف للجنة العليا.", "Sent to the higher committee.")); },
    onError: (e) => void message.error(e.message),
  });

  return (
    <QueryState query={detail}>
      {() => (
        <>
          <PageHeader
            back={{ to: `/indicators?standard=${d!.standardCode}`, label: `${d!.standardCode} — ${d!.standardName}` }}
            title={<span className="t1-code-title"><code>{d!.code}</code>{d!.current.nameAr}</span>}
            subtitle={<>
              <Tag color={definitionStatus[d!.current.status].color}>{definitionStatus[d!.current.status].text}</Tag>
              {L("الإصدار", "Version")} {d!.current.number}
              {d!.suspended && <Tag color="error" className="ms">{L("موقوف", "Suspended")}</Tag>}
            </>}
            extra={
              <div className="t1-sticky-actions no-print">
                {d!.actions.canEdit && <Button icon={<EditOutlined />} onClick={() => setEditing(true)}>{d!.actions.editCreatesVersion ? L("تعديل (إصدار جديد)", "Edit (new version)") : L("تعديل التعريف", "Edit definition")}</Button>}
                {d!.actions.canSubmit && <Button type="primary" icon={<SendOutlined />} loading={submit.isPending} onClick={() => submit.mutate()}>{L("إرسال للاعتماد", "Submit for approval")}</Button>}
                {d!.actions.canApprove && <Button type="primary" icon={<CheckOutlined />} onClick={() => setDecision("approve")}>{L("اعتماد", "Approve")}</Button>}
                {d!.actions.canApprove && <Button icon={<RollbackOutlined />} onClick={() => setDecision("return")}>{L("إعادة بملاحظات", "Return")}</Button>}
                <Link to={`/strategy?node=I:${d!.code}`}><Button icon={<PartitionOutlined />}>{L("في شجرة الخطة", "In the plan tree")}</Button></Link>
                <Tooltip title={L("طباعة بطاقة المؤشر", "Print the indicator card")}><Button icon={<PrinterOutlined />} onClick={() => window.print()} aria-label={L("طباعة", "Print")} /></Tooltip>
              </div>
            } />

          <NextStep d={d!} />

          <Panel title={L("التعريف", "Definition")} icon={<FileTextOutlined aria-hidden />}>
            <DefinitionView v={d!.current} />
          </Panel>

          <TargetsPanel code={code} universityId={university?.id} />
          <DistributionPanel code={code} />
          <ConflictsOf code={code} codes={d!.conflictCodes} />

          <Panel title={L("سجل الإصدارات", "Version history")} icon={<HistoryOutlined aria-hidden />}>
            <Timeline items={d!.history.map((h) => ({
              color: h.status === "Approved" ? "green" : h.status === "UnderReview" ? "blue" : "gray",
              children: (
                <>
                  <b>{L("الإصدار", "Version")} {h.number}</b> — <Tag color={definitionStatus[h.status].color}>{definitionStatus[h.status].text}</Tag>
                  {h.approvedAt && <span className="t1-help">{L("اعتمد", "Approved")} {formatDate(h.approvedAt)} ({h.decisionRef})</span>}
                  {h.changeReason && <div className="t1-help">{L("سبب التعديل:", "Reason:")} {h.changeReason}</div>}
                </>
              ),
            }))} />
          </Panel>

          {editing && <DefinitionDrawer detail={d!} onClose={() => setEditing(false)} />}
          {decision === "approve" && (
            <DecisionModal title={L(`اعتماد تعريف ${code}`, `Approve ${code}`)} field="decisionRef" label={L("رقم محضر اللجنة العليا", "Committee minutes reference")}
              okText={L("اعتماد", "Approve")} onClose={() => setDecision(null)}
              explain={L("يصبح هذا الإصدار التعريف الساري، ويحل محل أي إصدار معتمد سابق.", "This version becomes the definition in force and replaces any earlier approved version.")}
              onSubmit={async (decisionRef) => { await refresh(await api.post<IndicatorDetail>(`/api/kpi/indicators/${code}/approve`, { decisionRef })); message.success(L("اعتُمد التعريف.", "Definition approved.")); }} />
          )}
          {decision === "return" && (
            <DecisionModal title={L(`إعادة تعريف ${code}`, `Return ${code}`)} field="note" label={L("ملاحظات اللجنة", "Committee notes")} okText={L("إعادة", "Return")}
              onClose={() => setDecision(null)}
              explain={L("يعود التعريف مسودة لمن أعدّه، وتظهر له ملاحظاتك أعلى الصفحة.", "The definition returns to draft and your notes show at the top of its page.")}
              onSubmit={async (note) => { await refresh(await api.post<IndicatorDetail>(`/api/kpi/indicators/${code}/return`, { note })); message.success(L("أعيد التعريف بالملاحظات.", "Returned with notes.")); }} />
          )}
        </>
      )}
    </QueryState>
  );
}

/** الحالة الحالية والخطوة التالية بجملة واحدة، فيعرف المستخدم ما يفعله دون تدريب. */
function NextStep({ d }: { d: IndicatorDetail }) {
  const v = d.current;
  if (v.returnNote && v.status === "Draft")
    return <Alert type="error" showIcon className="mb" message={L("أعادت اللجنة التعريف بملاحظات", "The committee returned the definition")} description={v.returnNote} />;
  if (v.status === "Draft" && v.missing.length > 0)
    return <Alert type="warning" showIcon className="mb" message={L(`ينقص التعريف ${v.missing.length} بند قبل إرساله للاعتماد`, `${v.missing.length} items missing before submission`)}
      description={<>{v.missing.join("، ")}. {d.actions.canEdit && L("اضغط «تعديل التعريف» لإكمالها.", "Use “Edit definition” to complete them.")}</>} />;
  if (v.status === "Draft")
    return <Alert type="info" showIcon className="mb" message={L("التعريف مكتمل", "The definition is complete")}
      description={d.actions.canSubmit ? L("الخطوة التالية: «إرسال للاعتماد» إلى اللجنة العليا.", "Next: submit it to the higher committee.") : L("ينتظر إرساله للاعتماد.", "Waiting to be submitted.")} />;
  if (v.status === "UnderReview")
    return <Alert type="info" showIcon className="mb" message={L("ينتظر قرار اللجنة العليا", "Waiting for the higher committee")}
      description={d.effective ? L(`الإصدار ${d.effective.number} المعتمد ساري حتى يعتمد هذا الإصدار.`, `Version ${d.effective.number} stays in force until this one is approved.`) : undefined} />;
  return <Alert type="success" showIcon className="mb" message={L(`معتمد بقرار ${v.decisionRef ?? ""}`, `Approved by ${v.decisionRef ?? ""}`)}
    description={v.approvedAt ? L(`بتاريخ ${formatDate(v.approvedAt)}. أي تعديل ينشئ إصدارًا جديدًا يمر بالاعتماد.`, `On ${formatDate(v.approvedAt)}. Any edit creates a new version.`) : undefined} />;
}

function DefinitionView({ v }: { v: Version }) {
  const missing = new Set(v.missing);
  const row = (label: string, value: string | null | undefined, missingKey?: string) => {
    const empty = value === null || value === undefined || value === "";
    return (
      <>
        <dt>{label}</dt>
        <dd className={empty && missingKey && missing.has(missingKey) ? "missing" : undefined}>
          {empty ? (missingKey && missing.has(missingKey) ? L("لم يحدد بعد — مطلوب للاعتماد", "Not set — required for approval") : "—") : value}
        </dd>
      </>
    );
  };
  return (
    <dl className="t1-kv">
      {row(L("التعريف", "Definition"), v.definition, "التعريف")}
      {v.alternateName && row(L("الاسم في ملف Tier 1", "Name in the Tier 1 file"), v.alternateName)}
      {row(L("وحدة القياس", "Unit"), missing.has("وحدة القياس") ? null : v.unit, "وحدة القياس")}
      {row(L("الاتجاه المطلوب", "Direction"), v.direction === "Undefined" ? null : direction[v.direction], "الاتجاه المطلوب")}
      {row(L("قاعدة التجميع", "Aggregation"), v.aggregation === "Undefined" ? null : aggregation[v.aggregation], "قاعدة التجميع")}
      {v.aggregation === "RatioOfSums" && row(L("البسط", "Numerator"), v.numerator, "البسط والمقام")}
      {v.aggregation === "RatioOfSums" && row(L("المقام", "Denominator"), v.denominator, "البسط والمقام")}
      {v.aggregation === "WeightedAverage" && row(L("أساس الترجيح", "Weighting basis"), v.denominator, "أساس الترجيح")}
      {row(L("كيف تحسب الكلية", "College value"), howCalculated(v).college)}
      {row(L("كيف تحسب الجامعة", "University value"), howCalculated(v).university)}
      {row(L("مستوى جمع البيانات", "Collection level"), level[v.level])}
      {row(L("الدورية", "Frequency"), frequency[v.frequency])}
      {row(L("مصدر البيانات", "Data source"), v.dataSource, "مصدر البيانات")}
      {row(L("الجهة المالكة", "Owner"), v.ownerUnitName, "الجهة المالكة")}
      {row(L("الأدلة المطلوبة", "Evidence"), v.evidenceRequired)}
      {row(L("المرجع في الملفين", "Source pages"), v.sourcePages)}
      {v.changeReason && row(L("سبب هذا الإصدار", "Reason for this version"), v.changeReason)}
    </dl>
  );
}

/** التعارضات التي تذكر هذا المؤشر أو معياره، بحالتها وخطورتها؛ النقر يفتحها في سجل التعارضات. */
function ConflictsOf({ code, codes }: { code: string; codes: string[] }) {
  const list = useQuery({ queryKey: ["conflicts", "scope", code], queryFn: () => api.get<ConflictList>(`/api/kpi/conflicts?scope=${code}`), enabled: codes.length > 0 });
  if (codes.length === 0) return null;
  return (
    <Panel title={L(`التعارضات المرتبطة (${codes.length})`, `Related conflicts (${codes.length})`)} icon={<WarningOutlined aria-hidden />} query={list}>
      {() => (
        <ul className="lcp-list">
          {list.data!.items.map((c) => (
            <li key={c.code} className="lcp-list-item">
              <Link to={`/conflicts?open=${c.code}`}><code>{c.code}</code></Link>{" "}
              <Tag color={conflictSeverity[c.severity].color}>{conflictSeverity[c.severity].text}</Tag>
              <Tag color={conflictStatus[c.status].color}>{conflictStatus[c.status].text}</Tag>
              <div>{c.description}</div>
              {c.decision && <div className="t1-help">{L("القرار:", "Decision:")} {c.decision} ({c.minutesRef})</div>}
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}
