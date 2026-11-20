import { useState } from "react";
import { Alert, Button, DatePicker, Descriptions, Drawer, Popover, Tag } from "antd";
import { EditOutlined, InfoCircleOutlined, PlusOutlined, PrinterOutlined, StopOutlined } from "@ant-design/icons";
import { useQuery } from "@tanstack/react-query";
import dayjs from "dayjs";
import { useParams } from "react-router-dom";
import { api } from "../../api/client";
import type { CollegeChart, Department, Member, Partner, Position, TemplateUnit } from "../../api/tier1";
import { ProgressToTarget } from "../../shared/charts";
import { formatDate } from "../../shared/format";
import { L, useLang } from "../../shared/i18n";
import { QueryState } from "../../shared/QueryState";
import { initials, PageHeader, Panel } from "../../shared/ui";
import { EndModal, MemberModal, PartnerModal } from "./MemberForms";
import { StructureDocs } from "./StructureDocs";
import { queryKeys } from "../../api/queries";

type Dialog =
  | { kind: "add"; position: Position; departmentId: string | null; occupiedBy: string | null }
  | { kind: "edit"; position: Position; member: Member }
  | { kind: "end"; member: Member }
  | { kind: "partner"; department: Department; partner: Partner | null }
  | { kind: "endPartner"; partner: Partner };

/**
 * الرسم التنظيمي لهيكل Tier 1 في كلية كما هو في أي تاريخ: العميد في القمة، والوحدات التابعة له، ثم الأقسام بممثليها
 * وشركائها. المنصب الشاغر يظهر مكانه بزر «إضافة»، والنقر على عضو يعرض بطاقته.
 */
export default function CollegeStructurePage() {
  const { id = "" } = useParams();
  const [asOf, setAsOf] = useState<string | null>(null);
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [card, setCard] = useState<{ member: Member; position: Position } | null>(null);
  const chart = useQuery({
    queryKey: queryKeys.structureCollege(id, asOf),
    queryFn: () => api.get<CollegeChart>(`/api/structure/colleges/${id}${asOf ? `?asOf=${asOf}` : ""}`),
  });
  const c = chart.data;
  const editable = !!c?.canEdit && !asOf;

  const people = (position: Position, departmentId: string | null) => {
    const holders = c!.members.filter((m) => m.positionId === position.id && (departmentId === null || m.departmentId === departmentId));
    return (
      <div key={`${position.id}-${departmentId}`}>
        {holders.map((m) => (
          <button key={m.id} type="button" className="org-person" onClick={() => setCard({ member: m, position })}>
            <span className="lcp-avatar" aria-hidden>{initials(m.fullName)}</span>
            <span><b>{m.fullName}</b><small>{position.titleAr}{m.title ? ` · ${m.title}` : ""}</small></span>
          </button>
        ))}
        {(holders.length === 0 || (!position.single && editable)) && (holders.length === 0 && position.required ? (
          <div className="org-vacant">
            <span>{position.titleAr}: {L("شاغر", "vacant")}</span>
            {editable && <Button size="small" type="link" icon={<PlusOutlined />} onClick={() => setDialog({ kind: "add", position, departmentId, occupiedBy: null })}>{L("إضافة", "Add")}</Button>}
          </div>
        ) : editable && (
          <Button size="small" type="link" icon={<PlusOutlined />} onClick={() => setDialog({ kind: "add", position, departmentId, occupiedBy: null })}>
            {position.titleAr}
          </Button>
        ))}
      </div>
    );
  };

  const unitBox = (u: TemplateUnit) => (
    <div key={u.id} className="org-box">
      <h3>
        {u.nameAr}
        <Popover title={u.nameAr} content={<ul className="lcp-list">{u.duties.map((d) => <li key={d}>{d}</li>)}</ul>}>
          <InfoCircleOutlined aria-label={L("الاختصاصات", "Duties")} className="muted" />
        </Popover>
      </h3>
      <small>{u.roleAr}</small>
      {u.positions.map((p) => people(p, null))}
    </div>
  );

  return (
    <QueryState query={chart}>
      {() => {
        const head = c!.units.find((u) => u.isHead);
        const middle = c!.units.filter((u) => !u.isHead && !u.perDepartment);
        const deptUnit = c!.units.find((u) => u.perDepartment);
        return (
          <>
            <PageHeader back={{ to: "/structure", label: L("هياكل الكليات", "College structures") }}
              title={`${L("هيكل Tier 1 —", "Tier 1 structure —")} ${c!.nameAr}`}
              subtitle={asOf ? L(`الهيكل كما كان في ${formatDate(asOf)}`, `As of ${formatDate(asOf)}`) : L("الهيكل الحالي", "Current structure")}
              extra={
                <div className="t1-sticky-actions no-print">
                  <DatePicker allowClear placeholder={L("عرض في تاريخ سابق", "View as of a date")} aria-label={L("عرض في تاريخ سابق", "View as of a date")} onChange={(d) => setAsOf(d ? d.format("YYYY-MM-DD") : null)}
                    disabledDate={(d) => d.isAfter(dayjs(), "day")} />
                  <Button icon={<PrinterOutlined />} onClick={() => window.print()}>{L("طباعة", "Print")}</Button>
                </div>
              } />

            <Panel>
              <ProgressToTarget value={c!.completeness.percent} max={100}
                label={<><b>{L("اكتمال الهيكل", "Completeness")}</b><b className="ltr">{c!.completeness.percent}%</b></>} />
              {c!.completeness.issues.length > 0 && (
                <details className="mt">
                  <summary>{L(`${c!.completeness.issues.length} نقص يحتاج استكمالًا`, `${c!.completeness.issues.length} gaps to fill`)}</summary>
                  <ul>{c!.completeness.issues.map((i) => <li key={`${i.kind}-${i.departmentId}-${i.positionId}`}>{i.messageAr}</li>)}</ul>
                </details>
              )}
              {!c!.contactsVisible && <p className="t1-help mt">{L("الهاتف والبريد يظهران لأصحاب الصلاحية فقط.", "Phones and emails are visible to authorized users only.")}</p>}
            </Panel>

            <div className="org">
              {head && <div className="org-head">{unitBox(head)}</div>}
              <div className="org-row">{middle.map(unitBox)}</div>
              {deptUnit && (
                <>
                  <h2 className="org-title">{deptUnit.nameAr}</h2>
                  {c!.departments.length === 0 ? (
                    <Alert type="info" showIcon message={L("لا توجد أقسام مسجلة لهذه الكلية. أضفها من صفحة «الجهات».", "No departments yet. Add them from the Units page.")} />
                  ) : (
                    <div className="org-row">
                      {c!.departments.map((d) => {
                        const partner = c!.partners.find((p) => p.departmentId === d.id) ?? null;
                        return (
                          <div key={d.id} className="org-box dept">
                            <h3>{d.nameAr}</h3>
                            {deptUnit.positions.map((p) => people(p, d.id))}
                            {partner ? (
                              <button type="button" className="org-partner" onClick={() => setDialog({ kind: "partner", department: d, partner })} disabled={!editable}>
                                {L("الشريك الصناعي:", "Industry partner:")} <b>{partner.nameAr}</b>{partner.coordinatorName && ` · ${partner.coordinatorName}`}
                              </button>
                            ) : (
                              <div className="org-vacant">
                                <span>{L("لا يوجد شريك صناعي", "No industry partner")}</span>
                                {editable && <Button size="small" type="link" icon={<PlusOutlined />} onClick={() => setDialog({ kind: "partner", department: d, partner: null })}>{L("إضافة", "Add")}</Button>}
                              </div>
                            )}
                          </div>
                        );
                      })}
                    </div>
                  )}
                </>
              )}
            </div>

            {!asOf && <StructureDocs collegeId={c!.collegeId} editable={!!c!.canEdit} />}

            {card && <MemberCard card={card} editable={editable} onClose={() => setCard(null)}
              onEdit={() => { setDialog({ kind: "edit", position: card.position, member: card.member }); setCard(null); }}
              onEnd={() => { setDialog({ kind: "end", member: card.member }); setCard(null); }} />}
            {dialog?.kind === "add" && <MemberModal collegeId={c!.collegeId} departmentId={dialog.departmentId} position={dialog.position} member={null}
              occupiedBy={dialog.position.single ? c!.members.find((m) => m.positionId === dialog.position.id && m.departmentId === dialog.departmentId)?.fullName ?? null : null}
              onClose={() => setDialog(null)} />}
            {dialog?.kind === "edit" && <MemberModal collegeId={c!.collegeId} departmentId={dialog.member.departmentId} position={dialog.position} member={dialog.member} occupiedBy={null} onClose={() => setDialog(null)} />}
            {dialog?.kind === "end" && <EndModal title={`${L("إنهاء عضوية", "End membership of")} ${dialog.member.fullName}`} path={`/api/structure/members/${dialog.member.id}/end`} onClose={() => setDialog(null)} />}
            {dialog?.kind === "partner" && <PartnerModal departmentId={dialog.department.id} departmentName={dialog.department.nameAr} partner={dialog.partner} onClose={() => setDialog(null)} />}
            {dialog?.kind === "endPartner" && <EndModal title={`${L("إنهاء الشراكة مع", "End partnership with")} ${dialog.partner.nameAr}`} path={`/api/structure/partners/${dialog.partner.id}/end`} onClose={() => setDialog(null)} />}
          </>
        );
      }}
    </QueryState>
  );
}

