# -*- coding: utf-8 -*-
"""يحوّل ملف الإطار الشامل لتقييم أداء الجامعات (KPI.xlsx، بيانات الجامعة النهائية) إلى بذرة الخادم.

التشغيل:
    python tools/import_kpi_workbook.py "D:/Tier one/موقع المجلس الاعلي/KPI.xlsx"
الناتج:
    src/Tier1.Infrastructure/Persistence/Seed/kpi-framework.json   البذرة التي يحملها الخادم (أمر --reset-kpi وأول تشغيل)
    reference/KPI.xlsx                                               نسخة القالب نفسه لتصدير القيم الربعية بشكل الملف

من الملف كما هو: الركائز الست والمعايير الاثنا عشر بأهدافها، والمؤشرات الـ92 بتعريفها ووحدتها ومصدرها ومعادلتها،
ومستهدف كل سنة (السنوات 1–4، والمستهدف النهائي هو السنة 5)، ونوعها (تراكمي/لحظي)، ونمطها، ومناظير بطاقة أداء القطاع العام،
وأهداف التنمية المستدامة وأسباب الربط (من الورقة المخفية)، و«القدرات/الأنشطة» مبادرات مربوطة بمؤشراتها.

ما ليس في الملف ويلزم البرنامج: مستوى جمع البيانات وقاعدة التجميع والبسط والمقام لكل مؤشر (COLLECT أدناه)،
فالكليات تدخل قيمها ويجمعها البرنامج، أو تدخلها جهة مركزية على مستوى الجامعة.

التصحيحات المعتمدة على بيانات الملف (CORRECTIONS) تطبق هنا وتسجل في شرح خطة المؤشر.
"""
import json
import os
import re
import shutil
import sys

from openpyxl import load_workbook

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "Tier1.Infrastructure", "Persistence", "Seed", "kpi-framework.json")
TEMPLATE = os.path.join(ROOT, "reference", "KPI.xlsx")

# السنة 1 من الخطة = 2027 … السنة 5 = 2031 (المستهدف النهائي).
YEARS = [2027, 2028, 2029, 2030, 2031]
PILLAR_CODE = {"أ": "أ", "ب": "ب", "ج": "ج", "د": "د", "ه": "هـ", "هـ": "هـ", "و": "و"}

D, C, U = "Department", "College", "University"
RATIO, SUM, DISTINCT, WAVG, CENTRAL = "RatioOfSums", "Sum", "DistinctCount", "WeightedAverage", "Central"
RESP = "عدد المستجيبين للاستبيان"
ENROLLED = "إجمالي الطلاب الملتحقين"
FACULTY = "إجمالي أعضاء هيئة التدريس"

