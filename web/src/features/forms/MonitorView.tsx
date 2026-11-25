import { useId, useState, type ReactNode } from "react";
import { Button, Input, Skeleton, Switch } from "antd";
import {
  BellOutlined, CheckCircleOutlined, ClockCircleOutlined, EditOutlined, InboxOutlined, RollbackOutlined, WarningOutlined,
} from "@ant-design/icons";
import type { FormRow, SubmissionStatus } from "../../api/collection";
import { L } from "../../shared/i18n";
import { EmptyNote, FilterMeta, Panel } from "../../shared/ui";
import { Fold } from "./Fold";
import { groupByCollege, matches, statusText, type CollegeGroup, type Filters } from "./model";
import { Bar, FormLink } from "./parts";

const PIPE: { status: SubmissionStatus; tone: string; icon: ReactNode }[] = [
  { status: "NotStarted", tone: "idle", icon: <InboxOutlined aria-hidden /> },
  { status: "InProgress", tone: "work", icon: <EditOutlined aria-hidden /> },
  { status: "Returned", tone: "bad", icon: <RollbackOutlined aria-hidden /> },
  { status: "InReview", tone: "review", icon: <ClockCircleOutlined aria-hidden /> },
  { status: "Approved", tone: "ok", icon: <CheckCircleOutlined aria-hidden /> },
];

/**
 * «متابعة الجهات»: ما ينتظرني في شريط صغير، ثم بطاقات المراحل (المرشح الوحيد بالحالة)، ثم «مطلوب مني فقط» والبحث،
 * ثم كلية في كل صف قابل للفتح وفيه نموذجها ثم أقسامها.
 */
export function MonitorView({ rows, loading, filters, onFilters, onReset, onShowMine, canMine, readOnly, cycleOpen, resetKey }: {
  rows: FormRow[]; loading: boolean; filters: Filters; onFilters: (patch: Partial<Filters>) => void; onReset: () => void;
  onShowMine: () => void; canMine: boolean; readOnly: boolean; cycleOpen: boolean; resetKey: string;
}) {
  const mineId = useId();
  const shown = rows.filter((r) => matches(r, filters));
  const groups = groupByCollege(rows, shown);
  const active = !!filters.view || filters.mine || !!filters.q;
  const count = (s: SubmissionStatus) => rows.filter((r) => r.status === s).length;

  // ما فتحه المستخدم أو طواه بنفسه؛ يبدأ من جديد مع كل دورة أو مرشح، فتعود قاعدة الفتح الأولى.
  const [toggled, setToggled] = useState<{ key: string; open: Record<string, boolean> }>({ key: resetKey, open: {} });
  const opened = toggled.key === resetKey ? toggled.open : {};
  const startsOpen = (g: CollegeGroup) => active || groups.length === 1 || g.returned > 0 || g.overdue > 0;
  const isOpen = (g: CollegeGroup) => opened[g.key] ?? startsOpen(g);

  return (
    <>
      {!loading && <PendingBar rows={rows} onShow={onShowMine} />}

      <nav className="f-pipe" aria-label={L("النماذج بحسب مرحلتها", "Forms by stage")}>
        {PIPE.map((p, k) => {
          const n = count(p.status);
          const on = filters.view === p.status;
          return (
            <button key={p.status} type="button" className={`f-pipe-step tone-${p.tone}${on ? " on" : ""}`} aria-pressed={on}
              onClick={() => onFilters({ view: on ? undefined : p.status })}>
              <span className="f-pipe-n ltr">{loading ? "…" : n}</span>
              <span className="f-pipe-label">{p.icon}{statusText(p.status)}</span>
              <span className="f-pipe-bar"><i style={{ width: `${(100 * n) / (rows.length || 1)}%` }} /></span>
              {k < PIPE.length - 1 && <span className="f-pipe-arrow" aria-hidden />}
            </button>
          );
        })}
      </nav>

      <Panel>
        <h2 className="sr-only">{L("النماذج بحسب الكلية", "Forms by college")}</h2>
        <div className="lcp-filters">
          {canMine && (
            <span className="fm-mine">
              <Switch id={mineId} size="small" checked={filters.mine} onChange={(v) => onFilters({ mine: v })} />
              <label htmlFor={mineId}>{L("مطلوب مني فقط", "Only waiting for me")}</label>
            </span>
          )}
          <Input.Search key={filters.q} allowClear defaultValue={filters.q} aria-label={L("بحث باسم الجهة أو الكلية", "Search by unit or college")}
            placeholder={L("اسم الجهة أو الكلية", "Unit or college name")} onSearch={(v) => onFilters({ q: v.trim() })} />
          <FilterMeta shown={shown.length} total={rows.length} noun={L("نموذج", "forms")} active={active} onReset={onReset} />
        </div>

        {loading ? <Skeleton active paragraph={{ rows: 4 }} />
          : rows.length === 0 ? <EmptyNote>{L("لا نماذج في هذه الدورة ضمن نطاقك.", "No forms in this cycle within your scope.")}</EmptyNote>
          : groups.length === 0 ? <EmptyNote>{L("لا نماذج بهذه المرشحات.", "No forms match these filters.")}</EmptyNote>
          : (
            <div className="fm-colleges">
              {groups.map((g) => {
                const open = isOpen(g);
                return (
                  <Fold key={g.key} open={open} className="is-college" onToggle={() => setToggled({ key: resetKey, open: { ...opened, [g.key]: !open } })}
                    head={<CollegeHead group={g} />}>
                    <div className={g.central ? "fm-rows" : "fm-rows is-tree"}>
                      {g.shown.map((r) => <FormLink key={r.id} row={r} cycleOpen={cycleOpen} readOnly={readOnly} withParent={g.central} />)}
                    </div>
                  </Fold>
                );
              })}
            </div>
          )}
      </Panel>
    </>
  );
}

