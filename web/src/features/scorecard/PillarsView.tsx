import { memo, useEffect, useId, useMemo, useState } from "react";
import { Input, Tag, Tooltip } from "antd";
import type { IndicatorScore, Level, PillarScore, Scorecard } from "../../api/scorecard";
import { AccordionPanel, useSingleOpen } from "../../shared/AccordionPanel";
import { L, useLang } from "../../shared/i18n";
import { UniTarget } from "../../shared/CollegeTarget";
import { num } from "../../shared/labels";
import { tint } from "../../shared/pillars";
import { EmptyNote } from "../../shared/ui";
import { pillarNum } from "../strategy/model";
import { Bar, LEVEL_TEXT, cellTone, fmt, pct } from "./scoreParts";

/** The pillars view: each pillar as a folding panel with its standards and indicators. */
/**
 * كل ركيزة لوحة تطوى (AccordionPanel المشتركة)، وركيزة واحدة مفتوحة، كما في الشريط الجانبي: كلها مطوية عند الدخول،
 * فتح ركيزة يطوي المفتوحة، والضغط على رأس المفتوحة يطويها. ?pillar=01…06 يحفظ المفتوحة بـ replace فيبقيها التحميل؛
 * قيمة لا تطابق ركيزة تحذف بـ replace. المرشح (البحث والحالة) لا يخفي ركيزة: يعرض كل رأس عدد مؤشراته المطابقة،
 * والمفتوحة بلا مطابقة تقول ذلك في محتواها.
 */
export function PillarsView({ c, onPick }: { c: Scorecard; onPick: (i: IndicatorScore) => void }) {
  const [q, setQ] = useState("");
  const [level, setLevel] = useState<Level | "all">("all");
  const filtering = level !== "all" || q !== "";
  // صفوف كل ركيزة بعد المرشح، ثابتة ما لم يتغير المرشح: فتح ركيزة لا يعيد رسم جداول اللوحات (PillarPanel و PillarStandards مذكّرتان).
  const stdsByPillar = useMemo(() => {
    const match = (i: IndicatorScore) => (level === "all" || i.level === level) && (!q || i.name.includes(q) || i.code.startsWith(q));
    return c.pillars.map((p) => p.standards.map((s) => ({ s, rows: s.indicators.filter(match) })).filter((x) => x.rows.length > 0));
  }, [c.pillars, q, level]);
  // المفتاح في الرابط رقم الركيزة 01…06 كما في صفحة المراجعة (لا حروف في الرابط). رابط قديم برمز الركيزة (أ، ب …) مقبول:
  // يفتح ركيزته من أول رسم، ثم يكتب رقمها مكانه بـ replace.
  const nums = useMemo(() => c.pillars.map((_, index) => pillarNum(index)), [c.pillars]);
  const codes = useMemo(() => c.pillars.map((p) => p.code), [c.pillars]);
  const keys = useMemo(() => [...nums, ...codes], [nums, codes]);
  const { openKey, toggle, setOpenKey } = useSingleOpen({ param: "pillar", keys });
  useEffect(() => {
    const old = openKey === null ? -1 : codes.indexOf(openKey);
    if (old >= 0) setOpenKey(nums[old]);
  }, [openKey, codes, nums, setOpenKey]);
  return (
    <>
      <div className="sc-filter">
        <Input placeholder={L("ابحث بكود المؤشر أو اسمه", "Search")} value={q} onChange={(e) => setQ(e.target.value)} aria-label={L("بحث", "Search")} />
        {(["all", "InLevel", "Below", "NoData"] as const).map((k) => (
          <button key={k} type="button" className={level === k ? "on" : ""} onClick={() => setLevel(k)}>
            {k === "all" ? L("الكل", "All") : LEVEL_TEXT[k].ar}
          </button>
        ))}
      </div>
      <div className="sc-acc">
        {c.pillars.map((p, index) => (
          <PillarPanel key={p.id} p={p} index={index} open={openKey === nums[index] || openKey === p.code} onToggle={toggle} onPick={onPick} stds={stdsByPillar[index]} filtering={filtering} />
        ))}
      </div>
    </>
  );
}

export type StdRows = { s: PillarScore["standards"][number]; rows: IndicatorScore[] };

/**
 * لوحة ركيزة على AccordionPanel: بطاقة بحد علوي بلون الركيزة، والرأس الرقم 01…06 (ترتيب الخطة) والاسم وملخصه ودرجة الركيزة.
 * رمز الركيزة (أ، ب …) لا يعرض ولا يكتب في الرابط (الرابط بالرقم)؛ يبقى في المفاتيح ولقارئ الشاشة. مذكّرة: الفتح يعيد رسم لوحتين فقط.
 */
export const PillarPanel = memo(function PillarPanel({ p, index, open, onToggle, onPick, stds, filtering }: {
  p: PillarScore;
  index: number;
  open: boolean;
  onToggle: (code: string) => void;
  onPick: (i: IndicatorScore) => void;
  stds: StdRows[];
  filtering: boolean;
}) {
  useLang(); // memo skips equal props; its own texts still follow a language change
  const uid = useId();
  const shown = stds.reduce((n, x) => n + x.rows.length, 0);
  return (
    <AccordionPanel id={`sc-pillar${uid}`} open={open} onToggle={() => onToggle(pillarNum(index))} className="lcp-panel sc-pblock sc-acc-panel" style={tint(index)}
      header={(
        <span className="sc-pblock-head sc-acc-title">
          <span className="ltr sc-pillar-num" aria-hidden>{pillarNum(index)}</span>
          <span className="grow">
            <b><span className="sr-only">{L(`الركيزة ${p.code}: `, `Pillar ${p.code}: `)}</span>{p.name}</b>
            <span className="sc-acc-sum">
              {!filtering ? <>{L("لها قيمة", "Reported")} <span className="ltr">{p.evaluated}/{p.indicators}</span> · {L("في المستوى", "On target")} <span className="ltr">{p.inLevel}</span></>
                : shown === 0 ? L("لا مؤشرات مطابقة", "No matching indicators")
                : <>{L("المطابقة", "Matching")} <span className="ltr">{shown}/{p.indicators}</span></>}
            </span>
          </span>
          <span className="sc-score"><small>{L("درجة الركيزة", "Pillar score")}</small><b className="ltr">{num(p.score)}</b></span>
        </span>
      )}>
      <PillarStandards stds={stds} onPick={onPick} />
    </AccordionPanel>
  );
});

/** معايير الركيزة ومؤشراتها (كما كانت تحت رأس الركيزة)؛ مذكّرة على صفوف المرشح، فلا تعاد عند فتح لوحة أو طيها. */
export const PillarStandards = memo(function PillarStandards({ stds, onPick }: { stds: StdRows[]; onPick: (i: IndicatorScore) => void }) {
  useLang(); // memo skips equal props; its own texts still follow a language change
  return (
    <div className="sc-acc-content">
      {stds.length === 0 && <EmptyNote>{L("لا مؤشرات في هذه الركيزة تطابق البحث أو الحالة المختارة.", "No indicators in this pillar match the filter.")}</EmptyNote>}
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
                <span role="cell" className="ltr"><UniTarget>{fmt(i.finalTarget, i.unit)}</UniTarget></span>
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
    </div>
  );
});

// ----------------------------------------------------------------- بطاقة أداء القطاع العام
