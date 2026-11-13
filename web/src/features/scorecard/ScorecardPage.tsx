import { useState, type CSSProperties, type ReactNode } from "react";
import { Alert, Button, Drawer, Tag, Tooltip } from "antd";
import {
  AimOutlined, ApartmentOutlined, DownloadOutlined, FundOutlined, GlobalOutlined, LineChartOutlined, SettingOutlined, TrophyOutlined, WarningOutlined,
} from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../../api/client";
import type { IndicatorScore, Level, PillarScore, Rag, Scorecard, TierBand, TierResult } from "../../api/scorecard";
import { L } from "../../shared/i18n";
import { num } from "../../shared/labels";
import { tint } from "../../shared/pillars";
import { QueryState } from "../../shared/QueryState";
import { usePermissions } from "../../shared/session";
import { EmptyNote, PageHeader, Panel } from "../../shared/ui";
import "../strategy/strategy.css";
import "./scorecard.css";

/**
 * بطاقة أداء الجامعة وتصنيفها: ما كانت تحسبه أوراق ملف «الإطار الشامل» (بطاقة مؤشرات الأداء، وحاسبة التصنيف، والرسوم والتحليلات،
 * وبطاقة أداء القطاع العام، وأهداف التنمية المستدامة) في صفحة واحدة تتحدث مع كل اعتماد، بمعادلات الملف نفسها.
 */
type View = "tier" | "pillars" | "pss" | "sdg" | "trend";

const VIEWS: { key: View; icon: ReactNode; ar: string; en: string }[] = [
  { key: "tier", icon: <TrophyOutlined aria-hidden />, ar: "حاسبة التصنيف", en: "Tier calculator" },
  { key: "pillars", icon: <ApartmentOutlined aria-hidden />, ar: "الركائز والمؤشرات", en: "Pillars and indicators" },
  { key: "pss", icon: <FundOutlined aria-hidden />, ar: "بطاقة أداء القطاع العام", en: "Public Sector Scorecard" },
  { key: "sdg", icon: <GlobalOutlined aria-hidden />, ar: "أهداف التنمية المستدامة", en: "SDGs" },
  { key: "trend", icon: <LineChartOutlined aria-hidden />, ar: "الاتجاه عبر السنوات والأرباع", en: "Trends" },
];

const TIER_COLORS = ["#1f7a4c", "#4f9a3a", "#c9973f", "#d07a2a", "#b8452f"];
const RAG_TEXT: Record<Rag, { ar: string; en: string }> = {
  Strong: { ar: "قوي / على المسار", en: "Strong" },
  Developing: { ar: "قيد التطوير", en: "Developing" },
  AtRisk: { ar: "معرض للخطر", en: "At risk" },
};
const LEVEL_TEXT: Record<Level, { ar: string; color: string }> = {
  InLevel: { ar: "في المستوى", color: "success" },
  Below: { ar: "دون المستوى", color: "error" },
  NoData: { ar: "لا قيمة بعد", color: "default" },
};

const pct = (v: number | null | undefined) => (v === null || v === undefined ? "—" : `${num(v)}%`);
const fmt = (v: number | null | undefined, unit: string) => (v === null || v === undefined ? "—" : unit.trim() === "%" ? `${num(v)}%` : num(v));
const ragOf = (v: number): Rag => (v >= 85 ? "Strong" : v >= 70 ? "Developing" : "AtRisk");