# مستوى الإدخال، وقاعدة التجميع، والبسط، والمقام. «جامعة + نسبة» تدخلها جهة مركزية بالبسط والمقام فتحسب النسبة.
COLLECT = {
    "1.01": (D, RATIO, "الطلاب الناجحون في المقررات المسجلة (بالحد الأدنى للدرجات)", "إجمالي الطلاب المسجلين في المقررات"),
    "1.02": (C, RATIO, "الناجحون والخريجون من السنة النهائية في الوقت المحدد", "طلاب السنة النهائية المتقدمون في الوقت المحدد للدفعة"),
    "1.03": (C, RATIO, "الطلاب المشاركون في الأنشطة واللجان والحاصلون على جوائز", ENROLLED),
    "1.04": (C, RATIO, "طلاب السنة النهائية الذين أتموا التدريب الميداني اللائحي بنجاح", "طلاب السنة النهائية المستهدفون بالتدريب وفق اللائحة"),
    "1.05": (C, RATIO, "الطلاب الذين عُيّن لهم مرشد أكاديمي فعال ويتابعون بانتظام", ENROLLED),
    "1.06": (C, SUM, None, None),
    "1.07": (C, RATIO, "الطلاب المسجلون في التخصصات ذات الأولوية", ENROLLED),
    "1.08": (C, WAVG, None, RESP),
    "2.01": (U, RATIO, "الأنشطة التسويقية المكتملة", "الأنشطة التسويقية المخططة"),
    "2.02": (C, RATIO, "المسجلون هذا العام − المسجلون العام الماضي", "المسجلون العام الماضي"),
    "2.03": (C, RATIO, "الخريجون الموظفون فعليًا", "الخريجون المستجيبون للاستبيان"),
    "2.04": (C, WAVG, None, RESP),
    "3.01": (C, SUM, None, None),
    "3.02": (C, RATIO, "البرامج الأكاديمية المرتبطة والمعتمدة لسوق العمل", "إجمالي البرامج الأكاديمية"),
    "4.01": (C, SUM, None, None),
    "4.02": (D, DISTINCT, None, None),
    "4.03": (C, SUM, None, None),
    "4.04": (C, SUM, None, None),
    "4.05": (C, RATIO, "المشروعات والأبحاث الممولة من جهات خارجية", "إجمالي المشروعات والأبحاث المسجلة"),
    "4.06": (C, RATIO, "برامج الدراسات العليا الدولية المشتركة", "إجمالي برامج الدراسات العليا القائمة"),
    "4.07": (C, RATIO, "الكوادر الأكاديمية الفعالة في مشروعات بحثية مشتركة", "إجمالي الكوادر الأكاديمية والبحثية"),
    "4.08": (C, RATIO, "الأبحاث المنشورة في مجلات Q1", "إجمالي الأبحاث المنشورة"),
    "4.09": (C, SUM, None, None),
    "4.10": (C, DISTINCT, None, None),
    "4.11": (C, DISTINCT, None, None),
    "5.01": (U, RATIO, "أنشطة ومبادرات خطة التدويل المنفذة", "أنشطة خطة التدويل المخططة"),
    "5.02": (C, DISTINCT, None, None),
    "5.03": (C, DISTINCT, None, None),
    "5.04": (C, SUM, None, None),
    "5.05": (C, DISTINCT, None, None),
    "5.06": (C, SUM, None, None),
    "5.07": (C, WAVG, None, RESP),
    "6.01": (C, RATIO, "الطلاب الوافدون المقيدون", ENROLLED),
    "6.02": (C, RATIO, "الطلاب الوافدون والمغادرون في برامج التبادل", ENROLLED),
    "6.03": (C, DISTINCT, None, None),
    "6.04": (C, RATIO, "الوافدون هذا العام − الوافدون العام السابق", "الوافدون العام السابق"),
    "7.01": (C, RATIO, "أعضاء هيئة التدريس الذين استكملوا تقييم الأداء المعتمد", "أعضاء هيئة التدريس المستهدفون بالتقييم"),
    "7.02": (C, RATIO, "الطلاب المنتظمون بدوام كامل", "أعضاء هيئة التدريس بدوام كامل"),
    "7.03": (C, RATIO, "مجموع ساعات الاتصال الأسبوعية المجدولة", "أعضاء هيئة التدريس بوقت كامل"),
    "7.04": (C, RATIO, "أعضاء هيئة التدريس ذوو الحضور الموثق في مؤتمرات دولية", FACULTY),
    "7.05": (C, RATIO, "إجمالي ساعات التدريب الموثقة لأعضاء هيئة التدريس", "أعضاء هيئة التدريس بوقت كامل"),
    "7.06": (C, RATIO, "البرامج التدريبية المطورة مشتركة مع الصناعة", "إجمالي البرامج التدريبية المقدمة"),
    "7.07": (C, RATIO, "أعضاء هيئة التدريس الذين طوروا مواد تعليمية تفاعلية موثقة", FACULTY),
    "7.08": (C, RATIO, "الأبحاث المنشورة والموثقة دوليًا", "أعضاء هيئة التدريس على رأس العمل"),
    "7.09": (C, RATIO, "المستجيبون بـ«راضٍ» أو «راضٍ جدًا»", "المشاركون في استبيان أعضاء هيئة التدريس"),
    "8.01": (U, RATIO, "مؤشرات الأداء التي حققت المستهدف أو تجاوزته", "إجمالي مؤشرات الأداء المؤسسية"),
    "8.02": (U, RATIO, "طلبات الخدمة المنجزة في الإطار الزمني المعتمد", "إجمالي طلبات الخدمة المستلمة"),
    "8.03": (U, RATIO, "الكليات الحاصلة على الاعتماد المؤسسي الفعال", "إجمالي كليات الجامعة"),
    "8.04": (C, RATIO, "البرامج الأكاديمية المعتمدة", "إجمالي البرامج الأكاديمية المطروحة"),
    "8.05": (U, RATIO, "العمليات التشغيلية الموثقة والمعتمدة", "إجمالي العمليات المستهدفة"),
    "8.06": (U, RATIO, "ملاحظات التدقيق المغلقة في المدة المحددة", "إجمالي ملاحظات التدقيق الصادرة"),
    "8.07": (U, RATIO, "إجراءات ADRI المكتملة", "إجراءات ADRI المخططة"),
    "8.08": (C, RATIO, "الأقسام المستكملة للمراجعة الدورية في الموعد", "الأقسام المستحقة للمراجعة"),
    "8.09": (C, RATIO, "الطلاب الذين تلقوا التوجيه وإحاطة السياسات الموثقة", ENROLLED),
    "8.10": (U, CENTRAL, None, None),
    "9.01": (U, CENTRAL, None, None),
    "9.02": (C, SUM, None, None),
    "9.03": (U, CENTRAL, None, None),
    "9.04": (C, RATIO, "البرامج التي تتضمن تدريبًا ميدانيًا معتمدًا", "إجمالي البرامج الأكاديمية الفعالة"),
    "9.05": (C, RATIO, "المقررات الإلكترونية أو المدمجة", "إجمالي المقررات المطروحة"),
    "9.06": (C, RATIO, "مخرجات التعلم التي حققت المستوى المستهدف", "مخرجات التعلم التي قُيّمت"),
    "9.07": (C, RATIO, "ملاحظات الطلاب التي نُفذت إجراءات بشأنها", "إجمالي ملاحظات الطلاب المستلمة"),
    "9.08": (C, RATIO, "أعضاء هيئة التدريس الذين أتموا مراجعة تدريس رسمية", FACULTY),
    "9.09": (C, RATIO, "توصيات الفاحصين الخارجيين المطبقة", "إجمالي توصيات الفاحصين المستلمة"),
    "9.10": (C, WAVG, None, "عدد البرامج التي رُوجعت"),
    "9.11": (C, RATIO, "المقررات التي نُشرت معايير تقييمها قبل بدء الدراسة", "إجمالي المقررات المطروحة"),
    "9.12": (C, RATIO, "دورات التقييم المتحقق منها", "إجمالي دورات التقييم"),
    "9.13": (C, RATIO, "المقررات التي استكملت إجراءات الاعتدال والمراجعة", "إجمالي المقررات المستهدفة"),
    "10.01": (U, RATIO, "قيمة المنح الدراسية والمساعدات المالية الممنوحة", "إجمالي إيرادات الرسوم الدراسية"),
    "10.02": (U, RATIO, "الإيرادات التشغيلية − المصروفات التشغيلية", "إجمالي الإيرادات التشغيلية"),
    "10.03": (C, SUM, None, None),
    "10.04": (U, RATIO, "المصروفات الإدارية والداعمة", "إجمالي المصروفات التشغيلية"),
    "10.05": (U, RATIO, "الإيرادات غير الناتجة عن الرسوم الدراسية", "إجمالي الإيرادات"),
    "10.06": (C, SUM, None, None),
    "10.07": (C, SUM, None, None),
    "10.08": (C, RATIO, "المعامل الحاصلة على اعتماد دولي ساري", "إجمالي عدد المعامل"),
    "11.01": (U, RATIO, "المساحات التعليمية المستخدمة", "المساحات التعليمية المتاحة"),
    "11.02": (U, CENTRAL, None, None),
    "11.03": (U, CENTRAL, None, None),
    "11.04": (U, RATIO, "المباني الجيدة السليمة", "إجمالي المباني الرئيسية"),
    "11.05": (U, RATIO, "تكلفة الصيانة المؤجلة", "إجمالي قيمة الاستبدال"),
    "11.06": (U, CENTRAL, None, None),
    "11.07": (U, RATIO, "الطلاب المسكنون فعليًا في المدن الجامعية", "الطاقة الاستيعابية المتاحة (الأسرة)"),
    "11.08": (U, RATIO, "الطلاب المستجيبون بـ«راضٍ» أو «راضٍ جدًا»", "الطلاب المشاركون في استبيان الخدمات"),
    "12.01": (U, CENTRAL, None, None),
    "12.02": (U, CENTRAL, None, None),
    "12.03": (U, RATIO, "المعاملات الرقمية", "إجمالي المعاملات"),
    "12.04": (U, RATIO, "الإنفاق على تقنية المعلومات", "إجمالي الميزانية التشغيلية"),
    "12.05": (U, RATIO, "الكوادر المستكملون للتدريب على تقنية المعلومات", "إجمالي الكوادر المطلوب تدريبهم"),
    "12.06": (U, RATIO, "الطلاب ذوو وصول فعال إلى الحوسبة الافتراضية/السحابة", ENROLLED),
    "12.07": (U, RATIO, "الخدمات الجامعية المؤتمتة والمتاحة إلكترونيًا", "إجمالي الخدمات المقدمة من الجامعة"),
    "12.08": (C, RATIO, "الاختبارات المنفذة والمصححة إلكترونيًا بالكامل", "إجمالي الاختبارات المنفذة"),
}

