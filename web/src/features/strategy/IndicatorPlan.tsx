import { useEffect, useMemo, useState } from "react";
import { Alert, App, Button, Input, InputNumber, Select, Tag, Tooltip } from "antd";
import { AimOutlined, LineChartOutlined, SaveOutlined } from "@ant-design/icons";
import { useMutation } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { IndicatorNode, StrategyTree } from "../../api/tier1";
import { TrendChart } from "../../shared/charts";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import { horizon, withUnit } from "../../shared/labels";
import { Panel } from "../../shared/ui";
import { useRefreshPlan } from "./StrategyForms";

const INTEGER_UNITS = new Set(["عدد", "برنامج", "دولة", "سرير", "مركز"]);

/**
 * خطة مستهدف المؤشر للجامعة على سنوات الخطة: سنة البدء (مؤشر يبدأ بعد سنتين مثلًا) وسنة التحقق (قصير أو متوسط أو طويل المدى)،
 * وقيمة كل سنة، وشرح المستهدف. قبل سنة البدء لا مستهدف، وبعد سنة التحقق يستمر آخر مستهدف.
 * «تدرج تلقائي» يوزع المسافة من خط الأساس إلى قيمة سنة التحقق على السنوات بالتساوي، ثم تعدل أي سنة يدويًا.
 */
