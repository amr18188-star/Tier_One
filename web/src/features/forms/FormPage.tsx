import { useEffect, useMemo, useRef, useState } from "react";
import { Alert, App, Button, DatePicker, Form as AntForm, Input, Modal, Popover, Progress, Segmented, Space, Tag, Timeline, Tooltip } from "antd";
import dayjs, { type Dayjs } from "dayjs";
import {
  AimOutlined, CalendarOutlined, CheckOutlined, DownloadOutlined, FileExcelOutlined, HistoryOutlined, LoadingOutlined, RollbackOutlined, SendOutlined, UnlockOutlined,
  WarningOutlined,
} from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams } from "react-router-dom";
import { api } from "../../api/client";
import type { Form, FormItem } from "../../api/collection";
import { DecisionModal } from "../../shared/DecisionModal";
import { errorText, formatDate, formatDateTime } from "../../shared/format";
import { L, useLang } from "../../shared/i18n";
import { dueText, submissionStatus, unitKind } from "../../shared/labels";
import { stdLabel, tint, usePillarMap } from "../../shared/pillars";
import { usePermissions } from "../../shared/session";
import { EmptyNote, PageHeader, Panel, Stepper } from "../../shared/ui";
import { useSearchState } from "../../shared/usePeriodParams";
import { PillarCards, PillarHeader, PillarMobileBar, PillarStep, type PillarGroup } from "./FormPillars";
import { ImportModal } from "./ImportModal";
import { NoteCard } from "./NoteCard";
import { formEventText, skippedSteps } from "./model";
import { usePillarSwipe, useReducedMotion } from "./usePillarSwipe";
import { AutosaveContext, useAutosaveRegistry } from "./useValueAutosave";
import { savedAt, ValueRow, type ReturnMark } from "./ValueRow";
import "./form-page.css";

type Filter = "all" | "missing" | "issues" | "flagged";

/**
 * نموذج الجهة: الإدخال والمراجعة في صفحة واحدة. المسار أعلاها يقول أين النموذج ومن عليه الإجراء،
 * ثم بطاقات الركائز (ما أدخل وما يمنع الإرسال في كل ركيزة)، ونموذج ركيزة واحدة في عمود بزري السابقة والتالية على جانبيه
 * (pillar= في الرابط). شريط الإجراء أسفلها ثابت (إرسال، أو اعتماد وإعادة)، والمرشحات تعرض الناقص أو ما به ملاحظات فقط.
 * القيم تحفظ أثناء الكتابة (useValueAutosave)، وكل ما لم يحفظ يحفظ قبل التنقل بين الركائز وقبل الإرسال.
 */
