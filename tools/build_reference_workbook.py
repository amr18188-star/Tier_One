# -*- coding: utf-8 -*-
"""يبني ملف Excel المرجعي: قاموس المؤشرات والمستهدفات وسجل التعارضات والهيكل التنظيمي.

التشغيل:
    python tools/build_reference_workbook.py
الناتج:
    reference/قاموس_المؤشرات_Tier1_مسودة.xlsx
"""
import os
import sys
from collections import Counter

from openpyxl import Workbook
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter
from openpyxl.worksheet.datavalidation import DataValidation

sys.path.insert(0, os.path.dirname(__file__))
import reference_data as R  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "reference", "قاموس_المؤشرات_Tier1_مسودة.xlsx")

FONT = "Arial"
NAVY = "1F3864"
GOLD = "C9A227"
thin = Side(style="thin", color="BFBFBF")
BORDER = Border(left=thin, right=thin, top=thin, bottom=thin)
HEAD_FILL = PatternFill("solid", fgColor=NAVY)
HEAD_FONT = Font(name=FONT, bold=True, color="FFFFFF", size=11)
BODY_FONT = Font(name=FONT, size=10)
INPUT_FILL = PatternFill("solid", fgColor="FFF2CC")      # خلايا يملؤها المراجع
MISSING_FILL = PatternFill("solid", fgColor="F2F2F2")    # لا توجد قيمة في المصدر
SEV_FILL = {"عالية": "F8CBAD", "متوسطة": "FFE699", "منخفضة": "E2EFDA"}
WRAP = Alignment(wrap_text=True, vertical="top", horizontal="right", readingOrder=2)
CENTER = Alignment(wrap_text=True, vertical="top", horizontal="center", readingOrder=2)
YEARS = ["خط أساس 2026", "2027", "2028", "2029", "2030", "2031"]
APPROVER = "اللجنة العليا لـ Tier 1"


def sheet(wb, title, headers, widths, first=False):
    ws = wb.active if first else wb.create_sheet()
    ws.title = title
    ws.sheet_view.rightToLeft = True
    ws.append(headers)
    for i, w in enumerate(widths, 1):
        ws.column_dimensions[get_column_letter(i)].width = w
        c = ws.cell(row=1, column=i)
        c.font, c.fill, c.alignment, c.border = HEAD_FONT, HEAD_FILL, CENTER, BORDER
    ws.row_dimensions[1].height = 34
    ws.freeze_panes = "A2"
    return ws


def style_rows(ws, start=2, center_cols=()):
    for row in ws.iter_rows(min_row=start, max_row=ws.max_row):
        for c in row:
            c.font, c.border = BODY_FONT, BORDER
            c.alignment = CENTER if c.column in center_cols else WRAP


def num_format(v):
    if isinstance(v, (int, float)) and abs(v) >= 1000:
        return "#,##0" if float(v).is_integer() else "#,##0.00"
    return "General"


def finish(ws):
    ws.auto_filter.ref = ws.dimensions


std_name = {s[0]: s[1] for s in R.STANDARDS}
std_pillar = {s[0]: s[2] for s in R.STANDARDS}
pillar_name = {p[0]: p[1] for p in R.PILLARS}

wb = Workbook()

# ------------------------------------------------------------------ اقرأني
ws = sheet(wb, "اقرأني", ["البند", "التوضيح"], [28, 110], first=True)
readme = [
    ("الحالة", "مسودة للمراجعة — ليست بيانات معتمدة. لا تُستخدم أرقامها رسميًا قبل اعتماد القاموس وحسم التعارضات."),
    ("المصادر", "ملف 'مؤشرات الكليات.pdf' (يُشار إليه: كليات ص<n>) وملف 'TEIR 1.pdf' (يُشار إليه: Tier1 ص<n>). الملفان صور ممسوحة، والأرقام منقولة يدويًا منها."),
    ("قاعدة النقل", "كل قيمة منقولة كما وردت. أي اختلاف أو غموض أو رقم تالف مسجل في 'سجل التعارضات' ولم يُصحَّح تلقائيًا."),
    ("المرجعية", "مستهدفات الجامعة من ملف Tier 1، ومستهدفات الكليات من ملف الكليات وتُعامل توزيعًا لها. تحسم التعارضات اللجنة العليا لـ Tier 1 (قرار 2026-11-10)."),
    ("النسب", "مكتوبة كأرقام: 82 تعني 82%. الوحدة في عمود مستقل."),
    ("الخلايا الرمادية", "لا توجد قيمة لهذه السنة في المصدر."),
    ("الخلايا الصفراء", "يملؤها فريق المراجعة: الجهة المالكة، الأدلة المطلوبة، القرار في سجل التعارضات، وبيانات أعضاء الهيكل التنظيمي."),
    ("الأعمدة 'المقترحة'", "البسط والمقام ومستوى الجمع وقاعدة التجميع اقتراح مبني على التعريف، ويحتاج اعتمادًا."),
    ("الأكواد", "كود المؤشر ثابت (مثل S04-08) ويُستخدم في النظام وفي قوالب Excel. لا يُعاد استخدام كود حُذف مؤشره."),
    ("ما بعد المراجعة", "يُستورد هذا الملف بعد اعتماده إلى المنظومة كبيانات مرجعية أولى، ويُحفظ مع رقم الإصدار وتاريخ الاعتماد."),
]
for r in readme:
    ws.append(r)
