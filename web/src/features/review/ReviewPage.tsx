import { useEffect, useMemo, useRef, useState } from "react";
import { Alert, App, Button, Input, InputNumber, Tag, Tooltip } from "antd";
import { CheckCircleOutlined, DownOutlined, SearchOutlined, SendOutlined, SwapOutlined } from "@ant-design/icons";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { api } from "../../api/client";
import { invalidate } from "../../api/invalidate";
import type { Review, ReviewCycle, ReviewRow, ReviewState } from "../../api/scorecard";
import { AccordionPanel, Reveal, useSingleOpen } from "../../shared/AccordionPanel";
import { howCalculated } from "../../shared/calc";
import { errorText, formatDateTime } from "../../shared/format";
import { L } from "../../shared/i18n";
import { aggregation, cycleStatus, num, submissionStatus } from "../../shared/labels";
import { stdLabel, tint, usePillarMap, type PillarInfo } from "../../shared/pillars";
import { QueryError } from "../../shared/QueryState";
import { usePermissions } from "../../shared/session";
import { PanelSkeleton } from "../../shared/PanelSkeleton";
import { EmptyNote, FilterMeta, PageHeader } from "../../shared/ui";
import { usePeriodParams, useSearchState } from "../../shared/usePeriodParams";
import { pillarNum } from "../strategy/model";
import { latestCycle, ReviewPeriod, reviewable } from "./ReviewPeriod";
import "./review.css";
import "./review-redesign.css";
import { useCycles } from "../../api/queries";

/**
 * مراجعة مسئول ملف Tier 1: لكل مؤشر في الربع قائمة بتقدم الكليات (قيمة كل كلية ونسبتها من مستهدف الجامعة)، وحساب الجامعة منها
 * بقاعدة التجميع (مجموع البسوط ÷ مجموع المقامات للنسب)، ثم زر ينقل القيمة إلى مؤشر الجامعة في ذلك الربع.
 * القيمة المنقولة وحدها تدخل بطاقة الأداء والفئة وملف المجلس.
 * الركائز لوحات تطوى (لوحة واحدة مفتوحة، كلها مطوية في البداية، والمفتوحة في ?pillar=01…06)؛ رأس كل ركيزة يعرض حال نقلها في الربع.
 */
const STATES: { key: ReviewState; ar: string; en: string; color: string }[] = [
  { key: "Ready", ar: "جاهز للنقل", en: "Ready", color: "processing" },
  { key: "Changed", ar: "تغير بعد النقل", en: "Changed", color: "warning" },
  { key: "Incomplete", ar: "لم تكتمل الكليات", en: "Incomplete", color: "gold" },
  { key: "Transferred", ar: "منقول", en: "Transferred", color: "success" },
  { key: "NoData", ar: "لا قيم معتمدة", en: "No approved values", color: "default" },
];
const stateOf = (s: ReviewState) => STATES.find((x) => x.key === s)!;
/** ترتيب الحالات في ملخص رأس الركيزة: ما يحتاج عملًا أولًا (المنقول يعرض وحده في طرف الرأس). */
const SUMMARY_ORDER: ReviewState[] = ["Changed", "Ready", "Incomplete", "NoData"];
const fmt = (v: number | null | undefined, unit: string) => (v === null || v === undefined ? "—" : unit.trim() === "%" ? `${num(v)}%` : num(v));
const levelText = { Department: "من الأقسام", College: "من الكليات", University: "مركزي على مستوى الجامعة" } as const;
const cycleStatusEn = { Draft: "Draft", Open: "Open", Closed: "Closed" } as const;

/** مفتاح الركيزة في الرابط (?pillar=): رقمها في الخطة 01…06، و«none» للمعايير بلا ركيزة. */
const pillarKey = (index: number) => (index >= 0 ? pillarNum(index) : "none");

/** «14 مؤشرًا» بقواعد العدد في العربية. */
function indicatorCount(n: number): string {
  if (n === 0) return L("لا مؤشرات", "No indicators");
  if (n === 1) return L("مؤشر واحد", "1 indicator");
  if (n === 2) return L("مؤشران", "2 indicators");
  return L(n <= 10 ? `${n} مؤشرات` : `${n} مؤشرًا`, `${n} indicators`);
}