export default function ScorecardPage() {
  const card = useQuery({ queryKey: ["scorecard"], queryFn: () => api.get<Scorecard>("/api/scorecard") });
  const [params, setParams] = useSearchParams();
  const view = (VIEWS.find((v) => v.key === params.get("view"))?.key ?? "tier") as View;
  const [open, setOpen] = useState<IndicatorScore | null>(null);
  const c = card.data;
  const tierName = (t: number) => c?.tiers.find((x) => x.tier === t)?.nameAr ?? "";

  return (
    <>
      <PageHeader
        title={L("بطاقة أداء الجامعة وتصنيفها", "University scorecard and tier")}
        subtitle={c && (c.lastReported
          ? L(`محسوبة بمعادلات الإطار الشامل من قيم الجامعة المنقولة حتى ${c.lastReported.label}.`, `Computed from transferred university values up to ${c.lastReported.label}.`)
          : L("لم تنقل قيم أي ربع إلى مؤشرات الجامعة بعد. تظهر الدرجات بعد أن ينقلها مسئول ملف Tier 1 من «مراجعة مؤشرات الكليات».", "No transferred quarter yet."))}
        stats={c ? [
          { label: L("الدرجة الكلية", "Overall score"), value: <span className="ltr">{num(c.overall.score)}</span> },
          { label: L("الفئة النهائية", "Final tier"), value: L(`الفئة ${c.overall.tier.finalTier}`, `Tier ${c.overall.tier.finalTier}`), tone: c.overall.tier.finalTier <= 2 ? "ok" : c.overall.tier.finalTier === 3 ? "warn" : "bad" },
          { label: L("مؤشرات لها قيمة", "Reported"), value: <span className="ltr">{c.overall.reported}/{c.overall.indicators}</span> },
          { label: L("في المستوى", "On target"), value: c.overall.inLevel, tone: "ok" },
        ] : undefined}
        extra={c?.canExport && (
          <Tooltip title={L("الملف نفسه بقيم الأرباع المعتمدة في خلاياه الزرقاء، لنقلها إلى بوابة المجلس الأعلى للجامعات.", "The same workbook filled with the approved quarterly values.")}>
            <Button type="primary" icon={<DownloadOutlined />} href="/api/scorecard/kpi.xlsx">{L("ملف المجلس KPI.xlsx", "Council workbook")}</Button>
          </Tooltip>
        )} />

      <div className="sp-tabs-row">
        <div className="sp-tabs sc-tabs" role="tablist" aria-label={L("طريقة العرض", "View")}>
          {VIEWS.map((v) => (
            <button key={v.key} type="button" role="tab" aria-selected={view === v.key} className={view === v.key ? "on" : ""}
              onClick={() => setParams(v.key === "tier" ? {} : { view: v.key }, { replace: true })}>
              {v.icon}<span><b>{L(v.ar, v.en)}</b></span>
            </button>
          ))}
        </div>
      </div>

      <QueryState query={card}>
        {() => (
          <>
            {!c!.lastReported && (
              <Alert type="info" showIcon message={L("الدرجات أصفار حتى تنقل قيم أول ربع إلى مؤشرات الجامعة", "Scores are zero until a quarter is transferred")}
                description={L("كما في ملف الإطار: المؤشر بلا قيمة يحسب صفرًا في درجة معياره وركيزته. تعتمد الكليات قيمها في نماذج الدورة الربعية، ثم يراجعها مسئول ملف Tier 1 وينقلها إلى مؤشرات الجامعة، فتظهر الدرجات والفئة هنا.",
                  "As in the workbook, an indicator without a value counts as zero.")} />
            )}
            {view === "tier" && <TierView c={c!} tierName={tierName} onPick={setOpen} />}
            {view === "pillars" && <PillarsView c={c!} onPick={setOpen} />}
            {view === "pss" && <PssView c={c!} />}
            {view === "sdg" && <SdgView c={c!} />}
            {view === "trend" && <TrendView c={c!} />}
          </>
        )}
      </QueryState>
      {open && c && <IndicatorDrawer c={c} i={open} onClose={() => setOpen(null)} />}
    </>
  );
}

// ----------------------------------------------------------------- مكونات صغيرة

function Ring({ value, size = 76, color, label }: { value: number; size?: number; color: string; label?: string }) {
  const r = size / 2 - 7, C = 2 * Math.PI * r;
  return (
    <svg viewBox={`0 0 ${size} ${size}`} width={size} height={size} role="img" aria-label={label ?? `${num(value)}%`} className="sc-ring">
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="#e8edf3" strokeWidth="8" />
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke={color} strokeWidth="8" strokeLinecap="round"
        strokeDasharray={`${((C * Math.min(100, Math.max(0, value))) / 100).toFixed(1)} ${C.toFixed(1)}`} transform={`rotate(-90 ${size / 2} ${size / 2})`} />
      <text x={size / 2} y={size / 2 + 5} textAnchor="middle">{num(value)}</text>
    </svg>
  );
}

function Bar({ value, color }: { value: number | null; color?: string }) {
  return (
    <span className="sc-bar" aria-hidden>
      <i style={{ width: `${Math.min(100, Math.max(0, value ?? 0))}%`, background: color ?? (value === null ? "transparent" : value >= 100 ? "#2e9e63" : value >= 90 ? "#7cb342" : value >= 70 ? "#d9a227" : "#c2412d") }} />
    </span>
  );
}

/** لون خلية نسبة الإنجاز كما في الملف: 100% فأكثر، 90%، 70%، وما دونها. */
function cellTone(v: number | null) {
  return v === null ? "" : v >= 100 ? "c100" : v >= 90 ? "c90" : v >= 70 ? "c70" : "clow";
}

// ----------------------------------------------------------------- حاسبة التصنيف

