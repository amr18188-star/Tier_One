import type { ReactNode } from "react";
import { AimOutlined, ApartmentOutlined, ArrowLeftOutlined, ArrowRightOutlined, NodeIndexOutlined, RocketOutlined } from "@ant-design/icons";
import type { IndicatorNode, PillarNode, StandardNode } from "../../api/tier1";
import { L, useLang } from "../../shared/i18n";
import { stdLabel } from "../../shared/pillars";
import {
  allIndicators, allInitiatives, allStandards, countDots, dotState, dotText, span, targetsComplete, tint, usePlan, type DotState, type View,
} from "./model";

/**
 * واجهة الخطة: شريط علوي بهوية الخطة وسنواتها وجاهزيتها، وتبويبات العروض الثلاثة،
 * وخريطة الخطة: ركيزة بلونها في كل عمود، ومعاييرها تحتها، ولكل مؤشر نقطة بلون حالته.
 */

// ----------------------------------------------------------------- الشريط العلوي

function Ring({ value, total, label, hint }: { value: number; total: number; label: string; hint: string }) {
  const r = 26, c = 2 * Math.PI * r;
  const pct = total === 0 ? 0 : value / total;
  return (
    <div className="sp-ring" title={hint}>
      <svg viewBox="0 0 64 64" width="64" height="64" aria-hidden>
        <circle cx="32" cy="32" r={r} className="sp-ring-track" />
        <circle cx="32" cy="32" r={r} className="sp-ring-fill" strokeDasharray={`${(c * pct).toFixed(1)} ${c.toFixed(1)}`} transform="rotate(-90 32 32)" />
        <text x="32" y="37" textAnchor="middle">{Math.round(pct * 100)}%</text>
      </svg>
      <span><b>{label}</b><small>{L(`${value} من ${total}`, `${value} of ${total}`)}</small></span>
    </div>
  );
}

/** compact: سطر واحد بهوية الخطة وأرقامها، لعرض التفاصيل حيث المساحة للعنصر المختار. */
export function PlanHero({ compact = false }: { compact?: boolean }) {
  const { tree } = usePlan();
  const years = tree.years;
  const first = years[0];
  const last = years[years.length - 1];
  const standards = allStandards(tree);
  const indicators = allIndicators(tree);
  const initiatives = allInitiatives(tree);
  const complete = indicators.filter((i) => targetsComplete(i, years)).length;
  const approved = indicators.filter((i) => i.status === "Approved").length;
  const now = new Date().getFullYear();
  const rail = [tree.baselineYear, ...years];
  const nowYear = now < tree.baselineYear ? tree.baselineYear : now > last ? last : now;

  if (compact)
    return (
      <section className="sp-hero compact" aria-labelledby="sp-hero-title">
        <div className="sp-hero-id">
          <small>{L("جامعة العاصمة · الخطة الاستراتيجية", "Capital University · Strategic plan")}</small>
          <h1 id="sp-hero-title">{L("خطة", "Plan")} <span className="ltr-nums">{first}–{last}</span></h1>
        </div>
        <dl className="sp-hero-stats">
          <div><dt>{L("ركائز", "Pillars")}</dt><dd>{tree.pillars.length}</dd></div>
          <div><dt>{L("معايير", "Standards")}</dt><dd>{standards.length}</dd></div>
          <div><dt>{L("مبادرات", "Initiatives")}</dt><dd>{initiatives.length}</dd></div>
          <div><dt>{L("مؤشرات", "Indicators")}</dt><dd>{indicators.length}</dd></div>
          <div><dt>{L("خطط مكتملة", "Complete plans")}</dt><dd className="ltr-nums">{complete}/{indicators.length}</dd></div>
        </dl>
      </section>
    );
  return (
    <section className="sp-hero" aria-labelledby="sp-hero-title">
      <div className="sp-hero-top">
        <div className="sp-hero-id">
          <small>{L("جامعة العاصمة · الخطة الاستراتيجية", "Capital University · Strategic plan")}</small>
          <h1 id="sp-hero-title">{L("خطة", "Plan")} <span className="ltr-nums">{first}–{last}</span></h1>
          <p>{L(`${tree.pillars.length} ركائز تتفرع إلى ${standards.length} معيارًا، تنفذها ${initiatives.length} مبادرة، ويقاس التقدم فيها بـ${indicators.length} مؤشرًا لكل منها مستهدف في كل سنة.`,
            `${tree.pillars.length} pillars, ${standards.length} standards, ${initiatives.length} initiatives and ${indicators.length} indicators with yearly targets.`)}</p>
        </div>
        <div className="sp-hero-rings">
          <Ring value={complete} total={indicators.length} label={L("خطط مستهدف مكتملة", "Complete target plans")}
            hint={L("مؤشرات لها قيمة مستهدفة في كل سنة من سنة البدء حتى سنة التحقق.", "Indicators with a value for every year from start to target year.")} />
          <Ring value={approved} total={indicators.length} label={L("تعريفات معتمدة", "Approved definitions")}
            hint={L("مؤشرات اعتمدت اللجنة العليا تعريفها.", "Indicators whose definition is approved.")} />
        </div>
      </div>

      <dl className="sp-hero-stats">
        <div><dt>{L("ركائز", "Pillars")}</dt><dd>{tree.pillars.length}</dd></div>
        <div><dt>{L("معايير", "Standards")}</dt><dd>{standards.length}</dd></div>
        <div><dt>{L("مبادرات", "Initiatives")}</dt><dd>{initiatives.length}</dd></div>
        <div><dt>{L("مؤشرات", "Indicators")}</dt><dd>{indicators.length}</dd></div>
      </dl>

      <ol className="sp-rail" aria-label={L("سنوات الخطة وما يتحقق في كل سنة", "Plan years and what is due each year")}
        style={{ gridTemplateColumns: `repeat(${rail.length}, minmax(0, 1fr))` }}>
        {rail.map((y) => {
          const due = y === tree.baselineYear ? 0 : indicators.filter((i) => span(i, years).end === y).length;
          const starts = y === tree.baselineYear ? 0 : indicators.filter((i) => i.planStartYear === y && y > first).length;
          const ends = initiatives.filter((m) => m.endYear === y).length;
          return (
            <li key={y} className={`${y === nowYear ? "now" : ""}${y === tree.baselineYear ? " base" : ""}`} aria-current={y === nowYear ? "date" : undefined}>
              <span className="sp-rail-dot" aria-hidden />
              <b className="ltr-nums">{y}</b>
              {y === tree.baselineYear ? <small>{L("سنة الأساس", "Baseline year")}</small> : (
                <small>
                  {due > 0 ? L(`يتحقق ${due} مستهدفًا`, `${due} targets due`) : L("لا مستهدفات نهائية", "No final targets")}
                  {starts > 0 && <><br />{L(`ويبدأ ${starts}`, `${starts} start`)}</>}
                  {ends > 0 && <><br />{L(`تنتهي ${ends} مبادرة`, `${ends} initiatives end`)}</>}
                </small>
              )}
              {y === nowYear && <em>{L("نحن هنا", "We are here")}</em>}
            </li>
          );
        })}
      </ol>
    </section>
  );
}

