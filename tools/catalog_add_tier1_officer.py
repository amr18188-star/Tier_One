# -*- coding: utf-8 -*-
"""يضيف إلى كتالوج الصلاحيات دور «مسئول ملف Tier 1» وخاصية «القيم الرسمية للجامعة» (مراجعة مؤشرات الكليات ونقل القيمة).

التشغيل مرة واحدة:
    python tools/catalog_add_tier1_officer.py
الأدوار والخواص الجديدة تبذر عند تشغيل الخادم التالي (Seeder) دون مس منح الأدوار القائمة.
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATH = os.path.join(ROOT, "src", "Tier1.Application", "Access", "permission-catalog.json")

with open(PATH, encoding="utf-8") as fh:
    cat = json.load(fh)

if not any(r["code"] == "T1O" for r in cat["roles"]):
    at = next(i for i, r in enumerate(cat["roles"]) if r["code"] == "QA") + 1
    cat["roles"].insert(at, {
        "code": "T1O",
        "ar": "مسئول ملف Tier 1",
        "en": "Tier 1 file officer",
        "descriptionAr": "يراجع مؤشرات الكليات مؤشرًا مؤشرًا بحساب كل كلية وحساب الجامعة، وينقل القيمة إلى مؤشر الجامعة في كل ربع؛ "
                         "منها تحسب بطاقة الأداء والفئة وملف المجلس.",
        "isNew": True,
    })

# اطلاع واسع على ما يلزم المراجعة، دون إدخال قيم الجهات أو اعتماد نماذجها.
OFFICER = {
    "kpi.strategy": "VP:A", "kpi.dictionary": "VP:A", "kpi.targets": "VP:A", "dc.cycles": "VP:A",
    "dc.forms": "VP:A", "dc.results": "VP:A", "org.units": "V:A", "org.structure": "V:A",
}
for f in cat["features"]:
    if f["key"] in OFFICER:
        f["defaults"].setdefault("T1O", OFFICER[f["key"]])
    if f["key"] == "dc.results":
        for s in f["specials"]:
            if s["key"] == "export" and "T1O" not in s["roles"]:
                s["roles"].append("T1O")

if not any(f["key"] == "dc.university" for f in cat["features"]):
    at = next(i for i, f in enumerate(cat["features"]) if f["key"] == "dc.results") + 1
    cat["features"].insert(at, {
        "key": "dc.university",
        "module": "dc",
        "ar": "القيم الرسمية للجامعة",
        "en": "University official values",
        "caps": {
            "V": "مراجعة مؤشرات الكليات بحساب كل كلية والجامعة",
            "E": "نقل القيمة إلى مؤشر الجامعة في الربع",
        },
        "specials": [],
        "defaults": {"T1O": "VE:A", "PRES": "V:A", "HC": "V:A", "PM": "V:A", "QA": "V:A", "AUD": "V:A"},
    })

with open(PATH, "w", encoding="utf-8") as fh:
    json.dump(cat, fh, ensure_ascii=False, indent=2)
    fh.write("\n")
print("roles:", [r["code"] for r in cat["roles"]])