function Gauge({ score, tiers }: { score: number; tiers: TierBand[] }) {
  // نصف دائرة من 0 إلى 100 مقسمة بحدود الدرجة الكلية للفئات، ومؤشر عند الدرجة.
  const W = 320, H = 190, cx = 160, cy = 170, R = 140, w = 26;
  const angle = (v: number) => Math.PI * (1 - v / 100);
  const pt = (v: number, r: number) => [cx + r * Math.cos(angle(v)), cy - r * Math.sin(angle(v))];
  const arc = (a: number, b: number) => {
    const [x1, y1] = pt(a, R), [x2, y2] = pt(b, R), [x3, y3] = pt(b, R - w), [x4, y4] = pt(a, R - w);
    return `M${x1},${y1} A${R},${R} 0 0 1 ${x2},${y2} L${x3},${y3} A${R - w},${R - w} 0 0 0 ${x4},${y4} Z`;
  };
  const bands = [...tiers].sort((a, b) => b.tier - a.tier).map((t) => ({ t: t.tier, from: t.tier === 5 ? 0 : t.overallMin, to: t.tier === 1 ? 100 : t.overallMax + 1 }));
  const [nx, ny] = pt(Math.min(100, Math.max(0, score)), R - w - 14);
  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="sc-gauge" role="img" aria-label={L(`الدرجة الكلية ${num(score)} من 100`, `Overall ${num(score)} of 100`)}>
      {bands.map((b) => <path key={b.t} d={arc(b.from, b.to)} fill={TIER_COLORS[b.t - 1]} opacity={0.9} />)}
      {bands.filter((b) => b.t !== 5).map((b) => {
        const [x, y] = pt(b.from, R + 12);
        return <text key={b.t} x={x} y={y} textAnchor="middle" className="sc-gauge-tick">{b.from}</text>;
      })}
      <line x1={cx} y1={cy} x2={nx} y2={ny} className="sc-gauge-needle" />
      <circle cx={cx} cy={cy} r="9" className="sc-gauge-hub" />
      <text x={cx} y={cy - 40} textAnchor="middle" className="sc-gauge-value">{num(score)}</text>
    </svg>
  );
}

function TierView({ c, tierName, onPick }: { c: Scorecard; tierName: (t: number) => string; onPick: (i: IndicatorScore) => void }) {
  const o = c.overall;
  const perms = usePermissions();
  const alert: Record<TierResult["alert"], { type: "success" | "warning" | "error"; text: string }> = {
    Balanced: { type: "success", text: L("لا تفاوت كبير بين الركائز.", "Pillars are balanced.") },
    Warning: { type: "warning", text: L(`تنبيه: ${o.weakestPillar} (${num(o.weakestScore)}) أضعف بفئة كاملة من التصنيف العام، دون تخفيض تلقائي؛ تستدعي مراجعة.`, "Weakest pillar one tier below.") },
    Downgraded: { type: "error", text: L(`خفضت الفئة فئة واحدة بسبب ضعف ${o.weakestPillar} (${num(o.weakestScore)}): أضعف من التصنيف العام بفئتين أو أكثر.`, "Tier lowered by the weakest pillar.") },
  };
  return (
    <>
      <div className="sc-tier-top">
        <section className="lcp-panel sc-tier-card">
          <Gauge score={o.score} tiers={c.tiers} />
          <div className="sc-tier-name" style={{ "--tc": TIER_COLORS[o.tier.finalTier - 1] } as CSSProperties}>
            <small>{L("الفئة النهائية المعتمدة", "Final tier")}</small>
            <b>{L(`الفئة ${o.tier.finalTier}`, `Tier ${o.tier.finalTier}`)}</b>
            <span>{tierName(o.tier.finalTier)}</span>
          </div>
          <dl className="sc-tier-facts">
            <div><dt>{L("فئة الدرجة الكلية (+5)", "Overall-score tier")}</dt><dd>{o.tier.overallTier}</dd></div>
            <div><dt>{L("فئة أضعف ركيزة", "Weakest pillar tier")}</dt><dd>{o.tier.weakestTier}</dd></div>
            <div><dt>{L("أضعف ركيزة", "Weakest pillar")}</dt><dd className="small">{o.weakestPillar ?? "—"} <span className="ltr">({num(o.weakestScore)})</span></dd></div>
          </dl>
          <Alert type={alert[o.tier.alert].type} showIcon message={alert[o.tier.alert].text} />
        </section>

        <section className="lcp-panel sc-ladder">
          <div className="lcp-panel-head">
            <h2><TrophyOutlined aria-hidden />{L("سلم الفئات والفجوة إلى كل فئة", "Tier ladder and gaps")}</h2>
            {perms.can("adm.settings", "V") && <Link to="/settings"><SettingOutlined /> {L("حدود الفئات", "Thresholds")}</Link>}
          </div>
          <ol>
            {c.tiers.map((t) => {
              const here = t.tier === o.tier.finalTier;
              return (
                <li key={t.tier} className={here ? "here" : ""} style={{ "--tc": TIER_COLORS[t.tier - 1] } as CSSProperties}>
                  <span className="sc-ladder-n">{t.tier}</span>
                  <span className="sc-ladder-name"><b>{t.nameAr}</b><small className="ltr">{t.nameEn}</small></span>
                  <span className="sc-ladder-range">
                    <small>{L("الدرجة الكلية", "Overall")}</small><b className="ltr">{t.overallMin}–{t.overallMax}</b>
                    <small>{L("الركيزة", "Pillar")} <span className="ltr">{t.pillarMin}–{t.pillarMax}</span></small>
                  </span>
                  <span className="sc-ladder-gap">
                    {here ? <Tag color="green">{L("الفئة الحالية", "Current")}</Tag>
                      : t.tier < o.tier.finalTier && t.gap === 0 ? <small className="sc-ladder-held">{L("الدرجة تبلغها؛ يمنعها ضعف أضعف ركيزة", "Score reaches it; held back by the weakest pillar")}</small>
                      : t.tier < o.tier.finalTier ? <>{L("ينقص", "Need")} <b className="ltr">{num(t.gap)}</b> {L("نقطة", "pts")}</>
                      : <span className="muted">{L("تحققت", "Passed")}</span>}
                  </span>
                </li>
              );
            })}
          </ol>
          <p className="t1-help">{L(`حدود الدرجة الكلية أعلى من حدود الركائز بـ${c.thresholds.overallOffset} نقاط، وتخفض الفئة فئة واحدة إذا كانت أضعف ركيزة أسوأ بفئتين أو أكثر (فحص التوازن).`,
            "Overall thresholds are raised; the weakest pillar can lower the tier.")}</p>
        </section>
      </div>

      <Panel title={L("تفصيل الإنجاز وفئة كل ركيزة", "Achievement and tier per pillar")} icon={<ApartmentOutlined aria-hidden />}>
        <div className="sc-pillars">
          {c.pillars.map((p, index) => <PillarCard key={p.id} p={p} index={index} t1={c.thresholds.tier1} tierName={tierName} />)}
        </div>
      </Panel>

      <Panel title={L("أقرب المؤشرات أثرًا على الدرجة: الأبعد عن مستهدفها", "Indicators furthest from target")} icon={<WarningOutlined aria-hidden />}>
        <Furthest c={c} onPick={onPick} />
      </Panel>
    </>
  );
}

