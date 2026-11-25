import { useState } from "react";
import { Alert, App, Button, Tag, Timeline, Tooltip } from "antd";
import { CheckOutlined, DeleteOutlined, EditOutlined, FileTextOutlined, HistoryOutlined, PartitionOutlined, PauseCircleOutlined, PlayCircleOutlined, PrinterOutlined, RollbackOutlined, SendOutlined } from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";
import { api } from "../../api/client";
import type { IndicatorDetail, Version } from "../../api/tier1";
import { howCalculated } from "../../shared/calc";
import { DecisionModal } from "../../shared/DecisionModal";
import { formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { aggregation, definitionStatus, direction, frequency, level } from "../../shared/labels";
import { StdCode, usePillarMap } from "../../shared/pillars";
import { PageHeader, Panel } from "../../shared/ui";
import { QueryState } from "../../shared/QueryState";
import { DefinitionDrawer } from "./DefinitionDrawer";
import { DistributionPanel } from "./DistributionPanel";
import { TargetsPanel } from "./TargetsPanel";
import { useUnits, queryKeys } from "../../api/queries";

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
  const [discarding, setDiscarding] = useState(false);
  const [suspending, setSuspending] = useState(false);
  const detail = useQuery({ queryKey: queryKeys.indicator(code), queryFn: () => api.get<IndicatorDetail>(`/api/kpi/indicators/${code}`) });
  const units = useUnits();
  const university = units.data?.find((u) => u.kind === "University");
  const d = detail.data;
  const pillarMap = usePillarMap();

  const refresh = async (updated?: IndicatorDetail) => {
    if (updated) qc.setQueryData(["indicator", code], updated);
    await qc.invalidateQueries({ queryKey: queryKeys.indicators });
    await qc.invalidateQueries({ queryKey: queryKeys.home });
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
            kicker={<>{L("قاموس المؤشرات", "Indicator dictionary")} · <StdCode code={d!.standardCode} map={pillarMap.map} /></>}
            title={<span className="t1-code-title"><code>{d!.code}</code>{d!.current.nameAr}</span>}
            subtitle={<>
              <Tag color={definitionStatus[d!.current.status].color}>{definitionStatus[d!.current.status].text}</Tag>
              {L("الإصدار", "Version")} {d!.current.number}
              {d!.suspended && <Tag color="error" className="ms">{L("موقوف", "Suspended")}</Tag>}
            </>}
            stats={[
              { label: L("وحدة القياس", "Unit"), value: d!.current.unit },
              { label: L("الاتجاه", "Direction"), value: direction[d!.current.direction] },
              { label: L("بنود تعريف ناقصة", "Missing items"), value: d!.current.missing.length, tone: d!.current.missing.length > 0 ? "warn" : "ok" },
              { label: L("نوع المؤشر", "Type"), value: d!.current.accumulation === "Cumulative" ? L("تراكمي", "Cumulative") : L("لحظي", "Snapshot") },
            ]}
            extra={
              <div className="t1-sticky-actions no-print">
                {d!.actions.canEdit && <Button icon={<EditOutlined />} onClick={() => setEditing(true)}>{d!.actions.editCreatesVersion ? L("تعديل (إصدار جديد)", "Edit (new version)") : L("تعديل التعريف", "Edit definition")}</Button>}
                {d!.actions.canSubmit && <Button type="primary" icon={<SendOutlined />} loading={submit.isPending} onClick={() => submit.mutate()}>{L("إرسال للاعتماد", "Submit for approval")}</Button>}
                {d!.actions.canApprove && <Button type="primary" icon={<CheckOutlined />} onClick={() => setDecision("approve")}>{L("اعتماد", "Approve")}</Button>}
                {d!.actions.canApprove && <Button icon={<RollbackOutlined />} onClick={() => setDecision("return")}>{L("إعادة بملاحظات", "Return")}</Button>}
                {d!.actions.canDiscard && <Button danger icon={<DeleteOutlined />} onClick={() => setDiscarding(true)}>{L("تجاهل المسودة", "Discard the draft")}</Button>}
                {d!.actions.canSuspend && (d!.suspended
                  ? <Button icon={<PlayCircleOutlined />} onClick={() => setSuspending(true)}>{L("إعادة تفعيل المؤشر", "Reactivate")}</Button>
                  : <Button danger icon={<PauseCircleOutlined />} onClick={() => setSuspending(true)}>{L("إيقاف المؤشر", "Suspend")}</Button>)}
                <Link to={`/strategy?node=I:${d!.code}`}><Button icon={<PartitionOutlined />}>{L("في الخطة الاستراتيجية", "In the strategic plan")}</Button></Link>
                <Tooltip title={L("طباعة بطاقة المؤشر", "Print the indicator card")}><Button icon={<PrinterOutlined />} onClick={() => window.print()} aria-label={L("طباعة", "Print")} /></Tooltip>
              </div>
            } />

          <NextStep d={d!} />
          {discarding && (
            <DecisionModal title={L(`تجاهل مسودة ${d!.code}`, `Discard the draft of ${d!.code}`)} danger field="reason" label={L("السبب", "Reason")} okText={L("تجاهل", "Discard")}
              explain={L("تُحذف المسودة ويبقى الإصدار المعتمد ساريًا كما هو. يسجل ذلك في السجل.", "The draft is dropped and the approved version stays in force. This is logged.")}
              onSubmit={async (reason) => { await refresh(await api.post<IndicatorDetail>(`/api/kpi/indicators/${code}/discard-draft`, { reason })); }}
              onClose={() => setDiscarding(false)} />
          )}
          {suspending && (
            <DecisionModal danger={!d!.suspended} field="reason" label={L("السبب", "Reason")}
              title={d!.suspended ? L(`إعادة تفعيل ${d!.code}`, `Reactivate ${d!.code}`) : L(`إيقاف ${d!.code}`, `Suspend ${d!.code}`)}
              okText={d!.suspended ? L("إعادة التفعيل", "Reactivate") : L("إيقاف", "Suspend")}
              explain={d!.suspended
                ? L("يعود المؤشر إلى نماذج الدورات التي تفتح بعد الآن.", "The indicator returns to the forms of cycles opened from now on.")
                : L("لا يدخل المؤشر نماذج الدورات التي تفتح بعد الآن، وتبقى نتائجه السابقة كما هي. يسجل ذلك في السجل.", "The indicator is left out of cycles opened from now on; its past results stay. This is logged.")}
              onSubmit={async (reason) => { await refresh(await api.post<IndicatorDetail>(`/api/kpi/indicators/${code}/suspend`, { suspended: !d!.suspended, reason })); }}
              onClose={() => setSuspending(false)} />
          )}

          <Panel title={L("التعريف", "Definition")} icon={<FileTextOutlined aria-hidden />}>
            <DefinitionView v={d!.current} />
          </Panel>

          <TargetsPanel code={code} universityId={university?.id} />
          <DistributionPanel code={code} />
          <FrameworkPanel d={d!} />

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
      {row(L("معادلة التقييم", "Evaluation formula"), v.formula)}
      {row(L("نوع المؤشر حسب طريقة الحساب", "Type"), v.accumulation === "Cumulative"
        ? L("تراكمي: تدخل القيمة المتراكمة حتى تاريخه، وقيمة السنة آخر ربع فيها.", "Cumulative: the year value is its last quarter.")
        : L("غير تراكمي (لحظي): تدخل القيمة عند تاريخ القياس، وقيمة السنة متوسط أرباعها.", "Snapshot: the year value is the average of its quarters."))}
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

/** ربط المؤشر في الإطار الشامل: مناظير بطاقة أداء القطاع العام وأهداف التنمية المستدامة وأسباب الربط، وشرح المستهدف. */
function FrameworkPanel({ d }: { d: IndicatorDetail }) {
  const f = d.framework;
  const sdg = (n: number | null, role: string) => n && <Tag key={role} color="blue">{role}: {L(`الهدف ${n}`, `SDG ${n}`)} — {SDG_NAMES[n - 1]}</Tag>;
  return (
    <Panel title={L("الربط بالإطار الشامل لتقييم أداء الجامعات", "Links in the performance framework")} icon={<PartitionOutlined aria-hidden />}>
      <dl className="t1-kv">
        <dt>{L("مناظير بطاقة أداء القطاع العام", "Public Sector Scorecard")}</dt>
        <dd>{f.perspectives.length === 0 ? "—" : f.perspectives.map((p) => <Tag key={p} color="geekblue">{p}. {PSS_NAMES[p - 1]}</Tag>)}</dd>
        {f.pssRationale && <><dt>{L("سبب الربط بالمناظير", "Why these perspectives")}</dt><dd>{f.pssRationale}</dd></>}
        <dt>{L("أهداف التنمية المستدامة", "SDGs")}</dt>
        <dd>{[sdg(f.sdgPrimary, L("أساسي", "Primary")), sdg(f.sdgSecondary, L("ثانوي", "Secondary")), sdg(f.sdgInduced, L("أثر نهائي", "Induced"))]}</dd>
        {f.sdgRationale && <><dt>{L("سبب الربط بالأهداف", "Why these goals")}</dt><dd>{f.sdgRationale}</dd></>}
        {f.planNote && <><dt>{L("ملاحظة على المستهدف", "Target note")}</dt><dd>{f.planNote}</dd></>}
      </dl>
    </Panel>
  );
}

const PSS_NAMES = ["المنظور الاستراتيجي", "مستخدمو الخدمة وأصحاب المصلحة", "المالي", "تقديم الخدمة والتميز التشغيلي", "الابتكار والتعلم", "القيادة", "الأفراد والشراكات والموارد"];
const SDG_NAMES = ["القضاء على الفقر", "القضاء على الجوع", "الصحة الجيدة والرفاهية", "التعليم الجيد", "المساواة بين الجنسين", "المياه النظيفة والنظافة الصحية",
  "طاقة نظيفة وبأسعار معقولة", "العمل اللائق ونمو الاقتصاد", "الصناعة والابتكار والهياكل الأساسية", "الحد من أوجه عدم المساواة", "مدن ومجتمعات محلية مستدامة",
  "الاستهلاك والإنتاج المسؤولان", "العمل المناخي", "الحياة تحت الماء", "الحياة في البر", "السلام والعدل والمؤسسات القوية", "عقد الشراكات لتحقيق الأهداف"];

