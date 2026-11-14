import { Skeleton } from "antd";
import { CheckOutlined, ClockCircleOutlined, EditOutlined, LeftOutlined, MinusOutlined, RightOutlined } from "@ant-design/icons";
import type { Cycle } from "../api/collection";
import { L, useLang } from "./i18n";
import {
  chipLabel, cycleTimeline, quarterTimeline, selectedSlot, slotName, stateLine, stepSlot,
  type PeriodChange, type PeriodMode, type PeriodValue, type QuarterOptions, type SlotState,
} from "./period";
import { usePlanCalendar } from "./usePeriodParams";
import "./period-nav.css";

export type { PeriodChange, PeriodMode, PeriodValue, SlotState } from "./period";

const ICON: Record<SlotState, typeof CheckOutlined> = { closed: CheckOutlined, open: EditOutlined, future: ClockCircleOutlined, none: MinusOutlined };

export interface PeriodNavProps extends QuarterOptions {
  mode: PeriodMode;
  /** متحكم بها: الشريط لا يختار افتراضًا (الافتراض عند المستدعي أو usePeriodParams). */
  value: PeriodValue;
  onChange: (next: PeriodChange) => void;
  /** cycle: دورات جمع البيانات. */
  cycles?: readonly Cycle[];
  /** سنوات بدل سنوات الخطة. */
  years?: readonly number[];
  /** سطر الحالة تحت اسم الربع (افتراضيًا ظاهر). */
  stateLine?: boolean;
  /** داخل درج أو نموذج: بلا إطار ولا خلفية، والأجزاء تحت بعضها. */
  compact?: boolean;
  /** كل الأزرار معطلة (حقل نموذج للقراءة فقط)؛ المحدد يبقى ظاهرًا. */
  disabled?: boolean;
  ariaLabel?: string;
  className?: string;
}

/**
 * شريط السنة والربع المشترك: أزرار السنوات، ثم أرباع السنة بسهمين ينتقلان إلى الخانة المتاحة السابقة أو التالية
 * (عبر السنوات)، واسم الخانة وسطر حالتها، والخانات ملونة بحالتها ومعها أيقونة. الخانة المعطلة («لا دورة») لا تنقر.
 * السنوات من تقويم الخطة (/api/plan/calendar)، وبلا تقويم من الدورات.
 */
export function PeriodNav({
  mode, value, onChange, cycles, years, disabledQuarters, quarterState, stateLine: showState = true, compact, disabled, ariaLabel, className,
}: PeriodNavProps) {
  const lang = useLang();
  const calendar = usePlanCalendar();
  const cls = ["fq", compact && "is-compact", disabled && "is-disabled", className].filter(Boolean).join(" ");
  if (calendar.isPending) return <div className={`${cls} is-loading`} aria-hidden><Skeleton.Input active size="small" /></div>;

  const timeline = mode === "cycle"
    ? cycleTimeline(cycles ?? [], calendar.data, years)
    : quarterTimeline(calendar.data, { disabledQuarters, quarterState }, years);
  if (timeline.years.length === 0) return null;

  const slot = selectedSlot(timeline, mode, value);
  const year = slot?.year ?? value.year ?? null;
  const inYear = timeline.slots.filter((s) => s.year === year);
  const prev = stepSlot(timeline, slot, year, -1);
  const next = stepSlot(timeline, slot, year, 1);
  const PrevIcon = lang === "ar" ? RightOutlined : LeftOutlined;
  const NextIcon = lang === "ar" ? LeftOutlined : RightOutlined;
  const yearIndex = (y: number) => timeline.slots.find((s) => s.year === y && s.plan)?.plan?.yearIndex;
  const pick = (s: NonNullable<typeof slot>) => onChange({ year: s.year, quarter: s.quarter, ...(s.cycle ? { cycleId: s.cycle.id } : {}) });
  const empty = mode === "cycle" ? L("لا دورة", "No cycle") : L("لا ربع", "No quarter");

  return (
    <nav className={cls} aria-label={ariaLabel ?? (mode === "year" ? L("السنة", "Year") : L("السنة والربع", "Year and quarter"))}>
      <div className="fq-years" role="group" aria-label={L("سنة الخطة", "Plan year")}>
        {timeline.years.map((y) => {
          const idx = yearIndex(y);
          return (
            <button key={y} type="button" className="fq-year" aria-pressed={y === year} disabled={disabled} onClick={() => onChange({ year: y })}
              title={idx ? L(`السنة ${idx} من الخطة`, `Plan year ${idx}`) : undefined}>
              <span className="ltr">{y}</span>
            </button>
          );
        })}
      </div>

      {mode !== "year" && (
        <div className="fq-strip">
          <div className="fq-step">
            <button type="button" className="fq-arrow" disabled={disabled || !prev} onClick={() => prev && pick(prev)}
              aria-label={L("الربع السابق", "Previous quarter")} title={prev ? slotName(prev) : undefined}>
              <PrevIcon aria-hidden />
            </button>
            <div className="fq-now" aria-live="polite">
              <b className="fq-name">
                {slot ? slotName(slot) : year !== null ? L(`${empty} في ${year}`, `${empty} in ${year}`) : empty}
                {slot?.cycle?.trial && <span className="fq-trial">{L("بروفة لا تدخل بطاقة الأداء", "Rehearsal, not scored")}</span>}
              </b>
              {showState && (
                <span className="fq-state" data-state={slot?.state ?? "none"}>
                  {slot ? stateLine(slot)
                    : mode === "cycle" ? L("لم تُنشأ دورات لجمع البيانات في هذه السنة بعد.", "No collection cycles in this year yet.")
                    : L("اختر ربعًا.", "Pick a quarter.")}
                </span>
              )}
            </div>
            <button type="button" className="fq-arrow" disabled={disabled || !next} onClick={() => next && pick(next)}
              aria-label={L("الربع التالي", "Next quarter")} title={next ? slotName(next) : undefined}>
              <NextIcon aria-hidden />
            </button>
          </div>

          <div className="fq-chips" role="group" aria-label={year !== null ? L(`أرباع ${year}`, `Quarters of ${year}`) : undefined}>
            {inYear.map((s) => {
              const Icon = ICON[s.state];
              const current = s.source === "cycle" && s.plan?.current ? L(" · الربع الجاري", " · current quarter") : "";
              const text = `${slotName(s)} · ${stateLine(s)}${current}`;
              return (
                <button key={s.key} type="button" className="fq-chip" data-state={s.state} data-kind={s.kind}
                  aria-pressed={s.key === slot?.key} data-off={s.disabled || undefined} disabled={disabled || s.disabled} title={text} aria-label={text} onClick={() => pick(s)}>
                  <Icon className="fq-chip-icon" aria-hidden />
                  <span>{chipLabel(s)}</span>
                </button>
              );
            })}
          </div>
        </div>
      )}
    </nav>
  );
}
