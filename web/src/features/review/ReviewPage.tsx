import { useEffect, useMemo, useRef, useState } from "react";
import { App, Button, Input } from "antd";
import { SearchOutlined, SendOutlined } from "@ant-design/icons";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { api } from "../../api/client";
import { invalidate } from "../../api/invalidate";
import type { Review, ReviewCycle, ReviewRow, ReviewState } from "../../api/scorecard";
import { AccordionPanel, useSingleOpen } from "../../shared/AccordionPanel";
import { L } from "../../shared/i18n";
import { tint, usePillarMap } from "../../shared/pillars";
import { QueryError } from "../../shared/QueryState";
import { usePermissions } from "../../shared/session";
import { PanelSkeleton } from "../../shared/PanelSkeleton";
import { EmptyNote, FilterMeta, PageHeader } from "../../shared/ui";
import { usePeriodParams, useSearchState } from "../../shared/usePeriodParams";
import { latestCycle, ReviewPeriod, reviewable } from "./ReviewPeriod";
import { useCycles } from "../../api/queries";
import { IndicatorRow, PillarHeader } from "./ReviewRows";
import { PillarGroup, STATES, dropIndicator, periodText, pillarKey } from "./reviewParts";
import "./review.css";
import "./review-redesign.css";

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