function MemberCard({ card, editable, onClose, onEdit, onEnd }: { card: { member: Member; position: Position }; editable: boolean; onClose: () => void; onEdit: () => void; onEnd: () => void }) {
  const lang = useLang();
  const m = card.member;
  return (
    <Drawer open width={420} placement={lang === "ar" ? "left" : "right"} onClose={onClose} title={m.fullName}
      extra={editable && <><Button icon={<EditOutlined />} onClick={onEdit}>{L("تعديل", "Edit")}</Button> <Button danger icon={<StopOutlined />} onClick={onEnd}>{L("إنهاء", "End")}</Button></>}>
      <Tag color="cyan">{card.position.titleAr}</Tag>
      <Descriptions column={1} size="small" className="mt" items={[
        { key: "t", label: L("الصفة", "Title"), children: m.title ?? "—" },
        ...(m.employer ? [{ key: "e", label: L("جهة العمل", "Employer"), children: m.employer }] : []),
        { key: "m", label: L("البريد", "Email"), children: m.email ? <a href={`mailto:${m.email}`} className="ltr">{m.email}</a> : "—" },
        { key: "p", label: L("الهاتف", "Phone"), children: m.phone ? <a href={`tel:${m.phone}`} className="ltr">{m.phone}</a> : "—" },
        { key: "s", label: L("بداية العضوية", "Since"), children: formatDate(m.startDate) },
        { key: "d", label: L("قرار التشكيل", "Decision"), children: m.decisionRef ?? "—" },
        ...(m.notes ? [{ key: "n", label: L("ملاحظات", "Notes"), children: m.notes }] : []),
      ]} />
    </Drawer>
  );
}
