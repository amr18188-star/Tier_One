# -*- coding: utf-8 -*-
"""
بيانات تجريبية للنسخة التجريبية (scripts\\demo.ps1 على المنفذ 5160). تدخل عبر واجهة البرنامج نفسها بحساب كل دور،
فتمر بقواعد التحقق والاعتماد وفصل المهام وتسجل في سجلات التدقيق كأي استخدام فعلي:

- القاموس: إكمال التعريفات الناقصة واعتمادها من اللجنة العليا، مع ترك بعضها مسودة أو قيد المراجعة، واعتماد المستهدفات.
- التعارضات: بعضها محسوم بقرار، وبعضها في جدول أعمال اللجنة، والباقي مفتوح.
- هياكل Tier 1 بالكليات الثلاث (بشواغر في التجارة).
- دورتان مغلقتان معتمدتان (2023/2024 و2024/2025)، ودورة حالية 2025/2026 بنماذج في كل مراحل الاعتماد.

كل القيم والأسماء مولدة وليست بيانات الجامعة. مستوى الأداء يختلف بين الكليات فتظهر المقارنات والاتجاه.

التشغيل بعد تشغيل النسخة التجريبية:
    python tools/demo_data.py
"""
import datetime
import hashlib
import os
import random
import sys
import time

os.environ.setdefault("T1_BASE", "http://localhost:5160")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tests", "e2e"))
from scenario_test import BASE, Client  # noqa: E402
from collection_test import PDF, upload  # noqa: E402

TARGET_YEAR = 2027  # 2026 سنة الأساس في ملف Tier 1؛ المستهدفات تبدأ من 2027.
DECISION = "محضر اللجنة العليا 4/2026 (تجريبي)"
INTEGER_UNITS = {"عدد", "برنامج", "دولة", "سرير", "مركز"}
CENTRAL_BY_STANDARD = {"S02": "QAC", "S05": "INTL", "S08": "QAC", "S09": "QAC", "S11": "STU", "S12": "DIG"}
LOWER_IS_BETTER = ("لكل", "تسرب", "رسوب", "استهلاك", "ترتيب")

COLLEGES = {
    "ENG": {"entry": "entry", "committee": "college", "dean": "dean", "level": 1.0, "size": 9000,
            "departments": {"ENG-CIV": "deptrep", "ENG-ELE": "rep.ele", "ENG-MEC": "rep.mec"}},
    "CS": {"entry": "entry.cs", "committee": "college.cs", "dean": "dean.cs", "level": 1.07, "size": 4500,
           "departments": {"CS-SE": "rep.se", "CS-AI": "rep.ai"}},
    "COM": {"entry": "entry.com", "committee": "college.com", "dean": "dean.com", "level": 0.9, "size": 16000,
            "departments": {"COM-ACC": "rep.acc", "COM-BUS": "rep.bus"}},
}
SHARE = {"ENG": 0.35, "CS": 0.2, "COM": 0.45}
CENTRAL_ENTRY = "entry.central"

CYCLES = [
    {"name": "دورة القياس 2023/2024", "period": "2023/2024", "opens": "2024-10-01", "due": "2024-11-30", "trend": 0.9},
    {"name": "دورة القياس 2024/2025", "period": "2024/2025", "opens": "2025-10-01", "due": "2025-11-30", "trend": 0.95},
    {"name": "دورة القياس 2025/2026", "period": "2025/2026", "opens": "2026-10-15", "due": "2026-11-30", "trend": 1.0},
]

# الدورة الحالية: أين يقف نموذج كل جهة، ليجد كل حساب ما ينتظره في «مطلوب منك».
CURRENT_STAGES = {
    "ENG": "Dean", "ENG-CIV": "DepartmentReview", "ENG-ELE": "CollegeCommittee", "ENG-MEC": "partial",
    "CS": "Done", "CS-SE": "Done", "CS-AI": "Quality",
    "COM": "returned", "COM-ACC": "Dean", "COM-BUS": "empty",
    "QAC": "Done", "STU": "Quality", "DIG": "partial", "INTL": "Done",
}

FIRST = ["أحمد", "محمد", "محمود", "مصطفى", "خالد", "هشام", "طارق", "سامح", "وليد", "عمرو", "منى", "هبة", "نهى", "ريهام", "داليا", "سارة", "إيمان", "ياسمين"]
FAMILY = ["عبد الحميد", "السيد", "منصور", "الشافعي", "عبد الله", "حسنين", "رضوان", "سليمان", "فهمي", "الجمال", "عثمان", "البنا", "زكي", "شاكر"]

_clients: dict[str, Client] = {}