function periodText(c: ReviewCycle): string {
  return L(`${c.label} (${cycleStatus[c.status].text})`, `${c.year} · Q${c.quarter} (${cycleStatusEn[c.status]})`);
}

interface PillarGroup { key: string; index: number; info: PillarInfo | undefined; all: ReviewRow[]; shown: ReviewRow[] }

/** تغيير الربع يسقط رابط المؤشر (?indicator=) كما كان؛ ويبقي الركيزة المفتوحة. */
const dropIndicator = (next: URLSearchParams) => next.delete("indicator");

export default function ReviewPage() {
  // الرابط: cycle وyear (usePeriodParams) وpillar (useSingleOpen) وindicator، في نسخة واحدة لا يكتب أحدها فوق الآخر.
  const search = useSearchState(dropIndicator);
  const params = search.params;
  const perms = usePermissions();
  // الربع من الشريط المشترك: دورات المراجعة من /api/cycles (الاستعلام نفسه في صفحة النماذج)، وافتراضها أحدث دورة كما في الخادم.
  const canCycles = perms.can("dc.cycles", "V");
  const cyclesQ = useCycles({ enabled: canCycles });
  const navMode = canCycles && !cyclesQ.isError;
  const navCycles = useMemo(() => cyclesQ.data?.filter(reviewable), [cyclesQ.data]);
  const fallbackDefault = latestCycle(navCycles);
  // سنة بلا دورة في الرابط (نقر سنة لا دورة فيها) تبقى كما هي؛ الافتراض للرابط الخالي من السنة والدورة.
  const period = usePeriodParams({ mode: "cycle", cycles: navMode ? navCycles : [], search,
    defaults: !params.get("year") && fallbackDefault ? { cycleId: fallbackDefault.id } : undefined });
  const waiting = navMode && !period.ready;
  const noCycleInYear = navMode && period.ready && !period.cycle && (navCycles?.length ?? 0) > 0;
  // البيانات تتبع ربع الشريط؛ ومن لا يرى الدورات يتبع cycle= في الرابط كما كان (وبدونه افتراض الخادم).
  const cycleId = navMode ? period.cycle?.id : params.get("cycle") ?? undefined;
  const review = useQuery({
    queryKey: ["review", cycleId], queryFn: () => api.get<Review>(`/api/review${cycleId ? `?cycleId=${cycleId}` : ""}`), enabled: !waiting && !noCycleInYear,
    // تغيير الربع أو السنة يغير المفتاح: تبقى ركائز الربع السابق ظاهرة (وأجسامها هياكل) حتى يصل الجديد، فلا تُمحى اللوحات وتعود.
    placeholderData: keepPreviousData,
  });
  const qc = useQueryClient();
  const { message, modal } = App.useApp();
  const [seenCycles, setSeenCycles] = useState<ReviewCycle[]>([]);
  const [state, setState] = useState<ReviewState | "all">("all");
  const [q, setQ] = useState("");
  const [picked, setOpen] = useState<string | null | undefined>(undefined);
  const pillarMap = usePillarMap();
  const r = review.data;
  useEffect(() => { if (r) setSeenCycles(r.cycles); }, [r]);
  // ‎?indicator=1.01 يفتح تفصيل المؤشر مباشرة (رابط من بطاقة الأداء أو صفحة المؤشر).
  const deepRow = r?.rows.find((x) => x.code === params.get("indicator"));
  const open = picked !== undefined ? picked : deepRow?.indicatorId ?? null;

  const all = useMutation({
    mutationFn: () => api.post<{ transferred: number }>("/api/review/transfer-all", { cycleId: r!.cycle!.id }),
    onSuccess: async (res) => {
      message.success(L(`نقلت ${res.transferred} قيمة إلى مؤشرات الجامعة.`, `${res.transferred} values transferred.`));
      await invalidate.afterTransfer(qc);
    },
  });

  const rows = r?.rows ?? [];
  const count = (s: ReviewState) => rows.filter((x) => x.state === s).length;
  const ready = rows.filter((x) => (x.state === "Ready" || x.state === "Changed") && x.reported >= x.expected && x.computed !== null).length;
  const filtering = state !== "all" || !!q;
  const shown = useMemo(() => rows.filter((x) => (state === "all" || x.state === state) && (!q || x.name.includes(q) || x.code.startsWith(q))), [rows, state, q]);
  const resetFilters = () => { setState("all"); setQ(""); };
  // القائمة القديمة (لمن لا يرى الدورات): تكتب cycle وسنته في الرابط كما يكتبهما الشريط.
  const pickCycle = (id: string) => search.update({ cycle: id, year: String(seenCycles.find((c) => c.id === id)?.year ?? "") || undefined });
  // الانتقال إلى ربع آخر من حالة فارغة: بالشريط إن وجد، وإلا بالقائمة.
  const goToCycle = (id: string, year: number) => (navMode ? period.onChange({ year, cycleId: id }) : pickCycle(id));

  // كل ركائز الخطة (ولو بلا مؤشرات في الربع) ثم «بلا ركيزة» آخرًا؛ والمطابق للمرشحات في كل ركيزة.
  const groups = useMemo<PillarGroup[]>(() => {
    const byIndex = new Map<number, ReviewRow[]>();
    rows.forEach((x) => byIndex.set(x.pillarIndex, [...(byIndex.get(x.pillarIndex) ?? []), x]));
    pillarMap.pillars.forEach((p) => { if (!byIndex.has(p.index)) byIndex.set(p.index, []); });
    const visible = new Set(shown.map((x) => x.indicatorId));
    return [...byIndex.entries()]
      .sort((a, b) => (a[0] < 0 ? 1 : b[0] < 0 ? -1 : a[0] - b[0]))
      .map(([index, list]) => ({
        key: pillarKey(index), index, info: pillarMap.pillars.find((p) => p.index === index), all: list, shown: list.filter((x) => visible.has(x.indicatorId)),
      }));
  }, [rows, shown, pillarMap.pillars]);
  const keys = useMemo(() => (r ? groups.map((g) => g.key) : undefined), [r, groups]);
  const { openKey, toggle, setOpenKey } = useSingleOpen({ param: "pillar", keys });

  // رابط إلى مؤشر (?indicator=) بلا ركيزة في الرابط يفتح ركيزته، وإلا بقي مطويًا داخل لوحة مغلقة.
  const deepOpened = useRef(false);
  useEffect(() => {
    if (deepOpened.current || !deepRow) return;
    deepOpened.current = true;
    if (!params.get("pillar")) setOpenKey(pillarKey(deepRow.pillarIndex));
  }, [deepRow, params, setOpenKey]);

  const panels = filtering ? groups.filter((g) => g.shown.length > 0) : groups;
  const transferred = count("Transferred");
  const otherCycle = r?.cycles.find((c) => c.id !== r.cycle?.id);
  const cyclesLink = canCycles ? <Link to="/cycles">{L("دورات جمع البيانات", "Collection cycles")}</Link> : undefined;
  const purpose = L("راجع تقدم الكليات في كل مؤشر وحساب الجامعة منها، ثم انقل القيمة إلى مؤشر الجامعة؛ المنقول وحده يدخل بطاقة الأداء والفئة وملف المجلس.",
    "Check each college's progress and the computed university value, then transfer it. Only transferred values enter the scorecard, the tier and the board file.");
  // ما دام الشريط ينتظر الدورات والتقويم، المراجعة «تحمل» (الاستعلام لم يبدأ بعد).
  // جسم اللوحات يعرض هيكلًا في التحميل الأول، وأثناء انتظار الربع الجديد (بيانات السابق مؤقتة).
  const reviewState = { ...review, isLoading: review.isLoading || waiting || review.isPlaceholderData };
  const firstLoad = !r && reviewState.isLoading;
  const failed = !r && review.isError && !reviewState.isLoading;

  return (
    <>
      <PageHeader title={L("مراجعة مؤشرات الكليات ونقلها إلى الجامعة", "Review college indicators")}
        subtitle={r?.cycle
          ? <><b className="rv-head-period">{periodText(r.cycle)}</b>{" · "}{purpose}</>
          : r && !r.cycle ? L("لا توجد دورة ربعية مفتوحة أو مغلقة بعد.", "No quarterly cycle yet.")
          : purpose}
        stats={r?.cycle ? [
          { label: L("مؤشرات منقولة", "Transferred"), value: <span className="ltr">{transferred}/{rows.length}</span>, tone: rows.length > 0 && transferred === rows.length ? "ok" : undefined },
          { label: L("جاهز للنقل", "Ready"), value: ready, tone: ready > 0 ? "warn" : undefined },
          { label: L("تغير بعد النقل", "Changed"), value: count("Changed"), tone: count("Changed") > 0 ? "bad" : undefined },
          { label: L("لم تكتمل الكليات", "Incomplete"), value: count("Incomplete") },
        ] : undefined}
        extra={r?.cycle && r.canTransfer && (
          <Button type="primary" icon={<SendOutlined />} disabled={ready === 0} loading={all.isPending}
            onClick={() => modal.confirm({
              title: L(`نقل ${ready} قيمة إلى مؤشرات الجامعة`, `Transfer ${ready} values`),
              content: L("تنقل القيم المحسوبة كما هي للمؤشرات التي اكتملت كلياتها. ما يحتاج تعديلًا أو لم تكتمل كلياته ينقل من صفه بسبب.", "Computed values for complete indicators."),
              okText: L("نقل", "Transfer"), cancelText: L("إلغاء", "Cancel"), onOk: () => all.mutateAsync(),
            })}>
            {L(`نقل كل الجاهز (${ready})`, `Transfer all ready (${ready})`)}
          </Button>
        )} />

      <div className="rv-body">
        <ReviewPeriod cycles={navMode ? navCycles : undefined} period={period}
          fallback={{ cycles: seenCycles, value: r?.cycle?.id ?? params.get("cycle") ?? undefined, onChange: pickCycle }} />

        {noCycleInYear ? (
          <EmptyNote action={(fallbackDefault || cyclesLink) && (
            <span className="rv-empty-actions">
              {fallbackDefault && <Button onClick={() => goToCycle(fallbackDefault.id, fallbackDefault.targetYear)}>{L("عرض أحدث ربع", "Show the latest quarter")}</Button>}
              {cyclesLink}
            </span>
          )}>
            {L(`لا دورة ربعية للمراجعة في ${period.value.year ?? ""}: تظهر المراجعة بعد فتح دورة ربعية في هذه السنة.`, `No quarterly cycle to review in ${period.value.year ?? ""}.`)}
          </EmptyNote>
        ) : (
          failed ? <QueryError error={review.error} onRetry={() => void review.refetch()} />
          : r && !r.cycle ? (
              <EmptyNote action={cyclesLink}>
                {L("تظهر المراجعة بعد فتح أول دورة ربعية رسمية واعتماد نماذج الكليات.", "Open a quarterly cycle first.")}
              </EmptyNote>
            ) : r && rows.length === 0 ? (
              <EmptyNote action={(otherCycle || cyclesLink) && (
                <span className="rv-empty-actions">
                  {otherCycle && <Button onClick={() => goToCycle(otherCycle.id, otherCycle.year)}>{L(`عرض ${otherCycle.label}`, `Show ${otherCycle.year} · Q${otherCycle.quarter}`)}</Button>}
                  {cyclesLink}
                </span>
              )}>
                {L(`لا مؤشرات للمراجعة في ${r!.cycle!.label} بعد: تظهر حين تعتمد الكليات نماذج هذا الربع.`, "No indicators to review for this quarter yet.")}
              </EmptyNote>
            ) : (
              <>
                {r && (
                <div className="lcp-filters rv-toolbar">
                  <Input className="rv-search" allowClear prefix={<SearchOutlined aria-hidden />} value={q} onChange={(e) => setQ(e.target.value)}
                    placeholder={L("كود المؤشر أو اسمه", "Code or name")} aria-label={L("بحث بكود المؤشر أو اسمه", "Search by code or name")} />
                  <FilterMeta shown={shown.length} total={rows.length} noun={L("مؤشر", "indicators")} active={filtering} onReset={resetFilters} />
                  <div className="rv-chips" role="group" aria-label={L("تصفية بالحالة", "Filter by state")}>
                    <button type="button" className="rv-chip" aria-pressed={state === "all"} onClick={() => setState("all")}>
                      {L("الكل", "All")} <b className="ltr">{rows.length}</b>
                    </button>
                    {STATES.map((s) => (
                      <button key={s.key} type="button" className="rv-chip" data-st={s.key} aria-pressed={state === s.key} onClick={() => setState(s.key)}>
                        <i aria-hidden />{L(s.ar, s.en)} <b className="ltr">{count(s.key)}</b>
                      </button>
                    ))}
                  </div>
                </div>
                )}

                {r && panels.length === 0 ? (
                  <EmptyNote action={<Button onClick={resetFilters}>{L("إعادة ضبط المرشحات", "Reset filters")}</Button>}>
                    {L("لا مؤشرات تطابق البحث أو الحالة المختارة.", "No indicators match the search or the selected state.")}
                  </EmptyNote>
                ) : (
                  <div className="rv-acc">
                    {panels.map((g) => (
                      <AccordionPanel key={g.key} id={`rv-p-${g.key}`} className="rv-pillar" style={tint(g.index)}
                        open={openKey === g.key} onToggle={() => toggle(g.key)} header={<PillarHeader group={g} filtering={filtering} loading={firstLoad} />}
                        query={reviewState} skeleton={<PanelSkeleton rows={Math.min(Math.max(g.all.length, 3), 6)} variant="rows" />}>
                        {g.all.length === 0 ? (
                          <div className="rv-pillar-empty"><EmptyNote>{L("لا مؤشرات من هذه الركيزة تجمع في هذا الربع.", "No indicators of this pillar are collected this quarter.")}</EmptyNote></div>
                        ) : (
                          <div className="rv-list">
                            {g.shown.map((row) => (
                              <IndicatorRow key={row.indicatorId} row={row} cycleId={r!.cycle!.id} canTransfer={r!.canTransfer}
                                open={open === row.indicatorId} onToggle={() => setOpen(open === row.indicatorId ? null : row.indicatorId)} />
                            ))}
                          </div>
                        )}
                      </AccordionPanel>
                    ))}
                  </div>
                )}
              </>
            )
        )}
      </div>
    </>
  );
}

