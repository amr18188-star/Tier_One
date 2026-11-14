import { useState } from "react";
import { Button } from "antd";
import { ArrowLeftOutlined, ArrowRightOutlined, CheckCircleOutlined, ClockCircleOutlined, InboxOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { api } from "../../api/client";
import type { Form, FormRow } from "../../api/collection";
import { formatDate } from "../../shared/format";
import { L, useLang } from "../../shared/i18n";
import { EmptyNote, Panel } from "../../shared/ui";
import { Fold } from "./Fold";
import { byPriority, dueLabel, isCollegeForm, taskAction } from "./model";
import { Bar, FormLink, StatusTag } from "./parts";

/**
 * «مهامي»: بطاقة لكل نموذج ينتظرني (المعاد للتصحيح أولًا) بإجراء واحد، وتحتها ما أرسل وينتظر المراجعة وما اعتمد، مطويين.
 */
export function TasksView({ rows, loading, periodName, cycleOpen }: { rows: FormRow[]; loading: boolean; periodName: string; cycleOpen: boolean }) {
  const tasks = rows.filter((r) => r.awaitingMe).sort(byPriority);
  const sent = rows.filter((r) => !r.awaitingMe && r.status === "InReview").sort(byPriority);
  const approved = rows.filter((r) => r.status === "Approved");
  // ما لم يرسل ولا ينتظرني: عند جهة أخرى في نطاقي، أو بقي ناقصًا حين أغلقت الدورة. يظهر فقط إن وجد.
  const unsent = rows.filter((r) => !r.awaitingMe && (r.status === "NotStarted" || r.status === "InProgress" || r.status === "Returned")).sort(byPriority);

  return (
    <>
      <Panel title={loading ? L("مطلوب منك الآن", "Waiting for you") : L(`مطلوب منك الآن (${tasks.length})`, `Waiting for you (${tasks.length})`)}
        icon={<InboxOutlined aria-hidden />} loading={loading}>
        {tasks.length === 0 ? (
          <EmptyNote><CheckCircleOutlined aria-hidden /> {L(`لا شيء ينتظرك في ${periodName}.`, `Nothing is waiting for you in ${periodName}.`)}</EmptyNote>
        ) : (
          <ul className="fm-cards">
            {tasks.map((r) => <li key={r.id}><TaskCard row={r} /></li>)}
          </ul>
        )}
      </Panel>
      {!loading && (
        <div className="fm-folds">
          <RowsFold title={L("تم إرسالها وتنتظر المراجعة", "Sent, waiting for review")} rows={sent} cycleOpen={cycleOpen} />
          <RowsFold title={L("معتمدة", "Approved")} rows={approved} cycleOpen={cycleOpen} />
          {unsent.length > 0 && (
            <RowsFold title={cycleOpen ? L("لم تُرسل بعد", "Not sent yet") : L("لم تُرسل قبل إغلاق الدورة", "Not sent before the cycle closed")}
              rows={unsent} cycleOpen={cycleOpen} />
          )}
        </div>
      )}
    </>
  );
}

function TaskCard({ row: r }: { row: FormRow }) {
  const navigate = useNavigate();
  const lang = useLang();
  const Forward = lang === "ar" ? ArrowLeftOutlined : ArrowRightOutlined;
  const returned = r.status === "Returned";
  const review = r.status === "InReview";
  const progress = review
    ? L(`مكتمل ${r.completed} من ${r.required} قيمة · ينتظر مراجعتك`, `${r.completed} of ${r.required} values complete · waiting for your review`)
    : L(`أدخلت ${r.completed} من ${r.required} قيمة`, `${r.completed} of ${r.required} values entered`);
  return (
    <article className={`fm-card${returned ? " is-returned" : ""}`}>
      <div className="fm-card-head">
        <h3>{r.unitName}</h3>
        <StatusTag row={r} />
      </div>
      <small className="fm-card-parent">{isCollegeForm(r) ? L("نموذج الكلية", "College form") : r.parentName}</small>
      {returned && <ReturnNote id={r.id} />}
      <div className="fm-card-progress">
        <span>{progress}</span>
        <Bar value={r.completed} max={r.required} label={progress} />
      </div>
      <span className={`fm-due${r.overdue ? " is-late" : ""}`}>
        <ClockCircleOutlined aria-hidden /> {dueLabel(r.dueOn)} · <span className="ltr">{formatDate(r.dueOn)}</span>
      </span>
      <Button className="fm-card-cta" type="primary" danger={returned} icon={<Forward aria-hidden />} iconPosition="end" onClick={() => navigate(`/forms/${r.id}`)}>
        {taskAction(r)}
      </Button>
    </article>
  );
}

/** ملاحظة المراجع عند الإعادة: ليست في صف القائمة، فتطلب من النموذج نفسه للبطاقات المعادة وحدها (ومفتاحها مفتاح صفحة النموذج). */
function ReturnNote({ id }: { id: string }) {
  const form = useQuery({ queryKey: ["form", id], queryFn: () => api.get<Form>(`/api/forms/${id}`), staleTime: 60_000 });
  const note = form.data?.returnNote?.trim();
  return (
    <div className="fm-note">
      <b>{L("ملاحظة المراجع:", "Reviewer's note:")}</b>{" "}
      {form.isLoading ? <span className="fm-muted">…</span>
        : note ? <span>{note}</span>
        : <span className="fm-muted">{L("أعاده المراجع للتصحيح؛ ملاحظاته على القيم داخل النموذج.", "Returned for correction; the notes are on the values inside the form.")}</span>}
    </div>
  );
}

function RowsFold({ title, rows, cycleOpen }: { title: string; rows: FormRow[]; cycleOpen: boolean }) {
  const [open, setOpen] = useState(false);
  return (
    <Fold open={open} onToggle={() => setOpen(!open)} className="is-plain"
      head={<span className="fm-fold-name">{title} <span className="ltr">({rows.length})</span></span>}>
      {rows.length === 0
        ? <p className="fm-none">{L("لا شيء هنا.", "Nothing here.")}</p>
        : <div className="fm-rows">{rows.map((r) => <FormLink key={r.id} row={r} cycleOpen={cycleOpen} withParent />)}</div>}
    </Fold>
  );
}