def user(name: str) -> Client:
    if name not in _clients:
        _clients[name] = Client(f"{name}@tier1.local")
    return _clients[name]


def rnd(*keys) -> random.Random:
    return random.Random(int(hashlib.sha256("|".join(map(str, keys)).encode("utf-8")).hexdigest()[:12], 16))


def say(text: str):
    print(text, flush=True)


# ---------------------------------------------------------------------------------------------------- القاموس
def complete_definition(d: dict, units: dict) -> dict:
    """يكمل ما ينقص التعريف للاعتماد بقيم معقولة؛ في البيانات الحقيقية تقرها اللجنة العليا."""
    v = d["current"]
    body = {k: v[k] for k in ("nameAr", "nameEn", "definition", "purpose", "exclusions", "numerator", "denominator", "unit", "direction",
                              "frequency", "level", "aggregation", "aggregationNote", "dataSource", "ownerUnitId", "evidenceRequired")}
    if not (body["definition"] or "").strip():
        body["definition"] = body["nameAr"]
    unit = body["unit"].strip()
    if "kWh" in unit:
        body["unit"] = "ميجاوات ساعة"
    elif "غير محدد" in unit:
        body["unit"] = "%"
    if body["direction"] == "Undefined":
        body["direction"] = "LowerIsBetter" if any(w in body["nameAr"] for w in LOWER_IS_BETTER) else "HigherIsBetter"
    has_ratio = bool((body["numerator"] or "").strip() and (body["denominator"] or "").strip())
    if body["aggregation"] == "Undefined" or (body["aggregation"] == "RatioOfSums" and not has_ratio):
        if has_ratio:
            body["aggregation"] = "RatioOfSums"
        elif body["level"] == "University":
            body["aggregation"] = "Central"
        elif body["unit"].strip() in INTEGER_UNITS:
            body["aggregation"] = "Sum"
        else:
            body["aggregation"] = "WeightedAverage"
        body["aggregationNote"] = None
    if not (body["dataSource"] or "").strip():
        body["dataSource"] = "سجلات الجهة المالكة للمؤشر"
    if body["ownerUnitId"] is None:
        code = CENTRAL_BY_STANDARD.get(d["standardCode"], "QAC") if body["level"] == "University" else "CU"
        body["ownerUnitId"] = units[code]["id"]
    body["version"] = v["version"]
    return body


def dictionary(units: dict):
    planner, committee = user("planner"), user("committee")
    rows = planner.get("/api/kpi/indicators")
    by_standard: dict[str, list] = {}
    for r in rows:
        by_standard.setdefault(r["standardCode"], []).append(r["code"])
    # يترك آخر مؤشر في بعض المعايير بلا اعتماد، ليظهر عمل اللجنة ومدير الخطة الجاري.
    under_review = {by_standard[s][-1] for s in ("S03", "S06", "S10") if s in by_standard}
    drafts = {by_standard[s][-1] for s in ("S04", "S07") if s in by_standard}
    approved = 0
    for r in rows:
        if r["status"] != "Draft":
            continue
        d = planner.get(f"/api/kpi/indicators/{r['code']}")
        if r["code"] in drafts:
            continue
        if d["current"]["missing"]:
            d = planner.call("PUT", f"/api/kpi/indicators/{r['code']}/definition", complete_definition(d, units), expect=200)[1]
        planner.call("POST", f"/api/kpi/indicators/{r['code']}/submit", expect=200)
        if r["code"] in under_review:
            continue
        committee.call("POST", f"/api/kpi/indicators/{r['code']}/approve", {"decisionRef": DECISION, "note": None}, expect=200)
        approved += 1
    say(f"  اعتمد {approved} تعريفًا، و{len(under_review)} قيد مراجعة اللجنة، و{len(drafts)} مسودة")


