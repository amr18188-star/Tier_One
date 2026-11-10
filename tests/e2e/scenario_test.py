# -*- coding: utf-8 -*-
"""
اختبار شامل على خادم يعمل فعلًا (قاعدة حقيقية وحسابات تجريبية بأدوارها):
دورة تعريف المؤشر واعتماده، والإصدار الجديد، والمستهدفات ونطاق الكليات، والتعارضات وقرار اللجنة، والهيكل.

التشغيل (الخادم يعمل على http://localhost:5150 بقاعدة تجريبية):
    python tests/e2e/scenario_test.py
كلمة مرور الحسابات التجريبية تقرأ من .env (T1_DEMO_PASSWORD).
"""
import datetime
import http.cookiejar
import json
import os
import sys
import urllib.error
import urllib.request
import uuid

BASE = os.environ.get("T1_BASE", "http://localhost:5150")
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def demo_password() -> str:
    with open(os.path.join(ROOT, ".env"), encoding="ascii") as f:
        for line in f:
            if line.startswith("T1_DEMO_PASSWORD="):
                return line.split("=", 1)[1].strip()
    raise SystemExit("T1_DEMO_PASSWORD is missing from .env")


PASSWORD = demo_password()
RESULTS: list[tuple[bool, str]] = []


class Client:
    """جلسة مستخدم واحد: كوكي الجلسة ورمز CSRF المرتبط بها."""

    def __init__(self, email: str):
        self.email = email
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))
        self.csrf = None
        self.call("POST", "/api/auth/login", {"email": email, "password": PASSWORD}, expect=200)
        self.csrf = None  # الرمز مرتبط بالهوية؛ يجدد بعد الدخول.

    def _token(self) -> str:
        if self.csrf is None:
            self.csrf = self._raw("GET", "/api/auth/csrf")[1]["token"]
        return self.csrf

    def _raw(self, method: str, path: str, body=None):
        headers = {"Accept-Language": "ar"}
        data = None
        if body is not None:
            data = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json"
        if method != "GET":
            headers["X-XSRF-TOKEN"] = self._token() if path != "/api/auth/csrf" else ""
        req = urllib.request.Request(BASE + path, data=data, method=method, headers=headers)
        try:
            with self.opener.open(req) as r:
                text = r.read().decode("utf-8")
                return r.status, (json.loads(text) if text else None)
        except urllib.error.HTTPError as e:
            text = e.read().decode("utf-8")
            try:
                return e.code, json.loads(text)
            except json.JSONDecodeError:
                return e.code, {"title": text}

    def call(self, method: str, path: str, body=None, expect: int | None = None):
        status, data = self._raw(method, path, body)
        if expect is not None and status != expect:
            raise AssertionError(f"{self.email} {method} {path}: expected {expect}, got {status}: {data}")
        return status, data

    def get(self, path):
        return self.call("GET", path, expect=200)[1]


def check(ok: bool, name: str):
    RESULTS.append((ok, name))
    print(("  ✓ " if ok else "  ✗ ") + name)


def step(title: str):
    print("\n" + title)


