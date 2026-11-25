import { useState, type ReactNode } from "react";
import { Alert, Button, Space, Tooltip } from "antd";
import { ApartmentOutlined, DownloadOutlined, FundOutlined, GlobalOutlined, LineChartOutlined, TrophyOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import { api } from "../../api/client";
import type { IndicatorScore, Scorecard } from "../../api/scorecard";
import { L } from "../../shared/i18n";
import { num } from "../../shared/labels";
import { QueryState } from "../../shared/QueryState";
import { PageHeader } from "../../shared/ui";
import { IndicatorDrawer } from "./IndicatorDrawer";
import { PillarsView } from "./PillarsView";
import { PssView } from "./PssView";
import { SdgView } from "./SdgView";
import { TierView } from "./TierView";
import { TrendView } from "./TrendView";
import "../strategy/strategy.css";
import "../../shared/score.css";
import "./scorecard.css";
import "./scorecard-accordion.css";
import { queryKeys } from "../../api/queries";

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

export default function ScorecardPage() {
  const card = useQuery({ queryKey: queryKeys.scorecard, queryFn: () => api.get<Scorecard>("/api/scorecard") });
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
          : L("لم تنقل قيم أي ربع إلى مؤشرات الجامعة بعد. تظهر الدرجات بعد أن تنقلها لجنة Tier 1 بالجامعة من «مراجعة مؤشرات الكليات».", "No transferred quarter yet."))}
        stats={c ? [
          { label: L("الدرجة الكلية", "Overall score"), value: <span className="ltr">{num(c.overall.score)}</span> },
          { label: L("الفئة النهائية", "Final tier"), value: L(`الفئة ${c.overall.tier.finalTier}`, `Tier ${c.overall.tier.finalTier}`), tone: c.overall.tier.finalTier <= 2 ? "ok" : c.overall.tier.finalTier === 3 ? "warn" : "bad" },
          { label: L("مؤشرات لها قيمة", "Reported"), value: <span className="ltr">{c.overall.reported}/{c.overall.indicators}</span> },
          { label: L("في المستوى", "On target"), value: c.overall.inLevel, tone: "ok" },
        ] : undefined}
        extra={c?.canExport && (
          <Space direction="vertical" size={4}>
            <Tooltip title={L("الملف نفسه بقيم الأرباع المعتمدة في خلاياه الزرقاء، لنقلها إلى بوابة المجلس الأعلى للجامعات.", "The same workbook filled with the approved quarterly values.")}>
              <Button type="primary" icon={<DownloadOutlined />} href="/api/scorecard/kpi.xlsx">{L("ملف المجلس KPI.xlsx", "Council workbook")}</Button>
            </Tooltip>
            {!!c.exportOmitted?.length && (
              <Tooltip title={c.exportOmitted.join("، ")}>
                <span className="sc-omitted">{L(`${c.exportOmitted.length} مؤشرًا أضيفت في المنظومة ليست في ملف المجلس`, `${c.exportOmitted.length} indicators added here are not in the council's workbook`)}</span>
              </Tooltip>
            )}
          </Space>
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
                description={L("كما في ملف الإطار: المؤشر بلا قيمة يحسب صفرًا في درجة معياره وركيزته. تعتمد الكليات قيمها في نماذج الدورة الربعية، ثم تعتمدها لجنة Tier 1 بالجامعة نهائيًا وتراجعها وتنقلها إلى مؤشرات الجامعة، فتظهر الدرجات والفئة هنا.",
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
