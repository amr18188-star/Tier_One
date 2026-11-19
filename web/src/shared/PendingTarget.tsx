import { ClockCircleOutlined } from "@ant-design/icons";
import { Tooltip } from "antd";
import { withUnit } from "./labels";
import { L } from "./i18n";

/**
 * مستهدف مقترح بانتظار اعتماد اللجنة العليا (D3، ADR-0003): يظهر بجانب المستهدف المعتمد ليعرف صاحبه لماذا لم يحتسب بعد،
 * ولا يدخل أي حساب. الأداء يقاس بالمعتمد وحده.
 */
export function PendingTarget({ value, year, unit }: { value?: number | null; year?: number | null; unit: string }) {
  if (year === null || year === undefined) return null;
  const shown = value === null || value === undefined ? L("نصي", "text") : withUnit(value, unit);
  return (
    <Tooltip title={L(`مستهدف مقترح لسنة ${year} بانتظار اعتماد اللجنة العليا؛ لا يقاس عليه الأداء حتى يعتمد.`,
      `Proposed target for ${year}, awaiting the higher committee; performance is not measured against it until approved.`)}>
      <small className="muted t1-pending-target">
        {" "}<ClockCircleOutlined aria-hidden /> {L("مقترح بانتظار الاعتماد:", "Pending approval:")} <span className="ltr">{shown}</span>
      </small>
    </Tooltip>
  );
}