export default function FormPage() {
  const { id } = useParams<{ id: string }>();
  const qc = useQueryClient();
  const { message } = App.useApp();
  const key = ["form", id];
  const form = useQuery({ queryKey: key, queryFn: () => api.get<Form>(`/api/forms/${id}`) });
  const [filter, setFilter] = useState<Filter>("all");
  const [marks, setMarks] = useState<Record<string, ReturnMark>>({});
  const [dialog, setDialog] = useState<"submit" | "approve" | "direct" | "return" | "reopen" | "reopenApprove" | "reopenReject" | "import" | "extend" | null>(null);
  const canExtend = usePermissions().can("dc.cycles", "E");
  // الحفظ التلقائي في الصفوف: الجاري منه لشريط الحالة، وflushAll قبل التنقل والإرسال.
  const autosave = useAutosaveRegistry();
  const saving = autosave.saving;
  const { params, update } = useSearchState();

  const refresh = () => qc.invalidateQueries({ queryKey: key });
  const onSaved = (item: FormItem) => {
    qc.setQueryData<Form>(key, (f) => f && {
      ...f,
      sections: f.sections.map((s) => ({ ...s, items: s.items.map((i) => (i.indicatorId === item.indicatorId ? item : i)) })),
    });
    void refresh();
  };

  const submit = useMutation({
    mutationFn: () => api.post<Form>(`/api/forms/${id}/submit`),
    onSuccess: (f) => {
      qc.setQueryData(key, f);
      void qc.invalidateQueries({ queryKey: ["forms"] });
      message.success(L(`أرسل النموذج إلى «${f.path.find((p) => p.state === "current")?.name ?? ""}»`, "Form sent for review"));
    },
  });

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

  // الركيزة المعروضة من الرابط (pillar=رمزها)، وإلا الأولى.
  const position = Math.max(0, pillars.findIndex((p) => p.key === params.get("pillar")));
  const pillar = pillars[position];
  // الصفوف التي عدلت في هذا العرض تبقى ظاهرة ولو لم تعد تطابق المرشح (الناقص يختفي بمجرد حفظه).
  const kept = useRef(new Set<string>());
  const viewKey = `${filter}|${pillar?.key}`;
  const lastView = useRef(viewKey);
  if (lastView.current !== viewKey) {
    lastView.current = viewKey;
    kept.current = new Set();
  }
  for (const rowId of Object.keys(autosave.active)) kept.current.add(rowId);

  // بعد الانتقال إلى ركيزة: أعلاها، أو معيار فيها، أو صف بعينه (التالي الناقص).
  const scrollTo = useRef<{ id: string; focus?: boolean } | null>(null);
  const [, setTick] = useState(0);
  useEffect(() => {
    const target = scrollTo.current;
    if (!target) return;
    scrollTo.current = null;
    requestAnimationFrame(() => {
      const el = document.getElementById(target.id);
      if (!el) return;
      el.scrollIntoView({ behavior: "smooth", block: target.focus ? "center" : "start" });
      if (target.focus) el.querySelector<HTMLInputElement>("input")?.focus({ preventScroll: true });
      else if (target.id === "fp-pillar") el.focus({ preventScroll: true });
    });
  });
  /** ينتقل إلى ركيزة (بالأزرار أو البطاقات أو السحب أو شريط الجوال) بعد حفظ ما لم يحفظ، ثم إلى أعلاها. */
  const [enter, setEnter] = useState<"next" | "prev" | null>(null);
  const goPillar = async (key: string, scrollId = "fp-pillar", focus = false) => {
    await autosave.flushAll();
    scrollTo.current = { id: scrollId, focus };
    const to = pillars.findIndex((p) => p.key === key);
    if (key !== pillar?.key) {
      setEnter(to > position ? "next" : "prev");
      update({ pillar: key });
    } else setTick((t) => t + 1);
  };
  // السحب بين الركائز على العمود (usePillarSwipe): التالية جهة نهاية السطر، يسارًا في العربية.
  const [stage, setStage] = useState<HTMLElement | null>(null);
  const rtl = useLang() === "ar";
  const reduced = useReducedMotion();
  usePillarSwipe(stage, {
    rtl, reduced, hasPrev: position > 0, hasNext: position < pillars.length - 1,
    onPrev: () => void goPillar(pillars[position - 1].key),
    onNext: () => void goPillar(pillars[position + 1].key),
  });
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
    ? L("اعتماد مباشر وإحالة للجودة المركزية", "Approve directly and forward to central quality")
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

      {(pillars.length > 1 || editable || reviewing || f.actions.canReopen || canDecideReopen) && (
      <div className="fp-dock no-print">
      {pillars.length > 1 && (
        <PillarMobileBar position={position} total={pillars.length} hasPrev={position > 0} hasNext={position < pillars.length - 1}
          onPrev={() => void goPillar(pillars[position - 1].key)} onNext={() => void goPillar(pillars[position + 1].key)} />
      )}
      {(editable || reviewing || f.actions.canReopen || canDecideReopen) && (
        <div className="f-actions">
          <span className="grow">
            {editable && (
              <span className="f-summary" aria-live="polite">
                <span>{L("أدخلت", "Entered")} <b className="ltr">{f.completed}/{f.required}</b></span>
                {missing.length > 0 && <span>{L("ناقص", "Missing")} <b className="ltr">{missing.length}</b></span>}
                {saving > 0 ? <span><LoadingOutlined aria-hidden /> {L("جار الحفظ…", "Saving…")}</span>
                  : lastSaved && <span>{L("آخر حفظ", "Last saved")} <b className="ltr">{savedAt(lastSaved)}</b></span>}
                {f.blocking.length === 0
                  ? <span className="f-issue" style={{ color: "var(--ok)" }}><CheckOutlined /> {L("مكتمل وجاهز للإرسال", "Complete and ready")}</span>
                  : <Popover title={L("ما يمنع الإرسال", "What blocks sending")} content={<ul style={{ maxWidth: 420, maxHeight: 300, overflow: "auto" }}>{f.blocking.map((b) => <li key={b}>{b}</li>)}</ul>}>
                      <Button type="link" size="small" className="f-issue warn" style={{ padding: 0 }}><WarningOutlined /> {L(`${f.blocking.length} أمر يمنع الإرسال`, `${f.blocking.length} items block sending`)}</Button>
                    </Popover>}
              </span>
            )}
            {canReturn && (flaggedCount > 0
              ? L(`علّمت ${flaggedCount} مؤشرًا للتصحيح.`, `${flaggedCount} marked for correction.`)
              : L("راجع القيم والأدلة، ثم اعتمد أو أعد.", "Review values and evidence, then approve or return."))}
          </span>
          {submit.error && <span className="f-issue err">{errorText(submit.error)}</span>}
          {editable && missing.length > 0 && (
            <Button size="large" icon={<AimOutlined />} onClick={goToMissing}>{L("التالي الناقص", "Next missing")}</Button>
          )}
          {editable && (
            <Tooltip title={!f.actions.canSubmit ? L("يتفعل حين يكتمل النموذج؛ اعرض «ما يمنع الإرسال».", "Enabled once the form is complete.") : undefined}>
              <Button type="primary" size="large" icon={<SendOutlined />} disabled={!f.actions.canSubmit} loading={submit.isPending}
                onClick={async () => { await autosave.flushAll(); setDialog("submit"); }}>
                {L("إرسال للمراجعة", "Send for review")}
              </Button>
            </Tooltip>
          )}
          {reviewing && (
            <>
              {canReturn && <Button danger size="large" icon={<RollbackOutlined />} onClick={() => setDialog("return")}>{L("إعادة للتصحيح", "Return")}</Button>}
              {directApproval && (
                <Button type={canApprove ? "default" : "primary"} size="large" icon={<CheckOutlined />} disabled={flaggedCount > 0} onClick={() => setDialog("direct")}>
                  {directLabel}
                </Button>
              )}
              {canApprove && (
                <Button type="primary" size="large" icon={<CheckOutlined />} disabled={flaggedCount > 0} onClick={() => setDialog("approve")}>
                  {f.nextStepName ? L("اعتماد وإحالة", "Approve") : L("اعتماد نهائي", "Final approval")}
                </Button>
              )}
            </>
          )}
          {f.actions.canReopen && <Button icon={<UnlockOutlined />} onClick={() => setDialog("reopen")}>{L("طلب إعادة فتح للتصحيح", "Request reopening")}</Button>}
          {canDecideReopen && (
            <>
              <Button danger size="large" onClick={() => setDialog("reopenReject")}>{L("رفض طلب إعادة الفتح", "Reject the request")}</Button>
              <Button type="primary" size="large" icon={<UnlockOutlined />} onClick={() => setDialog("reopenApprove")}>{L("اعتماد إعادة الفتح", "Approve reopening")}</Button>
            </>
          )}
        </div>
      )}
      </div>
      )}

      {dialog === "submit" && (
        <SubmitModal form={f} items={items} pending={submit.isPending} error={submit.error} onClose={() => setDialog(null)}
          onConfirm={async () => { await autosave.flushAll(); await submit.mutateAsync(); setDialog(null); }} />
      )}
      {(dialog === "approve" || dialog === "direct") && (
        <ApproveModal form={f} direct={dialog === "direct"} onClose={() => setDialog(null)}
          onDone={(next) => { qc.setQueryData(key, next); void qc.invalidateQueries({ queryKey: ["forms"] }); }} />
      )}
      {dialog === "return" && (
        <DecisionModal title={L("إعادة النموذج للتصحيح", "Return the form")} field="note" label={L("الملاحظة العامة لمدخل البيانات", "Note to the data entry")} okText={L("إعادة", "Return")} danger
          explain={flaggedCount > 0
            ? L(`ستصل ملاحظاتك على ${flaggedCount} مؤشرًا مميزة في النموذج، مع هذه الملاحظة العامة.`, `${flaggedCount} indicator notes will be sent.`)
            : L("لم تعلّم مؤشرات بعينها؛ تكفي ملاحظة عامة واضحة. يمكنك تعليم المؤشرات بخانة «يحتاج تصحيحًا».", "No indicator marked; write a clear note.")}
          onSubmit={async (note) => {
            const next = await api.post<Form>(`/api/forms/${f.id}/return`, {
              note, items: Object.entries(marks).filter(([, m]) => m.checked).map(([indicatorId, m]) => ({ indicatorId, note: m.note || note })),
            });
            setMarks({});
            qc.setQueryData(key, next);
            void qc.invalidateQueries({ queryKey: ["forms"] });
          }}
          onClose={() => setDialog(null)} />
      )}
      {dialog === "reopen" && (
        <DecisionModal title={L("طلب إعادة فتح نموذج معتمد", "Request reopening an approved form")} field="note" label={L("سبب إعادة الفتح", "Reason")} okText={L("إرسال الطلب", "Send request")}
          explain={L("يرسل الطلب إلى اللجنة العليا ويبقى النموذج معتمدًا ومقفلًا حتى تعتمده برقم المحضر. عندها يعود إلى الإدخال ويمر بخطوات الاعتماد كلها من جديد، وتبقى قيمه المعتمدة الحالية رسمية في النتائج حتى يعاد اعتماده.",
            "The request goes to the higher committee; the form stays approved and locked until it approves with a decision reference.")}
          onSubmit={async (note) => {
            qc.setQueryData(key, await api.post<Form>(`/api/forms/${f.id}/reopen`, { note }));
            void qc.invalidateQueries({ queryKey: ["forms"] });
          }}
          onClose={() => setDialog(null)} />
      )}
      {dialog === "reopenApprove" && (
        <DecisionModal title={L("اعتماد إعادة فتح النموذج", "Approve reopening the form")} field="decisionRef" label={L("رقم محضر اللجنة العليا", "Committee decision reference")} okText={L("اعتماد وإعادة الفتح", "Approve and reopen")}
          explain={<>{L("سبب الطلب:", "Reason:")} {reopen?.reason}<br />{L("يعود النموذج إلى الإدخال، وتحفظ قيمه المعتمدة الحالية وتبقى الرسمية في النتائج حتى يعاد اعتماده.", "The form returns to entry; its approved values are kept and stay official until re-approval.")}</>}
          onSubmit={async (decisionRef) => {
            qc.setQueryData(key, await api.post<Form>(`/api/forms/${f.id}/reopen/approve`, { decisionRef, note: null }));
            void qc.invalidateQueries({ queryKey: ["forms"] });
            void qc.invalidateQueries({ queryKey: ["results"] });
          }}
          onClose={() => setDialog(null)} />
      )}
      {dialog === "reopenReject" && (
        <DecisionModal title={L("رفض طلب إعادة الفتح", "Reject the reopen request")} field="note" label={L("سبب الرفض", "Reason")} okText={L("رفض", "Reject")} danger
          explain={<>{L("سبب الطلب:", "Reason:")} {reopen?.reason}<br />{L("يبقى النموذج معتمدًا ومقفلًا، ويرى مقدم الطلب سبب الرفض.", "The form stays approved; the requester sees the reason.")}</>}
          onSubmit={async (note) => {
            qc.setQueryData(key, await api.post<Form>(`/api/forms/${f.id}/reopen/reject`, { note }));
            void qc.invalidateQueries({ queryKey: ["forms"] });
          }}
          onClose={() => setDialog(null)} />
      )}
      {dialog === "extend" && <ExtendModal form={f} onClose={() => setDialog(null)} onDone={(next) => { qc.setQueryData(key, next); void qc.invalidateQueries({ queryKey: ["forms"] }); }} />}
      {dialog === "import" && <ImportModal formId={f.id} onClose={() => setDialog(null)} onApplied={() => void refresh()} />}
    </AutosaveContext.Provider>
  );
}

