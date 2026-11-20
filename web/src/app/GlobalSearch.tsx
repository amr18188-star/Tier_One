import { useMemo, useState } from "react";
import { AutoComplete, Input } from "antd";
import { ClusterOutlined, SearchOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { api } from "../api/client";
import type { IndicatorRow } from "../api/tier1";
import { L } from "../shared/i18n";
import { usePermissions } from "../shared/session";
import { useUnits } from "../api/queries";
import { canOpen, ROUTE_NEEDS } from "./routeAccess";

/** Global search in the top bar: an indicator by code or name, or a college by name, then straight to it. */
/** بحث موحد: مؤشر بكوده أو اسمه، أو كلية باسمها، ثم الانتقال إليه مباشرة. */
export function GlobalSearch() {
  const perms = usePermissions();
  const navigate = useNavigate();
  const [text, setText] = useState("");
  const indicators = useQuery({ queryKey: ["indicators", "all"], queryFn: () => api.get<IndicatorRow[]>("/api/kpi/indicators"), enabled: perms.can("kpi.dictionary", "V"), staleTime: 300_000 });
  const units = useUnits({ enabled: perms.can("org.units", "V"), staleTime: 300_000 });

  const opensStructure = canOpen(perms, ROUTE_NEEDS["/structure"]);
  const options = useMemo(() => {
    const t = text.trim();
    if (t.length < 2) return [];
    const low = t.toLowerCase();
    const ind = (indicators.data ?? []).filter((i) => i.code.toLowerCase().includes(low) || i.nameAr.includes(t)).slice(0, 8)
      .map((i) => ({ value: `/indicators/${i.code}`, label: <span><code>{i.code}</code> {i.nameAr}</span> }));
    // A college opens its structure page, so it is offered only to who may open that page (FE-11).
    const col = (opensStructure ? units.data ?? [] : []).filter((u) => (u.kind === "College" || u.kind === "Institute") && (u.nameAr.includes(t) || u.code.toLowerCase().includes(low))).slice(0, 5)
      .map((u) => ({ value: `/structure/${u.id}`, label: <span><ClusterOutlined aria-hidden /> {u.nameAr}</span> }));
    return [
      ...(ind.length ? [{ label: L("المؤشرات", "Indicators"), options: ind }] : []),
      ...(col.length ? [{ label: L("الكليات", "Colleges"), options: col }] : []),
    ];
  }, [text, indicators.data, units.data, opensStructure]);

  return (
    <AutoComplete className="t1-search" options={options} value={text} onChange={setText}
      onSelect={(to: string) => { setText(""); navigate(to); }} popupMatchSelectWidth={420} style={{ width: "min(420px, 46vw)" }}>
      <Input prefix={<SearchOutlined aria-hidden />} allowClear placeholder={L("ابحث بكود المؤشر أو اسمه أو اسم الكلية", "Search an indicator code or name, or a college")}
        aria-label={L("بحث", "Search")} />
    </AutoComplete>
  );
}