def main():
    planner = Client("planner@tier1.local")
    committee = Client("committee@tier1.local")
    dean = Client("dean@tier1.local")
    college = Client("college@tier1.local")
    viewer = Client("viewer@tier1.local")
    sysadmin = Client("sysadmin@tier1.local")

    units = planner.get("/api/org/units")
    uni = next(u for u in units if u["kind"] == "University")
    eng = next(u for u in units if u["code"] == "ENG")
    cs = next(u for u in units if u["code"] == "CS")

    # ---------------------------------------------------------------- القاموس
    step("1. تعريف المؤشر: الإكمال ثم الإرسال ثم الاعتماد بفصل المهام")
    # مؤشر لم يمسه تشغيل سابق: مسودة لا ينقصها إلا الجهة المالكة، فيتكرر الاختبار على قاعدة واحدة.
    d = None
    for row in planner.get("/api/kpi/indicators?status=Draft"):
        if row["versionNumber"] != 1 or row["missingCount"] != 1:
            continue
        candidate = planner.get(f"/api/kpi/indicators/{row['code']}")
        if candidate["current"]["missing"] == ["الجهة المالكة"] and candidate["effective"] is None:
            d = candidate
            break
    if d is None:
        raise SystemExit("No untouched draft indicator left; reset the demo database.")
    code = d["code"]
    print(f"  (المؤشر: {code})")
    check(d["current"]["status"] == "Draft", "المؤشر يبدأ مسودة من البذرة")
    if d["current"]["status"] == "Draft":
        status, _ = planner.call("POST", f"/api/kpi/indicators/{code}/submit")
        check(status == 422, "الإرسال يرفض والتعريف ناقص (الجهة المالكة)")
        v = d["current"]
        body = {k: v[k] for k in ("nameAr", "nameEn", "definition", "purpose", "exclusions", "numerator", "denominator", "unit", "direction",
                                  "frequency", "level", "aggregation", "aggregationNote", "dataSource", "evidenceRequired")}
        body.update(ownerUnitId=uni["id"], version=v["version"])
        d = planner.call("PUT", f"/api/kpi/indicators/{code}/definition", body, expect=200)[1]
        check(d["current"]["missing"] == [], "اكتمل التعريف بعد تحديد الجهة المالكة")
        d = planner.call("POST", f"/api/kpi/indicators/{code}/submit", expect=200)[1]
        check(d["current"]["status"] == "UnderReview", "أرسل للاعتماد")

        status, _ = planner.call("POST", f"/api/kpi/indicators/{code}/approve", {"decisionRef": "محضر 1"})
        check(status == 403, "مدير متابعة الخطة لا يملك الاعتماد (403)")
        status, err = committee.call("POST", f"/api/kpi/indicators/{code}/approve", {"decisionRef": " "})
        check(status == 400 and err.get("code") == "decision_ref_required", "الاعتماد يطلب رقم المحضر")
        d = committee.call("POST", f"/api/kpi/indicators/{code}/approve", {"decisionRef": "محضر اللجنة العليا 1/2026"}, expect=200)[1]
        check(d["current"]["status"] == "Approved", "اعتمدت اللجنة العليا التعريف")

    step("2. تعديل تعريف معتمد ينشئ إصدارًا جديدًا ويبقى المعتمد ساريًا")
    v = d["current"]
    body = {k: v[k] for k in ("nameAr", "nameEn", "definition", "purpose", "exclusions", "numerator", "denominator", "unit", "direction",
                              "frequency", "level", "aggregation", "aggregationNote", "dataSource", "ownerUnitId", "evidenceRequired")}
    if d["actions"]["editCreatesVersion"]:
        status, err = planner.call("PUT", f"/api/kpi/indicators/{code}/definition", body)
        check(status == 400 and err.get("code") == "change_reason_required", "تعديل المعتمد بلا سبب يرفض")
        body["changeReason"] = "حصر الأبحاث في Scopus فقط بقرار اللجنة"
        body["denominator"] = "الأبحاث المفهرسة في Scopus"
        d = planner.call("PUT", f"/api/kpi/indicators/{code}/definition", body, expect=200)[1]
        check(d["current"]["number"] >= 2 and d["current"]["status"] == "Draft", f"أنشئ الإصدار {d['current']['number']} مسودة")
        check(d["effective"] is not None and d["effective"]["denominator"] != "الأبحاث المفهرسة في Scopus", "الإصدار المعتمد السابق ما زال ساريًا بمقامه القديم")
    else:
        check(True, "الإصدار الجديد موجود من تشغيل سابق")

    # ---------------------------------------------------------------- المستهدفات
    step("3. المستهدفات: نطاق الكليات والتوزيع وفصل المهام في الاعتماد")
    # مؤشر مجموع على مستوى الكلية وسنة بلا مستهدف للهندسة بعد، فيتكرر الاختبار على قاعدة واحدة.
    code2, year = None, None
    sums = [r["code"] for r in planner.get("/api/kpi/indicators") if r["aggregation"] == "Sum" and r["level"] == "College" and not r["suspended"]]
    for candidate in ["S04-09"] + [c for c in sums if c != "S04-09"]:
        existing = {t["year"] for t in planner.get(f"/api/kpi/indicators/{candidate}/targets")["colleges"] if t["unitId"] == eng["id"]}
        free = next((y for y in range(2027, 2032) if y not in existing), None)
        if free is not None:
            code2, year = candidate, free
            break
    if code2 is None:
        raise SystemExit("Every sum indicator already has engineering targets; reset the demo database.")
    print(f"  (المؤشر {code2}، السنة {year})")
    dist = planner.get(f"/api/kpi/indicators/{code2}/distribution?year={year}")
    check(dist["result"]["status"] in ("Covers", "Short", "Incomplete"), f"التوزيع: {dist['result']['summaryAr']}")
    status, _ = dean.call("PUT", f"/api/kpi/indicators/{code2}/targets", {"unitId": cs["id"], "year": year, "kind": "Target", "value": 3})
    check(status == 404, "عميد الهندسة لا يصل إلى كلية الحاسبات (404)")
    status, _ = dean.call("PUT", f"/api/kpi/indicators/{code2}/targets", {"unitId": uni["id"], "year": year, "kind": "Target", "value": 30})
    check(status == 404, "عميد الهندسة لا يعدل مستهدف الجامعة")
    t = dean.call("PUT", f"/api/kpi/indicators/{code2}/targets", {"unitId": eng["id"], "year": year, "kind": "Target", "value": 3, "source": "خطة الكلية"}, expect=200)[1]
    check(t["value"] == 3, "عميد الهندسة يدخل مستهدف كليته")
    status, err = dean.call("PUT", f"/api/kpi/indicators/S04-08/targets", {"unitId": eng["id"], "year": year, "kind": "Target", "value": 120})
    check(status == 400 and err.get("code") == "percent_range", "النسبة فوق 100 ترفض برسالة واضحة")
    status, err = dean.call("PUT", f"/api/kpi/indicators/S04-08/targets", {"unitId": eng["id"], "year": year, "kind": "Target", "value": None, "numerator": 520, "denominator": 500})
    check(status == 400 and "520" in (err.get("title") or ""), "البسط أكبر من المقام يرفض ويذكر الرقمين")
    dist2 = planner.get(f"/api/kpi/indicators/{code2}/distribution?year={year}")
    eng_share = next(s for s in dist2["shares"] if s["unitId"] == eng["id"])
    check(eng_share["value"] == 3 and not eng_share["fromEachCollegeDefault"], "التوزيع يأخذ مستهدف الكلية الخاص بدل الموحد")
    status, err = committee.call("POST", f"/api/kpi/targets/{t['id']}/approve", {"decisionRef": "محضر 2"})
    check(status == 200, "اللجنة تعتمد مستهدف الكلية")
    status, err = dean.call("PUT", f"/api/kpi/indicators/{code2}/targets", {"unitId": eng["id"], "year": year, "kind": "Target", "value": 4})
    check(status == 403, "تعديل المستهدف المعتمد يحتاج صلاحية الاعتماد")
    status, err = committee.call("PUT", f"/api/kpi/indicators/{code2}/targets", {"unitId": eng["id"], "year": year, "kind": "Target", "value": 4})
    check(status == 403, "اللجنة العليا لا تدخل مستهدفات (ليست مدخلة بيانات)")

    # ---------------------------------------------------------------- التعارضات
    step("4. التعارضات: التجهيز ثم قرار اللجنة برقم المحضر")
    c = planner.get("/api/kpi/conflicts/C-16")
    if c["status"] != "Resolved":
        status, _ = planner.call("POST", "/api/kpi/conflicts/C-16/decide", {"defer": False, "decision": "قرار", "decisionDate": "2026-11-10", "minutesRef": "1"})
        check(status == 403, "مدير متابعة الخطة لا يسجل قرار اللجنة")
        c = planner.call("POST", "/api/kpi/conflicts/C-16/prepare", {"proposal": "توزيع مستهدف الجامعة على الكليات صراحة", "version": c["version"]}, expect=200)[1]
        check(c["status"] == "Ready", "التجهيز يدخل التعارض جدول الأعمال")
        agenda = committee.get("/api/kpi/conflicts/agenda")
        check(any(a["code"] == "C-16" for a in agenda), "التعارض ظاهر في جدول أعمال اللجنة")
        status, err = committee.call("POST", "/api/kpi/conflicts/C-16/decide", {"defer": False, "decision": "يوزع مستهدف الجامعة", "decisionDate": "2026-11-10", "version": c["version"]})
        check(status == 400 and err.get("code") == "minutes_required", "القرار بلا رقم محضر يرفض")
        c = committee.call("POST", "/api/kpi/conflicts/C-16/decide", {"defer": False, "decision": "يوزع مستهدف الجامعة على الكليات صراحة", "decisionDate": "2026-11-10", "minutesRef": "محضر 3/2026", "version": c["version"]}, expect=200)[1]
        check(c["status"] == "Resolved" and c["minutesRef"] == "محضر 3/2026", "سجل قرار اللجنة العليا")
    status, _ = planner.call("POST", "/api/kpi/conflicts/C-16/prepare", {"proposal": "تعديل بعد القرار"})
    check(status == 422, "لا يعدل التعارض بعد حسمه")

    # ---------------------------------------------------------------- الهيكل
    step("5. هيكل الكلية: النطاق والاستبدال وإخفاء بيانات التواصل")
    template = college.get("/api/structure/template")
    dean_pos = next(p for u in template if u["code"] == "DEAN" for p in u["positions"])
    name = f"أ.د. عميد تجريبي {uuid.uuid4().hex[:4]}"
    status, _ = college.call("POST", "/api/structure/members", {"collegeId": cs["id"], "positionId": dean_pos["id"], "fullName": name, "startDate": "2026-09-01", "replaceCurrent": False})
    check(status == 404, "لجنة كلية الهندسة لا تضيف إلى كلية الحاسبات")
    chart = college.get(f"/api/structure/colleges/{eng['id']}")
    occupied = any(m["positionId"] == dean_pos["id"] for m in chart["members"])
    body = {"collegeId": eng["id"], "positionId": dean_pos["id"], "fullName": name, "email": "dean.test@cu.edu.eg", "phone": "0100 000 0000",
            "startDate": "2026-11-01" if occupied else "2026-09-01", "replaceCurrent": False}
    if occupied:
        status, err = college.call("POST", "/api/structure/members", body)
        check(status == 409 and err.get("code") == "position_taken", "المنصب المشغول يطلب «استبدال الحالي»")
        body["replaceCurrent"] = True
        # بعد بداية الشاغل الحالي بيوم، فيتكرر الاختبار على قاعدة واحدة.
        # الاستبدال في تشغيل سابق قد يبدأ غدًا فلا يظهر في هيكل اليوم؛ أحدث بداية من هيكل تاريخ بعيد.
        future = college.get(f"/api/structure/colleges/{eng['id']}?asOf=2099-12-31")
        current_start = max(m["startDate"] for m in chart["members"] + future["members"] if m["positionId"] == dean_pos["id"])
        body["startDate"] = (datetime.date.fromisoformat(current_start) + datetime.timedelta(days=1)).isoformat()
    m = college.call("POST", "/api/structure/members", body, expect=200)[1]
    check(m["fullName"] == name and m["phone"] == "01000000000", "أضيف العميد بالهاتف بعد توحيده")
    status, _ = viewer.call("GET", f"/api/structure/colleges/{eng['id']}")
    check(status == 404, "مستخدم الاطلاع على الحاسبات لا يرى هيكل الهندسة")
    dchart = dean.get(f"/api/structure/colleges/{eng['id']}")
    check(dchart["contactsVisible"] and any(x["email"] for x in dchart["members"]), "العميد يرى بيانات التواصل")
    deptrep = Client("deptrep@tier1.local")
    rchart = deptrep.call("GET", f"/api/structure/colleges/{eng['id']}")
    check(rchart[0] == 404, "ممثل القسم نطاقه القسم فقط لا الكلية كلها")
    status, _ = sysadmin.call("GET", f"/api/structure/colleges/{eng['id']}")
    check(status == 403, "مسؤول النظام لا يرى أعضاء الهياكل (فصل المهام)")

    # ---------------------------------------------------------------- الرئيسية
    step("6. «مطلوب منك» بحسب الدور")
    home = committee.get("/api/home")
    check(any(t["key"] == "targets_proposed" for t in home["tasks"]), "اللجنة ترى المستهدفات المقترحة التي تنتظرها")
    home = planner.get("/api/home")
    check(any(t["key"] == "conflicts_prepare" for t in home["tasks"]), "مدير الخطة يرى التعارضات المحتاجة للتجهيز")
    check(not any(t["key"] == "definitions_review" for t in home["tasks"]), "مدير الخطة لا يرى مهمة الاعتماد")
    status, _ = viewer.call("PUT", f"/api/kpi/indicators/{code}/definition", body)
    check(status == 403, "مستخدم الاطلاع لا يعدل")

    passed = sum(1 for ok, _ in RESULTS if ok)
    print(f"\n{passed}/{len(RESULTS)} passed")
    sys.exit(0 if passed == len(RESULTS) else 1)


if __name__ == "__main__":
    main()