style_rows(ws)
for row in ws.iter_rows(min_row=2, max_col=1):
    row[0].font = Font(name=FONT, bold=True, size=10)

# ------------------------------------------------------------------ ملخص (بالمعادلات)
ws_sum = sheet(wb, "ملخص", ["كود المعيار", "المعيار", "عدد المؤشرات",
                            "مؤشرات لها مستهدف جامعة 2031", "مؤشرات لها مستهدف كليات 2027",
                            "تعارضات عالية", "تعارضات متوسطة", "تعارضات منخفضة"],
               [12, 46, 13, 18, 18, 13, 13, 13])
# أسماء الأوراق ثابتة ومذكورة هنا لتستخدمها المعادلات
DICT, UNIV, COLL, CONF = "قاموس المؤشرات", "مستهدفات الجامعة", "مستهدفات الكليات 2027", "سجل التعارضات"
n_ind, n_conf = len(R.I), len(R.CONFLICTS)
for i, (code, name, *_rest) in enumerate(R.STANDARDS, start=2):
    a = f"$A{i}"
    ws_sum.append([
        code, name,
        f"=COUNTIF('{DICT}'!$B$2:$B${n_ind + 1},{a})",
        f"=COUNTIFS('{UNIV}'!$B$2:$B${n_ind + 1},{a},'{UNIV}'!$J$2:$J${n_ind + 1},\"<>\")",
        f"=COUNTIFS('{COLL}'!$B$2:$B${n_ind + 1},{a},'{COLL}'!$D$2:$D${n_ind + 1},\"<>\")",
        f"=COUNTIFS('{CONF}'!$B$2:$B${n_conf + 1},\"*\"&{a}&\"*\",'{CONF}'!$D$2:$D${n_conf + 1},\"عالية\")",
        f"=COUNTIFS('{CONF}'!$B$2:$B${n_conf + 1},\"*\"&{a}&\"*\",'{CONF}'!$D$2:$D${n_conf + 1},\"متوسطة\")",
        f"=COUNTIFS('{CONF}'!$B$2:$B${n_conf + 1},\"*\"&{a}&\"*\",'{CONF}'!$D$2:$D${n_conf + 1},\"منخفضة\")",
    ])
last = ws_sum.max_row
ws_sum.append(["الإجمالي", "",
               f"=SUM(C2:C{last})", f"=SUM(D2:D{last})", f"=SUM(E2:E{last})",
               f"=COUNTIF('{CONF}'!$D$2:$D${n_conf + 1},\"عالية\")",
               f"=COUNTIF('{CONF}'!$D$2:$D${n_conf + 1},\"متوسطة\")",
               f"=COUNTIF('{CONF}'!$D$2:$D${n_conf + 1},\"منخفضة\")"])
style_rows(ws_sum, center_cols=(1, 3, 4, 5, 6, 7, 8))
for c in ws_sum[ws_sum.max_row]:
    c.font = Font(name=FONT, bold=True, size=10)
ws_sum.cell(row=ws_sum.max_row + 2, column=1,
            value="ملاحظة: تعارضات المعيار تُحسب من كل تعارض يذكر كود المعيار أو أحد مؤشراته؛ "
                  "التعارض الواحد قد يُحسب لأكثر من معيار، لذا صف الإجمالي يعدّ التعارضات مرة واحدة.").font = Font(name=FONT, italic=True, size=9)

# ------------------------------------------------------------------ الركائز والمعايير
ws = sheet(wb, "الركائز والمعايير",
           ["كود الركيزة", "الركيزة (ملف الكليات)", "كود المعيار", "المعيار (ملف Tier 1)",
            "صفحات Tier 1", "ملاحظة التصنيف"], [12, 36, 12, 48, 12, 60])
