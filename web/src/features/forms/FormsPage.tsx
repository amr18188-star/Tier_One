import { useMemo, type KeyboardEvent, type ReactNode } from "react";
import { Skeleton } from "antd";
import { ApartmentOutlined, InboxOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { api, query } from "../../api/client";
import type { FormRow, SubmissionStatus } from "../../api/collection";
import { formatDate } from "../../shared/format";
import { L } from "../../shared/i18n";
import { slotName } from "../../shared/period";
import { PeriodNav } from "../../shared/PeriodNav";
import { QueryError } from "../../shared/QueryState";
import { usePermissions } from "../../shared/session";
import { EmptyNote, oneOf, Panel, PageHeader } from "../../shared/ui";
import { usePeriodParams, useSearchState } from "../../shared/usePeriodParams";
import { byPriority, formModes, type Filters, type Mode } from "./model";
import { MonitorView } from "./MonitorView";
import { TasksView } from "./TasksView";
import "./forms.css";
import { useCycles, queryKeys } from "../../api/queries";

const STATUSES = ["NotStarted", "InProgress", "InReview", "Returned", "Approved"] as const satisfies readonly SubmissionStatus[];
const MODES = ["tasks", "monitor"] as const satisfies readonly Mode[];

/** نسبة المؤشرات المدخلة من المطلوب في كل نماذج الدورة. */
function entryPct(rows: FormRow[]) {
  const req = rows.reduce((n, r) => n + r.required, 0);
  return req === 0 ? 0 : Math.round((100 * rows.reduce((n, r) => n + r.completed, 0)) / req);
}

/** الروابط القديمة: view=mine تصير mine=1، وview=all بلا مرشح؛ يرتب الرابط قبل كل تغيير. */
function normalizeLegacy(next: URLSearchParams) {
  const legacy = next.get("view");
  if (legacy === "mine" || legacy === "all") {
    next.delete("view");
    if (legacy === "mine") next.set("mine", "1");
  }
}

/**
 * نماذج الجهات: سنة الخطة وأرباعها في صف تحت الرأس، ثم وضعان بحسب الدور: «مهامي» (ما ينتظرني بإجراء واحد لكل نموذج)
 * و«متابعة الجهات» (الكليات صفوفًا تفتح على أقسامها). بطاقات المراحل هي مرشح الحالة الوحيد في المتابعة.
 */
export default function FormsPage() {
  // الرابط هو الحالة: السنة والدورة (usePeriodParams) ومعهما tab وview وmine وq، في نسخة واحدة لا يكتب أحدها فوق الآخر.
  const search = useSearchState(normalizeLegacy);
  const { params, update } = search;
  const perms = usePermissions();

  const cycles = useCycles();
  const period = usePeriodParams({ mode: "cycle", cycles: cycles.data, search });
  const ready = cycles.isSuccess && period.ready;
  const timeline = period.timeline;
  const slot = ready ? period.slot : null;
  const cycle = slot?.cycle ?? null;
  // قبل وصول الدورات يطلب ما في الرابط مباشرة؛ الدورة قيد الإعداد لا نماذج لها بعد.
  const cycleId = ready ? (cycle && cycle.status !== "Draft" ? cycle.id : undefined) : period.urlCycleId ?? undefined;
  const cycleOpen = cycle ? cycle.status === "Open" : true;

  const all = useQuery({
    queryKey: queryKeys.forms(cycleId),
    queryFn: ({ signal }) => api.get<FormRow[]>(`/api/forms${query({ cycleId })}`, { signal }),
    enabled: !!cycleId,
  });
  const rows = useMemo(() => [...(all.data ?? [])].sort(byPriority), [all.data]);
  const loading = all.isLoading;
  const count = (f: (r: FormRow) => boolean) => rows.filter(f).length;

  const { modes, initial, readOnly } = formModes(perms, rows.some((r) => r.awaitingMe));
  const tab = oneOf(params.get("tab"), MODES);
  const mode: Mode = tab && modes.includes(tab) ? tab : initial;
  const legacyView = params.get("view");
  const filters: Filters = {
    view: oneOf(legacyView, STATUSES),
    mine: params.get("mine") === "1" || legacyView === "mine",
    q: params.get("q") ?? "",
  };

  const setFilters = (patch: Partial<Filters>) => update({
    ...("view" in patch ? { view: patch.view } : {}),
    ...("mine" in patch ? { mine: patch.mine ? "1" : undefined } : {}),
    ...("q" in patch ? { q: patch.q || undefined } : {}),
  });
  const resetFilters = () => update({ cycle: params.get("cycle") ?? undefined, year: params.get("year") ?? undefined, tab: params.get("tab") ?? undefined }, true);
  const setMode = (m: Mode) => update({ tab: m === initial ? undefined : m });
  const showMine = () => (modes.includes("tasks") && mode !== "tasks" ? setMode("tasks") : setFilters({ mine: true }));

  const waiting = count((r) => r.awaitingMe);
  const overdue = count((r) => r.overdue);
  const periodName = slot ? slotName(slot) : "";

  let body;
  if (cycles.isError && !cycles.data) body = <Panel><QueryError error={cycles.error} onRetry={() => void cycles.refetch()} /></Panel>;
  else if (!ready) body = <Panel loading><span /></Panel>;
  else if (timeline.years.length === 0)
    body = <Panel><EmptyNote>{L("لا توجد دورة لجمع البيانات بعد. تفتح الدورات من صفحة «دورات جمع البيانات».", "There is no collection cycle yet.")}</EmptyNote></Panel>;
  else if (!slot)
    body = <Panel><EmptyNote>{L(`لا دورة لجمع البيانات في ${period.value.year} بعد.`, `No collection cycle in ${period.value.year} yet.`)}</EmptyNote></Panel>;
  else if (slot.state === "future")
    body = (
      <Panel>
        <EmptyNote>{L(`لم يُفتح ${periodName} بعد. يفتح الإدخال في ${formatDate(slot.cycle!.opensOn)}.`, `${periodName} is not open yet. Entry opens on ${formatDate(slot.cycle!.opensOn)}.`)}</EmptyNote>
      </Panel>
    );
  else if (all.isError && !all.data) body = <Panel><QueryError error={all.error} onRetry={() => void all.refetch()} /></Panel>;
  else if (mode === "tasks") body = <TasksView rows={rows} loading={loading} periodName={periodName} cycleOpen={cycleOpen} />;
  else
    body = (
      <MonitorView rows={rows} loading={loading} filters={filters} onFilters={setFilters} onReset={resetFilters} onShowMine={showMine}
        canMine={!readOnly} readOnly={readOnly} cycleOpen={cycleOpen} resetKey={`${cycleId}|${filters.view}|${filters.mine}|${filters.q}`} />
    );

  return (
    <>
      <PageHeader title={L("نماذج الجهات", "Unit forms")}
        subtitle={readOnly
          ? L("اطلاع على نماذج الجهات في نطاقك ومراحلها، دون إدخال أو مراجعة.", "A read-only view of the unit forms in your scope and their stages.")
          : L("كل جهة تدخل قيم مؤشراتها في نموذج واحد، ثم يمر على المراجعين حتى اعتماد الجودة المركزية.",
            "Each unit enters its values in one form, which then passes through reviewers up to central quality approval.")}
        stats={all.data ? [
          ...(readOnly ? [] : [{ label: L("مطلوب مني الآن", "Waiting for me"), value: waiting, tone: waiting > 0 ? "warn" as const : undefined }]),
          { label: L("متأخر عن الموعد", "Overdue"), value: overdue, tone: overdue > 0 ? "bad" as const : undefined },
          { label: L("معتمد نهائيًا", "Approved"), value: <span className="ltr">{count((r) => r.status === "Approved")}/{rows.length}</span>, tone: "ok" as const },
          { label: L("اكتمال الإدخال", "Entry complete"), value: <span className="ltr">{entryPct(rows)}%</span> },
        ] : undefined} />

      {ready
        ? <PeriodNav mode="cycle" cycles={cycles.data} value={period.value} onChange={period.onChange} />
        : !cycles.isError && <div className="fq is-loading" aria-hidden><Skeleton.Input active size="small" /></div>}

      {modes.length > 1 && ready && slot && slot.state !== "future"
        ? <ModeTabs modes={modes} mode={mode} onMode={setMode} taskCount={all.data ? waiting : undefined}>{body}</ModeTabs>
        : body}
    </>
  );
}

const MODE_TEXT: Record<Mode, () => string> = {
  monitor: () => L("متابعة الجهات", "Units overview"),
  tasks: () => L("مهامي", "My tasks"),
};

/** تبويبا «متابعة الجهات / مهامي» لمن له الوضعان (العميد، اللجنة، الجودة، المدير الأعلى). الأسهم تنقل بين التبويبين. */
function ModeTabs({ modes, mode, onMode, taskCount, children }: { modes: Mode[]; mode: Mode; onMode: (m: Mode) => void; taskCount?: number; children: ReactNode }) {
  const onKey = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== "ArrowLeft" && e.key !== "ArrowRight" && e.key !== "Home" && e.key !== "End") return;
    e.preventDefault();
    const i = modes.indexOf(mode);
    const rtl = document.documentElement.dir === "rtl";
    const step = e.key === "Home" ? -i : e.key === "End" ? modes.length - 1 - i : (e.key === "ArrowRight") !== rtl ? 1 : -1;
    const next = modes[(i + step + modes.length) % modes.length];
    onMode(next);
    document.getElementById(`fm-tab-${next}`)?.focus();
  };
  return (
    <>
      <div className="fm-tabs" role="tablist" aria-label={L("طريقة العرض", "View")} onKeyDown={onKey}>
        {modes.map((m) => (
          <button key={m} id={`fm-tab-${m}`} type="button" role="tab" className="fm-tab" aria-selected={m === mode} aria-controls="fm-tabpanel"
            aria-label={m === "tasks" && taskCount !== undefined ? L(`مهامي، ${taskCount} مطلوب مني الآن`, `My tasks, ${taskCount} waiting for me`) : undefined}
            tabIndex={m === mode ? 0 : -1} onClick={() => onMode(m)}>
            {m === "tasks" ? <InboxOutlined aria-hidden /> : <ApartmentOutlined aria-hidden />}
            <span>{MODE_TEXT[m]()}</span>
            {m === "tasks" && taskCount !== undefined && (
              <span className="fm-tab-count" aria-hidden="true">{taskCount}</span>
            )}
          </button>
        ))}
      </div>
      <div id="fm-tabpanel" role="tabpanel" aria-labelledby={`fm-tab-${mode}`} className="fm-tabpanel">{children}</div>
    </>
  );
}