/** رأس الركيزة (داخل زر اللوحة، فعناصر سطرية فقط): الرقم 01…06 والاسم، وعدد المؤشرات وحالاتها، والمنقول منها بشريط. */
function PillarHeader({ group, filtering, loading }: { group: PillarGroup; filtering: boolean; loading?: boolean }) {
  const { index, info, all, shown } = group;
  const total = all.length;
  const moved = all.filter((x) => x.state === "Transferred").length;
  const pct = total > 0 ? Math.round((moved / total) * 100) : 0;
  const name = info?.name ?? (index >= 0 ? L(`الركيزة ${pillarNum(index)}`, `Pillar ${pillarNum(index)}`) : L("معايير بلا ركيزة", "Standards without a pillar"));
  return (
    <>
      <span className="rv-p-num ltr" aria-hidden>{index >= 0 ? pillarNum(index) : "—"}</span>
      <span className="rv-p-main">
        <b className="rv-p-name">{name}</b>
        <span className="rv-p-sum">
          {loading ? null : filtering
            ? <span>{L("المطابقة", "Matching")} <span className="ltr">{shown.length}/{total}</span></span>
            : <span>{indicatorCount(total)}</span>}
          {!filtering && !loading && SUMMARY_ORDER.map((s) => {
            const n = all.filter((x) => x.state === s).length;
            return n === 0 ? null : <span key={s} className="rv-p-st" data-st={s}><i aria-hidden />{L(stateOf(s).ar, stateOf(s).en)} <span className="ltr">{n}</span></span>;
          })}
        </span>
      </span>
      {total > 0 && !loading && (
        <span className="rv-p-state">
          <span className="rv-p-state-label">{L("منقول", "Transferred")} <b className="ltr">{moved}/{total}</b></span>
          <span className="rv-p-bar" aria-hidden><i style={{ width: `${pct}%` }} /></span>
        </span>
      )}
    </>
  );
}

