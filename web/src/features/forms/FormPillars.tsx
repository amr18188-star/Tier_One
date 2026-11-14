import {
  CheckCircleOutlined, CloseCircleOutlined, ColumnWidthOutlined, ExclamationCircleOutlined, LeftOutlined, MessageOutlined, RightOutlined,
} from "@ant-design/icons";
import type { FormItem, FormSection } from "../../api/collection";
import { L, useLang } from "../../shared/i18n";
import { stdLabel, tint } from "../../shared/pillars";

/** ركيزة في النموذج: مفتاح ثابت للرابط (رمزها)، وترتيبها في الخطة للون والرقم، ومعاييرها بترتيب الخادم. */
export interface PillarGroup { key: string; code: string | null; name: string | null; index: number; sections: FormSection[] }

/** ما يمنع الإرسال في مؤشر: خطأ تحقق، أو دليل مطلوب لقيمة رقمية بلا ملف (القاعدة نفسها في مرشح «به تنبيهات»). */
export function blocks(i: FormItem): boolean {
  return i.issues.some((x) => x.severity === "Error") || (!!i.evidenceRequired && i.evidence.every((e) => e.voided) && i.value?.kind === "Number");
}

export function pillarStats(g: PillarGroup) {
  const items = g.sections.flatMap((s) => s.items);
  return {
    total: items.length,
    entered: items.filter((i) => i.value).length,
    missing: items.filter((i) => !i.value).length,
    blocking: items.filter(blocks).length,
    notes: items.filter((i) => !!i.value?.reviewNote).length,
  };
}

/** رقم الركيزة 01–06 بترتيبها في الخطة، وإلا بترتيبها في النموذج. */
export function pillarNumber(g: PillarGroup, position: number): string {
  return String((g.index >= 0 ? g.index : position) + 1).padStart(2, "0");
}

export function pillarTitle(g: PillarGroup, position: number): string {
  return g.name ? L(`الركيزة ${pillarNumber(g, position)}`, `Pillar ${pillarNumber(g, position)}`) : L("معايير بلا ركيزة", "Standards without a pillar");
}

const pillarName = (g: PillarGroup) => g.name ?? L("معايير بلا ركيزة", "Standards without a pillar");

/**
 * بطاقات الركائز الصغيرة (6 في الصف على الشاشة العريضة، 4 ثم 3 ثم 2، وشريط أفقي على الجوال): النقر يفتح الركيزة.
 * في كل بطاقة ما أدخل وما ينقص وما يمنع الإرسال، فتبقى جاهزية الإرسال ظاهرة وأنت في ركيزة واحدة.
 */
export function PillarCards({ groups, selected, onSelect }: { groups: PillarGroup[]; selected: string; onSelect: (key: string) => void }) {
  return (
    <ul className="fp-cards" aria-label={L("الركائز", "Pillars")}>
      {groups.map((g, k) => {
        const st = pillarStats(g);
        const on = g.key === selected;
        const pct = st.total ? Math.round((100 * st.entered) / st.total) : 0;
        return (
          <li key={g.key} className="fp-card" data-selected={on} style={tint(g.index)}>
            <button type="button" className="fp-card-main" aria-pressed={on} aria-controls="fp-pillar" onClick={() => onSelect(g.key)} title={pillarName(g)}>
              <span className="fp-card-num ltr">{pillarNumber(g, k)}</span>
              <span className="fp-card-name">{pillarName(g)}</span>
              <span className="fp-card-bar" aria-hidden><i style={{ width: `${pct}%` }} /></span>
              <span className="fp-card-meta">
                <span className="fp-card-count ltr">{st.entered}/{st.total}</span>
                {st.missing > 0 && <span className="fp-badge tone-warn"><ExclamationCircleOutlined aria-hidden /> {L(`ناقص ${st.missing}`, `${st.missing} missing`)}</span>}
                {st.blocking > 0 && <span className="fp-badge tone-bad"><CloseCircleOutlined aria-hidden /> {L(`يمنع ${st.blocking}`, `${st.blocking} blocking`)}</span>}
                {st.notes > 0 && <span className="fp-badge tone-bad"><MessageOutlined aria-hidden /> {L(`ملاحظات ${st.notes}`, `${st.notes} notes`)}</span>}
                {st.missing === 0 && st.blocking === 0 && <span className="fp-badge tone-ok"><CheckCircleOutlined aria-hidden /> {L("مكتملة", "Complete")}</span>}
              </span>
            </button>
          </li>
        );
      })}
    </ul>
  );
}

/**
 * رأس الركيزة المفتوحة: رقمها واسمها، وعدد مؤشراتها، والمدخل من الكل بشريط رفيع، والناقص، والملاحظات، وجاهزيتها للإرسال،
 * ورموز معاييرها للانتقال إليها. الرأس سطح السحب بين الركائز (عدا أزراره).
 */
