# -*- coding: utf-8 -*-
"""يحول ملف هيكل الجامعة الرسمي (Capital-University-Structure.xlsx) إلى بذرة الجهات في الخادم.

التشغيل:
    python tools/import_structure.py "C:/Users/IT/Downloads/Capital-University-Structure - fINAL.xlsx"
الناتج:
    src/Tier1.Infrastructure/Persistence/Seed/university-structure.json   يحملها الخادم بأمر --load-structure
                                                                          (scripts/load-structure.ps1) وتبذر النسخة التجريبية منها
    reference/university-structure.xlsx                                   نسخة الملف المصدر

أوراق الملف:
    القطاعات والقيادة   الجامعة، وقطاع رئيس الجامعة، وقطاعات نواب الرئيس، وقطاع أمين الجامعة وإدارته المركزية للموارد البشرية
    الإدارات العامة     الإدارات العامة بتبعيتها (اسم القطاع أو الإدارة المركزية في عمود «التبعية المثبتة بالمصدر»)
    الكليات والمعاهد   25 كلية ومعهدًا
    الأقسام العلمية     الأقسام بكود كليتها؛ صف «لايوجد أقسام علمية» لا ينشئ قسمًا

الرموز كما في الملف (F01، F01-D01، A01...)؛ والجامعة تبقى بالرمز CU لأنه قيمة نطاق حسابات الجامعة.
يضاف ما طلبته الجامعة وليس في الملف (EXTRA): وحدة التخطيط الاستراتيجي ومركز الاتصالات وتكنولوجيا المعلومات،
جهتان تتبعان قطاع رئيس الجامعة ويطلع من يعمل فيهما على الجامعة كاملة لدورهما الرئيسي في Tier 1.
"""
import json
import os
import re
import shutil
import sys

from openpyxl import load_workbook

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "Tier1.Infrastructure", "Persistence", "Seed", "university-structure.json")
COPY = os.path.join(ROOT, "reference", "university-structure.xlsx")

CODE = re.compile(r"^[A-Z0-9][A-Z0-9-]{0,31}$")

# نوع الجهة في الملف ← نوعها في المنظومة.
KINDS = {
    "قيادة الجامعة": "Sector",
    "قطاع": "Sector",
    "جهاز إداري": "Sector",
    "إدارة مركزية": "Administration",
    "إدارة عامة": "Administration",
    "كلية": "College",
    "معهد": "Institute",
    "قسم علمي": "Department",
    "قسم علمي مساند": "Department",
}

# الإدارة المركزية للموارد البشرية تتبع قطاع أمين الجامعة (اسمها في الملف «قطاع أمين الجامعة - الإدارة المركزية...»).
PARENT_OF_LEADERSHIP = {"HR01": "L02"}

# تصحيح أخطاء كتابة ظاهرة في الملف، والأسماء التي تغيرت بقرار بعد إعداده.
NAME_FIXES = {
    "اإدارة لأمن الجامعي": "إدارة الأمن الجامعي",
    # كلية الآداب: الاسم وفق القرار الوزاري الجديد.
    "قسم المكتبات والمعلومات": "قسم علم المعلومات",
}

# تتبعان قطاع رئيس الجامعة (L01) حاليًا، بطلب الجامعة. وحدة التخطيط الاستراتيجي تعد الخطة الاستراتيجية.
EXTRA = [
    {"code": "SPU", "nameAr": "وحدة التخطيط الاستراتيجي", "kind": "Administration", "parent": "L01", "viewsWholeUniversity": True},
    {"code": "ICT", "nameAr": "مركز الاتصالات وتكنولوجيا المعلومات", "kind": "Center", "parent": "L01", "viewsWholeUniversity": True},
]


def clean(v):
    return re.sub(r"\s+", " ", str(v)).strip() if v is not None else ""


def rows(ws):
    for r in ws.iter_rows(min_row=3, values_only=True):
        if r and r[0]:
            yield [clean(x) for x in r]


def main(path):
    wb = load_workbook(path, read_only=True)
    units, by_name = [], {}

    def add(code, name, kind, parent, order):
        name = NAME_FIXES.get(name, name)
        if not CODE.match(code):
            raise SystemExit(f"رمز غير صالح: {code}")
        if any(u["code"] == code for u in units):
            raise SystemExit(f"رمز مكرر: {code}")
        units.append({"code": code, "nameAr": name, "kind": kind, "parent": parent, "sortOrder": order, "viewsWholeUniversity": False})
        by_name[name] = code

    university = None
    for i, (code, name, kind, *_) in enumerate(rows(wb["القطاعات والقيادة"])):
        if kind == "جامعة":
            university = name
            continue
        add(code, name, KINDS[kind], PARENT_OF_LEADERSHIP.get(code), i)

    for i, (code, name, _level, parent, *_) in enumerate(rows(wb["الإدارات العامة"])):
        if parent not in by_name:
            raise SystemExit(f"{code}: تبعية غير معروفة «{parent}»")
        add(code, name, "Administration", by_name[parent], i + 1)

    for i, (code, name, kind, *_) in enumerate(rows(wb["الكليات والمعاهد"])):
        add(code, name, KINDS[kind], None, 100 + i)

    colleges = {u["code"] for u in units if u["kind"] in ("College", "Institute")}
    skipped = []
    for code, college, _college_name, name, kind, *_ in rows(wb["الأقسام العلمية"]):
        if kind not in KINDS:
            skipped.append(code)
            continue
        if college not in colleges:
            raise SystemExit(f"{code}: كلية غير معروفة {college}")
        add(code, name, KINDS[kind], college, int(code.rsplit("D", 1)[-1]))

    for i, e in enumerate(EXTRA):
        units.append({**e, "sortOrder": 50 + i})

    data = {"source": os.path.basename(path), "university": university or "جامعة العاصمة", "units": units}
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
        f.write("\n")
    shutil.copyfile(path, COPY)

    count = {}
    for u in units:
        count[u["kind"]] = count.get(u["kind"], 0) + 1
    print(f"Wrote {len(units)} units to {os.path.relpath(OUT, ROOT)}: {count}; skipped (no departments): {', '.join(skipped) or '-'}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    main(sys.argv[1])
