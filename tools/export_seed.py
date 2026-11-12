# -*- coding: utf-8 -*-
"""يحوّل البيانات المرجعية (reference_data.py) إلى ملف البذرة الذي يقرؤه الخادم عند أول تشغيل.

التشغيل:
    python tools/export_seed.py
الناتج:
    src/Tier1.Infrastructure/Persistence/Seed/reference-seed.json
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import reference_data as R  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "Tier1.Infrastructure", "Persistence", "Seed", "reference-seed.json")

DIRECTION = {R.H: "HigherIsBetter", R.L: "LowerIsBetter", R.N: "Undefined", R.TGT: "Range"}
LEVEL = {"قسم": "Department", "كلية": "College", "جامعة": "University"}
YEARS = [2026, 2027, 2028, 2029, 2030, 2031]


def aggregation(text: str) -> str:
    if text.startswith(R.RATIO):
        return "RatioOfSums"
    if text.startswith(R.DISTINCT):
        return "DistinctCount"
    if text.startswith(R.SUM):
        return "Sum"
    if text.startswith(R.CENTRAL):
        return "Central"
    if text.startswith("متوسط مرجح"):
        return "WeightedAverage"
    if text.startswith("عرض حالة"):
        return "StatusOnly"
    return "Undefined"


def number(text: str):
    """المستهدف النصي الذي هو رقم صريح فقط («82» أو «30.0»)؛ غيره يبقى نصًا."""
    try:
        return float(text.replace(",", ""))
    except ValueError:
        return None


indicators = []
for i, d in enumerate(R.I):
    ct = d["college_target"]
    each = None
    if ct and ct != "–":
        n = number(ct)
        each = {"value": n, "text": None if n is not None else ct, "unitAsWritten": d["college_unit"] or None}
    indicators.append({
        "code": d["code"],
        "standard": d["std"],
        "sortOrder": i + 1,
        "nameAr": d["name"],
        "alternateName": d["name_tier1"] or None,
        "definition": d["definition"] or None,
        "numerator": d["numerator"] or None,
        "denominator": d["denominator"] or None,
        "unit": d["unit"],
        "direction": DIRECTION[d["direction"]],
        "level": LEVEL[d["level"]],
        "aggregation": aggregation(d["agg"]),
        "aggregationNote": d["agg"] or None,
        "dataSource": d["source"] or None,
        "sourcePages": d["pages"] or None,
        "conflicts": [c for c in d["conflicts"].split(";") if c],
        "university": [{"year": y, "value": v} for y, v in zip(YEARS, d["univ"]) if v is not None],
        "universityNote": d["univ_note"] or None,
        "eachCollege": each,
    })

seed = {
    "pillars": [{"code": c, "nameAr": n, "sortOrder": i + 1} for i, (c, n, _) in enumerate(R.PILLARS)],
    "standards": [{"code": c, "nameAr": n, "pillar": p or None, "sourcePages": pg, "classificationNote": note or None, "sortOrder": i + 1}
                  for i, (c, n, p, pg, note) in enumerate(R.STANDARDS)],
    "indicators": indicators,
    "conflicts": [{"code": c, "scope": [s for s in scope.split(";") if s], "type": t, "severity": {"عالية": "High", "متوسطة": "Medium", "منخفضة": "Low"}[sev],
                   "description": desc, "valueA": a or None, "valueB": b or None, "proposal": prop or None}
                  for c, scope, t, sev, desc, a, b, prop in R.CONFLICTS],
    "structure": [{"code": code, "nameAr": unit, "roleAr": role, "duties": duties, "sortOrder": i + 1}
                  for i, (unit, parent, role, duties) in enumerate(R.ORG_UNITS)
                  for code in [["DEAN", "TIER1-COMMITTEE", "ADVISORY", "MEDIA", "CAREERS", "INNOVATION", "DEPARTMENTS"][i]]],
    "positions": [{"unit": unit, "titleAr": title, "countHint": count} for unit, title, count in R.ORG_POSITIONS],
}

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    json.dump(seed, f, ensure_ascii=False, indent=1)
print(OUT, len(indicators), "indicators", len(seed["conflicts"]), "conflicts")
