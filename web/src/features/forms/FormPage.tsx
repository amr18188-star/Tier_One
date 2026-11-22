import { useMemo, useRef, useState } from "react";
import { Alert, Button, Progress, Segmented, Space, Tag, Timeline } from "antd";
import { CalendarOutlined, DownloadOutlined, FileExcelOutlined, HistoryOutlined, RollbackOutlined, UnlockOutlined } from "@ant-design/icons";
import { useParams } from "react-router-dom";
import type { FormItem } from "../../api/collection";
import { formatDate, formatDateTime } from "../../shared/format";
import { L } from "../../shared/i18n";
import { dueText, submissionStatus, unitKind } from "../../shared/labels";
import { stdLabel, tint, usePillarMap } from "../../shared/pillars";
import { usePermissions } from "../../shared/session";
import { EmptyNote, PageHeader, Panel, Stepper } from "../../shared/ui";
import { FormActionDock } from "./FormActionDock";
import { FormDialogHost, type FormDialog } from "./FormDialogs";
import { PillarCards, PillarHeader, PillarStep, type PillarGroup } from "./FormPillars";
import { NoteCard } from "./NoteCard";
import { formEventText } from "./model";
import { useFormPage } from "./useFormPage";
import { usePillarNavigation } from "./usePillarNavigation";
import { AutosaveContext, useAutosaveRegistry } from "./useValueAutosave";
import { ValueRow, type ReturnMark } from "./ValueRow";
import "./form-page.css";

type Filter = "all" | "missing" | "issues" | "flagged";

/**
 * نموذج الجهة: الإدخال والمراجعة في صفحة واحدة. المسار أعلاها يقول أين النموذج ومن عليه الإجراء،
 * ثم بطاقات الركائز (ما أدخل وما يمنع الإرسال في كل ركيزة)، ونموذج ركيزة واحدة في عمود بزري السابقة والتالية على جانبيه
 * (pillar= في الرابط). شريط الإجراء أسفلها ثابت (إرسال، أو اعتماد وإعادة)، والمرشحات تعرض الناقص أو ما به ملاحظات فقط.
 * القيم تحفظ أثناء الكتابة (useValueAutosave)، وكل ما لم يحفظ يحفظ قبل التنقل بين الركائز وقبل الإرسال.
 * The data and its writes are in useFormPage, moving between pillars in usePillarNavigation, the bar in FormActionDock and the
 * dialogs in FormDialogHost.
 */
