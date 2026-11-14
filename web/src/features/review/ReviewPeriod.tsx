import { Select, Skeleton } from "antd";
import { CalendarOutlined } from "@ant-design/icons";
import type { Cycle } from "../../api/collection";
import type { ReviewCycle } from "../../api/scorecard";
import { L } from "../../shared/i18n";
import { PeriodNav } from "../../shared/PeriodNav";
import type { PeriodParams } from "../../shared/usePeriodParams";

/** الدورات التي تراجع: ربع سنوية رسمية غير مسودة (القاعدة نفسها في ReviewService.QuarterlyCyclesAsync). */
export function reviewable(c: Cycle): boolean {
  return c.status !== "Draft" && !c.trial && c.quarter != null;
}

/** افتراض الصفحة كما في الخادم حين لا يطلب ربع: أحدث دورة (السنة ثم الربع تنازليًا). */
export function latestCycle(cycles: readonly Cycle[] | undefined): Cycle | undefined {
  return cycles?.reduce<Cycle | undefined>((best, c) =>
    !best || c.targetYear > best.targetYear || (c.targetYear === best.targetYear && (c.quarter ?? 0) > (best.quarter ?? 0)) ? c : best, undefined);
}

/**
 * الربع المعروض فوق المرشحات. نقطة التبديل الوحيدة بين الشريط المشترك والقائمة القديمة:
 * cycles (دورات المراجعة من /api/cycles) ← PeriodNav بالسنة والربع؛ ومن لا يرى الدورات (أو تعذر طلبها) ← قائمة الأرباع
 * من رد المراجعة نفسه، كما كانت.
 */
export function ReviewPeriod({ cycles, period, fallback }: {
  cycles: Cycle[] | undefined;
  period: PeriodParams;
  fallback: { cycles: ReviewCycle[]; value: string | undefined; onChange: (cycleId: string) => void };
}) {
  if (cycles) {
    return period.ready
      ? <PeriodNav mode="cycle" cycles={cycles} value={period.value} onChange={period.onChange} className="rv-period-nav" ariaLabel={L("ربع المراجعة", "Review quarter")} />
      : <div className="fq is-loading" aria-hidden><Skeleton.Input active size="small" /></div>;
  }
  if (!fallback.value || fallback.cycles.length === 0) return null;
  return (
    <div className="lcp-filters rv-toolbar">
      <Select className="rv-period" value={fallback.value} onChange={fallback.onChange} aria-label={L("الربع", "Quarter")} prefix={<CalendarOutlined aria-hidden />}
        popupMatchSelectWidth={false} options={fallback.cycles.map((c) => ({ value: c.id, label: L(c.label, `${c.year} · Q${c.quarter}`) }))} />
    </div>
  );
}
