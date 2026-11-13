# -*- coding: utf-8 -*-
"""
تكليف الجهات بمؤشراتها، وحصر كل مسئول في جهته، والجهات التي تطلع على الجامعة كاملة — على خادم يعمل فعلًا.

1. كلية التجارة تخصص قائمتها (مؤشران للكلية ومؤشر لأقسامها)، والجهة المركزية QAC تكلف بمؤشر على مستوى الجامعة.
2. مؤشر الجامعة لا يسند إلى جهتين، والجهة المركزية لا تكلف بمؤشر كلية.
3. بروفة داخلية تفتح بالتكليف: نموذج التجارة وأقسامها بما كلفت به فقط، والهندسة بكل مؤشرات الكليات، وQAC بمؤشرها وحده.
4. مدخل بيانات التجارة لا يرى إلا نماذج كليته وأقسامها.
5. «تطلع على الجامعة كاملة»: لا يغيرها إلا من يدير الصلاحيات؛ بعدها يرى مدخل الجهات المركزية كل النماذج ولا يعدل إلا نماذج جهاته.
ثم يعيد ما غيره ويغلق البروفة.

التشغيل (خادم التطوير على http://localhost:5150 بالكليات التجريبية):
    python tests/e2e/assignment_test.py
"""
import datetime
import os
import sys
import uuid

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenario_test import RESULTS, Client, check, step  # noqa: E402


