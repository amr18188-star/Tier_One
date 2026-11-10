import { Button, Checkbox, Select, Tooltip } from "antd";
import { CheckSquareOutlined } from "@ant-design/icons";
import { CAPS, type CapLetter, type Catalog, type Feature, type GrantView, type ScopeCode } from "../../api/types";
import { L } from "../../shared/i18n";
import { EmptyNote } from "../../shared/ui";
import { describe, grantOf, same, setScope, toggleCap, toggleRow, toggleSpecial, type Grants } from "./grants";

const CAP_NAMES: Record<CapLetter, () => string> = {
  V: () => L("عرض", "View"),
  P: () => L("طباعة", "Print"),
  E: () => L("تعديل", "Edit"),
  A: () => L("إضافة", "Add"),
  D: () => L("حذف", "Delete"),
};

export function capName(c: CapLetter) {
  return CAP_NAMES[c]();
}

interface Props {
  catalog: Catalog;
  value: Grants;
  original?: Grants;
  compare?: Grants;
  compareName?: string;
  editable: boolean;
  /** نطاق واحد لكل منح دور؛ «effective» يعرض نطاق كل إمكانية من الصلاحيات الفعلية. */
  module: string;
  search: string;
  onChange?: (feature: string, grant: GrantView) => void;
}

/** شبكة المصفوفة: صف لكل خاصية مجمّعة بالوحدة، وأعمدة الإمكانيات الخمس والنطاق والإجراءات الخاصة. */
export function MatrixTable({ catalog, value, original, compare, compareName, editable, module, search, onChange }: Props) {
  const q = search.trim().toLowerCase();
  const visible = catalog.features.filter(
    (f) => (module === "all" || f.module === module) && (!q || f.name.toLowerCase().includes(q) || f.key.includes(q) || f.caps.some((c) => c.meaning.toLowerCase().includes(q))),
  );
  const groups = catalog.modules.map((m) => ({ module: m, features: visible.filter((f) => f.module === m.key) })).filter((g) => g.features.length > 0);

  function toggleColumn(cap: CapLetter) {
    if (!onChange) return;
    const holders = visible.filter((f) => f.caps.some((c) => c.cap === cap));
    const allOn = holders.every((f) => grantOf(value, f.key).caps.includes(cap));
    for (const f of holders) onChange(f.key, toggleCap(grantOf(value, f.key), cap, !allOn));
  }

  if (groups.length === 0) return <EmptyNote>{L("لا خاصية تطابق البحث.", "No feature matches the search.")}</EmptyNote>;

  return (
    <div className="matrix-wrap">
      <table className="matrix">
        <thead>
          <tr>
            <th className="col-feature">{L("الخاصية", "Feature")}</th>
            {CAPS.map((c) => (
              <th key={c} className="col-cap">
                {editable ? (
                  <Tooltip title={L("تبديل العمود للخصائص المعروضة", "Toggle the column for the shown features")}>
                    <button type="button" className="col-toggle" onClick={() => toggleColumn(c)}>
                      <span>{capName(c)}</span>
                      <code>{c}</code>
                    </button>
                  </Tooltip>
                ) : (
                  <span className="col-head"><span>{capName(c)}</span><code>{c}</code></span>
                )}
              </th>
            ))}
            <th className="col-scope">{L("النطاق", "Scope")}</th>
            <th>{L("إجراءات خاصة", "Special actions")}</th>
          </tr>
        </thead>
        <tbody>
          {groups.map((g) => (
            <GroupRows key={g.module.key} title={g.module.name} features={g.features} {...{ catalog, value, original, compare, compareName, editable, onChange }} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

function GroupRows({ title, features, catalog, value, original, compare, compareName, editable, onChange }: Omit<Props, "module" | "search"> & { title: string; features: Feature[] }) {
  return (
    <>
      <tr className="group-row">
        <th colSpan={8}>{title}</th>
      </tr>
      {features.map((f) => {
        const g = grantOf(value, f.key);
        const changed = original !== undefined && !same(original[f.key], value[f.key]);
        const cmp = compare ? grantOf(compare, f.key) : undefined;
        const differs = compare !== undefined && !same(compare[f.key], value[f.key]);
        return (
          <tr key={f.key} className={(changed ? "changed " : "") + (differs ? "differs" : "")}>
            <td className="col-feature">
              <div className="feature-cell">
                <div>
                  <div className="feature-name">{f.name}</div>
                  <code className="feature-code">{f.key}</code>
                  {compare && (
                    <div className="compare-line" title={compareName}>
                      {compareName}: {describe(catalog, f, cmp)}
                    </div>
                  )}
                </div>
                {editable && (
                  <Tooltip title={L("كل إمكانيات الخاصية أو لا شيء", "All capabilities of the feature, or none")}>
                    <Button size="small" type="text" icon={<CheckSquareOutlined />} onClick={() => onChange?.(f.key, toggleRow(g, f))} aria-label={L("تبديل الصف", "Toggle row")} />
                  </Tooltip>
                )}
              </div>
            </td>
            {CAPS.map((c) => {
              const def = f.caps.find((x) => x.cap === c);
              if (!def) return <td key={c} className="col-cap na">—</td>;
              const on = g.caps.includes(c);
              return (
                <td key={c} className={"col-cap" + (on ? " on" : "")}>
                  <Tooltip title={def.meaning}>
                    <span>
                      <Checkbox checked={on} disabled={!editable} onChange={(e) => onChange?.(f.key, toggleCap(g, c, e.target.checked))} aria-label={`${f.name}: ${capName(c)}`} />
                    </span>
                  </Tooltip>
                </td>
              );
            })}
            <td className="col-scope">
              {editable ? (
                <Select
                  size="small"
                  value={g.scope}
                  disabled={!g.caps}
                  onChange={(v: ScopeCode) => onChange?.(f.key, setScope(g, v))}
                  options={catalog.scopes.map((s) => ({ value: s.key, label: s.name }))}
                  popupMatchSelectWidth={false}
                  aria-label={`${f.name}: ${L("النطاق", "Scope")}`}
                />
              ) : g.caps ? (
                <span className="scope-pill">{catalog.scopes.find((s) => s.key === g.scope)?.name}</span>
              ) : (
                <span className="na">—</span>
              )}
            </td>
            <td className="col-specials">
              {f.specials.length === 0 ? (
                <span className="na">—</span>
              ) : (
                f.specials.map((s) => (
                  <label key={s.key} className={"special" + (g.specials.includes(s.key) ? " on" : "")}>
                    <Checkbox checked={g.specials.includes(s.key)} disabled={!editable} onChange={(e) => onChange?.(f.key, toggleSpecial(g, s.key, e.target.checked))} />
                    <span>{s.name}</span>
                  </label>
                ))
              )}
            </td>
          </tr>
        );
      })}
    </>
  );
}