def targets(units: dict) -> dict:
    """يعتمد المستهدفات المقترحة من الملفين ويعيد مستهدف الجامعة وكل كلية لسنة المقارنة."""
    planner, committee = user("planner"), user("committee")
    found, approved, proposed = {}, 0, 0
    for r in planner.get("/api/kpi/indicators"):
        if r["status"] != "Approved":
            continue
        t = planner.get(f"/api/kpi/indicators/{r['code']}/targets")
        rows = list(t["university"]) + ([t["eachCollege"]] if t.get("eachCollege") else []) + list(t.get("colleges") or [])
        for row in rows:
            if row["status"] == "Proposed":
                status, _ = committee.call("POST", f"/api/kpi/targets/{row['id']}/approve", {"decisionRef": DECISION, "note": None})
                approved += status == 200
        uni = next((x["value"] for x in t["university"] if x["year"] == TARGET_YEAR and x["kind"] == "Target"), None)
        each = t["eachCollege"]["value"] if t.get("eachCollege") and t["eachCollege"]["year"] == TARGET_YEAR else None
        found[r["code"]] = {"uni": uni, "each": each}
        if r["level"] == "College" and each is None:
            # لا مستهدف للكليات في ملفها لهذا المؤشر: يقترح كل عميد مستهدف كليته وتعتمده اللجنة العليا.
            for college, spec in COLLEGES.items():
                value = college_target(r, uni, college)
                saved = user(spec["dean"]).call("PUT", f"/api/kpi/indicators/{r['code']}/targets",
                                                {"unitId": units[college]["id"], "year": TARGET_YEAR, "kind": "Target", "value": value, "source": "خطة الكلية التنفيذية (تجريبية)"},
                                                expect=200)[1]
                committee.call("POST", f"/api/kpi/targets/{saved['id']}/approve", {"decisionRef": DECISION, "note": None}, expect=200)
                proposed += 1
    say(f"  اعتمد {approved} مستهدفًا من الملفين، و{proposed} مستهدف كلية اقترحه العمداء")
    return found


def college_target(row: dict, uni: float | None, college: str) -> float:
    pct = row["unit"].strip() == "%"
    if uni is None:
        v = 70.0 if pct else 20.0
    elif row["aggregation"] in ("Sum", "DistinctCount"):
        v = uni * SHARE[college]
    else:
        v = uni
    if row["unit"].strip() in INTEGER_UNITS:
        return max(1, round(v))
    return round(min(v, 100.0), 1) if pct else round(v, 2)


def conflicts():
    planner, committee = user("planner"), user("committee")
    items = planner.get("/api/kpi/conflicts")["items"]
    decided = prepared = 0
    for i, c in enumerate(items):
        if i % 3 == 2:
            continue  # مفتوح لم يجهز بعد
        c = planner.call("POST", f"/api/kpi/conflicts/{c['code']}/prepare",
                         {"proposal": c["proposal"] or "اعتماد مستهدف الجامعة وتوزيعه على الكليات", "preparationNote": "جهز للعرض على اللجنة العليا", "version": c["version"]},
                         expect=200)[1]
        prepared += 1
        if i % 3 == 0:
            committee.call("POST", f"/api/kpi/conflicts/{c['code']}/decide",
                           {"defer": False, "decision": f"يعتمد المقترح: {c['proposal']}", "decisionDate": "2026-10-20", "minutesRef": DECISION, "version": c["version"]},
                           expect=200)
            decided += 1
    say(f"  حسم {decided} تعارضًا، و{prepared - decided} في جدول أعمال اللجنة، و{len(items) - prepared} مفتوح")


# ---------------------------------------------------------------------------------------------------- الهيكل
def structures(units: dict):
    template = user("college").get("/api/structure/template")
    added = 0
    for college_code, spec in COLLEGES.items():
        client = user(spec["committee"])
        college = units[college_code]
        r = rnd("structure", college_code)
        for tu in template:
            departments = [units[d] for d in spec["departments"]] if tu["perDepartment"] else [None]
            for dept in departments:
                for pos in tu["positions"]:
                    if college_code == "COM" and not pos["required"]:
                        continue  # شواغر تظهر في لوحة الاكتمال
                    for _ in range(1 if pos["single"] else 2):
                        name = f"{'أ.د.' if tu['isHead'] else 'د.'} {r.choice(FIRST)} {r.choice(FAMILY)}"
                        body = {"collegeId": college["id"], "departmentId": dept["id"] if dept else None, "positionId": pos["id"], "fullName": name,
                                "title": None, "employer": None, "email": f"member{r.randint(100, 999)}@demo.capu.edu.eg",
                                "phone": f"010{r.randint(10000000, 99999999)}", "startDate": "2025-09-01", "decisionRef": f"قرار مجلس الكلية {r.randint(10, 99)}/2025",
                                "notes": None, "replaceCurrent": False, "version": None}
                        status, err = client.call("POST", "/api/structure/members", body)
                        if status == 200:
                            added += 1
                        elif status not in (409, 400):
                            raise AssertionError(f"structure {college_code}: {status} {err}")
    say(f"  أضيف {added} عضوًا في هياكل الكليات")