// ----------------------------------------------------------------- التبويبات

const VIEWS: { key: View; icon: ReactNode; ar: string; en: string; hint: string }[] = [
  { key: "map", icon: <ApartmentOutlined aria-hidden />, ar: "خريطة الخطة", en: "Plan map", hint: "الركائز ومعاييرها وحالة كل مؤشر" },
  { key: "roadmap", icon: <NodeIndexOutlined aria-hidden />, ar: "الخط الزمني", en: "Roadmap", hint: "متى يبدأ كل مستهدف ومتى يتحقق، ومدد المبادرات" },
  { key: "explore", icon: <AimOutlined aria-hidden />, ar: "التفاصيل والتعديل", en: "Details", hint: "شجرة الخطة وشرح كل عنصر وتعديله" },
];

export function ViewTabs({ view, onView, extra }: { view: View; onView: (v: View) => void; extra?: ReactNode }) {
  return (
    <div className="sp-tabs-row">
    <div className="sp-tabs" role="tablist" aria-label={L("طريقة العرض", "View")}>
      {VIEWS.map((v) => (
        <button key={v.key} type="button" role="tab" aria-selected={view === v.key} className={view === v.key ? "on" : ""} onClick={() => onView(v.key)}>
          {v.icon}
          <span><b>{L(v.ar, v.en)}</b><small>{L(v.hint, "")}</small></span>
        </button>
      ))}
    </div>
    {extra}
    </div>
  );
}

// ----------------------------------------------------------------- نقاط المؤشرات

const ORDER: DotState[] = ["Achieved", "Near", "Behind", "planned", "gap"];
const SHORT: Record<DotState, string> = { Achieved: "محقق", Near: "قريب", Behind: "متأخر", planned: "خطته مكتملة", gap: "خطته ناقصة" };

export function DotLegend({ list }: { list: IndicatorNode[] }) {
  const { tree, results, resultsFrom } = usePlan();
  const c = countDots(list, tree.years, results);
  // ألوان الأداء تظهر حين يحكم على مؤشر واحد على الأقل؛ وإلا فأصفارها توهم بحكم لم يحدث.
  const judged = !!resultsFrom && c.Achieved + c.Near + c.Behind > 0;
  return (
    <div className="sp-legend">
      <span className="sp-legend-title">{L("لون كل نقطة حالة مؤشر:", "Each dot is an indicator:")}</span>
      {ORDER.filter((k) => judged || (k !== "Achieved" && k !== "Near" && k !== "Behind")).map((k) => (
        <span key={k} className="sp-legend-item"><i className={`sp-dot d-${k}`} aria-hidden />{L(SHORT[k], k)} <b className="ltr-nums">{c[k]}</b></span>
      ))}
      {resultsFrom && <small className="muted">{judged ? L(`الأداء من ${resultsFrom}`, `Performance from ${resultsFrom}`) : L(`لا نتائج معتمدة يحكم بها بعد (${resultsFrom})`, `No judged results yet (${resultsFrom})`)}</small>}
    </div>
  );
}