for code, name, p, pages, note in R.STANDARDS:
    ws.append([p or "—", pillar_name.get(p, "غير محدد"), code, name, pages, note])
style_rows(ws, center_cols=(1, 3, 5))
finish(ws)

# ------------------------------------------------------------------ قاموس المؤشرات
dict_headers = ["كود المؤشر", "كود المعيار", "المعيار", "اسم المؤشر", "الاسم في ملف Tier 1 (إن اختلف)",
                "التعريف", "البسط (مقترح)", "المقام (مقترح)", "الوحدة", "الاتجاه", "الدورية",
                "مستوى الجمع (مقترح)", "قاعدة التجميع (مقترحة)", "مصدر البيانات",
                "الجهة المالكة", "الأدلة المطلوبة", "مرجع الصفحات", "تعارضات مرتبطة", "حالة الاعتماد"]
ws = sheet(wb, DICT, dict_headers,
           [10, 9, 26, 34, 26, 55, 26, 26, 12, 12, 9, 12, 28, 34, 18, 22, 22, 14, 12])
for d in R.I:
    ws.append([d["code"], d["std"], std_name[d["std"]], d["name"], d["name_tier1"],
               d["definition"] or "— لا يوجد تعريف في المصدر —", d["numerator"], d["denominator"],
               d["unit"], d["direction"], d["frequency"], d["level"], d["agg"], d["source"],
               "", "", d["pages"], d["conflicts"], "مسودة"])
style_rows(ws, center_cols=(1, 2, 9, 10, 11, 12, 18, 19))
for r in range(2, ws.max_row + 1):
    for col in (15, 16):
        ws.cell(row=r, column=col).fill = INPUT_FILL
    if ws.cell(row=r, column=6).value.startswith("—"):
        ws.cell(row=r, column=6).fill = MISSING_FILL
dv = DataValidation(type="list", formula1='"مسودة,قيد المراجعة,معتمد,موقوف"', allow_blank=False)
ws.add_data_validation(dv)
dv.add(f"S2:S{ws.max_row}")
dv2 = DataValidation(type="list", formula1='"الأعلى أفضل,الأقل أفضل,نطاق مطلوب,غير محدد"')
ws.add_data_validation(dv2)
dv2.add(f"J2:J{ws.max_row}")
finish(ws)

# ------------------------------------------------------------------ مستهدفات الجامعة
ws = sheet(wb, UNIV, ["كود المؤشر", "كود المعيار", "اسم المؤشر", "الوحدة"] + YEARS + ["ملاحظات المصدر", "تعارضات"],
           [10, 9, 40, 12, 13, 12, 12, 12, 12, 12, 60, 14])
for d in R.I:
    ws.append([d["code"], d["std"], d["name"], d["unit"], *d["univ"], d["univ_note"], d["conflicts"]])
style_rows(ws, center_cols=(1, 2, 4, 5, 6, 7, 8, 9, 10, 12))
for r in range(2, ws.max_row + 1):
    for col in range(5, 11):
        c = ws.cell(row=r, column=col)
        if c.value is None:
            c.fill = MISSING_FILL
        else:
            c.number_format = num_format(c.value)
finish(ws)

# ------------------------------------------------------------------ مستهدفات الكليات
ws = sheet(wb, COLL, ["كود المؤشر", "كود المعيار", "اسم المؤشر", "المستهدف كما ورد (نهاية 2027)",
                      "الوحدة كما وردت", "الوحدة في القاموس", "تعارضات"],
           [10, 9, 40, 42, 16, 14, 14])
for d in R.I:
    ws.append([d["code"], d["std"], d["name"], d["college_target"] or None,
               d["college_unit"] or None, d["unit"], d["conflicts"]])
style_rows(ws, center_cols=(1, 2, 5, 6, 7))
for r in range(2, ws.max_row + 1):
    c = ws.cell(row=r, column=4)
    if c.value is None:
        c.fill = MISSING_FILL
finish(ws)

# ------------------------------------------------------------------ سجل التعارضات
ws = sheet(wb, CONF, ["رقم التعارض", "المؤشرات / المعايير", "نوع المشكلة", "الخطورة", "الوصف",
                      "القيمة أ ومصدرها", "القيمة ب ومصدرها", "المعالجة المقترحة",
                      "القرار المعتمد", "الجهة المعتمدة", "تاريخ القرار", "رقم المستند الداعم", "الحالة"],
           [11, 22, 20, 10, 48, 40, 40, 40, 30, 18, 13, 16, 12])