/** ملخص ما سيرسل قبل التأكيد: كم قيمة وكم صفر وغير متاح ولا ينطبق، والتفسيرات والأدلة، وإلى من يذهب النموذج. */
function SubmitModal({ form, items, pending, error, onConfirm, onClose }: {
  form: Form; items: FormItem[]; pending: boolean; error: unknown; onConfirm: () => Promise<void>; onClose: () => void;
}) {
  const v = (i: FormItem) => i.value;
  const numbers = items.filter((i) => v(i)?.kind === "Number");
  const zeros = numbers.filter((i) => (i.usesRatio ? v(i)!.numerator : v(i)!.value) === 0).length;
  const rows = [
    { label: L("قيم مدخلة", "Values entered"), n: numbers.length - zeros },
    { label: L("منها صفر", "Zero values"), n: zeros },
    { label: L("غير متاح (بسبب مكتوب)", "Not available"), n: items.filter((i) => v(i)?.kind === "NotAvailable").length },
    { label: L("لا ينطبق (بسبب مكتوب)", "Not applicable"), n: items.filter((i) => v(i)?.kind === "NotApplicable").length },
    { label: L("تفسيرات للمراجع", "Explanations"), n: items.filter((i) => !!v(i)?.explanation).length },
    { label: L("أدلة مرفقة", "Evidence files"), n: items.reduce((s, i) => s + i.evidence.filter((e) => !e.voided).length, 0) },
  ];
  return (
    <Modal open title={L("مراجعة قبل الإرسال", "Review before sending")} onCancel={onClose} onOk={() => void onConfirm()} confirmLoading={pending}
      okText={L("إرسال للمراجعة", "Send for review")} cancelText={L("رجوع للتعديل", "Back to editing")}>
      {!!error && <Alert type="error" showIcon message={errorText(error)} className="mb" />}
      <p>{L(`${form.unitName} · ${form.periodLabel}: ${form.completed} من ${form.required} مؤشرًا.`, `${form.completed} of ${form.required} indicators.`)}</p>
      <ul className="f-submit-sum">{rows.map((r) => <li key={r.label}><span>{r.label}</span><b className="ltr">{r.n}</b></li>)}</ul>
      <Alert type="info" showIcon message={L(`يقفل النموذج للتعديل وينتقل إلى «${form.path[1]?.name ?? ""}». يعاد إليك إن احتاج تصحيحًا.`, "The form will be locked until returned.")} />
    </Modal>
  );
}