export default function FormPage() {
  const { id } = useParams<{ id: string }>();
  const { form, refresh, onSaved, submit, applyDecision } = useFormPage(id);
  const [filter, setFilter] = useState<Filter>("all");
  const [marks, setMarks] = useState<Record<string, ReturnMark>>({});
  const [dialog, setDialog] = useState<FormDialog | null>(null);
  const canExtend = usePermissions().can("dc.cycles", "E");
  // الحفظ التلقائي في الصفوف: الجاري منه لشريط الحالة، وflushAll قبل التنقل والإرسال.
  const autosave = useAutosaveRegistry();
  const saving = autosave.saving;

  const f = form.data;
  const items = useMemo(() => f?.sections.flatMap((s) => s.items) ?? [], [f]);
  // المعايير بترتيبها من الخادم (الركيزة ثم المعيار)، مجمعة تحت ركائزها.
  // لون الركيزة: بترتيبها في الخطة إن أتيحت الخطة للمستخدم، وإلا برقم رمزها (P1 الأولى).
  const pillarMap = usePillarMap();
  const pillarIndex = (code: string | null) => {
    if (!code) return -1;
    const known = pillarMap.pillars.find((p) => p.code === code);
    if (known) return known.index;
    const n = Number(code.replace(/\D/g, ""));
    return Number.isFinite(n) && n > 0 ? n - 1 : -1;
  };
  const pillars = useMemo(() => {
    const list: PillarGroup[] = [];
    for (const s of f?.sections ?? []) {
      const last = list.at(-1);
      if (last && last.code === s.pillarCode) last.sections.push(s);
      else list.push({ key: s.pillarCode ?? "none", code: s.pillarCode, name: s.pillarName, index: pillarIndex(s.pillarCode), sections: [s] });
    }
    return list;
  }, [f, pillarMap.pillars]);

  const { position, pillar, goPillar, enter, setStage } = usePillarNavigation(pillars, autosave.flushAll);
  // الصفوف التي عدلت في هذا العرض تبقى ظاهرة ولو لم تعد تطابق المرشح (الناقص يختفي بمجرد حفظه).
  const kept = useRef(new Set<string>());
  const viewKey = `${filter}|${pillar?.key}`;
  const lastView = useRef(viewKey);
  if (lastView.current !== viewKey) {
    lastView.current = viewKey;
    kept.current = new Set();
  }
  for (const rowId of Object.keys(autosave.active)) kept.current.add(rowId);

  const flaggedCount = Object.values(marks).filter((m) => m.checked).length;
  const visible = (i: FormItem) =>
    kept.current.has(i.indicatorId) ? true
    : filter === "all" ? true
      : filter === "missing" ? !i.value
        : filter === "issues" ? i.issues.length > 0 || (!!i.evidenceRequired && i.evidence.every((e) => e.voided) && i.value?.kind === "Number")
          : !!i.value?.reviewNote || !!marks[i.indicatorId]?.checked;

  const missing = items.filter((i) => !i.value);
  const lastSaved = items.reduce<string | null>((m, i) => (i.value && (!m || i.value.editedAt > m) ? i.value.editedAt : m), null);
  /** ينتقل إلى أول مؤشر بلا قيمة (في ركيزته) ويضع المؤشر في حقله. */
  const goToMissing = () => {
    const first = missing[0];
    if (!first) return;
    setFilter("all");
    const home = pillars.find((p) => p.sections.some((s) => s.items.includes(first)));
    if (home) void goPillar(home.key, `i-${first.code}`, true);
  };

  if (!f) return <Panel query={form}>{null}</Panel>;
  const editable = f.actions.canEdit;
  const reviewing = f.actions.canReview;
  // D4: طلب إعادة الفتح ينتظر قرار اللجنة العليا؛ من يقرره يرى الاعتماد والرفض.
  const canDecideReopen = f.actions.canDecideReopen ?? false;
  const reopen = f.reopen ?? null;
  // D2: الاعتماد المباشر اختيار صريح بجانب الاعتماد العادي، ومن يملك أيهما يملك الإعادة للتصحيح.
  const canApprove = f.actions.canApprove ?? (reviewing && !f.actions.directDepartmentApproval && !f.actions.directDeanApproval);
  const directDeanApproval = f.actions.directDeanApproval;
  const directApproval = reviewing && (f.actions.directDepartmentApproval || directDeanApproval);
  const canReturn = reviewing;
  const directStep = f.actions.directStepName ?? f.nextStepName;
  const directLabel = directDeanApproval
    ? L("اعتماد مباشر وإحالة للجنة Tier 1 بالجامعة", "Approve directly and forward to the university Tier 1 committee")
    : L("اعتماد مباشر وإحالة للعميد", "Approve directly and forward to the dean");
  const skippedByDean = f.path.some((p) => p.step === "CollegeCommittee" && p.state === "skipped");
  const current = f.path.findIndex((p) => p.state === "current");

  const noted = items.filter((i) => !!i.value?.reviewNote).length;

  return (
    <AutosaveContext.Provider value={autosave.registry}>
      <PageHeader back={{ to: "/forms", label: L("النماذج", "Forms") }}
        title={<>{f.unitName} <Tag color={submissionStatus[f.status].color}>{submissionStatus[f.status].text}</Tag>{f.trial && <Tag color="purple">{L("بروفة", "Rehearsal")}</Tag>}</>}
        subtitle={<>{unitKind[f.unitKind]} · {f.cycleName} · {L("الفترة", "Period")} <b className="ltr">{f.periodLabel}</b> · {L("الموعد النهائي", "Due")} <b className="ltr">{formatDate(f.dueOn)}</b>{f.overdue && <Tag color="error" className="ms">{L("متأخر", "Overdue")}</Tag>}</>}
        stats={[
          { label: L("أدخلت", "Entered"), value: <span className="ltr">{f.completed}/{f.required}</span>, tone: f.completed === f.required ? "ok" : undefined },
          { label: L("ناقص", "Missing"), value: missing.length, tone: missing.length > 0 ? "warn" : "ok" },
          { label: L("يمنع الإرسال", "Blocking"), value: f.blocking.length, tone: f.blocking.length > 0 ? "bad" : "ok" },
          { label: L("الموعد", "Due"), value: f.status === "Approved" ? "—" : dueText(f.dueOn), tone: f.overdue ? "bad" : undefined },
        ]}
        extra={
          <Space wrap>
            <Button icon={<DownloadOutlined />} href={`/api/forms/${f.id}/template.xlsx`}>{L("تنزيل Excel", "Download Excel")}</Button>
            {editable && <Button icon={<FileExcelOutlined />} onClick={() => setDialog("import")}>{L("إدخال من Excel", "Import from Excel")}</Button>}
            {canExtend && f.cycleStatus === "Open" && f.status !== "Approved" && (
              <Button icon={<CalendarOutlined />} onClick={() => setDialog("extend")}>{L("تمديد موعد الجهة", "Extend deadline")}</Button>
            )}
          </Space>
        } />

      <Panel>
        <Stepper current={current < 0 ? f.path.length : current} steps={f.path.map((p) => ({
          title: p.name,
          skipped: p.state === "skipped",
          note: p.state === "skipped"
            ? skippedByDean ? L("اعتمد العميد مباشرةً", "Approved directly by the dean") : L("اعتمدت لجنة الكلية مباشرةً", "Approved directly by the college committee")
            : undefined,
        }))} />
      </Panel>

      {f.status === "Returned" && (f.returnNote || noted > 0) && (
        <NoteCard notes={f.returnNote ? [f.returnNote] : []} title={L("أعيد النموذج للتصحيح · مطلوب التعديل:", "Returned for correction · changes requested:")}
          extra={noted > 0 && (
            <>
              <RollbackOutlined aria-hidden /> {L(`وملاحظات على ${noted} مؤشرًا داخل الركائز.`, `Plus notes on ${noted} indicators.`)}{" "}
              <Button size="small" type="link" onClick={() => setFilter("flagged")}>{L("اعرضها", "Show them")}</Button>
            </>
          )} />
      )}
      {f.status === "InProgress" && f.returnNote && f.events.at(-1)?.action === "Reopened" && (
        <NoteCard tone="warn" notes={[f.returnNote]}
          title={reopen?.status === "Approved" && reopen.decisionRef
            ? L(`أعيد فتح النموذج بقرار اللجنة العليا (${reopen.decisionRef}):`, `Reopened by committee decision ${reopen.decisionRef}:`)
            : L("أعيد فتح النموذج بعد اعتماده:", "Reopened after approval:")}
          extra={<><UnlockOutlined aria-hidden /> {L("صحّح ما يلزم ثم أعد الإرسال. قيمه المعتمدة السابقة تبقى الرسمية في النتائج حتى يعاد اعتماده.", "Correct what is needed, then send again. The previously approved values stay official until re-approval.")}</>} />
      )}
      {reopen?.status === "Pending" && (
        <Alert type="warning" showIcon className="mb"
          message={L("طلب إعادة فتح ينتظر قرار اللجنة العليا", "A reopen request is awaiting the higher committee")}
          description={<>{reopen.reason}<br /><small className="muted">{reopen.requestedByName ?? ""} · {formatDateTime(reopen.requestedAt)}</small></>} />
      )}
      {reopen?.status === "Rejected" && f.status === "Approved" && (
        <Alert type="info" showIcon className="mb"
          message={L("رفضت اللجنة العليا آخر طلب لإعادة فتح النموذج", "The higher committee rejected the last reopen request")}
          description={<>{reopen.decisionNote}<br /><small className="muted">{reopen.decidedByName ?? ""} · {reopen.decidedAt ? formatDateTime(reopen.decidedAt) : ""}</small></>} />
      )}
      {f.cycleStatus === "Closed" && <Alert type="info" showIcon className="mb" message={L("الدورة مغلقة؛ النموذج للاطلاع فقط.", "The cycle is closed; read only.")} />}
      {reviewing && (
        <Alert type="info" showIcon className="mb"
          message={directApproval && !canApprove
            ? L(`النموذج عند «${f.path[current]?.name}». يمكنك اعتماده مباشرة وإحالته إلى «${directStep}» بسبب مكتوب، أو إعادته للتصحيح.`,
                `The form is at “${f.path[current]?.name}”. You can approve it directly to “${directStep}” with a written reason, or return it.`)
            : directApproval
              ? L(`النموذج ينتظر مراجعتك (${f.path[current]?.name}). اعتمده لينتقل إلى ${f.nextStepName ?? "الاعتماد النهائي"}، أو اعتمده مباشرة إلى «${directStep}» بسبب مكتوب، أو أعده للتصحيح.`,
                  `This form is waiting for your review. Approve it to ${f.nextStepName ?? "final approval"}, approve it directly to “${directStep}” with a reason, or return it.`)
              : L(`النموذج ينتظر مراجعتك (${f.path[current]?.name}). اعتمده لينتقل إلى ${f.nextStepName ?? "الاعتماد النهائي"}، أو علّم المؤشرات التي تحتاج تصحيحًا وأعده.`,
                  "This form is waiting for your review.")} />
      )}

      <Panel className="fp-summary">
        <div className="f-head">
          <div className="f-progress">
            <span>{L("أدخلت", "Entered")} <b className="ltr">{f.completed}/{f.required}</b></span>
            <Progress percent={f.required ? Math.round((100 * f.completed) / f.required) : 0} showInfo={false} />
          </div>
          <Segmented value={filter} onChange={(v) => setFilter(v as Filter)}
            options={[
              { value: "all", label: L("الكل", "All") },
              { value: "missing", label: L(`الناقص (${items.filter((i) => !i.value).length})`, "Missing") },
              { value: "issues", label: L("به تنبيهات", "With issues") },
              { value: "flagged", label: L("ملاحظات المراجعة", "Review notes") },
            ]} />
        </div>
        {pillars.length > 0 && pillar && <PillarCards groups={pillars} selected={pillar.key} onSelect={(key) => void goPillar(key)} />}
      </Panel>

      {/* ركيزة واحدة في عمود: معاييرها، وفي كل معيار مؤشراته؛ وزرا السابقة والتالية على جانبيه في منتصف الشاشة. */}
      {pillar && (
        <div className="fp-stage" style={tint(pillar.index)}>
          <div className="fp-side is-prev">
            <PillarStep dir="prev" target={pillars[position - 1] ?? null} position={position - 1} onGo={() => void goPillar(pillars[position - 1].key)} />
          </div>
          <div id="fp-pillar" ref={setStage} className="fp-main" tabIndex={-1} role="region"
            aria-label={pillar.name ?? L("معايير بلا ركيزة", "Standards without a pillar")}>
            <div key={pillar.key} className="fp-slide f-pillar" data-enter={enter ?? undefined}>
            <PillarHeader group={pillar} position={position} total={pillars.length} onStandard={(code) => void goPillar(pillar.key, `s-${code}`)} />
            {(() => {
              const shown = pillar.sections.map((s) => ({ s, rows: s.items.filter(visible) })).filter((x) => x.rows.length > 0);
              if (shown.length === 0)
                return <Panel><EmptyNote>{L("لا مؤشرات في هذه الركيزة بهذا المرشح. اختر ركيزة أخرى أو «الكل».", "No indicators in this pillar with this filter.")}</EmptyNote></Panel>;
              return shown.map(({ s, rows }) => (
                <Panel key={s.standardCode} id={`s-${s.standardCode}`} className="f-std"
                  title={<><code className="pill-code ltr">{stdLabel(s.standardCode)}</code> {s.standardName}</>}
                  extra={<span className="f-std-count ltr">{s.items.filter((i) => i.value).length}/{s.items.length}</span>}>
                  {rows.map((i) => (
                    <ValueRow key={`${f.id}:${i.indicatorId}`} formId={f.id} item={i} editable={editable} reviewing={canReturn}
                      mark={marks[i.indicatorId]} onMark={(m) => setMarks((x) => ({ ...x, [i.indicatorId]: m }))} onSaved={onSaved} />
                  ))}
                </Panel>
              ));
            })()}
            </div>
          </div>
          <div className="fp-side is-next">
            <PillarStep dir="next" target={pillars[position + 1] ?? null} position={position + 1} onGo={() => void goPillar(pillars[position + 1].key)} />
          </div>
        </div>
      )}

      {f.events.length > 0 && (
        <Panel title={L("سجل النموذج", "Form history")} icon={<HistoryOutlined />}>
          <Timeline items={[...f.events].reverse().map((e) => ({
            color: e.action === "Returned" ? "red" : e.action === "Approved" ? "green" : e.action === "Reopened" ? "var(--ds-status-caution)" : "blue",
            children: (
              <>
                <b>{formEventText(e)}</b> — {e.byName} <small className="muted ltr">{formatDateTime(e.at)}</small>
                {e.note && <div className="muted">{e.note}</div>}
              </>
            ),
          }))} />
        </Panel>
      )}

      <FormActionDock f={f} rights={{ editable, reviewing, canReturn, canApprove, directApproval, directLabel, canDecideReopen }}
        pillarCount={pillars.length} position={position} onPillar={(to) => void goPillar(pillars[to].key)}
        missing={missing} saving={saving} lastSaved={lastSaved} flaggedCount={flaggedCount} submit={submit}
        onNextMissing={goToMissing} onSend={async () => { await autosave.flushAll(); setDialog("submit"); }} onDialog={setDialog} />

      <FormDialogHost dialog={dialog} onClose={() => setDialog(null)} f={f} items={items} submit={submit} flushAll={autosave.flushAll}
        marks={marks} clearMarks={() => setMarks({})} applyDecision={applyDecision} refresh={() => void refresh()} />
    </AutosaveContext.Provider>
  );
}