function PillarCard({ p, index, t1, tierName }: { p: PillarScore; index: number; t1: number; tierName: (t: number) => string }) {
  return (
    <article className="sc-pillar" style={tint(index)}>
      <header><span className="ltr sc-pillar-num">{String(index + 1).padStart(2, "0")}</span><b>{p.name}</b></header>
      <div className="sc-pillar-body">
        <Ring value={p.score} color={TIER_COLORS[p.tier - 1]} />
        <dl>
          <div><dt>{L("الفئة", "Tier")}</dt><dd>{L(`الفئة ${p.tier}`, `Tier ${p.tier}`)} <small className="muted">{tierName(p.tier)}</small></dd></div>
          <div><dt>{L("المؤشرات المقيمة", "Evaluated")}</dt><dd className="ltr">{p.evaluated}/{p.indicators}</dd></div>
          <div><dt>{L("المحققة للمستهدف", "On target")}</dt><dd className="ltr">{p.inLevel}{p.evaluated ? ` (${Math.round((100 * p.inLevel) / p.evaluated)}%)` : ""}</dd></div>
          <div><dt>{L("فجوة إلى الفئة 1", "Gap to tier 1")}</dt><dd>{p.score >= t1 ? L("محققة", "Reached") : <><span className="ltr">{num(Math.round((t1 - p.score) * 10) / 10)}</span> {L("نقطة", "pts")}</>}</dd></div>
        </dl>
      </div>
      <Tag className="sc-rag" color={p.rag === "Strong" ? "green" : p.rag === "Developing" ? "gold" : "red"}>{L(RAG_TEXT[p.rag].ar, RAG_TEXT[p.rag].en)}</Tag>
    </article>
  );
}

function Furthest({ c, onPick }: { c: Scorecard; onPick: (i: IndicatorScore) => void }) {
  const all = c.pillars.flatMap((p) => p.standards.flatMap((s) => s.indicators));
  const reported = all.filter((i) => i.latest !== null).sort((a, b) => a.score - b.score).slice(0, 8);
  if (reported.length === 0) return <EmptyNote>{L("لا مؤشرات لها قيم معتمدة بعد.", "No reported indicators yet.")}</EmptyNote>;
  return (
    <ul className="sc-list">
      {reported.map((i) => (
        <li key={i.id}>
          <button type="button" onClick={() => onPick(i)}>
            <code className="ltr">{i.code}</code><span>{i.name}</span>
            <Bar value={i.score} /><b className="ltr">{pct(i.score)}</b>
          </button>
        </li>
      ))}
    </ul>
  );
}

// ----------------------------------------------------------------- الركائز والمؤشرات