function ApproveModal({ form, direct, onClose, onDone }: { form: Form; direct: boolean; onClose: () => void; onDone: (f: Form) => void }) {
  const [version] = useState(form.version);
  const [fields] = AntForm.useForm<{ note: string }>();
  const run = useMutation({
    mutationFn: (note: string | null) => api.post<Form>(`/api/forms/${form.id}/approve`, { note, version, direct }),
    onSuccess: (f) => { onDone(f); onClose(); },
  });
  const final = !form.nextStepName;
  const skipped = direct ? skippedSteps(form, form.actions.directStepName) : [];
  const title = direct
    ? form.actions.directDeanApproval ? L("اعتماد مباشر من العميد", "Direct dean approval") : L("اعتماد مباشر لنموذج القسم", "Direct approval of the department form")
    : final ? L("الاعتماد النهائي", "Final approval") : L("اعتماد وإحالة", "Approve and forward");
  return (
    <Modal open title={title} onCancel={onClose} onOk={() => (direct ? fields.submit() : run.mutate(null))} confirmLoading={run.isPending}
      okText={L("اعتماد", "Approve")} cancelText={L("إلغاء", "Cancel")} destroyOnHidden>
      {run.error && <Alert type="error" showIcon message={errorText(run.error)} className="mb" />}
      {direct ? (
        <>
          <p>{L(`ينتقل النموذج إلى «${form.actions.directStepName ?? ""}» متخطيًا: ${skipped.join("، ")}. يسجل التخطي وسببه في سجل النموذج.`,
            `The form moves to ${form.actions.directStepName ?? ""}, skipping ${skipped.join(", ")}. The skip and its reason are recorded.`)}</p>
          <AntForm form={fields} layout="vertical" onFinish={(v) => run.mutate(v.note.trim())}>
            <AntForm.Item name="note" label={L("سبب التخطي", "Reason for skipping")}
              rules={[{ required: true, whitespace: true, min: 5, message: L("اكتب سبب التخطي (5 أحرف على الأقل)", "Enter the reason (at least 5 characters)") }]}>
              <Input.TextArea rows={3} maxLength={2000} autoFocus />
            </AntForm.Item>
          </AntForm>
        </>
      ) : (
        <p>{final
          ? L(`تعتمد قيم ${form.unitName} نهائيًا فتدخل نتائج الجامعة، ويقفل النموذج.`, "Values become final and enter the results.")
          : L(`ينتقل النموذج إلى «${form.nextStepName}».`, `The form moves to ${form.nextStepName}.`)}</p>
      )}
    </Modal>
  );
}