# ---------------------------------------------------------------------------------------------------- الدورات
class Values:
    def __init__(self, units: dict, tg: dict):
        self.units, self.tg = units, tg

    def college_of(self, unit: dict) -> str | None:
        for code, spec in COLLEGES.items():
            if unit["code"] == code or unit["code"] in spec["departments"]:
                return code
        return None

    def size(self, unit: dict) -> int:
        c = self.college_of(unit)
        if c is None:
            return 1200
        s = COLLEGES[c]["size"]
        return s if unit["code"] == c else s // len(COLLEGES[c]["departments"])

    def anchor(self, item: dict, unit: dict) -> float:
        if item["target"] is not None:
            return float(item["target"])
        t = self.tg.get(item["code"], {})
        c = self.college_of(unit)
        if c and t.get("each") is not None:
            return float(t["each"])
        if t.get("uni") is not None:
            if c and item["aggregation"] in ("Sum", "DistinctCount"):
                share = SHARE[c] / (1 if unit["code"] == c else len(COLLEGES[c]["departments"]))
                return float(t["uni"]) * share
            return float(t["uni"])
        return 65.0 if item["unit"].strip() == "%" else 20.0

    def body(self, item: dict, unit: dict, cycle: dict) -> dict:
        r = rnd(item["code"], unit["code"])
        rc = rnd(item["code"], unit["code"], cycle["period"])
        c = self.college_of(unit)
        level = COLLEGES[c]["level"] if c else 0.97
        f = level * r.uniform(0.82, 1.16) * cycle["trend"] * rc.uniform(0.98, 1.02)
        base = self.anchor(item, unit)
        v = base / f if item["direction"] == "LowerIsBetter" else base * f
        pct = item["unit"].strip() == "%"
        body = {"indicatorId": item["indicatorId"], "kind": "Number", "value": None, "numerator": None, "denominator": None,
                "reason": None, "explanation": None, "version": item["value"]["version"] if item["value"] else None}
        if rc.random() < 0.03:
            body.update(kind="NotAvailable", reason="لم تصدر البيانات بعد من الجهة المصدرة")
        elif item["usesRatio"]:
            if pct:
                v = max(1.0, min(v, 99.0))
                d = max(20, int(self.size(unit) * r.uniform(0.3, 1.0)))
                body.update(numerator=min(d, round(v / 100 * d)), denominator=d)
            else:
                d = r.randint(150, 900)
                body.update(numerator=max(0, round(v * d)), denominator=d)
        elif item["aggregation"] == "WeightedAverage":
            body.update(value=round(min(v, 100.0) if pct else v, 2), denominator=max(10, int(self.size(unit) * r.uniform(0.2, 1.0))))
        elif item["unit"].strip() in INTEGER_UNITS:
            body["value"] = max(0, round(v))
        else:
            body["value"] = round(min(v, 100.0) if pct else v, 2)
        return body


def fill(client: Client, form: dict, unit: dict, cycle: dict, values: Values, limit: int | None = None):
    items = [i for s in form["sections"] for i in s["items"]]
    for item in items[:limit]:
        body = values.body(item, unit, cycle)
        status, saved = client.call("PUT", f"/api/forms/{form['id']}/values", body)
        if status != 200:
            raise AssertionError(f"{unit['code']} {item['code']}: {status} {saved}")
        if any(i["code"] == "big_change" for i in saved["issues"]):
            body.update(explanation="تغير متوقع بعد تحديث نظام القبول والتسجيل", version=saved["value"]["version"])
            client.call("PUT", f"/api/forms/{form['id']}/values", body, expect=200)
        if item["evidenceRequired"] and body["kind"] == "Number" and not [e for e in saved["evidence"] if not e["voided"]]:
            upload(client, f"/api/forms/{form['id']}/values/{item['indicatorId']}/evidence", f"{item['code']}.pdf", PDF,
                   {"description": "كشف معتمد من الجهة (ملف تجريبي)"})


def reviewers(unit: dict, values: Values) -> dict:
    c = values.college_of(unit)
    if c is None:
        return {"Quality": "quality"}
    spec = COLLEGES[c]
    steps = {"CollegeCommittee": spec["committee"], "Dean": spec["dean"], "Quality": "quality"}
    if unit["code"] != c:
        steps["DepartmentReview"] = spec["departments"][unit["code"]]
    return steps


def entry_of(unit: dict, values: Values) -> str:
    c = values.college_of(unit)
    return COLLEGES[c]["entry"] if c else CENTRAL_ENTRY


def advance(form: dict, unit: dict, values: Values, until: str) -> dict:
    steps = reviewers(unit, values)
    while form["step"] != until and form["status"] != "Approved":
        form = user(steps[form["step"]]).call("POST", f"/api/forms/{form['id']}/approve", {"note": "روجعت القيم والأدلة"}, expect=200)[1]
    return form