function IndicatorRow({ row, cycleId, canTransfer, open, onToggle }: { row: ReviewRow; cycleId: string; canTransfer: boolean; open: boolean; onToggle: () => void }) {
  const st = stateOf(row.state);
  // النسب والمتوسطات: ارتفاع العمود نسبة الكلية من مستهدف الجامعة ولونه حكمها. المجموع والعدد: حصتها من رقم الجامعة بلون محايد.
  const measure = (u: ReviewRow["units"][number]) => (row.comparable ? u.universityAchievement : u.share) ?? 0;
  const max = Math.max(1, ...row.units.map(measure));
  return (
    <article className={`rv-row${open ? " open" : ""}`}>
      <button type="button" className="rv-head" onClick={onToggle} aria-expanded={open} aria-controls={`rv-d-${row.indicatorId}`}>
        <span className="rv-name">
          <code className="pill-code ltr">{row.code}</code>
          <b>{row.name}</b>
          <small className="muted">{stdLabel(row.standardCode)} · {L(levelText[row.level], row.level)} · {aggregation[row.aggregation]}</small>
        </span>
        {/* تقدم الكليات: نسبة كل كلية من مستهدف الجامعة. */}
        <span className="rv-colleges" aria-label={L("تقدم الكليات", "College progress")}>
          {row.units.length === 0 ? <small className="muted">—</small> : row.units.slice(0, 8).map((u) => (
            <Tooltip key={u.unitId} title={`${u.name}: ${fmt(u.value, row.unit)}${row.comparable
              ? u.universityAchievement !== null ? ` (${num(u.universityAchievement)}% ${L("من مستهدف الجامعة", "of target")})` : ""
              : u.share !== null ? ` (${num(u.share)}% ${L("من رقم الجامعة", "of the university total")})` : ""}`}>
              <span className="rv-col">
                <i style={{ height: `${u.value === null ? 4 : Math.max(6, (measure(u) / max) * 36)}px` }}
                  className={u.value === null ? "none" : !row.comparable ? "share" : measure(u) >= 100 ? "ok" : measure(u) >= 90 ? "near" : "low"} />
              </span>
            </Tooltip>
          ))}
        </span>
        <span className="rv-uni">
          <small>{L("المحسوبة", "Computed")} <span className="ltr">({row.reported}/{row.expected})</span></small>
          <b className="ltr">{fmt(row.computed, row.unit)}</b>
        </span>
        <span className="rv-uni">
          <small>{L("المنقولة للجامعة", "Transferred")}</small>
          <b className="ltr">{fmt(row.official?.value, row.unit)}</b>
        </span>
        <span className="rv-state"><Tag color={st.color}>{L(st.ar, st.en)}</Tag><DownOutlined className="rv-caret" aria-hidden /></span>
      </button>
      <Reveal open={open} id={`rv-d-${row.indicatorId}`}><Detail row={row} cycleId={cycleId} canTransfer={canTransfer} /></Reveal>
    </article>
  );
}

