import { useState } from "react";
import { Button, Segmented, Table, Tag } from "antd";
import { ClusterOutlined, EditOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { Distribution, IndicatorTargets } from "../../api/tier1";
import { HBarChart } from "../../shared/charts";
import { L } from "../../shared/i18n";
import { aggregation, distributionStatus, num, withUnit } from "../../shared/labels";
import { Panel } from "../../shared/ui";
import { TargetEditor, type TargetSlot } from "./TargetEditor";

const YEARS = [2027, 2028, 2029, 2030, 2031];

/**
 * هل يحقق توزيع الكليات مستهدف الجامعة؟ الملخص مكتوب أولًا، ثم الأعمدة لكل كلية، ثم جدول الإدخال.
 * الكلية بلا مستهدف خاص يسري عليها المستهدف الموحد «لكل كلية» ويظهر بلون مختلف.
 */
export function DistributionPanel({ code }: { code: string }) {
  const [year, setYear] = useState(2027);
  const [slot, setSlot] = useState<TargetSlot | null>(null);
  const dist = useQuery({ queryKey: ["distribution", code, year], queryFn: () => api.get<Distribution>(`/api/kpi/indicators/${code}/distribution?year=${year}`) });
  const targets = useQuery({ queryKey: ["targets", code], queryFn: () => api.get<IndicatorTargets>(`/api/kpi/indicators/${code}/targets`) });
  const r = dist.data?.result;
  const t = targets.data;

  return (
    <Panel title={L("التوزيع على الكليات", "Distribution to colleges")} icon={<ClusterOutlined aria-hidden />} query={[dist, targets]}
      extra={<Segmented value={year} onChange={(v) => setYear(Number(v))} options={YEARS} />}>
      {() => (
        <>
          <div className="t1-summary">
            <Tag color={distributionStatus[r!.status].color}>{distributionStatus[r!.status].text}</Tag> {r!.summaryAr}
            <div className="t1-help">{L("قاعدة التجميع:", "Aggregation:")} {aggregation[t!.aggregation]}</div>
          </div>
          {dist.data!.shares.length === 0 ? (
            <p className="muted">{L("لا توجد كليات في الهيكل بعد. أضفها من صفحة «الجهات».", "No colleges yet. Add them from the Units page.")}</p>
          ) : (
            <>
              <HBarChart title={L(`مستهدفات الكليات ${year}`, `College targets ${year}`)} unit={t!.unit}
                reference={t!.unit.trim() === "%" && r!.universityValue !== null ? { value: r!.universityValue, label: L(`مستهدف الجامعة ${num(r!.universityValue)}%`, `University target ${num(r!.universityValue)}%`) } : undefined}
                rows={dist.data!.shares.map((s) => ({
                  key: s.unitId, label: s.name, value: s.value, tone: s.fromEachCollegeDefault ? "gold" : "teal",
                  note: s.fromEachCollegeDefault ? L("من المستهدف الموحد لكل كلية", "From the per-college target") : s.text ?? undefined,
                }))} />
              <div className="ch-legend">
                <span><i className="ch-key" style={{ borderColor: "var(--ds-chart-college)" }} />{L("مستهدف خاص بالكلية", "College's own target")}</span>
                <span><i className="ch-key" style={{ borderColor: "#c99a2e" }} />{L("المستهدف الموحد لكل كلية", "Per-college target")}</span>
              </div>
              {t!.actions.canEditColleges && (
                <details className="mt">
                  <summary>{L("إدخال مستهدفات الكليات لهذه السنة", "Enter college targets for this year")}</summary>
                  <Table size="small" pagination={false} rowKey="unitId" dataSource={dist.data!.shares} className="mt"
                    columns={[
                      { title: L("الكلية", "College"), dataIndex: "name" },
                      {
                        title: `${L("المستهدف", "Target")} ${year}`, dataIndex: "value",
                        render: (v: number | null, s) => s.fromEachCollegeDefault ? <span className="muted">{L("الموحد", "default")}: {s.text ?? withUnit(v, t!.unit)}</span> : <b className="ltr">{withUnit(v, t!.unit)}</b>,
                      },
                      {
                        title: "", key: "edit", width: 90,
                        render: (_, s) => (
                          <Button size="small" icon={<EditOutlined />} onClick={() => setSlot({
                            unitId: s.unitId, unitName: s.name, year, kind: "Target",
                            existing: t!.colleges.find((c) => c.unitId === s.unitId && c.year === year && c.kind === "Target") ?? null,
                          })}>{L("إدخال", "Enter")}</Button>
                        ),
                      },
                    ]} />
                </details>
              )}
            </>
          )}
          {slot && <TargetEditor code={code} unit={t!.unit} slot={slot} onClose={() => setSlot(null)} />}
        </>
      )}
    </Panel>
  );
}