def return_form(form: dict, unit: dict, values: Values, note: str) -> dict:
    reviewer = user(reviewers(unit, values)[form["step"]])
    items = [i for s in form["sections"] for i in s["items"] if i["value"] and i["value"]["kind"] == "Number"][:2]
    return reviewer.call("POST", f"/api/forms/{form['id']}/return",
                         {"note": note, "items": [{"indicatorId": i["indicatorId"], "note": "الرقم لا يطابق الكشف المرفق؛ راجعوا المصدر"} for i in items]},
                         expect=200)[1]


def run_cycle(spec: dict, units: dict, by_id: dict, values: Values, stages: dict | None):
    planner = user("planner")
    body = {"name": spec["name"], "periodLabel": spec["period"], "targetYear": TARGET_YEAR, "opensOn": spec["opens"], "dueOn": spec["due"], "trial": False, "version": None}
    cycle = planner.call("POST", "/api/cycles", body, expect=200)[1]
    cycle = planner.call("POST", f"/api/cycles/{cycle['id']}/open", expect=200)[1]
    rows = planner.get(f"/api/forms?cycleId={cycle['id']}")
    say(f"  {spec['name']}: {len(rows)} نموذجًا")
    for row in rows:
        unit = by_id[row["unitId"]]
        stage = (stages or {}).get(unit["code"], "Done")
        entry = user(entry_of(unit, values))
        form = entry.get(f"/api/forms/{row['id']}")
        if stage == "empty":
            continue
        if stage == "partial":
            fill(entry, form, unit, spec, values, limit=max(1, len([i for s in form["sections"] for i in s["items"]]) // 2))
            continue
        fill(entry, form, unit, spec, values)
        form = entry.call("POST", f"/api/forms/{form['id']}/submit", expect=200)[1]
        returned_once = stages is None and spec["period"] == "2024/2025" and unit["code"] == "ENG"
        if stage == "returned" or returned_once:
            form = return_form(user(entry_of(unit, values)).get(f"/api/forms/{form['id']}"), unit, values, "يرجى مراجعة مؤشرين قبل الإحالة للعميد")
            if stage == "returned":
                planner.call("POST", f"/api/forms/{form['id']}/extend", {"dueOn": "2026-12-07", "reason": "تمديد أسبوع لتصحيح ملاحظات لجنة الكلية"}, expect=200)
                continue
            fill(entry, entry.get(f"/api/forms/{form['id']}"), unit, spec, values)
            form = entry.call("POST", f"/api/forms/{form['id']}/submit", expect=200)[1]
        advance(form, unit, values, "Done" if stage == "Done" else stage)
    if stages is None:
        planner.call("POST", f"/api/cycles/{cycle['id']}/close", {"note": "انتهت الدورة واعتمدت كل النماذج"}, expect=200)
    return cycle


def main():
    started = time.time()
    say(f"البيانات التجريبية على {BASE}")
    planner = user("planner")
    if any(c["name"] == CYCLES[0]["name"] for c in planner.get("/api/cycles")):
        raise SystemExit("البيانات التجريبية موجودة بالفعل. للبدء من جديد: scripts\\demo.ps1 -Reset ثم شغل هذا الملف.")
    units = {u["code"]: u for u in planner.get("/api/org/units")}
    by_id = {u["id"]: u for u in units.values()}
    missing = [c for c in ["CU", "QAC", "STU", "DIG", "INTL", *COLLEGES] if c not in units]
    if missing:
        raise SystemExit(f"Missing demo units {missing}; start the server with scripts\\demo.ps1 (Seed:Demo).")

    say("1. قاموس المؤشرات")
    dictionary(units)
    say("2. المستهدفات")
    tg = targets(units)
<<<<<<< HEAD
    say("3. الخطة: الشروح والمبادرات ومدد المستهدفات")
    plan(units, tg)
    say("4. التعارضات")
    conflicts()
    say("5. هياكل Tier 1 بالكليات")
    structures(units)
    say("6. دورات جمع البيانات")
=======
    say("3. التعارضات")
    conflicts()
    say("4. هياكل Tier 1 بالكليات")
    structures(units)
    say("5. دورات جمع البيانات")
>>>>>>> b37aed0bbdb0f3da34e297227373ec53737dd898
    values = Values(units, tg)
    for spec in CYCLES[:-1]:
        run_cycle(spec, units, by_id, values, None)
    run_cycle(CYCLES[-1], units, by_id, values, CURRENT_STAGES)
    say(f"\nاكتملت البيانات التجريبية في {round(time.time() - started)} ثانية: {BASE}")


if __name__ == "__main__":
    main()
