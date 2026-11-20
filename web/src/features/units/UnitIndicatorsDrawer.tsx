import { useEffect, useMemo, useState } from "react";
import { Alert, App, Button, Checkbox, Drawer, Empty, Input, Skeleton, Switch, Tag } from "antd";
import { SaveOutlined, SearchOutlined } from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { OrgUnit, UnitIndicatorItem, UnitIndicators } from "../../api/tier1";
import { errorText } from "../../shared/format";
import { L } from "../../shared/i18n";
import "./units.css";
import { queryKeys } from "../../api/queries";

const LEVEL: Record<UnitIndicatorItem["level"], { text: string; color: string }> = {
  College: { text: "تدخله الكلية", color: "blue" },
  Department: { text: "تدخله الأقسام", color: "cyan" },
  University: { text: "رقم الجامعة كلها", color: "gold" },
};

/**
 * تكليف جهة بمؤشراتها: ما يظهر في نموذجها عند فتح الدورة التالية. الكلية والقسم يدخلان كل مؤشرات مستواهما افتراضيًا،
 * والتخصيص يحصرهما فيما كلفا به. الجهة المركزية لا تدخل إلا ما تكلف به من مؤشرات الجامعة كلها.
 */
export default function UnitIndicatorsDrawer({ unit, onClose }: { unit: OrgUnit; onClose: () => void }) {
  const qc = useQueryClient();
  const { message } = App.useApp();
  const data = useQuery({ queryKey: queryKeys.unitIndicators(unit.id), queryFn: () => api.get<UnitIndicators>(`/api/org/units/${unit.id}/indicators`) });
  const central = !["College", "Institute", "Department"].includes(unit.kind);
  const [custom, setCustom] = useState(false);
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [q, setQ] = useState("");

  useEffect(() => {
    if (!data.data) return;
    setCustom(data.data.custom || central);
    setPicked(new Set(data.data.standards.flatMap((s) => s.indicators.filter((i) => i.selected).map((i) => i.id))));
  }, [data.data, central]);

  const save = useMutation({
    mutationFn: () => api.put<UnitIndicators>(`/api/org/units/${unit.id}/indicators`, { custom, indicatorIds: custom ? [...picked] : [] }),
    onSuccess: async (d) => {
      qc.setQueryData(["unit-indicators", unit.id], d);
      await qc.invalidateQueries({ queryKey: queryKeys.units });
      message.success(L(`حفظت مؤشرات «${unit.nameAr}».`, "Saved."));
      onClose();
    },
  });

  const d = data.data;
  const editable = !!d?.canEdit && custom;
  const all = useMemo(() => d?.standards.flatMap((s) => s.indicators) ?? [], [d]);
  const free = (i: UnitIndicatorItem) => !i.takenBy;
  const term = q.trim();
  const groups = useMemo(() => (d?.standards ?? [])
    .map((s) => ({ ...s, indicators: s.indicators.filter((i) => !term || i.code.includes(term) || i.nameAr.includes(term)) }))
    .filter((s) => s.indicators.length > 0), [d, term]);
  const toggle = (ids: string[], on: boolean) => setPicked((prev) => {
    const next = new Set(prev);
    ids.forEach((id) => (on ? next.add(id) : next.delete(id)));
    return next;
  });
  const shown = custom ? picked.size : all.filter((i) => i.selected).length;

  return (
    <Drawer open width={760} onClose={onClose} destroyOnHidden
      title={<>{L("مؤشرات", "Indicators of")} {unit.nameAr} <span className="muted un-count">{shown} {L("مؤشرًا", "indicators")}</span></>}
      extra={d?.canEdit && (
        <Button type="primary" icon={<SaveOutlined />} loading={save.isPending} onClick={() => save.mutate()}>{L("حفظ", "Save")}</Button>
      )}>
      {data.isLoading && <Skeleton active />}
      {data.error && <Alert type="error" showIcon message={errorText(data.error)} />}
      {d && (
        <>
          <Alert type="info" showIcon className="mb" message={
            central ? L("الجهة المركزية تدخل ما تكلف به من مؤشرات الجامعة كلها نيابة عن الجامعة. مؤشر الجامعة تدخله جهة واحدة؛ وما لا تكلف به جهة يدخل في نموذج الجامعة.",
              "Central units enter the university-level indicators assigned to them.")
              : d.kind === "Department"
                ? (d.followsUnit
                  ? L(`القسم يتبع قائمة ${d.followsUnit}. خصص قائمته ليدخل غيرها.`, `Follows ${d.followsUnit}.`)
                  : L("القسم يدخل كل مؤشرات الأقسام ما لم تخصص قائمته أو قائمة كليته.", "Departments enter every department-level indicator unless customised."))
                : L("الكلية تدخل كل مؤشرات الكليات، وأقسامها كل مؤشرات الأقسام، ما لم تخصص. ويمكن تكليفها برقم الجامعة كلها لمؤشر (كلية الدراسات العليا لمؤشرات البحث مثلًا).",
                  "Colleges enter every college-level indicator unless customised.")
          } description={L("يسري على الدورات التي تفتح بعد الحفظ.", "Applies to cycles opened after saving.")
            + (d.openCycles > 0 ? L(" الدورة المفتوحة الآن تبقى بنماذجها كما فتحت.", " The open cycle keeps its forms.") : "")} />
          {save.error && <Alert type="error" showIcon className="mb" message={errorText(save.error)} />}

          <div className="un-bar">
            {!central && (
              <label className="lcp-switch">
                <Switch size="small" checked={custom} disabled={!d.canEdit} onChange={(v) => {
                  setCustom(v);
                  // بداية التخصيص من القائمة الحالية، فيلغي المستخدم ما لم تكلف به الجهة بدل أن يبدأ من الصفر.
                  if (v && !d.custom) setPicked(new Set(all.filter((i) => i.selected).map((i) => i.id)));
                }} />
                {L("تخصيص مؤشرات هذه الجهة", "Customise this unit's indicators")}
              </label>
            )}
            <Input allowClear prefix={<SearchOutlined />} placeholder={L("بحث بالكود أو الاسم", "Search code or name")} aria-label={L("بحث بالكود أو الاسم", "Search code or name")} value={q} onChange={(e) => setQ(e.target.value)} className="un-search" />
            {editable && (
              <span className="un-bulk">
                <Button size="small" onClick={() => toggle(groups.flatMap((g) => g.indicators.filter(free).map((i) => i.id)), true)}>{L("تحديد الظاهر", "Select shown")}</Button>
                <Button size="small" onClick={() => toggle(groups.flatMap((g) => g.indicators.map((i) => i.id)), false)}>{L("إلغاء الظاهر", "Clear shown")}</Button>
              </span>
            )}
          </div>

          {groups.length === 0 && <Empty description={L("لا مؤشرات تناسب نوع هذه الجهة", "No indicators fit this unit")} />}
          {groups.map((s) => {
            const ids = s.indicators.filter(free).map((i) => i.id);
            const on = ids.filter((id) => (custom ? picked.has(id) : s.indicators.find((i) => i.id === id)?.selected)).length;
            return (
              <section key={s.code} className="un-std">
                <header>
                  {editable
                    ? <Checkbox checked={on > 0 && on === ids.length} indeterminate={on > 0 && on < ids.length} onChange={(e) => toggle(ids, e.target.checked)} />
                    : null}
                  <b>{L("م", "S")}{s.code}</b> {s.nameAr}
                  <span className="muted">{s.pillarName}</span>
                  <span className="un-std-count">{on}/{s.indicators.length}</span>
                </header>
                {s.indicators.map((i) => {
                  const checked = custom ? picked.has(i.id) : i.selected;
                  return (
                    <label key={i.id} className={`un-row${checked ? " on" : ""}${i.takenBy ? " taken" : ""}`}>
                      <Checkbox checked={checked} disabled={!editable || !!i.takenBy} onChange={(e) => toggle([i.id], e.target.checked)} />
                      <code className="un-code">{i.code}</code>
                      <span className="un-name">{i.nameAr}</span>
                      <Tag color={LEVEL[i.level].color}>{LEVEL[i.level].text}</Tag>
                      {i.takenBy && <span className="un-taken">{L("تدخله", "Entered by")}: {i.takenBy}</span>}
                    </label>
                  );
                })}
              </section>
            );
          })}
        </>
      )}
    </Drawer>
  );
}
