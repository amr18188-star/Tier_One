import { useMemo, useState } from "react";
import { Alert, App, Button, Input, InputNumber, Select, Tag, Tooltip } from "antd";
import { CheckCircleOutlined, DownOutlined, SendOutlined, SwapOutlined } from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../../api/client";
import type { Review, ReviewRow, ReviewState } from "../../api/scorecard";
import { howCalculated } from "../../shared/calc";
import { errorText, formatDateTime } from "../../shared/format";
import { L } from "../../shared/i18n";
import { aggregation, num, submissionStatus } from "../../shared/labels";
import { PillarBand, stdLabel, tint, usePillarMap } from "../../shared/pillars";
import { QueryState } from "../../shared/QueryState";
import { EmptyNote, FilterMeta, PageHeader } from "../../shared/ui";
import "./review.css";

/**
 * مراجعة مسئول ملف Tier 1: لكل مؤشر في الربع قائمة بتقدم الكليات (قيمة كل كلية ونسبتها من مستهدف الجامعة)، وحساب الجامعة منها
 * بقاعدة التجميع (مجموع البسوط ÷ مجموع المقامات للنسب)، ثم زر ينقل القيمة إلى مؤشر الجامعة في ذلك الربع.
 * القيمة المنقولة وحدها تدخل بطاقة الأداء والفئة وملف المجلس.
 */
const STATES: { key: ReviewState; ar: string; color: string }[] = [
  { key: "Ready", ar: "جاهز للنقل", color: "processing" },
  { key: "Changed", ar: "تغير بعد النقل", color: "warning" },
  { key: "Incomplete", ar: "لم تكتمل الكليات", color: "gold" },
  { key: "Transferred", ar: "منقول", color: "success" },
  { key: "NoData", ar: "لا قيم معتمدة", color: "default" },
];
const stateOf = (s: ReviewState) => STATES.find((x) => x.key === s)!;
const fmt = (v: number | null | undefined, unit: string) => (v === null || v === undefined ? "—" : unit.trim() === "%" ? `${num(v)}%` : num(v));
const levelText = { Department: "من الأقسام", College: "من الكليات", University: "مركزي على مستوى الجامعة" } as const;