export function Dots({ list }: { list: IndicatorNode[] }) {
  const { tree, results } = usePlan();
  const c = countDots(list, tree.years, results);
  const label = ORDER.filter((k) => c[k] > 0).map((k) => `${c[k]} ${SHORT[k]}`).join("، ");
  return (
    <span className="sp-dots" role="img" aria-label={label || L("لا مؤشرات", "No indicators")}>
      {list.map((i) => {
        const s = dotState(i, tree.years, results.get(i.code));
        return <i key={i.code} className={`sp-dot d-${s}`} title={`${i.code} — ${i.nameAr}: ${dotText[s]}`} />;
      })}
    </span>
  );
}

/** شريط مكدس بحالات المؤشرات، بنسبة كل حالة. */
export function StateBar({ list }: { list: IndicatorNode[] }) {
  const { tree, results } = usePlan();
  const c = countDots(list, tree.years, results);
  const total = list.length || 1;
  return (
    <div className="sp-statebar" aria-hidden>
      {ORDER.filter((k) => c[k] > 0).map((k) => <span key={k} className={`d-${k}`} style={{ flexGrow: c[k] / total }} />)}
    </div>
  );
}

// ----------------------------------------------------------------- الخريطة

export function PlanMap() {
  const { tree } = usePlan();
  return (
    <div className="sp-map-wrap">
      <DotLegend list={allIndicators(tree)} />
      <div className="sp-map">
        {tree.pillars.map((p, index) => <PillarCard key={p.id} pillar={p} index={index} />)}
        {tree.unassigned.length > 0 && (
          <PillarCard index={-1} pillar={{ id: "", code: "—", nameAr: L("معايير لم تربط بركيزة", "Standards without a pillar"), nameEn: null, description: null, sortOrder: 99, version: 0, standards: tree.unassigned }} />
        )}
      </div>
    </div>
  );
}

function PillarCard({ pillar, index }: { pillar: PillarNode; index: number }) {
  const { select } = usePlan();
  const lang = useLang();
  const Go = lang === "ar" ? ArrowLeftOutlined : ArrowRightOutlined;
  const indicators = pillar.standards.flatMap((s) => s.indicators);
  const initiatives = pillar.standards.reduce((n, s) => n + s.initiatives.length, 0);
  return (
    <article className={`sp-pillar${index < 0 ? " loose" : ""}`} style={tint(index)}>
      <header>
        {index >= 0 ? (
          <button type="button" className="sp-pillar-head" onClick={() => select(`P:${pillar.id}`)}>
            <span className="sp-pillar-num ltr-nums">{String(index + 1).padStart(2, "0")}</span>
            <span className="sp-pillar-name"><small>{L(`الركيزة ${pillar.code}`, `Pillar ${pillar.code}`)}</small><b>{pillar.nameAr}</b></span>
          </button>
        ) : (
          <div className="sp-pillar-head"><span className="sp-pillar-name"><b>{pillar.nameAr}</b></span></div>
        )}
        <dl className="sp-pillar-stats">
          <div><dt>{L("معيار", "standards")}</dt><dd>{pillar.standards.length}</dd></div>
          <div><dt>{L("مبادرة", "initiatives")}</dt><dd>{initiatives}</dd></div>
          <div><dt>{L("مؤشر", "indicators")}</dt><dd>{indicators.length}</dd></div>
        </dl>
      </header>
      <div className="sp-pillar-body">
        <StateBar list={indicators} />
        <ul className="sp-stds">
          {pillar.standards.map((s) => <StandardRow key={s.id} standard={s} />)}
        </ul>
        {index >= 0 && (
          <button type="button" className="sp-pillar-more" onClick={() => select(`P:${pillar.id}`)}>
            {L("شرح الركيزة وتفاصيلها", "Pillar details")} <Go aria-hidden />
          </button>
        )}
      </div>
    </article>
  );
}

function StandardRow({ standard: s }: { standard: StandardNode }) {
  const { select } = usePlan();
  return (
    <li>
      <button type="button" onClick={() => select(`S:${s.id}`)}>
        <span className="sp-std-head"><code>{stdLabel(s.code)}</code><b>{s.nameAr}</b></span>
        <span className="sp-std-meta">
          <span><AimOutlined aria-hidden /> {L(`${s.indicators.length} مؤشر`, `${s.indicators.length} indicators`)}</span>
          <span><RocketOutlined aria-hidden /> {L(`${s.initiatives.length} مبادرة`, `${s.initiatives.length} initiatives`)}</span>
        </span>
        <Dots list={s.indicators} />
      </button>
    </li>
  );
}
