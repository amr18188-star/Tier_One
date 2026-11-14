import { useMemo, useState } from "react";
import { App } from "antd";
import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import { api } from "../../api/client";
import type { Results } from "../../api/collection";
import type { StrategyTree } from "../../api/tier1";
import { DecisionModal } from "../../shared/DecisionModal";
import { L } from "../../shared/i18n";
import { QueryState } from "../../shared/QueryState";
import { usePermissions } from "../../shared/session";
import { NewIndicatorModal } from "../indicators/NewIndicatorModal";
import { Explorer } from "./Explorer";
import { allStandards, boardSearch, detailSearch, pillarNum, PlanCtx, usePlan, type Editing, type PlanContext, type ResultMap } from "./model";
import { PillarBoard } from "./PillarBoard";
import { PlanHero, PlanMap, ViewTabs, type PlanView } from "./PlanMap";
import { Roadmap } from "./Roadmap";
import { InitiativeDrawer, PillarDrawer, StandardDrawer, useRefreshPlan } from "./StrategyForms";
import "./strategy.css";

/**
 * الخطة الاستراتيجية بثلاثة عروض في التبويبات:
 * «خريطة الخطة» (الركائز ومعاييرها وحالة كل مؤشر)، و«الخط الزمني» (متى يبدأ كل مستهدف ومتى يتحقق، ومدد المبادرات)،
 * و«الهيكل والعلاقات» (?view=board، PillarBoard.tsx): معايير الركيزة أعمدة، وتحديد عنصر يبرز ما يرتبط به.
 * وصفحة تفاصيل عنصر واحد بلا تبويب (?view=explore&node=P:|S:|M:|I:، Explorer.tsx): تفتح من داخل العروض،
 * وفيها الإضافة والتعديل والحذف بحسب الصلاحية؛ وتبويبها المضيء «الهيكل والعلاقات» لأنها داخله.
 * ?node=… بلا view يفتح التفاصيل أيضًا (رابط «في الخطة» في بطاقة المؤشر)؛ والعنصر الناقص أو المجهول يحول إلى اللوحة بالاستبدال.
 * حالة المؤشر من آخر نتيجة معتمدة لمن يرى النتائج، وإلا من اكتمال خطة مستهدفه.
 */
export default function StrategyPage() {
  const perms = usePermissions();
  const tree = useQuery({ queryKey: ["strategy"], queryFn: () => api.get<StrategyTree>("/api/strategy/tree") });
  const seesResults = perms.can("dc.results", "V");
  const results = useQuery({ queryKey: ["results", undefined, undefined], queryFn: () => api.get<Results | null>("/api/results"), enabled: seesResults });
  const [params, setParams] = useSearchParams();
  const [editing, setEditing] = useState<Editing | null>(null);

  const node = params.get("node") ?? "root";
  const raw = params.get("view");
  const view: PlanView = raw === "map" || raw === "roadmap" || raw === "board" || raw === "explore" ? raw : params.get("node") ? "explore" : "map";
  // من صفحة التفاصيل يفتح تبويب الهيكل اللوحة على ركيزة العنصر وتحديده، كأول خطوة في سطر المسار.
  const setView = (v: PlanView) =>
    setParams(v === "board" && view === "explore" && tree.data ? new URLSearchParams(boardSearch(tree.data, node)) : { view: v }, { replace: false });
  // تفاصيل عنصر تضاف إلى السجل، فيرجع زر الرجوع إلى ما فتحت منه؛ وصفحة التفاصيل تصعد إلى أولها وتركز عنوانها.
  const select = (k: string) => setParams(new URLSearchParams(k === "root" ? boardSearch(tree.data!, null) : detailSearch(k)));

  const r = results.data;
  const resultMap: ResultMap = useMemo(() => new Map((r?.indicators ?? []).map((i) => [i.code, i])), [r]);
  const ctx: PlanContext | null = tree.data
    ? { tree: tree.data, results: resultMap, resultsFrom: r ? `${r.unit.name} · ${r.cycle.name}` : null, resultsYear: r?.cycle.targetYear ?? null, select, edit: setEditing }
    : null;

  return (
    <QueryState query={tree}>
      {() => (
        <PlanCtx.Provider value={ctx}>
          <PlanHero compact={view === "explore" || view === "board"} heading={view !== "explore"} />
          <ViewTabs view={view === "explore" ? "board" : view} onView={setView} />
          {view === "map" && <PlanMap />}
          {view === "roadmap" && <Roadmap />}
          {view === "board" && <PillarBoard />}
          {view === "explore" && <Explorer selected={node} />}
          <Editors editing={editing} onClose={() => setEditing(null)} />
        </PlanCtx.Provider>
      )}
    </QueryState>
  );
}

function Editors({ editing, onClose }: { editing: Editing | null; onClose: () => void }) {
  const { message } = App.useApp();
  const refresh = useRefreshPlan();
  const [, setParams] = useSearchParams();
  const { tree: t } = usePlan();
  if (!editing) return null;
  switch (editing.kind) {
    case "pillar": {
      const at = editing.pillar ? t.pillars.findIndex((p) => p.id === editing.pillar!.id) : -1;
      return <PillarDrawer pillar={editing.pillar} num={at >= 0 ? pillarNum(at) : undefined} onClose={onClose} />;
    }
    case "standard": return <StandardDrawer standard={editing.standard} pillars={t.pillars} pillarId={editing.pillarId} onClose={onClose} />;
    case "initiative": return <InitiativeDrawer initiative={editing.initiative} standardId={editing.standardId} tree={t} onClose={onClose} />;
    case "indicator": return <NewIndicatorModal standards={allStandards(t)} standard={editing.standardCode} onClose={onClose} />;
    case "delete":
      return (
        <DecisionModal title={editing.title} explain={editing.explain} field="reason" label={L("سبب الحذف", "Reason")} okText={L("حذف", "Delete")} danger onClose={onClose}
          onSubmit={async (reason) => {
            await api.post(editing.url, { reason });
            // ينتقل قبل تحديث الشجرة إلى الأب (أو لوحة الركيزة)، فلا تعرض لحظة صفحة عنصر حذف؛ وبالاستبدال فلا يرجع الرجوع إليه.
            const after = /^[PSMI]:/.test(editing.after) ? detailSearch(editing.after) : boardSearch(t, null);
            setParams(new URLSearchParams(after), { replace: true });
            await refresh();
            message.success(L("تم الحذف.", "Deleted."));
          }} />
      );
  }
}