for cid, inds, typ, sev, desc, a, b, prop in R.CONFLICTS:
    ws.append([cid, inds, typ, sev, desc, a, b, prop, "", APPROVER, "", "", "مفتوح"])
style_rows(ws, center_cols=(1, 3, 4, 11, 13))
for r in range(2, ws.max_row + 1):
    sev = ws.cell(row=r, column=4)
    sev.fill = PatternFill("solid", fgColor=SEV_FILL[sev.value])
    for col in (9, 10, 11, 12):
        ws.cell(row=r, column=col).fill = INPUT_FILL
dv3 = DataValidation(type="list", formula1='"مفتوح,قيد المعالجة,محسوم,مؤجل"')
ws.add_data_validation(dv3)
dv3.add(f"M2:M{ws.max_row}")
finish(ws)

# ------------------------------------------------------------------ الهيكل التنظيمي
ws = sheet(wb, "الهيكل التنظيمي للكلية",
           ["الوحدة التنظيمية", "تتبع", "الدور", "المهام والاختصاصات"], [36, 26, 40, 80])
for unit, parent, role, duties in R.ORG_UNITS:
    ws.append([unit, parent or "—", role, "\n".join("• " + x for x in duties)])
style_rows(ws)
ws.cell(row=ws.max_row + 2, column=1,
        value="المصدر: كليات ص7. تبعية وحدات الإعلام والتوظيف والابتكار لعميد الكلية مؤكدة بقرار 2026-11-10."
        ).font = Font(name=FONT, italic=True, size=9)

# ------------------------------------------------------------------ أعضاء الهيكل (قالب)
mem_headers = ["الكلية / المعهد", "القسم العلمي (إن وجد)", "الوحدة التنظيمية", "المنصب في الهيكل",
               "الاسم", "الدرجة / الصفة الوظيفية", "جهة العمل (للأعضاء من خارج الجامعة)",
               "البريد الإلكتروني", "الهاتف", "تاريخ بداية العضوية", "تاريخ نهاية العضوية",
               "رقم وتاريخ قرار التشكيل", "ملاحظات"]
ws = sheet(wb, "أعضاء الهيكل (قالب)", mem_headers,
           [22, 20, 32, 24, 26, 20, 26, 26, 15, 14, 14, 20, 24])
ws.append(["مثال: كلية الهندسة", "", "لجنة متابعة الخطة الاستراتيجية Tier 1", "رئيس اللجنة",
           "أ.د. الاسم الرباعي", "أستاذ — وكيل الكلية للدراسات العليا", "",
           "name@example.edu.eg", "01000000000", "2026-09-01", "", "قرار رقم 123 بتاريخ 2026-08-25",
           "صف توضيحي — يُحذف عند الإدخال"])
for c in ws[2]:
    c.font = Font(name=FONT, size=10, italic=True, color="7F7F7F")
for unit, pos, count in R.ORG_POSITIONS:
    ws.append(["", "", unit, pos, "", "", "", "", "", "", "", "", f"العدد: {count}"])
style_rows(ws, start=3)
for r in range(3, ws.max_row + 1):
    for col in (1, 2, 5, 6, 7, 8, 9, 10, 11, 12):
        ws.cell(row=r, column=col).fill = INPUT_FILL
unit_list = ",".join(u[0] for u in R.ORG_UNITS)
dv4 = DataValidation(type="list", formula1=f'"{unit_list}"')
ws.add_data_validation(dv4)
dv4.add(f"C3:C500")
finish(ws)

# ------------------------------------------------------------------ فحوص قبل الحفظ
codes = [d["code"] for d in R.I]
dups = [c for c, n in Counter(codes).items() if n > 1]
assert not dups, f"أكواد مكررة: {dups}"
conf_ids = {c[0] for c in R.CONFLICTS}
for d in R.I:
    assert d["std"] in std_name, d["code"]
    assert len(d["univ"]) == 6, d["code"]
    for ref in filter(None, d["conflicts"].split(";")):
        assert ref in conf_ids, f"{d['code']} يشير إلى تعارض غير موجود {ref}"
for c in R.CONFLICTS:
    assert c[3] in SEV_FILL, c[0]

os.makedirs(os.path.dirname(OUT), exist_ok=True)
wb.save(OUT)
print(OUT)
print(f"indicators={len(R.I)} conflicts={len(R.CONFLICTS)} standards={len(R.STANDARDS)}")
