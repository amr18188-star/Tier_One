import { useState } from "react";
import { App, Button, Tag, Tooltip } from "antd";
import { AimOutlined, CheckOutlined, EditOutlined } from "@ant-design/icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { IndicatorTargets, Target } from "../../api/tier1";
import { TrendChart } from "../../shared/charts";
import { DecisionModal } from "../../shared/DecisionModal";
import { formatDateTime } from "../../shared/format";
import { L } from "../../shared/i18n";
import { direction, withUnit } from "../../shared/labels";
import { Panel } from "../../shared/ui";
import { TargetEditor, type TargetSlot } from "./TargetEditor";

/** مستهدفات الجامعة: الرسم من خط الأساس حتى 2031، وبطاقة لكل سنة تعدل أو تعتمد من مكانها، والمستهدف الموحد لكل كلية. */
export function TargetsPanel({ code, universityId }: { code: string; universityId: string | undefined }) {
  const qc = useQueryClient();
  const { message } = App.useApp();
  const targets = useQuery({ queryKey: ["targets", code], queryFn: () => api.get<IndicatorTargets>(`/api/kpi/indicators/${code}/targets`) });
  const [slot, setSlot] = useState<TargetSlot | null>(null);
  const [approve, setApprove] = useState<Target | null>(null);
  const t = targets.data;

  const tile = (year: number) => {
    const kind = year === 2026 ? "Baseline" : "Target";
    const existing = t?.university.find((x) => x.year === year && x.kind === kind) ?? null;
    return (
      <div key={year} className={`t1-year${kind === "Baseline" ? " baseline" : ""}${existing ? "" : " empty"}`}>
        <small>{kind === "Baseline" ? L(`خط الأساس ${year}`, `Baseline ${year}`) : year}</small>
        <b className="ltr">{existing?.value !== null && existing?.value !== undefined ? withUnit(existing.value, t!.unit) : existing?.text ?? "—"}</b>
        {existing && <Tag color={existing.status === "Approved" ? "success" : "default"}>{existing.status === "Approved" ? L("معتمد", "Approved") : L("مقترح", "Proposed")}</Tag>}
        <div>
          {t!.actions.canEditUniversity && universityId && (
            <Tooltip title={existing ? L("تعديل", "Edit") : L("إضافة", "Add")}>
              <Button size="small" type="text" icon={<EditOutlined />} aria-label={L(`تعديل مستهدف ${year}`, `Edit ${year}`)}
                onClick={() => setSlot({ unitId: universityId, unitName: L("الجامعة", "University"), year, kind, existing })} />
            </Tooltip>
          )}
          {t!.actions.canApprove && existing?.status === "Proposed" && (
            <Tooltip title={L("اعتماد", "Approve")}>
              <Button size="small" type="text" icon={<CheckOutlined />} aria-label={L(`اعتماد مستهدف ${year}`, `Approve ${year}`)} onClick={() => setApprove(existing)} />
            </Tooltip>
          )}
        </div>
      </div>
    );
  };

  return (
    <Panel title={L("خط الأساس والمستهدفات", "Baseline and targets")} icon={<AimOutlined aria-hidden />} query={targets}
      extra={t && <span className="t1-help">{L("الاتجاه:", "Direction:")} {direction[t.direction]}</span>}>
      {() => (
        <>
          <div className="t1-years">{t!.years.map(tile)}</div>
          <TrendChart title={L("مستهدفات الجامعة من خط الأساس حتى 2031", "University targets from the baseline to 2031")} unit={t!.unit}
            points={t!.years.map((y) => {
              const x = t!.university.find((u) => u.year === y);
              return { year: y, value: x?.value ?? null, kind: y === 2026 ? "baseline" as const : "target" as const };
            })} />
          <div className="t1-summary mt">
            <b>{L("المستهدف الموحد لكل كلية (ملف الكليات، 2027):", "Per-college target (colleges file, 2027):")}</b>{" "}
            {t!.eachCollege ? (t!.eachCollege.value !== null ? withUnit(t!.eachCollege.value, t!.unit) : `«${t!.eachCollege.text}»`) : L("لم يحدد.", "Not set.")}
            {t!.eachCollege?.source && <span className="t1-help"> — {t!.eachCollege.source}</span>}
            {t!.actions.canEditUniversity && universityId && (
              <Button size="small" type="link" icon={<EditOutlined />}
                onClick={() => setSlot({ unitId: universityId, unitName: L("كل كلية", "Each college"), year: 2027, kind: "EachCollege", existing: t!.eachCollege })}>
                {t!.eachCollege ? L("تعديل", "Edit") : L("تحديد", "Set")}
              </Button>
            )}
          </div>
          {t!.revisions.length > 0 && (
            <details className="mt">
              <summary>{L(`سجل تعديل المستهدفات المعتمدة وحذفها (${t!.revisions.length})`, `Changes to approved targets (${t!.revisions.length})`)}</summary>
              <ul className="t1-history">
                {t!.revisions.map((r, n) => (
                  <li key={`${r.changedAt}-${n}`}>
                    <b>{r.unitName ?? ""}{r.year ? ` · ${r.year}` : ""}</b>{" "}
                    {r.action === "Deleted"
                      ? <><Tag color="red">{L("حذف", "Deleted")}</Tag> <span className="ltr">{r.oldValue ?? r.oldText}</span></>
                      : <span className="ltr">{r.oldValue ?? r.oldText ?? "—"} → {r.newValue ?? r.newText ?? "—"}</span>}
                    {" "}— {r.reason}{r.decisionRef && <> ({r.decisionRef})</>}
                    <div className="muted"><small>{r.changedByName ?? ""} · <span className="ltr">{formatDateTime(r.changedAt)}</span></small></div>
                  </li>
                ))}
              </ul>
            </details>
          )}
          {slot && <TargetEditor code={code} unit={t!.unit} slot={slot} onClose={() => setSlot(null)} />}
          {approve && (
            <DecisionModal title={L(`اعتماد مستهدف ${approve.year}`, `Approve the ${approve.year} target`)} field="decisionRef"
              label={L("رقم محضر اللجنة العليا", "Committee minutes reference")} okText={L("اعتماد", "Approve")}
              explain={L("يصبح المستهدف معتمدًا، وأي تعديل لاحق عليه يحتاج سببًا ورقم قرار.", "The target becomes approved; later changes need a reason and decision.")}
              onClose={() => setApprove(null)}
              onSubmit={async (decisionRef) => {
                await api.post(`/api/kpi/targets/${approve.id}/approve`, { decisionRef });
                message.success(L("اعتُمد المستهدف.", "Target approved."));
                await qc.invalidateQueries({ queryKey: ["targets", code] });
              }} />
          )}
        </>
      )}
    </Panel>
  );
}
