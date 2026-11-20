import { useMemo } from "react";
import { PartitionOutlined } from "@ant-design/icons";
import { Link, useNavigate } from "react-router-dom";
import { L } from "../../shared/i18n";
import { usePermissions } from "../../shared/session";
import { Panel } from "../../shared/ui";
import { PlanCtx, tint, type PlanContext, type ResultMap } from "./model";
import { DotLegend, StateBar } from "./PlanMap";
import "./strategy.css";
import { useLatestResults, useStrategyTree } from "../../api/queries";

/**
 * الخطة في لمحة للصفحة الأولى: كل ركيزة بلونها وعدد معاييرها ومؤشراتها، وشريط حالة مؤشراتها
 * (الأداء في آخر قياس معتمد، أو اكتمال خطة المستهدف). النقر يفتح الركيزة في صفحة الخطة.
 */
export function PlanGlance() {
  const perms = usePermissions();
  const navigate = useNavigate();
  const tree = useStrategyTree({ enabled: perms.can("kpi.strategy", "V") });
  const results = useLatestResults({ enabled: perms.can("dc.results", "V") });
  const r = results.data;
  const map: ResultMap = useMemo(() => new Map((r?.indicators ?? []).map((i) => [i.code, i])), [r]);
  const t = tree.data;
  if (!t) return null;

  const ctx: PlanContext = {
    tree: t, results: map, resultsFrom: r ? `${r.unit.name} · ${r.cycle.name}` : null, resultsYear: r?.cycle.targetYear ?? null,
    select: (k) => navigate(k === "root" ? "/strategy?view=explore" : `/strategy?view=explore&node=${k}`),
    edit: () => undefined,
  };
  const first = t.years[0];
  const last = t.years[t.years.length - 1];

  return (
    <PlanCtx.Provider value={ctx}>
      <Panel title={L(`الخطة الاستراتيجية ${first}–${last} في لمحة`, `Strategic plan ${first}–${last} at a glance`)} icon={<PartitionOutlined aria-hidden />}
        extra={<Link to="/strategy">{L("افتح الخطة", "Open the plan")}</Link>}>
        <DotLegend list={t.pillars.flatMap((p) => p.standards.flatMap((s) => s.indicators))} />
        <div className="sp-pgrid mt">
          {t.pillars.map((p, index) => {
            const list = p.standards.flatMap((s) => s.indicators);
            return (
              <button key={p.id} type="button" className="sp-pmini" style={tint(index)} onClick={() => ctx.select(`P:${p.id}`)}>
                <span className="sp-pillar-num ltr-nums">{String(index + 1).padStart(2, "0")}</span>
                <span className="sp-pmini-text">
                  <b>{p.nameAr}</b>
                  <small>{L(`${p.standards.length} معيار · ${list.length} مؤشر`, `${p.standards.length} standards · ${list.length} indicators`)}</small>
                  <StateBar list={list} />
                </span>
              </button>
            );
          })}
        </div>
      </Panel>
    </PlanCtx.Provider>
  );
}
