# -*- coding: utf-8 -*-
"""
اختبار شامل لبطاقة الأداء على خادم يعمل فعلًا بالإطار الشامل (92 مؤشرًا):
دورة ربعية رسمية للسنة 1 الربع 1، وإدخال كل النماذج واعتمادها بمسار الاعتماد كاملًا بفصل المهام،
ثم الدرجة والفئة كما يحسبها ملف KPI.xlsx، وتصدير الملف نفسه بالقيم.

التشغيل (الخادم يعمل على http://localhost:5150 بعد scripts\\reset-kpi.ps1):
    python tests/e2e/scorecard_test.py
"""
import datetime
import io
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from collection_test import download, input_for  # noqa: E402
from scenario_test import RESULTS, Client, check, step  # noqa: E402

ENTRY = ["entry", "entry.cs", "entry.com", "entry.central", "entry.uni", "deptrep", "rep.ele", "rep.mec", "rep.se", "rep.ai", "rep.acc", "rep.bus", "planner"]
REVIEW = ["deptrep", "rep.ele", "rep.mec", "rep.se", "rep.ai", "rep.acc", "rep.bus", "college", "college.cs", "college.com",
          "dean", "dean.cs", "dean.com", "quality", "planner"]


def main():
    planner = Client("planner@tier1.local")
    clients = {}

    def client(name):
        if name not in clients:
            clients[name] = Client(f"{name}@tier1.local")
        return clients[name]

    step("1. الإطار محمل: 92 مؤشرًا معتمدًا بمستهدفات السنوات الخمس")
    # مؤشرات الإطار من بذرته؛ اختبارات أخرى قد تضيف مؤشرات مسودة على القاعدة نفسها.
    import json
    seed = json.load(open(os.path.join(os.path.dirname(__file__), "..", "..", "src", "Tier1.Infrastructure", "Persistence", "Seed", "kpi-framework.json"), encoding="utf-8"))
    framework = {i["code"] for i in seed["indicators"]}
    indicators = {i["code"]: i for i in planner.get("/api/kpi/indicators")}
    check(len(framework) == 92 and framework <= set(indicators), f"مؤشرات الإطار الـ92 كلها موجودة (في القاعدة {len(indicators)})")
    check(all(indicators[c]["status"] == "Approved" for c in framework), "تعريفات الإطار كلها معتمدة")
    tree = planner.get("/api/strategy/tree")
    check(len(tree["pillars"]) == 6 and sum(len(p["standards"]) for p in tree["pillars"]) == 12, "6 ركائز و12 معيارًا")
    i103 = next(i for p in tree["pillars"] for s in p["standards"] for i in s["indicators"] if i["code"] == "7.03")
    check(i103["direction"] == "LowerIsBetter", "تصحيح 7.03 (تنازلي) مطبق")

    step("2. تقويم الأرباع ودورة ربعية رسمية واحدة لكل ربع")
    cal = planner.get("/api/plan/calendar")
    check(len(cal) == 20 and cal[0]["label"].startswith("السنة 1"), "عشرون ربعًا تبدأ بالسنة 1")
    today = datetime.date.today()
    body = {"name": "دورة السنة 1 الربع 1", "periodLabel": None, "targetYear": 2027, "quarter": 1, "trial": False,
            "opensOn": today.isoformat(), "dueOn": (today + datetime.timedelta(days=30)).isoformat(), "version": None}
    existing = [c for c in planner.get("/api/cycles") if c["quarter"] == 1 and c["targetYear"] == 2027 and not c["trial"]]
    if existing:
        cycle = existing[0]
        check(True, "دورة الربع موجودة من تشغيل سابق")
    else:
        cycle = planner.call("POST", "/api/cycles", body, expect=200)[1]
        check(cycle["periodLabel"].startswith("السنة 1"), f"الفترة تسمى تلقائيًا: {cycle['periodLabel']}")
    status, err = planner.call("POST", "/api/cycles", body)
    check(status in (400, 409, 422) and err.get("code") == "quarter_taken", "لا تنشأ دورة رسمية ثانية للربع نفسه")
    if cycle["status"] == "Draft":
        cycle = planner.call("POST", f"/api/cycles/{cycle['id']}/open", expect=200)[1]
    check(cycle["status"] == "Open", "الدورة مفتوحة")

    step("3. إدخال كل النماذج واعتمادها حتى الجودة المركزية")
    forms = planner.get(f"/api/forms?cycleId={cycle['id']}")
    check(len(forms) > 0, f"{len(forms)} نموذجًا في الدورة")
    approved = 0
    for row in forms:
        if row["status"] == "Approved":
            approved += 1
            continue
        # من يدخل: أول مستخدم يملك تعديل النموذج.
        filler = None
        for name in ENTRY:
            status, f = client(name).call("GET", f"/api/forms/{row['id']}")
            if status == 200 and f["actions"]["canEdit"]:
                filler, form = client(name), f
                break
        if filler:
            for section in form["sections"]:
                for item in section["items"]:
                    if item["value"]:
                        continue
                    b = input_for(item)
                    status, saved = filler.call("PUT", f"/api/forms/{form['id']}/values", b)
                    if status == 200 and any(x["code"] == "big_change" for x in saved["issues"]):
                        filler.call("PUT", f"/api/forms/{form['id']}/values", {**b, "explanation": "قيمة الربع الأول", "version": saved["value"]["version"]}, expect=200)
            form = filler.call("POST", f"/api/forms/{form['id']}/submit", expect=200)[1]
        # الاعتماد: في كل خطوة أول مراجع يملكها (غير من أدخل).
        for _ in range(6):
            form = planner.get(f"/api/forms/{row['id']}")
            if form["status"] == "Approved":
                break
            done = False
            for name in REVIEW:
                status, f = client(name).call("GET", f"/api/forms/{row['id']}")
                if status == 200 and f["actions"]["canReview"]:
                    client(name).call("POST", f"/api/forms/{row['id']}/approve", {"note": None}, expect=200)
                    done = True
                    break
            if not done:
                break
        if planner.get(f"/api/forms/{row['id']}")["status"] == "Approved":
            approved += 1
        else:
            print(f"   (لم يكتمل اعتماد {row['unitName']})")
    check(approved == len(forms), f"اعتمدت {approved} من {len(forms)} نموذجًا")

    step("3ب. مسئول ملف Tier 1 يراجع مؤشرات الكليات وينقل القيم إلى مؤشرات الجامعة")
    officer = Client("tier1@tier1.local")
    status, _ = client("college").call("GET", f"/api/review?cycleId={cycle['id']}")
    check(status == 403, "مسئول الكلية لا يرى شاشة المراجعة")
    review = officer.get(f"/api/review?cycleId={cycle['id']}")
    check(review["canTransfer"] and review["cycle"]["id"] == cycle["id"], "المراجعة على دورة الربع ومسموح بالنقل")
    rows = {r["code"]: r for r in review["rows"]}
    check(framework <= set(rows), f"كل مؤشرات الإطار في المراجعة ({len(rows)})")
    r101 = rows["1.01"]
    check(abs(r101["computed"] - 90) < 0.01 and r101["reported"] == r101["expected"], f"1.01 محسوبة 90 من {r101['expected']} جهة")
    check(len(r101["units"]) >= 2 and all(u["value"] is not None and u["numerator"] is not None for u in r101["units"]), "قيمة كل كلية ببسطها ومقامها")
    check(all(u["universityAchievement"] is not None for u in r101["units"]), "نسبة كل كلية من مستهدف الجامعة")
    r106 = rows["1.06"]
    check(not r106["comparable"] and all(u["universityAchievement"] is None for u in r106["units"]), "مؤشر المجموع 1.06 لا يقارن كلية بمستهدف الجامعة كلها")
    check(abs(sum(u["share"] or 0 for u in r106["units"]) - 100) < 0.5, "حصص الكليات من رقم الجامعة مجموعها 100%")
    status, err = officer.call("POST", "/api/review/transfer", {"cycleId": cycle["id"], "indicatorId": r101["indicatorId"], "value": 91, "note": None, "version": None})
    check(status == 400 and err.get("code") == "note_required", "تعديل القيمة عن المحسوبة يحتاج سببًا")
    status, _ = client("planner").call("POST", "/api/review/transfer", {"cycleId": cycle["id"], "indicatorId": r101["indicatorId"], "value": None, "note": None, "version": None})
    check(status == 403, "المخطط لا ينقل القيم")
    one = officer.call("POST", "/api/review/transfer", {"cycleId": cycle["id"], "indicatorId": r101["indicatorId"], "value": None, "note": None,
                                                         "version": r101["official"]["version"] if r101["official"] else None}, expect=200)[1]
    check(one["state"] == "Transferred" and abs(one["official"]["value"] - 90) < 0.01, "نقلت قيمة 1.01 إلى مؤشر الجامعة")
    moved = officer.call("POST", "/api/review/transfer-all", {"cycleId": cycle["id"]}, expect=200)[1]
    review = officer.get(f"/api/review?cycleId={cycle['id']}")
    pending = [r["code"] for r in review["rows"] if r["computed"] is not None and r["state"] != "Transferred"]
    check(not pending, f"نقل كل الجاهز ({moved['transferred']}); لم يبق جاهز: {pending[:5]}")

    step("3ج. تقدم الكلية مقابل مستهدف الجامعة")
    prog = client("college").get("/api/progress")
    check(len(prog["units"]) == 1, f"مسئول الكلية يرى كليته فقط: {prog['unit']['name']}")
    p101 = next(i for p in prog["pillars"] for s in p["standards"] for i in s["indicators"] if i["code"] == "1.01")
    check(p101["latest"] is not None and p101["yearTarget"] is not None and abs(p101["gap"] - (p101["latest"] - p101["yearTarget"])) < 0.01,
          f"1.01 للكلية {p101['latest']} مقابل مستهدف الجامعة {p101['yearTarget']}")
    check(p101["universityQuarters"][0] is not None and abs(p101["universityQuarters"][0] - 90) < 0.01, "قيمة الجامعة الرسمية بجوار قيمة الكلية")
    p106 = next(i for p in prog["pillars"] for s in p["standards"] for i in s["indicators"] if i["code"] == "1.06")
    check(not p106["comparable"] and p106["share"] is not None and (p106["ownTarget"] is not None or p106["score"] is None),
          f"1.06 (مجموع) للكلية: حصتها {p106['share']}% من رقم الجامعة ولا تقارن بمستهدف الجامعة كلها")
    whole = planner.get("/api/progress")
    check(len(whole["units"]) >= 2 and whole["universityScore"] is not None, f"المخطط يرى {len(whole['units'])} كليات ودرجة الجامعة")

    step("4. الدرجة والفئة بمعادلات الملف")
    card = planner.get("/api/scorecard")
    check(card["lastReported"] and card["lastReported"]["yearIndex"] == 1 and card["lastReported"]["quarter"] == 1, "آخر ربع معتمد: السنة 1 الربع 1")
    allind = {i["code"]: i for p in card["pillars"] for s in p["standards"] for i in s["indicators"]}
    i101 = allind["1.01"]
    # 1.01 نسبة على مستوى القسم: كل قسم 45/50 فنسبة الجامعة 90%، والمستهدف النهائي 90 فالإنجاز 100.
    check(abs(i101["quarters"][0] - 90) < 0.01, f"قيمة الجامعة للربع الأول 1.01 = {i101['quarters'][0]}")
    check(i101["score"] == 100 and i101["level"] == "InLevel", "1.01 محقق 100% وفي المستوى")
    check(i101["yearScoresVsYearly"][0] == 100, "مقابل مستهدف السنة 1 (82): 100%")
    # درجة المعيار = متوسط مؤشراته، والركيزة = متوسط معاييرها، والكلية = متوسط الركائز.
    for p in card["pillars"]:
        for s in p["standards"]:
            expected = round(sum(i["score"] for i in s["indicators"]) / len(s["indicators"]) + 1e-9, 1)
            check(abs(s["score"] - expected) < 0.051, f"درجة المعيار {s['code']} = {s['score']}")
        exp_p = round(sum(s["score"] for s in p["standards"]) / len(p["standards"]) + 1e-9, 1)
        check(abs(p["score"] - exp_p) < 0.051, f"درجة الركيزة {p['code']} = {p['score']} (متوسط معاييرها)")
    exp_o = round(sum(p["score"] for p in card["pillars"]) / len(card["pillars"]) + 1e-9, 1)
    check(abs(card["overall"]["score"] - exp_o) < 0.051, f"الدرجة الكلية {card['overall']['score']} = متوسط الركائز")
    o = card["overall"]
    t = card["thresholds"]
    tier_of = lambda v, off: 1 if v >= t["tier1"] + off else 2 if v >= t["tier2"] + off else 3 if v >= t["tier3"] + off else 4 if v >= t["tier4"] + off else 5
    overall_tier = tier_of(o["score"], t["overallOffset"])
    weakest = tier_of(min(p["score"] for p in card["pillars"]), 0)
    final = min(overall_tier + 1 if weakest - overall_tier >= 2 else overall_tier, 5)
    check(o["tier"]["finalTier"] == final, f"الفئة النهائية {final} (الكلية {overall_tier}، أضعف ركيزة {weakest})")
    check(len(card["perspectives"]) == 7 and len(card["sdgs"]) == 17, "سبعة مناظير و17 هدفًا")

    step("5. ملف المجلس KPI.xlsx بالقيم")
    status, content, ctype = download(planner, "/api/scorecard/kpi.xlsx")
    check(status == 200 and "spreadsheet" in ctype, "نزل الملف")
    from openpyxl import load_workbook
    wb = load_workbook(io.BytesIO(content))
    ws = wb["📊 بطاقة مؤشرات الأداء"]
    check(abs(float(ws["U10"].value) - 90) < 0.01, f"خلية ر1 للمؤشر 1.01 = {ws['U10'].value}")
    check(ws["V10"].value is None, "الربع الثاني فارغ")
    check(isinstance(ws["AS10"].value, str) and ws["AS10"].value.startswith("="), "صيغ الملف باقية كما هي")
    check(len(wb.sheetnames) == 8, "الأوراق الثماني كلها في الملف")

    failed = [n for ok, n in RESULTS if not ok]
    print(f"\nنجح {len(RESULTS) - len(failed)} من {len(RESULTS)}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