function PillarsView({ c, onPick }: { c: Scorecard; onPick: (i: IndicatorScore) => void }) {
  const [q, setQ] = useState("");
  const [level, setLevel] = useState<Level | "all">("all");
  const match = (i: IndicatorScore) => (level === "all" || i.level === level) && (!q || i.name.includes(q) || i.code.startsWith(q));
  return (
    <>
      <div className="sc-filter">
        <input className="ant-input" placeholder={L("ابحث بكود المؤشر أو اسمه", "Search")} value={q} onChange={(e) => setQ(e.target.value)} aria-label={L("بحث", "Search")} />
        {(["all", "InLevel", "Below", "NoData"] as const).map((k) => (
          <button key={k} type="button" className={level === k ? "on" : ""} onClick={() => setLevel(k)}>
            {k === "all" ? L("الكل", "All") : LEVEL_TEXT[k].ar}
          </button>
        ))}
      </div>
      {c.pillars.map((p, index) => {
        const stds = p.standards.map((s) => ({ s, rows: s.indicators.filter(match) })).filter((x) => x.rows.length > 0);
        if (stds.length === 0) return null;
        return (
          <section key={p.id} className="lcp-panel sc-pblock" style={tint(index)}>
            <header className="sc-pblock-head">
              <span className="ltr sc-pillar-num">{String(index + 1).padStart(2, "0")}</span>
              <span className="grow"><small>{L(`الركيزة ${p.code}`, `Pillar ${p.code}`)}</small><b>{p.name}</b></span>
              <span className="sc-score"><small>{L("درجة الركيزة", "Pillar score")}</small><b className="ltr">{num(p.score)}</b></span>
            </header>
            {stds.map(({ s, rows }) => (
              <div key={s.code} className="sc-std">
                <div className="sc-std-head"><code className="pill-code ltr">{L(`م${s.code}`, `S${s.code}`)}</code><b>{s.name}</b><span className="sc-score-sm ltr">{num(s.score)}</span></div>
                <div className="sc-table" role="table">
                  <div className="sc-row head" role="row">
                    <span role="columnheader">{L("المؤشر", "Indicator")}</span>
                    <span role="columnheader">{L("أحدث قيمة", "Latest")}</span>
                    <span role="columnheader">{L("المستهدف النهائي", "Final target")}</span>
                    <span role="columnheader">{L("الإنجاز", "Achievement")}</span>
                    <span role="columnheader">{L("السنوات 1–5", "Years 1–5")}</span>
                    <span role="columnheader">{L("الحالة", "Status")}</span>
                  </div>
                  {rows.map((i) => (
                    <div key={i.id} className="sc-row" role="row" onClick={() => onPick(i)} tabIndex={0} onKeyDown={(e) => e.key === "Enter" && onPick(i)}>
                      <span role="cell" className="sc-name"><code className="ltr">{i.code}</code> {i.name} <small className="muted">({i.unit})</small></span>
                      <span role="cell" className="ltr">{fmt(i.latest, i.unit)}{i.latestIndex >= 0 && <small className="muted"> Q{(i.latestIndex % 4) + 1}/Y{Math.floor(i.latestIndex / 4) + 1}</small>}</span>
                      <span role="cell" className="ltr">{fmt(i.finalTarget, i.unit)}</span>
                      <span role="cell" className="sc-ach"><Bar value={i.latest === null ? null : i.score} /><b className="ltr">{i.latest === null ? "—" : pct(i.score)}</b>
                        {i.rawAchievement !== null && i.rawAchievement > 100 && <Tooltip title={L("النسبة الفعلية قبل حد الـ100 (للعلم)", "Uncapped (info)")}><small className="sc-raw ltr">{pct(i.rawAchievement)}</small></Tooltip>}
                      </span>
                      <span role="cell" className="sc-years">
                        {i.yearScoresVsYearly.map((y, k) => (
                          <Tooltip key={k} title={L(`السنة ${k + 1}: ${y === null ? "لا قيمة" : `${num(y)}% من مستهدف السنة (${fmt(i.yearTargets[k], i.unit)})`}`, `Year ${k + 1}`)}>
                            <i className={cellTone(y)} />
                          </Tooltip>
                        ))}
                      </span>
                      <span role="cell"><Tag color={LEVEL_TEXT[i.level].color}>{LEVEL_TEXT[i.level].ar}</Tag></span>
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </section>
        );
      })}
    </>
  );
}

// ----------------------------------------------------------------- بطاقة أداء القطاع العام

const PSS_GROUPS = [
  { ar: "المخرجات والأثر", en: "Outcomes & impact", numbers: [1, 2, 3] },
  { ar: "العمليات التشغيلية", en: "Processes", numbers: [4] },
  { ar: "القدرات والممكّنات", en: "Capabilities & enablers", numbers: [5, 6, 7] },
];

function PssView({ c }: { c: Scorecard }) {
  return (
    <>
      {PSS_GROUPS.map((g) => (
        <Panel key={g.ar} title={L(g.ar, g.en)} icon={<FundOutlined aria-hidden />}>
          <div className="sc-pss">
            {c.perspectives.filter((p) => g.numbers.includes(p.number)).map((p) => (
              <article key={p.number} className="sc-pss-card">
                <header><span className="sc-pss-n">{p.number}</span><b>{p.nameAr}</b><small className="ltr">{p.nameEn}</small></header>
                <div className="sc-pss-body">
                  <Ring value={p.inLevelPercent ?? 0} color={p.inLevelPercent === null ? "#c4ccd8" : p.inLevelPercent >= 85 ? "#2e9e63" : p.inLevelPercent >= 70 ? "#d9a227" : "#c2412d"}
                    label={L(`${pct(p.inLevelPercent)} في المستوى`, "In level")} />
                  <dl>
                    <div><dt>{L("مؤشرات مرتبطة", "Linked")}</dt><dd>{p.indicators}</dd></div>
                    <div><dt>{L("في المستوى", "On target")}</dt><dd>{p.inLevel}</dd></div>
                    <div><dt>{L("دون المستوى", "Below")}</dt><dd className="bad">{p.below}</dd></div>
                  </dl>
                </div>
                <YearStrip label={L("نسبة المؤشرات المحققة لمستهدف السنة", "Share meeting the yearly target")} values={p.yearPercentVsYearly} />
              </article>
            ))}
          </div>
        </Panel>
      ))}
      <p className="t1-help">{L("المناظير السبعة لبطاقة أداء القطاع العام (Max Moullin). النسبة في الحلقة: المؤشرات التي بلغت أحدث قيمها المستهدف النهائي من كل مؤشرات المنظور.",
        "Seven PSS perspectives (Moullin).")}</p>
    </>
  );
}

function YearStrip({ label, values }: { label: string; values: (number | null)[] }) {
  return (
    <div className="sc-ystrip" aria-label={label}>
      <small>{label}</small>
      <div>
        {values.map((v, k) => (
          <span key={k} className={cellTone(v)}><b className="ltr">{v === null ? "—" : `${num(v)}%`}</b><small>{L(`س${k + 1}`, `Y${k + 1}`)}</small></span>
        ))}
      </div>
    </div>
  );
}

// ----------------------------------------------------------------- أهداف التنمية المستدامة

/** ألوان أهداف التنمية المستدامة السبعة عشر بترتيبها. */
const SDG_COLORS = ["#E5243B", "#DDA63A", "#4C9F38", "#C5192D", "#FF3A21", "#26BDE2", "#FCC30B", "#A21942", "#FD6925", "#DD1367", "#FD9D24", "#BF8B2E", "#3F7E44", "#0A97D9", "#56C02B", "#00689D", "#19486A"];

function SdgView({ c }: { c: Scorecard }) {
  return (
    <>
      <div className="sc-sdgs">
        {c.sdgs.map((g) => (
          <article key={g.number} className={`sc-sdg${g.total === 0 ? " none" : ""}`} style={{ "--sdg": SDG_COLORS[g.number - 1] } as CSSProperties}>
            <header><span className="sc-sdg-n ltr">{g.number}</span><b>{g.nameAr}</b></header>
            <dl>
              <div><dt>{L("أساسي", "Primary")}</dt><dd>{g.primary}</dd></div>
              <div><dt>{L("ثانوي", "Secondary")}</dt><dd>{g.secondary}</dd></div>
              <div><dt>{L("أثر نهائي", "Induced")}</dt><dd>{g.induced}</dd></div>
            </dl>
            <footer>
              {g.total === 0 ? <span className="muted">{L("لا مؤشرات مرتبطة", "No linked indicators")}</span> : g.inLevelPercent === null
                ? <span className="muted">{L(`${g.total} ارتباط · لا قيم بعد`, `${g.total} links · no data`)}</span>
                : <><b className="ltr">{pct(g.inLevelPercent)}</b><Tag color={g.rag === "Strong" ? "green" : g.rag === "Developing" ? "gold" : "red"}>{g.rag && L(RAG_TEXT[g.rag].ar, RAG_TEXT[g.rag].en)}</Tag></>}
            </footer>
          </article>
        ))}
      </div>
      <p className="t1-help">{L("لكل مؤشر ثلاثة أهداف: أساسي، وثانوي، وأثر نهائي (مستحث). النسبة: المؤشرات المرتبطة التي بلغت المستهدف النهائي من مجموع الارتباطات، والحالة: قوي 85% فأكثر، قيد التطوير 70–84%، معرض للخطر دونها.",
        "Each indicator links to three SDGs.")}</p>
    </>
  );
}

// ----------------------------------------------------------------- الاتجاه

function TrendView({ c }: { c: Scorecard }) {
  const [mode, setMode] = useState<"yearly" | "final">("yearly");
  const rows = c.pillars.map((p, index) => ({ p, index, values: mode === "yearly" ? p.yearScoresVsYearly : p.yearScores }));
  const overall = mode === "yearly" ? c.overall.yearScoresVsYearly : c.overall.yearScores;
  const last = (v: number[]) => { for (let k = v.length - 1; k >= 0; k--) if (v[k] !== 0) return k; return -1; };
  // آخر سنة وصلتها القيم المعتمدة: ما بعدها لم يأت بعد، فلا يرسم صفرًا ولا يعرض كأنه تراجع.
  const reached = c.lastReported ? c.lastReported.yearIndex - 1 : -1;
  return (
    <>
      <Panel title={L("ملخص أداء الركائز عبر السنوات", "Pillar performance over the years")} icon={<LineChartOutlined aria-hidden />}
        extra={(
          <span className="sc-mode" role="radiogroup" aria-label={L("المقارنة", "Compared with")}>
            <button type="button" role="radio" aria-checked={mode === "yearly"} className={mode === "yearly" ? "on" : ""} onClick={() => setMode("yearly")}>{L("مقابل مستهدف كل سنة", "vs yearly target")}</button>
            <button type="button" role="radio" aria-checked={mode === "final"} className={mode === "final" ? "on" : ""} onClick={() => setMode("final")}>{L("مقابل المستهدف النهائي", "vs final target")}</button>
          </span>
        )}>
        {reached < 0 ? <EmptyNote>{L("لا قيم معتمدة بعد لرسم الاتجاه.", "No approved values yet.")}</EmptyNote> : (
          <TrendChart series={rows.map((r) => ({ key: r.p.id, name: r.p.name, index: r.index, values: r.values.slice(0, reached + 1) }))}
            overall={overall.slice(0, reached + 1)} years={c.years} />
        )}
        <div className="sc-trend" role="table">
          <div className="sc-trow head" role="row">
            <span role="columnheader">{L("الركيزة", "Pillar")}</span>
            {c.years.map((y, k) => <span key={y} role="columnheader">{L(`السنة ${k + 1}`, `Year ${k + 1}`)}<small className="ltr">{y}</small></span>)}
            <span role="columnheader">{L("التغير السنوي", "Annual change")}</span>
          </div>
          {[...rows, { p: null, index: -1, values: overall }].map((r) => {
            const k = last(r.values);
            const change = k > 0 ? Math.round((r.values[k] - r.values[k - 1]) * 10) / 10 : null;
            return (
              <div key={r.p?.id ?? "all"} className={`sc-trow${r.p ? "" : " total"}`} role="row" style={r.p ? tint(r.index) : undefined}>
                <span role="cell">{r.p ? r.p.name : L("الدرجة الكلية", "Overall")}</span>
                {r.values.map((v, j) => j > reached
                  ? <span key={j} role="cell" className="ltr zero" title={L("لم تصلها البيانات بعد", "Not reached yet")}>—</span>
                  : <span key={j} role="cell" className={`ltr ${v === 0 ? "zero" : cellTone(v)}`}>{num(v)}</span>)}
                <span role="cell" className="ltr">
                  {change === null ? <span className="muted">{L("بيانات غير كافية", "Insufficient data")}</span>
                    : change > 0 ? <span className="up">▲ {num(change)}</span> : change < 0 ? <span className="down">▼ {num(Math.abs(change))}</span> : L("مستقر", "Stable")}
                </span>
              </div>
            );
          })}
        </div>
        <p className="t1-help">{L("قيمة السنة للمؤشر التراكمي آخر ربع فيها، وللحظي متوسط أرباعها. «مقابل المستهدف النهائي» يحسب المؤشر بلا قيمة صفرًا كما في الملف؛ «مقابل مستهدف كل سنة» يستبعده.",
          "Yearly value: last quarter (cumulative) or average (snapshot).")}</p>
      </Panel>
    </>
  );
}

function TrendChart({ series, overall, years }: { series: { key: string; name: string; index: number; values: number[] }[]; overall: number[]; years: number[] }) {
  const W = 720, H = 240, P = { l: 36, r: 40, t: 14, b: 30 };
  const x = (k: number) => P.l + ((W - P.l - P.r) * k) / (years.length - 1);
  const dots = (vals: number[]) => vals.length === 1;
  const y = (v: number) => P.t + (H - P.t - P.b) * (1 - v / 100);
  const line = (vals: number[]) => vals.map((v, k) => `${k === 0 ? "M" : "L"}${x(k).toFixed(1)},${y(v).toFixed(1)}`).join(" ");
  return (
    <figure className="sc-chart ltr">
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={L("الدرجة الكلية ودرجات الركائز عبر السنوات", "Scores over the years")}>
        {[0, 25, 50, 75, 100].map((v) => (
          <g key={v}><line x1={P.l} x2={W - P.r} y1={y(v)} y2={y(v)} className="grid" /><text x={P.l - 6} y={y(v) + 4} textAnchor="end" className="tick">{v}</text></g>
        ))}
        {years.map((yr, k) => <text key={yr} x={x(k)} y={H - 8} textAnchor="middle" className="tick">{L(`س${k + 1}`, `Y${k + 1}`)} · {yr}</text>)}
        {series.map((s) => dots(s.values)
          ? <circle key={s.key} cx={x(0)} cy={y(s.values[0])} r="4" style={tint(s.index)} className="pillar-pt" />
          : <path key={s.key} d={line(s.values)} style={tint(s.index)} className="pillar" />)}
        <path d={line(overall)} className="overall" />
        {overall.map((v, k) => <circle key={k} cx={x(k)} cy={y(v)} r="5" className="overall-pt" />)}
      </svg>
      <figcaption>
        <span><i className="k-overall" />{L("الدرجة الكلية", "Overall")}</span>
        {series.map((s) => <span key={s.key} style={tint(s.index)}><i className="k-pillar" />{s.name}</span>)}
      </figcaption>
    </figure>
  );
}

// ----------------------------------------------------------------- المؤشر

function IndicatorDrawer({ c, i, onClose }: { c: Scorecard; i: IndicatorScore; onClose: () => void }) {
  return (
    <Drawer open width={760} onClose={onClose} title={<span className="t1-code-title"><code className="ltr">{i.code}</code>{i.name}</span>}>
      <dl className="sc-facts">
        <div><dt>{L("أحدث قيمة", "Latest")}</dt><dd className="ltr">{fmt(i.latest, i.unit)}</dd><small>{i.latestIndex >= 0 ? c.quarters[i.latestIndex]?.label : L("لا قيمة معتمدة", "None")}</small></div>
        <div><dt>{L("المستهدف النهائي (السنة 5)", "Final target")}</dt><dd className="ltr">{fmt(i.finalTarget, i.unit)}</dd><small>{i.direction === "LowerIsBetter" ? L("تنازلي: الأقل أفضل", "Lower is better") : L("تصاعدي: الأعلى أفضل", "Higher is better")}</small></div>
        <div><dt>{L("الإنجاز", "Achievement")}</dt><dd className="ltr">{i.latest === null ? "—" : pct(i.score)}</dd><small>{L("الفرق عن المستهدف:", "Gap:")} <span className="ltr">{i.gap === null ? "—" : num(i.gap)}</span></small></div>
        <div><dt>{L("نوع المؤشر", "Type")}</dt><dd className="small">{i.accumulation === "Cumulative" ? L("تراكمي", "Cumulative") : L("غير تراكمي (لحظي)", "Snapshot")}</dd>
          <small>{i.accumulation === "Cumulative" ? L("قيمة السنة آخر ربع فيها", "Year = last quarter") : L("قيمة السنة متوسط أرباعها", "Year = average of quarters")}</small></div>
      </dl>

      <h3 className="sc-h3">{L("القيم الربعية ونسبة إنجازها من المستهدف النهائي", "Quarterly values")}</h3>
      <div className="sc-qgrid" role="table">
        <div className="sc-qrow head" role="row"><span role="columnheader">{L("السنة", "Year")}</span>{[1, 2, 3, 4].map((q) => <span key={q} role="columnheader">{L(`الربع ${q}`, `Q${q}`)}</span>)}
          <span role="columnheader">{L("قيمة السنة", "Year value")}</span><span role="columnheader">{L("مستهدف السنة", "Year target")}</span><span role="columnheader">{L("الإنجاز", "Achieved")}</span></div>
        {c.years.map((y, k) => (
          <div key={y} className="sc-qrow" role="row">
            <span role="cell">{L(`السنة ${k + 1}`, `Year ${k + 1}`)} <small className="muted ltr">{y}</small></span>
            {[0, 1, 2, 3].map((q) => {
              const idx = k * 4 + q;
              return <span key={q} role="cell" className={`ltr ${cellTone(i.quarterScores[idx])}`}>{fmt(i.quarters[idx], i.unit)}</span>;
            })}
            <span role="cell" className="ltr"><b>{fmt(i.yearValues[k], i.unit)}</b></span>
            <span role="cell" className="ltr">{fmt(i.yearTargets[k], i.unit)}</span>
            <span role="cell" className={`ltr ${cellTone(i.yearScoresVsYearly[k])}`}>{pct(i.yearScoresVsYearly[k])}</span>
          </div>
        ))}
      </div>
      <p className="t1-help">{L("ألوان الخلايا كما في الملف: أخضر 100% فأكثر من المستهدف، وأخضر فاتح 90%، وأصفر 70%، وأحمر دونها.", "Colours as in the workbook.")}</p>
      <Link to={`/indicators/${i.code}`}><Button icon={<AimOutlined />}>{L("بطاقة المؤشر وتعريفه", "Indicator card")}</Button></Link>
      {i.score >= 0 && <span className="sc-rag-note">{L("حالة الأداء:", "Status:")} <Tag color={ragOf(i.score) === "Strong" ? "green" : ragOf(i.score) === "Developing" ? "gold" : "red"}>{L(RAG_TEXT[ragOf(i.score)].ar, RAG_TEXT[ragOf(i.score)].en)}</Tag></span>}
    </Drawer>
  );
}
