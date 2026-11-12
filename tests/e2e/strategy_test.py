# -*- coding: utf-8 -*-
"""
اختبار شجرة الخطة على خادم يعمل: إضافة ركيزة ومعيار ومبادرة ومؤشر وتعديلها وحذفها، وخطة المستهدف بسنة بدء وسنة تحقق،
وحماية ما يقوم عليه غيره من الحذف، والصلاحيات. ينظف ما أنشأه، فيتكرر على قاعدة واحدة.

التشغيل (الخادم يعمل على http://localhost:5150):
    python tests/e2e/strategy_test.py
"""
import os
import sys
import uuid

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenario_test import RESULTS, Client, check, step  # noqa: E402


def main():
    planner = Client("planner@tier1.local")
    committee = Client("committee@tier1.local")
    viewer = Client("viewer@tier1.local")
    tag = uuid.uuid4().hex[:5]

    step("1. الشجرة والصلاحيات")
    tree = planner.get("/api/strategy/tree")
    check(len(tree["pillars"]) >= 6 and tree["years"][0] == tree["baselineYear"] + 1 and len(tree["years"]) == 5, "الشجرة بركائزها وسنوات الخطة الخمس")
    check(tree["actions"]["canAdd"] and tree["actions"]["canDelete"], "مدير الخطة يضيف ويحذف")
    v = viewer.get("/api/strategy/tree")
    check(not v["actions"]["canEdit"] and not v["actions"]["canEditTargets"], "مستخدم الاطلاع يرى الشجرة بلا تعديل")
    status, _ = viewer.call("POST", "/api/strategy/pillars", {"nameAr": f"ركيزة {tag}"})
    check(status == 403, "مستخدم الاطلاع لا يضيف ركيزة")

    step("2. ركيزة ومعيار ومبادرة")
    pillar = planner.call("POST", "/api/strategy/pillars", {"nameAr": f"ركيزة تجريبية {tag}", "description": "شرح الركيزة"}, expect=200)[1]
    check(pillar["code"].startswith("P"), f"أنشئت الركيزة {pillar['code']} برمز تلقائي")
    status, err = planner.call("POST", "/api/strategy/pillars", {"nameAr": f"ركيزة تجريبية {tag}"})
    check(status == 422 and err.get("code") == "duplicate_name", "لا تتكرر الركيزة بالاسم نفسه")
    pillar = planner.call("PUT", f"/api/strategy/pillars/{pillar['id']}", {"nameAr": pillar["nameAr"], "description": "شرح معدل", "version": pillar["version"]}, expect=200)[1]
    check(pillar["description"] == "شرح معدل", "عدل شرح الركيزة")
    std = planner.call("POST", "/api/strategy/standards", {"pillarId": pillar["id"], "nameAr": f"معيار تجريبي {tag}", "description": "ما يقيسه"}, expect=200)[1]
    check(std["code"].startswith("S"), f"أنشئ المعيار {std['code']}")
    status, err = planner.call("POST", "/api/strategy/initiatives",
                               {"standardId": std["id"], "nameAr": "مبادرة", "startYear": 2030, "endYear": 2028, "status": "Planned", "indicatorCodes": []})
    check(status == 400 and err.get("code") == "plan_order", "سنة انتهاء المبادرة لا تسبق بدايتها")
    ini = planner.call("POST", "/api/strategy/initiatives",
                       {"standardId": std["id"], "nameAr": f"مبادرة {tag}", "description": "أنشطة", "startYear": 2027, "endYear": 2029, "status": "InProgress", "indicatorCodes": []},
                       expect=200)[1]
    check(ini["code"] == f"{std['code']}-M01", f"أنشئت المبادرة {ini['code']}")

    step("3. مؤشر وخطة مستهدفه")
    created = planner.call("POST", "/api/kpi/indicators", {"standardCode": std["code"], "nameAr": f"مؤشر تجريبي {tag}", "unit": "%", "level": "College"}, expect=200)[1]
    code = created["code"]
    node = next(i for p in planner.get("/api/strategy/tree")["pillars"] for s in p["standards"] if s["id"] == std["id"] for i in s["indicators"])
    status, err = planner.call("PUT", f"/api/kpi/indicators/{code}/plan",
                               {"startYear": 2029, "endYear": 2031, "values": [{"year": 2028, "value": 50}], "version": node["version"]})
    check(status == 400 and err.get("code") == "before_start", "لا مستهدف قبل سنة بدء المؤشر")
    status, _ = planner.call("PUT", f"/api/kpi/indicators/{code}/plan",
                             {"startYear": 2029, "endYear": 2031, "note": "يبدأ بعد سنتين", "values": [{"year": 2029, "value": 40}, {"year": 2031, "value": 70}],
                              "version": node["version"]})
    check(status == 204, "حفظت خطة مؤشر يبدأ بعد سنتين ويتحقق في نهاية الخطة")
    node = next(i for p in planner.get("/api/strategy/tree")["pillars"] for s in p["standards"] if s["id"] == std["id"] for i in s["indicators"])
    check((node["planStartYear"], node["planEndYear"], node["planNote"]) == (2029, 2031, "يبدأ بعد سنتين"), "سنتا البدء والتحقق والشرح محفوظة")
    check({t["year"]: t["value"] for t in node["targets"]} == {2029: 40, 2031: 70}, "قيم السنوات محفوظة")
    status, _ = planner.call("PUT", f"/api/kpi/indicators/{code}/plan", {"startYear": 2029, "endYear": 2031, "values": [{"year": 2029, "value": 120}], "version": node["version"]})
    check(status == 400, "نسبة مستهدفة فوق 100 ترفض")
    target = next(t for t in node["targets"] if t["year"] == 2031)
    committee.call("POST", f"/api/kpi/targets/{target['id']}/approve", {"decisionRef": "محضر تجريبي"}, expect=200)
    node = next(i for p in planner.get("/api/strategy/tree")["pillars"] for s in p["standards"] if s["id"] == std["id"] for i in s["indicators"])
    status, _ = planner.call("PUT", f"/api/kpi/indicators/{code}/plan", {"startYear": 2029, "endYear": 2031, "values": [{"year": 2031, "value": 75}], "version": node["version"]})
    check(status == 403, "تعديل مستهدف معتمد يحتاج صلاحية الاعتماد")
    ini = planner.call("PUT", f"/api/strategy/initiatives/{ini['id']}",
                       {"standardId": std["id"], "nameAr": ini["nameAr"], "startYear": 2027, "endYear": 2029, "status": "InProgress", "indicatorCodes": [code], "version": ini["version"]},
                       expect=200)[1]
    check(ini["indicatorCodes"] == [code], "ربطت المبادرة بالمؤشر")

    step("4. الحذف يحمي ما يقوم عليه غيره")
    status, err = planner.call("POST", f"/api/strategy/standards/{std['id']}/delete", {"reason": "تنظيف الاختبار"})
    check(status == 409 and err.get("code") == "standard_in_use", "لا يحذف معيار تحته مؤشر ومبادرة")
    status, err = planner.call("POST", f"/api/strategy/pillars/{pillar['id']}/delete", {"reason": "تنظيف الاختبار"})
    check(status == 409 and err.get("code") == "pillar_in_use", "لا تحذف ركيزة تحتها معيار")
    status, _ = planner.call("POST", f"/api/kpi/indicators/{code}/delete", {"reason": "قصير"})
    check(status == 400, "الحذف يطلب سببًا واضحًا")
    planner.call("POST", f"/api/kpi/indicators/{code}/delete", {"reason": "تنظيف الاختبار"}, expect=204)
    tree = planner.get("/api/strategy/tree")
    left = next(m for p in tree["pillars"] for s in p["standards"] for m in s["initiatives"] if m["id"] == ini["id"])
    check(left["indicatorCodes"] == [], "حذف المؤشر أزاله من المبادرة")
    planner.call("POST", f"/api/strategy/initiatives/{ini['id']}/delete", {"reason": "تنظيف الاختبار"}, expect=204)
    planner.call("POST", f"/api/strategy/standards/{std['id']}/delete", {"reason": "تنظيف الاختبار"}, expect=204)
    planner.call("POST", f"/api/strategy/pillars/{pillar['id']}/delete", {"reason": "تنظيف الاختبار"}, expect=204)
    tree = planner.get("/api/strategy/tree")
    check(all(p["id"] != pillar["id"] for p in tree["pillars"]), "حذفت الركيزة بعد إفراغها")

    passed = sum(1 for ok, _ in RESULTS if ok)
    print(f"\n{passed}/{len(RESULTS)} passed")
    sys.exit(0 if passed == len(RESULTS) else 1)


if __name__ == "__main__":
    main()
