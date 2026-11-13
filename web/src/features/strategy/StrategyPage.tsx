import { useMemo, useState } from "react";
import { App, Button } from "antd";
import { PlusOutlined } from "@ant-design/icons";
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
import { allStandards, PlanCtx, usePlan, type Editing, type PlanContext, type ResultMap, type View } from "./model";
import { PlanHero, PlanMap, ViewTabs } from "./PlanMap";
import { Roadmap } from "./Roadmap";
import { InitiativeDrawer, PillarDrawer, StandardDrawer, useRefreshPlan } from "./StrategyForms";
import "./strategy.css";

/**
 * الخطة الاستراتيجية بثلاثة عروض:
 * «خريطة الخطة» (الركائز ومعاييرها وحالة كل مؤشر)، و«الخط الزمني» (متى يبدأ كل مستهدف ومتى يتحقق، ومدد المبادرات)،
 * و«التفاصيل والتعديل» (الشجرة وشرح كل عنصر وخطة مستهدف المؤشر، والإضافة والتعديل والحذف بحسب الصلاحية).
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
  const view: View = raw === "map" || raw === "roadmap" || raw === "explore" ? raw : params.get("node") ? "explore" : "map";
  const setView = (v: View) => setParams(v === "explore" && node !== "root" ? { view: v, node } : { view: v }, { replace: false });
  const select = (k: string) => {
    setParams(k === "root" ? { view: "explore" } : { view: "explore", node: k });
    window.scrollTo({ top: 0, behavior: "smooth" });
  };

  const r = results.data;
  const resultMap: ResultMap = useMemo(() => new Map((r?.indicators ?? []).map((i) => [i.code, i])), [r]);
  const ctx: PlanContext | null = tree.data
    ? { tree: tree.data, results: resultMap, resultsFrom: r ? `${r.unit.name} · ${r.cycle.name}` : null, resultsYear: r?.cycle.targetYear ?? null, select, edit: setEditing }
    : null;

  return (
    <QueryState query={tree}>
      {() => (
        <PlanCtx.Provider value={ctx}>
          <PlanHero compact={view === "explore"} />
          <ViewTabs view={view} onView={setView} extra={tree.data!.actions.canAdd && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing({ kind: "pillar" })}>{L("ركيزة جديدة", "New pillar")}</Button>
          )} />
          {view === "map" && <PlanMap />}
          {view === "roadmap" && <Roadmap />}
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
  const [params, setParams] = useSearchParams();
  const { tree: t } = usePlan();
  if (!editing) return null;
  switch (editing.kind) {
    case "pillar": return <PillarDrawer pillar={editing.pillar} onClose={onClose} />;
    case "standard": return <StandardDrawer standard={editing.standard} pillars={t.pillars} pillarId={editing.pillarId} onClose={onClose} />;
    case "initiative": return <InitiativeDrawer initiative={editing.initiative} standardId={editing.standardId} tree={t} onClose={onClose} />;
    case "indicator": return <NewIndicatorModal standards={allStandards(t)} standard={editing.standardCode} onClose={onClose} />;
    case "delete":
      return (
        <DecisionModal title={editing.title} explain={editing.explain} field="reason" label={L("سبب الحذف", "Reason")} okText={L("حذف", "Delete")} danger onClose={onClose}
          onSubmit={async (reason) => {
            await api.post(editing.url, { reason });
            await refresh();
            const next = new URLSearchParams(params);
            next.set("view", "explore");
            if (editing.after === "root") next.delete("node"); else next.set("node", editing.after);
            setParams(next);
            message.success(L("تم الحذف.", "Deleted."));
          }} />
      );
  }
}
