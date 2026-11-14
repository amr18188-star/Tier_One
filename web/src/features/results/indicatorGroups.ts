import type { IndicatorResult, StandardResult } from "../../api/collection";

/** Keep the result standards in plan order and account for indicators whose standard is absent from the summary. */
export function groupResultIndicators(standards: StandardResult[], indicators: IndicatorResult[]) {
  const groups = new Map<string, { code: string; name: string; indicators: IndicatorResult[] }>();
  for (const standard of standards) {
    groups.set(standard.code, { code: standard.code, name: standard.name, indicators: [] });
  }
  for (const indicator of indicators) {
    if (!groups.has(indicator.standardCode)) {
      groups.set(indicator.standardCode, { code: indicator.standardCode, name: indicator.standardCode, indicators: [] });
    }
    groups.get(indicator.standardCode)!.indicators.push(indicator);
  }
  return [...groups.values()].filter((group) => group.indicators.length > 0);
}