# تصحيحات بيانات الملف المعتمدة: (الحقل، القيمة الجديدة، الشرح). تسجل في شرح خطة المؤشر.
CORRECTIONS = {
    "7.03": [("direction", "LowerIsBetter",
              "صحح النمط إلى تنازلي (الأقل أفضل): مستهدفات الملف تنخفض من 13 إلى 8 ساعات، بينما سجل النمط تصاعديًا فكان الإنجاز سيحسب خطأ.")],
    "8.08": [("target", (2029, 78),
              "صحح مستهدف السنة 3 (2029) من 70 إلى 78: مستهدفات الملف 73 ثم 76 ثم 70 ثم 80، فكان يهبط في السنة الثالثة.")],
    "11.02": [("unit", "كيلوواط ساعة (إجمالي سنوي)",
               "صححت الوحدة من «كيلوواط ساعة / م²» إلى إجمالي الكيلوواط ساعة: قيم الملف (نحو 21 مليونًا) استهلاك كلي لا معدل للمتر المربع.")],
    "7.05": [("unit", "ساعة / عضو / سنة", "صححت الوحدة من «% ساعة/عضو/سنة» إلى «ساعة / عضو / سنة»: المؤشر ساعات لا نسبة.")],
    "7.08": [("unit", "بحث / عضو", "صححت الوحدة من «% (بحث/عضو)» إلى «بحث / عضو»: المؤشر متوسط أبحاث لا نسبة.")],
}