export function IndicatorPlan({ node, tree }: { node: IndicatorNode; tree: StrategyTree }) {
  const { message } = App.useApp();
  const refresh = useRefreshPlan();
  const years = tree.years;
  const first = years[0];
  const last = years[years.length - 1];
  const canEdit = tree.actions.canEditTargets;
  const canAmend = tree.actions.canApproveTargets;

  const initial = useMemo(() => Object.fromEntries(years.map((y) => [y, node.targets.find((t) => t.year === y)?.value ?? null])) as Record<number, number | null>, [node, years]);
  const [start, setStart] = useState<number | null>(node.planStartYear);
  const [end, setEnd] = useState<number | null>(node.planEndYear);
  const [note, setNote] = useState(node.planNote ?? "");
  const [values, setValues] = useState(initial);
  const [reason, setReason] = useState("");
  const [decisionRef, setDecisionRef] = useState("");
  useEffect(() => {
    setStart(node.planStartYear);
    setEnd(node.planEndYear);
    setNote(node.planNote ?? "");
    setValues(initial);
    setReason("");
    setDecisionRef("");
  }, [node, initial]);

  const s = start ?? first;
  const e = end ?? last;
  const approved = (y: number) => node.targets.find((t) => t.year === y && t.status === "Approved");
  const changedApproved = years.filter((y) => approved(y) && values[y] !== initial[y]);
  const dirty = start !== node.planStartYear || end !== node.planEndYear || note !== (node.planNote ?? "") || years.some((y) => values[y] !== initial[y]);
  const integer = INTEGER_UNITS.has(node.unit.trim());

  const save = useMutation({
    mutationFn: () => api.put(`/api/kpi/indicators/${node.code}/plan`, {
      startYear: start, endYear: end, note: note.trim() || null,
      values: years.filter((y) => values[y] !== initial[y]).map((y) => ({ year: y, value: values[y] })),
      reason: reason.trim() || null, decisionRef: decisionRef.trim() || null, version: node.version,
    }),
    onSuccess: async () => { await refresh(); message.success(L("حُفظت خطة المستهدف.", "Target plan saved.")); },
  });

  /** يوزع من خط الأساس (أو قيمة سنة البدء) إلى قيمة سنة التحقق بالتساوي على السنوات بينهما. */
  function interpolate() {
    const to = values[e];
    const from = node.baseline ?? values[s];
    if (to === null || from === null || from === undefined) {
      message.warning(L(`أدخل قيمة سنة التحقق (${e})${node.baseline === null ? ` وقيمة سنة البدء (${s})` : ""} أولًا.`, "Enter the end-year value first."));
      return;
    }
    // من خط الأساس: المسافة على السنوات من البدء حتى التحقق؛ ومن قيمة سنة البدء: على ما بعدها.
    const base = node.baseline !== null ? s - 1 : s;
    const next = { ...values };
    for (let y = s; y <= e; y++) {
      if (y === base) continue;
      const v = from + ((to - from) * (y - base)) / (e - base);
      next[y] = integer ? Math.round(v) : Math.round(v * 100) / 100;
    }
    setValues(next);
  }

  const yearOptions = (none: string) => [{ value: 0, label: none }, ...years.map((y) => ({ value: y, label: String(y) }))];

  return (
    <Panel title={L("خطة المستهدف على سنوات الخطة", "Target plan by year")} icon={<AimOutlined aria-hidden />}
      extra={<Tag color="cyan">{horizon(start, end, first, last)}</Tag>}>
      <div className="t1-plan-years-head">
        <label>
          <span>{L("يبدأ المستهدف في", "Starts in")}</span>
          <Select value={start ?? 0} disabled={!canEdit} onChange={(v) => setStart(v || null)} options={yearOptions(L(`أول الخطة (${first})`, `Plan start (${first})`))} style={{ width: 170 }} />
        </label>
        <label>
          <span>{L("ويتحقق في", "Achieved in")}</span>
          <Select value={end ?? 0} disabled={!canEdit} onChange={(v) => setEnd(v || null)} options={yearOptions(L(`نهاية الخطة (${last})`, `Plan end (${last})`))} style={{ width: 170 }} />
        </label>
        {canEdit && <Button icon={<LineChartOutlined aria-hidden />} onClick={interpolate}>{L("تدرج تلقائي", "Fill gradually")}</Button>}
      </div>

      <div className="t1-years t1-plan-years">
        <div className="t1-year baseline">
          <small>{L(`خط الأساس ${tree.baselineYear}`, `Baseline ${tree.baselineYear}`)}</small>
          <b className="ltr">{withUnit(node.baseline, node.unit)}</b>
        </div>
        {years.map((y) => {
          const before = y < s;
          const after = y > e;
          const ap = approved(y);
          const locked = !canEdit || (!!ap && !canAmend);
          return (
            <div key={y} className={`t1-year${before ? " off" : ""}${after ? " after" : ""}`}>
              <small>{y}{y === e && <> · {L("سنة التحقق", "target year")}</>}</small>
              {before ? <b className="t1-year-note">{L("لم يبدأ", "Not started")}</b> : (
                <Tooltip title={locked && ap ? L("معتمد؛ يعدله من يملك صلاحية الاعتماد بسبب ورقم قرار.", "Approved; changed only with approval rights.") : undefined}>
                  <InputNumber value={values[y]} disabled={locked} min={0} max={node.unit.trim() === "%" ? 100 : undefined} precision={integer ? 0 : undefined}
                    onChange={(v) => setValues({ ...values, [y]: v })} aria-label={L(`مستهدف ${y}`, `Target ${y}`)} className="ltr" style={{ width: "100%" }}
                    placeholder={after ? L("يستمر", "Sustained") : "—"} />
                </Tooltip>
              )}
              {ap && !before && <Tag color="success">{L("معتمد", "Approved")}</Tag>}
              {!ap && values[y] !== null && !before && <Tag>{L("مقترح", "Proposed")}</Tag>}
            </div>
          );
        })}
      </div>
      <p className="t1-help">
        {L("قبل سنة البدء لا يحكم على المؤشر («لم يبدأ مستهدفه»). السنة الفارغة بعد سنة التحقق يستمر فيها آخر مستهدف محدد. الكليات بلا مستهدف خاص تقارن بهذا المستهدف إن كان المؤشر نسبة أو متوسطًا.",
          "Before the start year the indicator is not judged. An empty year after the target year keeps the latest target.")}
      </p>

      <label className="t1-field">
        <span>{L("شرح المستهدف ومراحله", "Explanation of the target")}</span>
        <Input.TextArea rows={3} maxLength={2000} showCount value={note} disabled={!canEdit} onChange={(ev) => setNote(ev.target.value)}
          placeholder={L("لماذا هذه القيم، وما المرحلة المطلوبة كل سنة، ومن أين جاءت (قرار أو ملف Tier 1).", "Why these values and where they come from.")} />
      </label>

      {canEdit && changedApproved.length > 0 && (
        <Alert type="warning" showIcon className="mt" message={L(`عدلت مستهدفًا معتمدًا (${changedApproved.join("، ")})`, `You changed approved targets (${changedApproved.join(", ")})`)}
          description={(
            <div className="lcp-grid-2 mt">
              <Input value={reason} onChange={(ev) => setReason(ev.target.value)} placeholder={L("سبب التعديل", "Reason")} maxLength={500} />
              <Input value={decisionRef} onChange={(ev) => setDecisionRef(ev.target.value)} placeholder={L("رقم قرار اللجنة العليا", "Decision reference")} maxLength={200} />
            </div>
          )} />
      )}
      {save.error && <Alert type="error" showIcon className="mt" message={errorText(save.error)} />}
      {canEdit && (
        <div className="t1-plan-save">
          <Button type="primary" icon={<SaveOutlined aria-hidden />} disabled={!dirty} loading={save.isPending} onClick={() => save.mutate()}>{L("حفظ الخطة", "Save plan")}</Button>
          {dirty && <span className="t1-help">{L("تعديلات لم تحفظ بعد.", "Unsaved changes.")}</span>}
        </div>
      )}

      <TrendChart title={L("مسار المستهدف من خط الأساس", "Target path from the baseline")} unit={node.unit}
        points={[{ year: tree.baselineYear, value: node.baseline, kind: "baseline" as const },
          ...years.map((y) => ({ year: y, value: y < s ? null : values[y], kind: "target" as const }))]} />
      {node.eachCollege && (
        <div className="t1-summary mt">
          <b>{L("المستهدف الموحد لكل كلية:", "Per-college target:")}</b>{" "}
          {Number.isNaN(Number(node.eachCollege)) ? `«${node.eachCollege}»` : withUnit(Number(node.eachCollege), node.unit)}
        </div>
      )}
    </Panel>
  );
}