export default function ReviewPage() {
  const [params, setParams] = useSearchParams();
  const cycleId = params.get("cycle") ?? undefined;
  const review = useQuery({ queryKey: ["review", cycleId], queryFn: () => api.get<Review>(`/api/review${cycleId ? `?cycleId=${cycleId}` : ""}`) });
  const qc = useQueryClient();
  const { message, modal } = App.useApp();
  const [state, setState] = useState<ReviewState | "all">("all");
  const [q, setQ] = useState("");
  const [picked, setOpen] = useState<string | null | undefined>(undefined);
  const pillarMap = usePillarMap();
  const r = review.data;
  // ‎?indicator=1.01 يفتح تفصيل المؤشر مباشرة (رابط من بطاقة الأداء أو صفحة المؤشر).
  const open = picked !== undefined ? picked : r?.rows.find((x) => x.code === params.get("indicator"))?.indicatorId ?? null;

  const all = useMutation({
    mutationFn: () => api.post<{ transferred: number }>("/api/review/transfer-all", { cycleId: r!.cycle!.id }),
    onSuccess: async (res) => {
      message.success(L(`نقلت ${res.transferred} قيمة إلى مؤشرات الجامعة.`, `${res.transferred} values transferred.`));
      await qc.invalidateQueries({ queryKey: ["review"] });
      await qc.invalidateQueries({ queryKey: ["scorecard"] });
    },
  });

  const rows = r?.rows ?? [];
  const count = (s: ReviewState) => rows.filter((x) => x.state === s).length;
  const ready = rows.filter((x) => (x.state === "Ready" || x.state === "Changed") && x.reported >= x.expected && x.computed !== null).length;
  const shown = rows.filter((x) => (state === "all" || x.state === state) && (!q || x.name.includes(q) || x.code.startsWith(q)));
  const groups = useMemo(() => {
    const g = new Map<number, ReviewRow[]>();
    shown.forEach((x) => g.set(x.pillarIndex, [...(g.get(x.pillarIndex) ?? []), x]));
    return [...g.entries()].sort((a, b) => a[0] - b[0]);
  }, [shown]);

  return (
    <>
      <PageHeader title={L("مراجعة مؤشرات الكليات ونقلها إلى الجامعة", "Review college indicators")}
        subtitle={r?.cycle
          ? L(`${r.cycle.label}: تقدم كل كلية في كل مؤشر وحساب الجامعة منها. ما تنقله هنا وحده يدخل بطاقة الأداء والفئة وملف المجلس.`, r.cycle.label)
          : L("لا توجد دورة ربعية مفتوحة أو مغلقة بعد.", "No quarterly cycle yet.")}
        stats={r?.cycle ? [
          { label: L("جاهز للنقل", "Ready"), value: ready, tone: ready > 0 ? "warn" : undefined },
          { label: L("منقول", "Transferred"), value: <span className="ltr">{count("Transferred")}/{rows.length}</span>, tone: "ok" },
          { label: L("تغير بعد النقل", "Changed"), value: count("Changed"), tone: count("Changed") > 0 ? "bad" : undefined },
          { label: L("لم تكتمل الكليات", "Incomplete"), value: count("Incomplete") },
        ] : undefined}
        extra={r?.cycle && (
          <>
            <Select value={r.cycle.id} style={{ minWidth: 240 }} onChange={(v) => setParams({ cycle: v }, { replace: true })} aria-label={L("الربع", "Quarter")}
              options={r.cycles.map((c) => ({ value: c.id, label: c.label }))} />
            {r.canTransfer && (
              <Button type="primary" icon={<SendOutlined />} disabled={ready === 0} loading={all.isPending}
                onClick={() => modal.confirm({
                  title: L(`نقل ${ready} قيمة إلى مؤشرات الجامعة`, `Transfer ${ready} values`),
                  content: L("تنقل القيم المحسوبة كما هي للمؤشرات التي اكتملت كلياتها. ما يحتاج تعديلًا أو لم تكتمل كلياته ينقل من صفه بسبب.", "Computed values for complete indicators."),
                  okText: L("نقل", "Transfer"), cancelText: L("إلغاء", "Cancel"), onOk: () => all.mutateAsync(),
                })}>
                {L(`نقل كل الجاهز (${ready})`, `Transfer all ready (${ready})`)}
              </Button>
            )}
          </>
        )} />

      <QueryState query={review}>
        {() => !r!.cycle ? (
          <EmptyNote action={<Link to="/cycles">{L("دورات جمع البيانات", "Collection cycles")}</Link>}>
            {L("تظهر المراجعة بعد فتح أول دورة ربعية رسمية واعتماد نماذج الكليات.", "Open a quarterly cycle first.")}
          </EmptyNote>
        ) : (
          <>
            <div className="rv-filter">
              <Input.Search allowClear placeholder={L("كود المؤشر أو اسمه", "Code or name")} onChange={(e) => setQ(e.target.value)} style={{ maxWidth: 280 }} />
              <button type="button" className={state === "all" ? "on" : ""} onClick={() => setState("all")}>{L("الكل", "All")} <b>{rows.length}</b></button>
              {STATES.map((s) => (
                <button key={s.key} type="button" className={state === s.key ? "on" : ""} onClick={() => setState(s.key)}>{s.ar} <b>{count(s.key)}</b></button>
              ))}
              <FilterMeta shown={shown.length} total={rows.length} noun={L("مؤشر", "indicators")} active={state !== "all" || !!q} onReset={() => { setState("all"); setQ(""); }} />
            </div>
            {groups.length === 0 && <EmptyNote>{L("لا مؤشرات بهذه المرشحات.", "Nothing matches.")}</EmptyNote>}
            {groups.map(([index, list]) => (
              <section key={index} className="rv-group" style={tint(index)}>
                <PillarBand info={pillarMap.pillars.find((p) => p.index === index) ?? (index >= 0 ? { index, id: String(index), code: list[0].pillarCode ?? "", name: list[0].pillarCode ?? "" } : undefined)}>
                  <span className="ltr">{list.filter((x) => x.state === "Transferred").length}/{list.length}</span> {L("منقول", "transferred")}
                </PillarBand>
                {list.map((row) => (
                  <IndicatorRow key={row.indicatorId} row={row} cycleId={r!.cycle!.id} canTransfer={r!.canTransfer}
                    open={open === row.indicatorId} onToggle={() => setOpen(open === row.indicatorId ? null : row.indicatorId)} />
                ))}
              </section>
            ))}
          </>
        )}
      </QueryState>
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
      <button type="button" className="rv-head" onClick={onToggle} aria-expanded={open}>
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
        <span className="rv-state"><Tag color={st.color}>{st.ar}</Tag><DownOutlined className="rv-caret" aria-hidden /></span>
      </button>
      {open && <Detail row={row} cycleId={cycleId} canTransfer={canTransfer} />}
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
      await qc.invalidateQueries({ queryKey: ["review"] });
      await qc.invalidateQueries({ queryKey: ["scorecard"] });
    },
  });

  return (
    <div className="rv-detail">
      <div className="rv-table" role="table" aria-label={L("قيمة كل كلية", "Value per college")}>
        <div className="rv-tr head" role="row">
          <span role="columnheader">{row.level === "University" ? L("الجهة المدخلة", "Entering unit") : L("الكلية", "College")}</span>
          <span role="columnheader">{L("النموذج", "Form")}</span>
          {ratio && <span role="columnheader">{row.numeratorLabel ?? L("البسط", "Numerator")}</span>}
          {ratio && <span role="columnheader">{row.denominatorLabel ?? L("المقام", "Denominator")}</span>}
          <span role="columnheader">{L("القيمة", "Value")}</span>
          <span role="columnheader">{L("مستهدف الكلية", "College target")}</span>
          {row.comparable ? (
            <>
              <span role="columnheader">{L("من مستهدف الجامعة", "vs university target")}</span>
              <span role="columnheader">{L("البعد عنه", "Gap")}</span>
            </>
          ) : (
            <>
              <span role="columnheader">{L("إنجازها من مستهدفها", "vs own target")}</span>
              <span role="columnheader">{L("حصتها من رقم الجامعة", "Share of university")}</span>
            </>
          )}
        </div>
        {row.units.map((u) => (
          <div key={u.unitId} className="rv-tr" role="row">
            <span role="cell"><b>{u.name}</b>{u.expected > 1 && <small className="muted"> ({L(`${u.reported} من ${u.expected} قسمًا`, `${u.reported}/${u.expected}`)})</small>}</span>
            <span role="cell">{u.formStatus ? <Tag color={submissionStatus[u.formStatus].color}>{submissionStatus[u.formStatus].text}</Tag> : <span className="muted">—</span>}</span>
            {ratio && <span role="cell" className="ltr">{u.numerator === null ? "—" : num(u.numerator)}</span>}
            {ratio && <span role="cell" className="ltr">{u.denominator === null ? "—" : num(u.denominator)}</span>}
            <span role="cell" className="ltr"><b>{fmt(u.value, row.unit)}</b></span>
            <span role="cell" className="ltr">{u.targetSource === "Unit" || u.targetSource === "EachCollege" ? fmt(u.target, row.unit) : <span className="muted">—</span>}</span>
            {row.comparable ? (
              <>
                <span role="cell" className="ltr">{u.universityAchievement === null ? "—" : `${num(u.universityAchievement)}%`}</span>
                <span role="cell" className="ltr">{u.gapToUniversity === null ? "—" : <span className={(row.direction === "LowerIsBetter" ? u.gapToUniversity <= 0 : u.gapToUniversity >= 0) ? "rv-up" : "rv-down"}>{u.gapToUniversity > 0 ? "+" : ""}{num(u.gapToUniversity)}</span>}</span>
              </>
            ) : (
              <>
                <span role="cell" className="ltr">{u.achievement === null ? "—" : `${num(u.achievement)}%`}</span>
                <span role="cell" className="ltr">{u.share === null ? "—" : `${num(u.share)}%`}</span>
              </>
            )}
          </div>
        ))}
        <div className="rv-tr total" role="row">
          <span role="cell"><b>{L("الجامعة (محسوبة)", "University (computed)")}</b></span>
          <span role="cell"><small className="ltr">{row.reported}/{row.expected}</small></span>
          {ratio && <span role="cell" className="ltr"><b>{row.computedNumerator === null ? "—" : num(row.computedNumerator)}</b></span>}
          {ratio && <span role="cell" className="ltr"><b>{row.computedDenominator === null ? "—" : num(row.computedDenominator)}</b></span>}
          <span role="cell" className="ltr"><b>{fmt(row.computed, row.unit)}</b></span>
          <span role="cell">{L("مستهدف الجامعة", "Univ. target")} <b className="ltr">{fmt(row.universityTarget, row.unit)}</b></span>
          <span role="cell" className="ltr">{row.computedAchievement === null ? "—" : `${num(row.computedAchievement)}%`}</span>
          <span role="cell" className="ltr">{!row.comparable ? (row.computed !== null ? "100%" : "—") : row.computed !== null && row.universityTarget !== null ? `${row.computed - row.universityTarget > 0 ? "+" : ""}${num(row.computed - row.universityTarget)}` : "—"}</span>
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

      {canTransfer && (
        <div className="rv-transfer">
          <label>
            <span>{L("القيمة التي تنقل إلى مؤشر الجامعة", "Value to transfer")}</span>
            <InputNumber value={value} onChange={(v) => setValue(v)} className="ltr" style={{ width: 180 }} addonAfter={row.unit.trim() === "%" ? "%" : undefined} />
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