# ملاحظات تشرح مستهدفًا خاصًا دون تغيير قيمته.
NOTES = {
    "2.02": "المستهدف صفر في كل السنوات يعني ألا يتراجع التسجيل: يحسب محققًا إذا كان النمو صفرًا أو أكثر، وغير محقق إذا تراجع.",
    "9.01": "قيمة الحالة: 1 = معتمد، 0 = غير معتمد. المطلوب الاعتماد في السنة الخامسة (مستهدفات السنوات قبلها صفر).",
}


def text(v):
    return " ".join(str(v).split()) if v is not None else None


def lines(v):
    return [ln.strip() for ln in str(v or "").split("\n") if ln.strip()]


def is_ar(s):
    return re.search(r"[\u0600-\u06FF]", s) is not None


def split_name(v):
    """الاسم بالعربية والإنجليزية من خلية فيها سطران (أو مفصولان بشرطة مائلة)، بأي ترتيب."""
    parts = lines(v)
    if len(parts) == 1 and " / " in parts[0]:
        parts = [p.strip() for p in parts[0].split(" / ") if p.strip()]
    ar = " ".join(p for p in parts if is_ar(p))
    en = " ".join(p for p in parts if not is_ar(p)) or None
    return ar, en


def sdg(v):
    m = re.search(r"\d+", str(v or ""))
    return int(m.group()) if m else None


def number(v):
    if v is None or v == "":
        return None
    return float(v)


def split_activities(v):
    items = re.split(r"[؛;،,]", str(v or ""))
    out = []
    for it in items:
        s = " ".join(it.split()).strip(" .-")
        if len(s) >= 3 and s not in out:
            out.append(s)
    return out