function ExtendModal({ form, onClose, onDone }: { form: Form; onClose: () => void; onDone: (f: Form) => void }) {
  const [fields] = AntForm.useForm<{ dueOn: Dayjs; reason: string }>();
  const run = useMutation({
    mutationFn: (v: { dueOn: Dayjs; reason: string }) => api.post<Form>(`/api/forms/${form.id}/extend`, { dueOn: v.dueOn.format("YYYY-MM-DD"), reason: v.reason }),
    onSuccess: (f) => { onDone(f); onClose(); },
  });
  return (
    <Modal open title={L(`تمديد موعد ${form.unitName}`, `Extend ${form.unitName}`)} onCancel={onClose} onOk={() => fields.submit()} confirmLoading={run.isPending}
      okText={L("تمديد", "Extend")} cancelText={L("إلغاء", "Cancel")} destroyOnHidden>
      {run.error && <Alert type="error" showIcon message={errorText(run.error)} className="mb" />}
      <p>{L("يمدد موعد هذه الجهة وحدها بقرار مسجل؛ موعد الدورة لبقية الجهات لا يتغير.", "Extends this unit only; the cycle deadline stays.")}</p>
      <AntForm form={fields} layout="vertical" onFinish={(v) => run.mutate(v)} initialValues={{ dueOn: dayjs(form.dueOn).add(7, "day") }}>
        <AntForm.Item name="dueOn" label={L("الموعد الجديد", "New deadline")} rules={[{ required: true }]}>
          <DatePicker disabledDate={(d) => !d.isAfter(dayjs(form.dueOn), "day")} />
        </AntForm.Item>
        <AntForm.Item name="reason" label={L("السبب أو رقم القرار", "Reason or decision")} rules={[{ required: true, min: 5, message: L("اكتب السبب", "Enter the reason") }]}>
          <Input maxLength={500} />
        </AntForm.Item>
      </AntForm>
    </Modal>
  );
}
