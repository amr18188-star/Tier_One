import { type CSSProperties } from "react";
import { Tag } from "antd";
import type { Scorecard } from "../../api/scorecard";
import { L } from "../../shared/i18n";
import { RAG_TEXT, pct } from "./scoreParts";

/** The SDG view: the indicators by sustainable development goal. */
/** ألوان أهداف التنمية المستدامة السبعة عشر بترتيبها. */
export const SDG_COLORS = ["#E5243B", "#DDA63A", "#4C9F38", "#C5192D", "#FF3A21", "#26BDE2", "#FCC30B", "#A21942", "#FD6925", "#DD1367", "#FD9D24", "#BF8B2E", "#3F7E44", "#0A97D9", "#56C02B", "#00689D", "#19486A"];

export function SdgView({ c }: { c: Scorecard }) {
  return (
    <>
      <div className="sc-sdgs">
        {c.sdgs.map((g) => (
          <article key={g.number} className={`sc-sdg${g.total === 0 ? " none" : ""}`} style={{ "--sdg": SDG_COLORS[g.number - 1] } as CSSProperties}>
            <header><span className="sc-sdg-n ltr">{g.number}</span><b>{g.nameAr}</b></header>
            <dl>
              <div><dt>{L("أساسي", "Primary")}</dt><dd>{g.primary}</dd></div>
              <div><dt>{L("ثانوي", "Secondary")}</dt><dd>{g.secondary}</dd></div>
              <div><dt>{L("أثر نهائي", "Induced")}</dt><dd>{g.induced}</dd></div>
            </dl>
            <footer>
              {g.total === 0 ? <span className="muted">{L("لا مؤشرات مرتبطة", "No linked indicators")}</span> : g.inLevelPercent === null
                ? <span className="muted">{L(`${g.total} ارتباط · لا قيم بعد`, `${g.total} links · no data`)}</span>
                : <><b className="ltr">{pct(g.inLevelPercent)}</b><Tag color={g.rag === "Strong" ? "green" : g.rag === "Developing" ? "gold" : "red"}>{g.rag && L(RAG_TEXT[g.rag].ar, RAG_TEXT[g.rag].en)}</Tag></>}
            </footer>
          </article>
        ))}
      </div>
      <p className="t1-help">{L("لكل مؤشر ثلاثة أهداف: أساسي، وثانوي، وأثر نهائي (مستحث). النسبة: المؤشرات المرتبطة التي بلغت المستهدف النهائي من مجموع الارتباطات، والحالة: قوي 85% فأكثر، قيد التطوير 70–84%، معرض للخطر دونها.",
        "Each indicator links to three SDGs.")}</p>
    </>
  );
}

// ----------------------------------------------------------------- الاتجاه