def main(path):
    wb = load_workbook(path, data_only=False)
    frame = wb["🏛 الإطار العام للتقييم"]
    card = wb["📊 بطاقة مؤشرات الأداء"]
    links = wb["تكامل مؤشرات الأداء"]

    # ----- الركائز والمعايير من «الإطار العام للتقييم» -----
    pillars, standards = [], []
    current = None
    for r in range(7, 19):
        letter = text(frame.cell(r, 1).value)
        if letter:
            ar, en = split_name(frame.cell(r, 2).value)
            current = PILLAR_CODE[letter]
            pillars.append({"code": current, "nameAr": ar, "nameEn": en, "sortOrder": len(pillars) + 1})
        raw = frame.cell(r, 3).value
        if not raw:
            continue
        first, *rest = lines(raw)
        m = re.match(r"المعيار\s*(\d+)\s*:\s*(.+)", first)
        standards.append({
            "code": m.group(1),
            "nameAr": m.group(2).strip(),
            "nameEn": " ".join(rest) or None,
            "description": text(frame.cell(r, 4).value),
            "pillar": current,
            "sortOrder": int(m.group(1)),
        })

    # ----- أسباب الربط من الورقة المخفية -----
    rationale = {}
    for r in range(2, links.max_row + 1):
        code = links.cell(r, 3).value
        if code is None:
            continue
        rationale[f"{float(code):.2f}"] = (text(links.cell(r, 5).value), text(links.cell(r, 6).value))

    # ----- المؤشرات من «بطاقة مؤشرات الأداء» -----
    indicators, initiatives = [], {}
    for r in range(10, card.max_row + 1):
        code = text(card.cell(r, 3).value)
        name = card.cell(r, 14).value
        if not code or not name:
            continue
        std = str(card.cell(r, 2).value)
        ar, en = split_name(name)
        kind = str(card.cell(r, 80).value or "")          # CB
        pattern = str(card.cell(r, 81).value or "")       # CC
        yearly = [number(card.cell(r, c).value) for c in range(82, 86)]  # CD..CG
        final = number(card.cell(r, 20).value)            # T
        level, agg, num, den = COLLECT[code]
        item = {
            "code": code,
            "row": r,
            "standard": std,
            "sortOrder": len(indicators) + 1,
            "nameAr": ar,
            "nameEn": en,
            "definition": text(card.cell(r, 15).value),
            "unit": text(card.cell(r, 16).value) or "%",
            "dataSource": text(card.cell(r, 17).value),
            "formula": text(card.cell(r, 18).value),
            "direction": "LowerIsBetter" if "تنازل" in pattern else "HigherIsBetter",
            "accumulation": "Cumulative" if ("تراكمي" in kind and "غير" not in kind) else "Snapshot",
            "level": level,
            "aggregation": agg,
            "numerator": num,
            "denominator": den,
            "perspectives": [i + 1 for i, col in enumerate(range(7, 14)) if str(card.cell(r, col).value or "").startswith("✔")],
            "sdgPrimary": sdg(card.cell(r, 4).value),
            "sdgSecondary": sdg(card.cell(r, 5).value),
            "sdgInduced": sdg(card.cell(r, 6).value),
            "pssRationale": rationale.get(code, (None, None))[0],
            "sdgRationale": rationale.get(code, (None, None))[1],
            "targets": [{"year": y, "value": v} for y, v in zip(YEARS, yearly + [final]) if v is not None],
            "planNote": NOTES.get(code),
            "corrections": [],
        }
        for field, value, why in CORRECTIONS.get(code, []):
            if field == "target":
                year, v = value
                for t in item["targets"]:
                    if t["year"] == year:
                        t["value"] = v
            else:
                item[field] = value
            item["corrections"].append(why)
        if item["corrections"]:
            item["planNote"] = " ".join(filter(None, [item["planNote"], *item["corrections"]]))
        indicators.append(item)

        for act in split_activities(card.cell(r, 19).value):   # S: القدرات/الأنشطة
            key = (std, act)
            initiatives.setdefault(key, []).append(code)

    seed = {
        "source": os.path.basename(path),
        "years": YEARS,
        "pillars": pillars,
        "standards": standards,
        "indicators": indicators,
        "initiatives": [{"standard": s, "nameAr": n, "indicatorCodes": codes} for (s, n), codes in initiatives.items()],
    }
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as fh:
        json.dump(seed, fh, ensure_ascii=False, indent=1)
    os.makedirs(os.path.dirname(TEMPLATE), exist_ok=True)
    if os.path.abspath(path) != os.path.abspath(TEMPLATE):
        shutil.copyfile(path, TEMPLATE)

    missing = [i["code"] for i in indicators if i["code"] not in COLLECT]
    print(f"pillars={len(pillars)} standards={len(standards)} indicators={len(indicators)} initiatives={len(seed['initiatives'])} "
          f"corrected={sum(1 for i in indicators if i['corrections'])} unmapped={missing}")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main(sys.argv[1] if len(sys.argv) > 1 else TEMPLATE)