export function PillarHeader({ group, position, total, onStandard }: {
  group: PillarGroup; position: number; total: number; onStandard: (standardCode: string) => void;
}) {
  const st = pillarStats(group);
  const pct = st.total ? Math.round((100 * st.entered) / st.total) : 0;
  const ready = st.missing === 0 && st.blocking === 0;
  return (
    <header className="fp-head">
      <div className="fp-head-title">
        <span className="fp-head-num ltr">{pillarNumber(group, position)}</span>
        <h2>{pillarName(group)}</h2>
        {total > 1 && <span className="fp-head-pos ltr" aria-label={L(`الركيزة ${position + 1} من ${total}`, `Pillar ${position + 1} of ${total}`)}>{position + 1}/{total}</span>}
      </div>
      <dl className="fp-head-stats">
        <div><dt>{L("المؤشرات", "Indicators")}</dt><dd className="ltr">{st.total}</dd></div>
        <div className="is-progress">
          <dt>{L("المُدخل", "Entered")}</dt>
          <dd><span className="ltr">{st.entered}/{st.total}</span><span className="fp-head-bar" aria-hidden><i style={{ width: `${pct}%` }} /></span></dd>
        </div>
        <div><dt>{L("ناقص", "Missing")}</dt><dd className="ltr">{st.missing}</dd></div>
        <div><dt>{L("ملاحظات المراجعة", "Review notes")}</dt><dd className="ltr">{st.notes}</dd></div>
        <div className="is-ready">
          <dt>{L("الإرسال", "Submission")}</dt>
          <dd>
            {ready
              ? <span className="fp-badge tone-ok"><CheckCircleOutlined aria-hidden /> {L("جاهزة", "Ready")}</span>
              : (
                <>
                  {st.missing > 0 && <span className="fp-badge tone-warn">{L(`${st.missing} ناقص`, `${st.missing} missing`)}</span>}
                  {st.blocking > 0 && <span className="fp-badge tone-bad">{L(`${st.blocking} يمنع الإرسال`, `${st.blocking} blocking`)}</span>}
                </>
              )}
          </dd>
        </div>
      </dl>
      <div className="fp-head-foot">
        <span className="fp-head-stds">
          {group.sections.map((s) => {
            const done = s.items.filter((i) => i.value).length;
            return (
              <button key={s.standardCode} type="button" className={`fp-std${done === s.items.length ? " is-done" : ""}`} title={s.standardName}
                onClick={() => onStandard(s.standardCode)}>
                {stdLabel(s.standardCode)} <span className="ltr">{done}/{s.items.length}</span>
              </button>
            );
          })}
        </span>
        {total > 1 && <span className="fp-head-hint" aria-hidden><ColumnWidthOutlined /> {L("اسحب يمينًا أو يسارًا للتنقل بين الركائز", "Drag left or right to switch pillars")}</span>}
      </div>
    </header>
  );
}

/** زر الركيزة السابقة أو التالية بجانب العمود، ثابت في منتصف الشاشة. التالية جهة نهاية السطر (يسارًا في العربية). */
export function PillarStep({ dir, target, position, onGo }: {
  dir: "prev" | "next"; target: PillarGroup | null; position: number; onGo: () => void;
}) {
  const lang = useLang();
  const Icon = (dir === "next") === (lang === "ar") ? LeftOutlined : RightOutlined;
  const what = dir === "next" ? L("الركيزة التالية", "Next pillar") : L("الركيزة السابقة", "Previous pillar");
  return (
    <button type="button" className={`fp-step is-${dir}`} disabled={!target} onClick={onGo}
      aria-label={target ? `${what}: ${pillarTitle(target, position)}${target.name ? ` · ${target.name}` : ""}` : what}>
      <Icon className="fp-step-icon" aria-hidden />
      {target ? (
        <>
          <b>{pillarTitle(target, position)}</b>
          {target.name && <small>{target.name}</small>}
        </>
      ) : <small>{dir === "next" ? L("آخر ركيزة", "Last pillar") : L("أول ركيزة", "First pillar")}</small>}
    </button>
  );
}

/** على الجوال: شريط صغير «‹ السابقة · 02/06 · التالية ›» فوق شريط الإجراء. */
export function PillarMobileBar({ position, total, hasPrev, hasNext, onPrev, onNext }: {
  position: number; total: number; hasPrev: boolean; hasNext: boolean; onPrev: () => void; onNext: () => void;
}) {
  const lang = useLang();
  const Back = lang === "ar" ? RightOutlined : LeftOutlined;
  const Forward = lang === "ar" ? LeftOutlined : RightOutlined;
  return (
    <nav className="fp-mbar" aria-label={L("التنقل بين الركائز", "Pillar navigation")}>
      <button type="button" disabled={!hasPrev} onClick={onPrev}><Back aria-hidden /> {L("السابقة", "Previous")}</button>
      <span className="ltr" aria-label={L(`الركيزة ${position + 1} من ${total}`, `Pillar ${position + 1} of ${total}`)}>
        {String(position + 1).padStart(2, "0")}/{String(total).padStart(2, "0")}
      </span>
      <button type="button" disabled={!hasNext} onClick={onNext}>{L("التالية", "Next")} <Forward aria-hidden /></button>
    </nav>
  );
}