function CollegeHead({ group: g }: { group: CollegeGroup }) {
  return (
    <>
      <span className="fm-col-name">{g.name}</span>
      <Bar value={g.approved} max={g.total} tone="ok" />
      <span className="fm-col-count"><span className="ltr">{g.approved}/{g.total}</span> {L("معتمد", "approved")}</span>
      <span className="fm-col-flags">
        {g.returned > 0 && <span className="fm-flag"><WarningOutlined aria-hidden /> {L(`${g.returned} مُعاد`, `${g.returned} returned`)}</span>}
        {g.overdue > 0 && <span className="fm-flag"><ClockCircleOutlined aria-hidden /> {L(`${g.overdue} متأخر`, `${g.overdue} overdue`)}</span>}
      </span>
    </>
  );
}

/** ما ينتظرني في هذه الدورة، بزر يعرضه: تبويب «مهامي» إن وجد، وإلا مرشح «مطلوب مني فقط». */
function PendingBar({ rows, onShow }: { rows: FormRow[]; onShow: () => void }) {
  const mine = rows.filter((r) => r.awaitingMe);
  if (mine.length === 0) return null;
  const reviews = mine.filter((r) => r.status === "InReview").length;
  // G-15: approved forms awaiting the user are reopen requests to decide, not entries.
  const reopens = mine.filter((r) => r.status === "Approved").length;
  const entries = mine.length - reviews - reopens;
  const text = reopens > 0
    ? L(`مطلوب منك ${mine.length} نموذج: ${[entries && `${entries} للإدخال`, reviews && `${reviews} للمراجعة`, `${reopens} طلب إعادة فتح`].filter(Boolean).join(" و")}`,
      `${mine.length} forms are waiting for you: ${[entries && `${entries} to enter`, reviews && `${reviews} to review`, `${reopens} reopen requests`].filter(Boolean).join(", ")}`)
    : entries === 0
    ? L(`مطلوب منك مراجعة ${reviews} نموذج`, `${reviews} forms are waiting for your review`)
    : reviews === 0
      ? L(`مطلوب منك إدخال ${entries} نموذج`, `${entries} forms are waiting for your entry`)
      : L(`مطلوب منك ${mine.length} نموذج: ${reviews} للمراجعة و${entries} للإدخال`, `${mine.length} forms are waiting for you: ${reviews} to review, ${entries} to enter`);
  return (
    <div className="fm-pending">
      <span><BellOutlined aria-hidden /> {text}</span>
      <Button type="primary" size="small" onClick={onShow}>{L("اعرضها", "Show them")}</Button>
    </div>
  );
}