def main():
    planner = Client("planner@tier1.local")
    admin = Client("sa1@tier1.local")
    entry_com = Client("entry.com@tier1.local")
    central = Client("entry.central@tier1.local")

    units = {u["code"]: u for u in planner.get("/api/org/units")}
    com, eng, qac, stu, acc = units["COM"], units["ENG"], units["QAC"], units["STU"], units["COM-ACC"]

    step("1. مؤشرات الجهة: الافتراضي ثم التخصيص")
    d = planner.get(f"/api/org/units/{com['id']}/indicators")
    items = [i for s in d["standards"] for i in s["indicators"]]
    college_level = [i for i in items if i["level"] == "College"]
    dept_level = [i for i in items if i["level"] == "Department"]
    check(not d["custom"] and all(i["selected"] for i in college_level), f"التجارة غير مخصصة: تدخل كل مؤشرات الكليات ({len(college_level)})")
    check(len(dept_level) >= 1, "قائمة الكلية تشمل مؤشرات أقسامها")
    chosen = [college_level[0]["id"], college_level[1]["id"], dept_level[0]["id"]]
    d = planner.call("PUT", f"/api/org/units/{com['id']}/indicators", {"custom": True, "indicatorIds": chosen}, expect=200)[1]
    picked = {i["id"] for s in d["standards"] for i in s["indicators"] if i["selected"]}
    check(d["custom"] and picked == set(chosen), "حفظت قائمة التجارة: 3 مؤشرات")
    status, _ = entry_com.call("PUT", f"/api/org/units/{com['id']}/indicators", {"custom": False, "indicatorIds": []})
    check(status == 403, "مدخل البيانات لا يغير تكليف الجهات")
    dacc = planner.get(f"/api/org/units/{acc['id']}/indicators")
    check(dacc["followsUnit"] == com["nameAr"] and [i["id"] for s in dacc["standards"] for i in s["indicators"] if i["selected"]] == [dept_level[0]["id"]],
          "قسم المحاسبة يتبع قائمة كليته")

    step("2. مؤشر الجامعة تدخله جهة واحدة")
    dq = planner.get(f"/api/org/units/{qac['id']}/indicators")
    uni_items = [i for s in dq["standards"] for i in s["indicators"]]
    check(uni_items and all(i["level"] == "University" for i in uni_items), f"الجهة المركزية تكلف بمؤشرات الجامعة فقط ({len(uni_items)})")
    target = next(i for i in uni_items if not i["takenBy"])
    planner.call("PUT", f"/api/org/units/{qac['id']}/indicators", {"custom": True, "indicatorIds": [target["id"]]}, expect=200)
    status, err = planner.call("PUT", f"/api/org/units/{stu['id']}/indicators", {"custom": True, "indicatorIds": [target["id"]]})
    check(status == 409 and err.get("code") == "already_assigned", f"لا يسند {target['code']} إلى جهة ثانية")
    status, err = planner.call("PUT", f"/api/org/units/{stu['id']}/indicators", {"custom": True, "indicatorIds": [college_level[0]["id"]]})
    check(status == 400 and err.get("code") == "not_assignable", "الجهة المركزية لا تكلف بمؤشر كلية")
    dst = planner.get(f"/api/org/units/{stu['id']}/indicators")
    check(next(i for s in dst["standards"] for i in s["indicators"] if i["id"] == target["id"])["takenBy"] == qac["nameAr"],
          "يظهر للجهة الأخرى أنه مسند إلى QAC")
    listed = {u["code"]: u for u in planner.get("/api/org/units")}
    check(listed["COM"]["indicatorCount"] == 3 and listed["QAC"]["indicatorCount"] == 1 and listed["ENG"]["indicatorCount"] > 3,
          f"عدد المؤشرات في قائمة الجهات: التجارة 3، QAC 1، الهندسة {listed['ENG']['indicatorCount']}")

    step("3. بروفة تفتح بالتكليف")
    today = datetime.date.today()
    label = f"تكليف-{uuid.uuid4().hex[:6]}"
    cycle = planner.call("POST", "/api/cycles", {"name": f"بروفة {label}", "periodLabel": label, "targetYear": 2027, "opensOn": today.isoformat(),
                                                  "dueOn": (today + datetime.timedelta(days=14)).isoformat(), "trial": True, "version": None}, expect=200)[1]
    cycle = planner.call("POST", f"/api/cycles/{cycle['id']}/open", expect=200)[1]
    forms = {f["unitId"]: f for f in planner.get(f"/api/forms?cycleId={cycle['id']}")}

    def codes(unit):
        f = planner.get(f"/api/forms/{forms[unit['id']]['id']}")
        return {i["indicatorId"] for s in f["sections"] for i in s["items"]}

    check(codes(com) == {college_level[0]["id"], college_level[1]["id"]} | ({dept_level[0]["id"]} & codes(com)),
          "نموذج التجارة بمؤشريها فقط")
    check(codes(acc) == {dept_level[0]["id"]}, "نموذج قسم المحاسبة بمؤشر القسم المكلف به فقط")
    check({i["id"] for i in college_level} <= codes(eng), "نموذج الهندسة بكل مؤشرات الكليات")
    check(codes(qac) == {target["id"]}, f"نموذج QAC بمؤشر الجامعة {target['code']} وحده")
    uni = next(u for u in units.values() if u["kind"] == "University")
    check(uni["id"] not in forms or target["id"] not in codes(uni), "المؤشر خرج من نموذج الجامعة")

    step("4. مسئول الجهة لا يرى إلا جهته")
    mine = entry_com.get(f"/api/forms?cycleId={cycle['id']}")
    allowed = {u["id"] for u in units.values() if u["path"].startswith(com["path"])}
    check(mine and all(f["unitId"] in allowed for f in mine), f"مدخل التجارة يرى {len(mine)} نموذجًا كلها للتجارة وأقسامها")
    status, _ = entry_com.call("GET", f"/api/forms/{forms[eng['id']]['id']}")
    check(status == 404, "نموذج الهندسة غير موجود بالنسبة له (404)")
    seen = central.get(f"/api/forms?cycleId={cycle['id']}")
    check(all(units_by_id(units)[f["unitId"]]["code"] in ("QAC", "STU", "DIG", "INTL") for f in seen), "مدخل الجهات المركزية يرى نماذج جهاته فقط")
    directory = entry_com.get("/api/org/units")
    check(directory and all(u["path"].startswith(com["path"]) for u in directory), f"دليل الجهات له: كليته وأقسامها فقط ({len(directory)} جهات)")
    status, _ = entry_com.call("GET", f"/api/org/units/{eng['id']}/indicators")
    check(status == 404, "لا يفتح مؤشرات كلية أخرى")
    status, _ = entry_com.call("GET", "/api/scorecard")
    check(status == 403 and planner.call("GET", "/api/scorecard")[0] == 200, "بطاقة الجامعة كاملة لمن نطاقه الجامعة فقط")
    row = next(c for c in entry_com.get("/api/cycles") if c["id"] == cycle["id"])
    check(row["forms"] == len(mine), f"أعداد الدورة له من نماذج جهته ({row['forms']}) لا الجامعة ({len(forms)})")

    step("5. جهة تطلع على الجامعة كاملة")
    edit = {"nameAr": qac["nameAr"], "nameEn": qac["nameEn"], "sortOrder": qac["sortOrder"], "effectiveOn": None, "reason": None, "version": None}
    status, err = planner.call("PUT", f"/api/org/units/{qac['id']}", {**edit, "viewsWholeUniversity": True})
    check(status == 403 and err.get("code") == "grants_required", "مدير الخطة لا يوسع اطلاع جهة")
    saved = admin.call("PUT", f"/api/org/units/{qac['id']}", {**edit, "viewsWholeUniversity": True}, expect=200)[1]
    check(saved["viewsWholeUniversity"], "المدير الأعلى (يدير الصلاحيات) جعل QAC تطلع على الجامعة كاملة")
    seen = central.get(f"/api/forms?cycleId={cycle['id']}")
    check(len(seen) == len(forms), f"مدخل QAC يرى كل النماذج ({len(seen)} من {len(forms)})")
    check(len(central.get("/api/org/units")) == len(units), "ويرى دليل الجهات كاملًا")
    f = central.get(f"/api/forms/{forms[eng['id']]['id']}")
    check(not f["actions"]["canEdit"], "ويطلع على نموذج الهندسة دون أن يعدله")
    f = central.get(f"/api/forms/{forms[qac['id']]['id']}")
    check(f["actions"]["canEdit"], "ويعدل نموذج جهته")
    admin.call("PUT", f"/api/org/units/{qac['id']}", {**edit, "viewsWholeUniversity": False}, expect=200)
    seen = central.get(f"/api/forms?cycleId={cycle['id']}")
    check(len(seen) < len(forms), "إلغاء الاطلاع يعيده إلى جهاته فورًا")

    step("6. إعادة ما تغير")
    for u in (com, qac, stu):
        planner.call("PUT", f"/api/org/units/{u['id']}/indicators", {"custom": False, "indicatorIds": []}, expect=200)
    planner.call("POST", f"/api/cycles/{cycle['id']}/close", {"note": "انتهى اختبار التكليف"}, expect=200)
    back = {u["code"]: u for u in planner.get("/api/org/units")}
    check(not back["COM"]["customIndicators"] and back["QAC"]["indicatorCount"] == 0, "عادت الجهات إلى الافتراضي وأغلقت البروفة")

    step("7. وثيقة الخطة الاستراتيجية: يرفعها مدير الخطة ويطلع عليها الجميع")
    from collection_test import PDF, upload  # noqa: E402
    import urllib.request
    status, _ = upload(entry_com, "/api/strategy/documents", "plan.pdf", PDF)
    check(status == 403, "مدخل البيانات لا يرفع وثيقة الخطة")
    status, err = upload(planner, "/api/strategy/documents", "plan.docx", b"PK\x03\x04rest")
    check(status == 400 and err.get("code") == "pdf_only", "الوثيقة PDF فقط")
    status, doc = upload(planner, "/api/strategy/documents", "plan.pdf", PDF, {"description": "خطة اختبار"})
    check(status == 200 and doc["description"] == "خطة اختبار", "رفع مدير الخطة إصدارًا")
    seen = entry_com.get("/api/strategy/documents")
    check(seen and seen[0]["document"]["id"] == doc["id"], "مدخل التجارة يرى الإصدار الأحدث")
    with entry_com.opener.open(urllib.request.Request(st_base() + f"/api/strategy/documents/{doc['id']}/view")) as r:
        body, ctype, frame = r.read(), r.headers.get("Content-Type", ""), r.headers.get("X-Frame-Options")
    check(body.startswith(b"%PDF") and ctype.startswith("application/pdf") and frame == "SAMEORIGIN", "يعرض داخل المنصة (PDF يسمح بإطار من المنظومة نفسها)")
    planner.call("POST", f"/api/documents/{doc['id']}/void", {"note": "انتهى الاختبار"}, expect=200)
    check(all(d["document"]["id"] != doc["id"] for d in entry_com.get("/api/strategy/documents")), "الإصدار المبطل يختفي عن أصحاب الحسابات")
    status, _ = entry_com.call("GET", f"/api/strategy/documents/{doc['id']}/view")
    check(status == 404, "ولا يفتح لهم")

    failed = [n for ok, n in RESULTS if not ok]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} passed")
    if failed:
        raise SystemExit(1)


def st_base():
    import scenario_test
    return scenario_test.BASE


def units_by_id(units):
    return {u["id"]: u for u in units.values()}


if __name__ == "__main__":
    main()
