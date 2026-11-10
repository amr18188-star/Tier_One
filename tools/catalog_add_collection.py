# -*- coding: utf-8 -*-
"""يضيف خصائص جمع البيانات (dc) إلى كتالوج الصلاحيات. آمن للتكرار."""
import json
import os

P = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src", "Tier1.Application", "Access", "permission-catalog.json")
cat = json.load(open(P, encoding="utf-8"))

if not any(m["key"] == "dc" for m in cat["modules"]):
    cat["modules"].insert(3, {"key": "dc", "ar": "جمع البيانات والنتائج", "en": "Data collection and results"})


def caps(**kw):
    return {k: v for k, v in kw.items()}


features = [
    {"key": "dc.cycles", "module": "dc", "ar": "دورات جمع البيانات", "en": "Collection cycles",
     "caps": caps(V="الدورات ومتابعة تقدمها", P="طباعة متابعة الدورة", E="تعديل الدورة وفتحها", A="إنشاء دورة", D="إغلاق الدورة"),
     "specials": [{"key": "reopen", "ar": "إعادة فتح نموذج معتمد بسبب", "en": "Reopen an approved form", "roles": ["PM"]}],
     "defaults": {"PRES": "VP:A", "VP": "V:A", "HC": "V:A", "PM": "VPEAD:A", "QA": "VP:A", "DEAN": "V:A", "CTC": "V:A", "DREP": "V:A", "DE": "V:A", "VIEW": "V:A", "AUD": "V:A"}},
    {"key": "dc.forms", "module": "dc", "ar": "نماذج الجهات وقيمها", "en": "Unit forms and values",
     "caps": caps(V="نماذج الجهات في نطاقه", P="طباعة النموذج", E="إدخال القيم والأدلة وإرسال النموذج"),
     "specials": [
         {"key": "review_department", "ar": "مراجعة نموذج القسم (ممثل Tier 1)", "en": "Department review", "roles": ["DREP"]},
         {"key": "review_college", "ar": "مراجعة لجنة Tier 1 بالكلية", "en": "College committee review", "roles": ["CTC"]},
         {"key": "approve_college", "ar": "اعتماد العميد", "en": "Dean approval", "roles": ["DEAN"]},
         {"key": "approve_final", "ar": "الاعتماد النهائي (الجودة المركزية)", "en": "Final approval (quality)", "roles": ["QA"]},
     ],
     "defaults": {"PRES": "VP:A", "VP": "VP:U", "HC": "V:A", "PM": "VP:A", "QA": "VP:A", "DEAN": "VP:U", "CTC": "VPE:U", "DREP": "VPE:U", "DE": "VPE:U", "VIEW": "V:U", "AUD": "V:A"}},
    {"key": "dc.results", "module": "dc", "ar": "النتائج واللوحات والتقارير", "en": "Results, dashboards and reports",
     "caps": caps(V="النتائج المعتمدة مقابل المستهدف", P="طباعة التقرير"),
     "specials": [{"key": "export", "ar": "تصدير Excel", "en": "Export to Excel", "roles": ["PRES", "PM", "QA", "AUD"]}],
     "defaults": {"PRES": "VP:A", "VP": "VP:U", "HC": "VP:A", "PM": "VP:A", "QA": "VP:A", "DEAN": "VP:U", "CTC": "VP:U", "DREP": "V:U", "DE": "V:U", "INIT": "V:A", "VIEW": "V:U", "AUD": "VP:A"}},
]
existing = {f["key"] for f in cat["features"]}
at = next(i for i, f in enumerate(cat["features"]) if f["module"] == "adm")
for f in features:
    if f["key"] not in existing:
        cat["features"].insert(at, f)
        at += 1
json.dump(cat, open(P, "w", encoding="utf-8", newline="\n"), ensure_ascii=False, indent=2)
print(len(cat["features"]), "features")