function Detail({ row, cycleId, canTransfer }: { row: ReviewRow; cycleId: string; canTransfer: boolean }) {
  const qc = useQueryClient();
  const { message } = App.useApp();
  const [value, setValue] = useState<number | null>(row.computed);
  const [note, setNote] = useState("");
  const adjusted = value !== row.computed;
  const partial = row.reported < row.expected;
  const needNote = adjusted || partial;
  const calc = howCalculated({ aggregation: row.aggregation, level: row.level, direction: row.direction, unit: row.unit, numerator: row.numeratorLabel, denominator: row.denominatorLabel });
  const ratio = row.aggregation === "RatioOfSums";
  const transfer = useMutation({
    mutationFn: () => api.post<ReviewRow>("/api/review/transfer", { cycleId, indicatorId: row.indicatorId, value, note: note.trim() || null, version: row.official?.version ?? null }),
    onSuccess: async () => {
      message.success(L(`نقلت قيمة ${row.code} إلى مؤشر الجامعة.`, "Transferred."));
      await invalidate.afterTransfer(qc);
    },
  });
  // عناوين الأعمدة؛ على الجوال تصير الصفوف بطاقات وكل خلية تحمل عنوانها (data-label).
  const col = {
    form: L("النموذج", "Form"),
    num: row.numeratorLabel ?? L("البسط", "Numerator"),
    den: row.denominatorLabel ?? L("المقام", "Denominator"),
    value: L("القيمة", "Value"),
    target: L("مستهدف الكلية", "College target"),
    a: row.comparable ? L("من مستهدف الجامعة", "vs university target") : L("إنجازها من مستهدفها", "vs own target"),
    b: row.comparable ? L("البعد عنه", "Gap") : L("حصتها من رقم الجامعة", "Share of university"),
  };

  return (
    <div className="rv-detail">
      <div className="rv-table" role="table" aria-label={L("قيمة كل كلية", "Value per college")}>
        <div className="rv-tr head" role="row">
          <span role="columnheader">{row.level === "University" ? L("الجهة المدخلة", "Entering unit") : L("الكلية", "College")}</span>
          <span role="columnheader">{col.form}</span>
          {ratio && <span role="columnheader">{col.num}</span>}
          {ratio && <span role="columnheader">{col.den}</span>}
          <span role="columnheader">{col.value}</span>
          <span role="columnheader">{col.target}</span>
          <span role="columnheader">{col.a}</span>
          <span role="columnheader">{col.b}</span>
        </div>
        {row.units.map((u) => (
          <div key={u.unitId} className="rv-tr" role="row">
            <span role="cell" className="rv-td-name"><b>{u.name}</b>{u.expected > 1 && <small className="muted"> ({L(`${u.reported} من ${u.expected} قسمًا`, `${u.reported}/${u.expected}`)})</small>}</span>
            <span role="cell" data-label={col.form}>{u.formStatus ? <Tag color={submissionStatus[u.formStatus].color}>{submissionStatus[u.formStatus].text}</Tag> : <span className="muted">—</span>}</span>
            {ratio && <span role="cell" className="ltr" data-label={col.num}>{u.numerator === null ? "—" : num(u.numerator)}</span>}
            {ratio && <span role="cell" className="ltr" data-label={col.den}>{u.denominator === null ? "—" : num(u.denominator)}</span>}
            <span role="cell" className="ltr" data-label={col.value}><b>{fmt(u.value, row.unit)}</b></span>
            <span role="cell" className="ltr" data-label={col.target}>{u.targetSource === "Unit" || u.targetSource === "EachCollege" ? fmt(u.target, row.unit) : <span className="muted">—</span>}</span>
            {row.comparable ? (
              <>
                <span role="cell" className="ltr" data-label={col.a}>{u.universityAchievement === null ? "—" : `${num(u.universityAchievement)}%`}</span>
                <span role="cell" className="ltr" data-label={col.b}>{u.gapToUniversity === null ? "—" : <span className={(row.direction === "LowerIsBetter" ? u.gapToUniversity <= 0 : u.gapToUniversity >= 0) ? "rv-up" : "rv-down"}>{u.gapToUniversity > 0 ? "+" : ""}{num(u.gapToUniversity)}</span>}</span>
              </>
            ) : (
              <>
                <span role="cell" className="ltr" data-label={col.a}>{u.achievement === null ? "—" : `${num(u.achievement)}%`}</span>
                <span role="cell" className="ltr" data-label={col.b}>{u.share === null ? "—" : `${num(u.share)}%`}</span>
              </>
            )}
          </div>
        ))}
        <div className="rv-tr total" role="row">
          <span role="cell" className="rv-td-name"><b>{L("الجامعة (محسوبة)", "University (computed)")}</b></span>
          <span role="cell" data-label={L("الجهات المعتمدة", "Units reported")}><small className="ltr">{row.reported}/{row.expected}</small></span>
          {ratio && <span role="cell" className="ltr" data-label={col.num}><b>{row.computedNumerator === null ? "—" : num(row.computedNumerator)}</b></span>}
          {ratio && <span role="cell" className="ltr" data-label={col.den}><b>{row.computedDenominator === null ? "—" : num(row.computedDenominator)}</b></span>}
          <span role="cell" className="ltr" data-label={col.value}><b>{fmt(row.computed, row.unit)}</b></span>
          <span role="cell" className="rv-td-target">{L("مستهدف الجامعة", "Univ. target")} <b className="ltr">{fmt(row.universityTarget, row.unit)}</b></span>
          <span role="cell" className="ltr" data-label={col.a}>{row.computedAchievement === null ? "—" : `${num(row.computedAchievement)}%`}</span>
          <span role="cell" className="ltr" data-label={col.b}>{!row.comparable ? (row.computed !== null ? "100%" : "—") : row.computed !== null && row.universityTarget !== null ? `${row.computed - row.universityTarget > 0 ? "+" : ""}${num(row.computed - row.universityTarget)}` : "—"}</span>
        </div>
      </div>

      <div className="rv-calc">
        <p><b>{L("حساب الجامعة:", "University value:")}</b> {calc.university}
          {ratio && row.computedNumerator !== null && row.computedDenominator ? (
            <span className="rv-eq ltr"> {num(row.computedNumerator)} ÷ {num(row.computedDenominator)}{row.unit.trim() === "%" ? " × 100" : ""} = {fmt(row.computed, row.unit)}</span>
          ) : null}
        </p>
        {row.formula && <p className="muted"><b>{L("معادلة التقييم في الإطار:", "Framework formula:")}</b> {row.formula}</p>}
        {row.note && <p className="muted">{row.note}</p>}
      </div>

      {row.official && (
        <Alert type={row.state === "Changed" ? "warning" : "success"} showIcon icon={row.state === "Changed" ? <SwapOutlined /> : <CheckCircleOutlined />} className="mb"
          message={row.state === "Changed"
            ? L(`نقل ${fmt(row.official.value, row.unit)} ثم تغيرت قيم الكليات فصارت المحسوبة ${fmt(row.computed, row.unit)}. انقل من جديد إن كانت الجديدة هي الصحيحة.`, "Changed since transfer.")
            : L(`منقولة: ${fmt(row.official.value, row.unit)}`, "Transferred")}
          description={<>{row.official.transferredByName} · <span className="ltr">{formatDateTime(row.official.transferredAt)}</span>{row.official.note && <> · {row.official.note}</>}</>} />
      )}
      {(row.history?.length ?? 0) > 1 && (
        <details className="mb">
          <summary>{L(`تاريخ القيمة الرسمية (${row.history!.length})`, `Official value history (${row.history!.length})`)}</summary>
          <ul className="t1-history">
            {row.history!.map((h, n) => (
              <li key={`${h.changedAt}-${n}`}>
                <span className="ltr">{h.oldValue === null ? "—" : fmt(h.oldValue, row.unit)} → <b>{fmt(h.newValue, row.unit)}</b></span>
                {h.computedValue !== null && h.computedValue !== h.newValue && <> <small className="muted">({L("المحسوبة", "computed")} <span className="ltr">{fmt(h.computedValue, row.unit)}</span>)</small></>}
                {h.reason && <> — {h.reason}</>}
                <div className="muted"><small>{h.changedByName ?? ""} · <span className="ltr">{formatDateTime(h.changedAt)}</span> · {L(`${h.reported} من ${h.expected} جهة`, `${h.reported}/${h.expected} units`)}</small></div>
              </li>
            ))}
          </ul>
        </details>
      )}

      {canTransfer && (
        <div className="rv-transfer">
          <label>
            <span>{L("القيمة التي تنقل إلى مؤشر الجامعة", "Value to transfer")}</span>
            <InputNumber value={value} onChange={(v) => setValue(v)} className="ltr" style={{ width: 180 }} suffix={row.unit.trim() === "%" ? "%" : undefined} />
          </label>
          {needNote && (
            <label className="grow">
              <span>{partial ? L(`اعتمدت ${row.reported} من ${row.expected} جهة فقط؛ سبب النقل الآن`, "Reason (partial)") : L("سبب تعديل القيمة عن المحسوبة", "Reason for the adjustment")}</span>
              <Input value={note} onChange={(e) => setNote(e.target.value)} maxLength={1000} />
            </label>
          )}
          <Button type="primary" icon={<SendOutlined />} loading={transfer.isPending} disabled={value === null || (needNote && note.trim().length < 5)} onClick={() => transfer.mutate()}>
            {row.official ? L("نقل من جديد", "Transfer again") : L("نقل إلى مؤشر الجامعة", "Transfer to the university")}
          </Button>
          {adjusted && row.computed !== null && <Button type="link" onClick={() => setValue(row.computed)}>{L("استعادة المحسوبة", "Use computed")}</Button>}
        </div>
      )}
      {transfer.error && <Alert type="error" showIcon className="mt" message={errorText(transfer.error)} />}
    </div>
  );
}
