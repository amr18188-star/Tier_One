import { type CSSProperties } from "react";
import { Alert, Tag } from "antd";
import { ApartmentOutlined, SettingOutlined, TrophyOutlined, WarningOutlined } from "@ant-design/icons";
import { Link } from "react-router-dom";
import type { IndicatorScore, PillarScore, Scorecard, TierBand, TierResult } from "../../api/scorecard";
import { L } from "../../shared/i18n";
import { num } from "../../shared/labels";
import { tint } from "../../shared/pillars";
import { usePermissions } from "../../shared/session";
import { EmptyNote, Panel } from "../../shared/ui";
import { Bar, RAG_TEXT, Ring, TIER_COLORS, pct } from "./scoreParts";

/** The tier view: the university's score on its tier gauge, the pillars, and the indicators furthest from target. */
export function Gauge({ score, tiers }: { score: number; tiers: TierBand[] }) {
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

export function TierView({ c, tierName, onPick }: { c: Scorecard; tierName: (t: number) => string; onPick: (i: IndicatorScore) => void }) {
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

export function PillarCard({ p, index, t1, tierName }: { p: PillarScore; index: number; t1: number; tierName: (t: number) => string }) {
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

export function Furthest({ c, onPick }: { c: Scorecard; onPick: (i: IndicatorScore) => void }) {
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
