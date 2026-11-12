# -*- coding: utf-8 -*-
"""
اختبار شامل لجمع البيانات على خادم يعمل فعلًا: فتح دورة، وإدخال نموذج كلية بالتحقق والأدلة، والإعادة بملاحظات،
ومسار الاعتماد بفصل المهام حتى الجودة المركزية، والقفل وإعادة الفتح، والاستيراد من Excel، والنتائج والتصدير، والبريد.

التشغيل (الخادم يعمل على http://localhost:5150 بقاعدة تجريبية):
    python tests/e2e/collection_test.py
"""
import datetime
import glob
import io
import os
import sys
import uuid

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenario_test import BASE, RESULTS, ROOT, Client, check, step  # noqa: E402

import urllib.error  # noqa: E402
import urllib.request  # noqa: E402


def upload(client: Client, path: str, name: str, content: bytes, fields: dict | None = None, query: str = ""):
    """رفع ملف واحد multipart بالجلسة نفسها ورمز CSRF."""
    boundary = "----t1" + uuid.uuid4().hex
    parts = []
    for k, v in (fields or {}).items():
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n'.encode("utf-8"))
    parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n'.encode("utf-8"))
    parts.append(content + b"\r\n")
    parts.append(f"--{boundary}--\r\n".encode("utf-8"))
    body = b"".join(parts)
    headers = {"Accept-Language": "ar", "Content-Type": f"multipart/form-data; boundary={boundary}", "X-XSRF-TOKEN": client._token()}
    req = urllib.request.Request(BASE + path + query, data=body, method="POST", headers=headers)
    import json
    try:
        with client.opener.open(req) as r:
            return r.status, json.loads(r.read().decode("utf-8") or "null")
    except urllib.error.HTTPError as e:
        text = e.read().decode("utf-8")
        try:
            return e.code, json.loads(text)
        except json.JSONDecodeError:
            return e.code, {"title": text}


def download(client: Client, path: str) -> tuple[int, bytes, str]:
    req = urllib.request.Request(BASE + path, headers={"Accept-Language": "ar"})
    try:
        with client.opener.open(req) as r:
            return r.status, r.read(), r.headers.get("Content-Type", "")
    except urllib.error.HTTPError as e:
        return e.code, e.read(), ""


PDF = b"%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"


def input_for(item: dict, good: bool = True) -> dict:
    """قيمة صالحة بحسب وحدة المؤشر وقاعدة تجميعه."""
    body = {"indicatorId": item["indicatorId"], "kind": "Number", "value": None, "numerator": None, "denominator": None,
            "reason": None, "explanation": None, "version": item["value"]["version"] if item["value"] else None}
    if item["usesRatio"]:
        body.update(numerator=45, denominator=50)
    elif item["unit"].strip() == "%":
        body["value"] = 80
    else:
        body["value"] = 12
    if item["aggregation"] == "WeightedAverage":
        # المتوسط المرجح لا يقبل بلا وزنه.
        body["denominator"] = 30
    if item["value"] and item["value"]["kind"] == "Number" and item["value"]["value"] is not None:
        # قيمة سابقة في النموذج: لا تغيير كبير يطلب تفسيرًا.
        pass
    return body


def fill(entry: Client, form: dict, note: str = "") -> dict:
    """إدخال كل مؤشرات النموذج وإرفاق الأدلة المطلوبة."""
    for section in form["sections"]:
        for item in section["items"]:
            body = input_for(item)
            status, saved = entry.call("PUT", f"/api/forms/{form['id']}/values", body)
            if status == 400 and saved.get("code"):
                raise AssertionError(f"{item['code']}: {saved}")
            if any(i["code"] == "big_change" for i in saved["issues"]):
                body.update(explanation="تغير متوقع بعد إعادة هيكلة البرامج " + note, version=saved["value"]["version"])
                saved = entry.call("PUT", f"/api/forms/{form['id']}/values", body, expect=200)[1]
            if item["evidenceRequired"] and not [e for e in saved["evidence"] if not e["voided"]]:
                upload(entry, f"/api/forms/{form['id']}/values/{item['indicatorId']}/evidence", "evidence.pdf", PDF, {"description": "كشف من شؤون الطلاب"})
    return entry.get(f"/api/forms/{form['id']}")


def main():
    planner = Client("planner@tier1.local")
    entry = Client("entry@tier1.local")
    college = Client("college@tier1.local")
    dean = Client("dean@tier1.local")
    quality = Client("quality@tier1.local")
    deptrep = Client("deptrep@tier1.local")
    viewer = Client("viewer@tier1.local")

    units = planner.get("/api/org/units")
    eng = next(u for u in units if u["code"] == "ENG")
    civ = next(u for u in units if u["code"] == "ENG-CIV")
    mail_dir = os.path.join(ROOT, "data", "mail")
    mails_before = len(glob.glob(os.path.join(mail_dir, "*.eml")))

    # ---------------------------------------------------------------- الدورة
    step("1. إنشاء دورة تجريبية وفتحها")
    today = datetime.date.today()
    label = f"تجربة-{uuid.uuid4().hex[:6]}"
    body = {"name": f"الدورة التجريبية {label}", "periodLabel": label, "targetYear": 2027, "opensOn": today.isoformat(),
            "dueOn": (today + datetime.timedelta(days=14)).isoformat(), "trial": True, "version": None}
    status, _ = viewer.call("POST", "/api/cycles", body)
    check(status == 403, "مستخدم الاطلاع لا ينشئ دورة")
    status, err = planner.call("POST", "/api/cycles", {**body, "dueOn": (today - datetime.timedelta(days=1)).isoformat()})
    check(status == 400 and err.get("code") == "dates", "الموعد النهائي قبل الفتح يرفض")
    cycle = planner.call("POST", "/api/cycles", body, expect=200)[1]
    check(cycle["status"] == "Draft", "أنشئت الدورة قيد الإعداد")
    check(all(c["id"] != cycle["id"] for c in entry.get("/api/cycles")), "الدورة قيد الإعداد لا تظهر لمدخل البيانات")
    preview = planner.get(f"/api/cycles/{cycle['id']}/preview")
    check(preview["indicators"] > 0 and preview["forms"] > 0, f"المعاينة: {preview['indicators']} مؤشر في {preview['forms']} نموذج")
    cycle = planner.call("POST", f"/api/cycles/{cycle['id']}/open", expect=200)[1]
    check(cycle["status"] == "Open", "فتحت الدورة وأنشئت النماذج")
    status, _ = planner.call("POST", f"/api/cycles/{cycle['id']}/open")
    check(status in (400, 409, 422), "لا تفتح الدورة مرتين")

    # ---------------------------------------------------------------- الإدخال
    step("2. نماذج مدخل البيانات وإدخال القيم بالتحقق")
    mine = entry.get(f"/api/forms?cycleId={cycle['id']}&awaitingMe=true")
    check(any(f["unitId"] == eng["id"] for f in mine), "نموذج كلية الهندسة في «مطلوب مني»")
    check(all(f["unitName"] for f in mine) and not any(f["unitId"] != eng["id"] and f["parentName"] not in (None, eng["nameAr"]) for f in mine),
          "مدخل الهندسة لا يرى نماذج كليات أخرى")
    form_row = next(f for f in mine if f["unitId"] == eng["id"])
    status, _ = viewer.call("GET", f"/api/forms/{form_row['id']}")
    check(status == 404, "مستخدم الاطلاع على الحاسبات لا يرى نموذج الهندسة (404)")
    form = entry.get(f"/api/forms/{form_row['id']}")
    items = [i for s in form["sections"] for i in s["items"]]
    check(form["actions"]["canEdit"] and not form["actions"]["canReview"], "المدخل يعدل ولا يراجع")
    check(form["path"][0]["state"] == "current", "مسار الاعتماد يبدأ بخطوة الإدخال")

    pct = next((i for i in items if i["unit"].strip() == "%" and not i["usesRatio"]), None)
    if pct:
        status, err = entry.call("PUT", f"/api/forms/{form['id']}/values", {**input_for(pct), "value": 150})
        check(status == 400, "نسبة أكبر من 100% ترفض")
    ratio = next((i for i in items if i["usesRatio"]), None)
    if ratio:
        status, err = entry.call("PUT", f"/api/forms/{form['id']}/values", {**input_for(ratio), "numerator": 60, "denominator": 50})
        check(status == 400, "البسط أكبر من المقام في نسبة يرفض")
        saved = entry.call("PUT", f"/api/forms/{form['id']}/values", input_for(ratio), expect=200)[1]
        check(abs(saved["value"]["value"] - 90) < 0.001, "النسبة تحسب في الخادم من البسط والمقام (45/50 = 90%)")
    na = items[-1]
    status, err = entry.call("PUT", f"/api/forms/{form['id']}/values", {**input_for(na), "kind": "NotAvailable", "reason": "لا"})
    check(status == 400, "«غير متاح» بلا سبب كاف يرفض")

    status, err = entry.call("POST", f"/api/forms/{form['id']}/submit")
    check(status in (400, 422) and err.get("code"), "الإرسال يرفض والنموذج ناقص")

    ev_item = next((i for i in items if i["evidenceRequired"]), items[0])
    status, err = upload(entry, f"/api/forms/{form['id']}/values/{ev_item['indicatorId']}/evidence", "fake.pdf", b"hello world")
    check(status == 400, "ملف لا يطابق توقيع PDF يرفض قبل حفظ القيمة أو بعده")

    form = fill(entry, form)
    check(form["completed"] == form["required"], f"اكتمل النموذج: {form['completed']}/{form['required']}")

    # الدليل: رفع PDF صحيح، وتنزيله بالصلاحية، وإبطاله بسبب، ومنعه عمن خارج النطاق.
    status, doc = upload(entry, f"/api/forms/{form['id']}/values/{ev_item['indicatorId']}/evidence", "كشف الطلاب.pdf", PDF, {"description": "كشف"})
    check(status == 200 and doc["contentType"] == "application/pdf", "رفع دليل PDF صحيح")
    status, body, ctype = download(entry, f"/api/documents/{doc['id']}")
    check(status == 200 and body == PDF, "نزل الدليل كما رفع")
    status, _, _ = download(viewer, f"/api/documents/{doc['id']}")
    check(status in (403, 404), "مستخدم الاطلاع على كلية أخرى لا ينزل الدليل")
    status, err = entry.call("POST", f"/api/documents/{doc['id']}/void", {"note": ""})
    check(status == 400, "الإبطال يطلب سببًا")
    voided = entry.call("POST", f"/api/documents/{doc['id']}/void", {"note": "رفع بالخطأ"}, expect=200)[1]
    check(voided["voided"], "أبطل الدليل ويبقى في السجل")

    # مستند الهيكل المعتمد للكلية.
    status, sdoc = upload(college, f"/api/structure/colleges/{eng['id']}/documents", "هيكل الهندسة المعتمد.pdf", PDF)
    check(status == 200, "لجنة الكلية ترفع الهيكل المعتمد")
    docs = college.get(f"/api/structure/colleges/{eng['id']}/documents")
    check(any(d["id"] == sdoc["id"] for d in docs), "المستند في قائمة مستندات الكلية")
    status, _ = viewer.call("GET", f"/api/structure/colleges/{eng['id']}/documents")
    check(status == 404, "من خارج نطاق الكلية لا يرى مستنداتها")
    check(form["blocking"] == [], "لا عوائق للإرسال")
    form = entry.call("POST", f"/api/forms/{form['id']}/submit", expect=200)[1]
    check(form["step"] == "CollegeCommittee" and form["status"] == "InReview", "أرسل النموذج إلى لجنة الكلية")
    status, err = entry.call("PUT", f"/api/forms/{form['id']}/values", input_for(items[0]))
    check(status == 409, "النموذج المرسل مقفل للتعديل")

    # ---------------------------------------------------------------- تمديد موعد جهة
    status, _ = entry.call("POST", f"/api/forms/{form['id']}/extend", {"dueOn": (today + datetime.timedelta(days=20)).isoformat(), "reason": "ظروف الكلية"})
    check(status == 403, "مدخل البيانات لا يمدد موعده")
    ext = planner.call("POST", f"/api/forms/{form['id']}/extend", {"dueOn": (today + datetime.timedelta(days=20)).isoformat(), "reason": "قرار اللجنة بتمديد الهندسة"}, expect=200)[1]
    check(ext["dueOn"] == (today + datetime.timedelta(days=20)).isoformat(), "مدد موعد الهندسة وحدها بقرار مسجل")
    others_due = {f["dueOn"] for f in planner.get(f"/api/forms?cycleId={cycle['id']}") if f["id"] != form["id"]}
    check(others_due == {cycle["dueOn"]}, "موعد بقية الجهات لم يتغير")

    # ---------------------------------------------------------------- المراجعة
    step("3. الإعادة بملاحظات ثم مسار الاعتماد بفصل المهام")
    status, _ = entry.call("POST", f"/api/forms/{form['id']}/approve", {"note": None})
    check(status == 403, "مدخل البيانات لا يعتمد")
    status, _ = dean.call("POST", f"/api/forms/{form['id']}/approve", {"note": None})
    check(status == 403, "العميد لا يعتمد قبل دوره (خطوة لجنة الكلية)")
    waiting = college.get(f"/api/forms?cycleId={cycle['id']}&awaitingMe=true")
    check(any(f["id"] == form["id"] for f in waiting), "النموذج في «مطلوب مني» لدى لجنة الكلية")
    status, err = college.call("POST", f"/api/forms/{form['id']}/return", {"note": "", "items": []})
    check(status == 400, "الإعادة تطلب ملاحظة")
    first = items[0]
    form = college.call("POST", f"/api/forms/{form['id']}/return",
                        {"note": "راجعوا مصدر المؤشر الأول", "items": [{"indicatorId": first["indicatorId"], "note": "الرقم لا يطابق كشف شؤون الطلاب"}]}, expect=200)[1]
    check(form["status"] == "Returned" and form["step"] == "Entry", "أعيد النموذج للتصحيح")
    form = entry.get(f"/api/forms/{form['id']}")
    flagged = next(i for s in form["sections"] for i in s["items"] if i["indicatorId"] == first["indicatorId"])
    check(flagged["value"]["reviewNote"] == "الرقم لا يطابق كشف شؤون الطلاب", "ملاحظة المراجع تظهر على المؤشر")
    body = input_for(flagged)
    body["explanation"] = "صحح من الكشف المعتمد"
    entry.call("PUT", f"/api/forms/{form['id']}/values", body, expect=200)
    form = entry.call("POST", f"/api/forms/{form['id']}/submit", expect=200)[1]
    check(form["step"] == "CollegeCommittee", "أعيد الإرسال بعد التصحيح")

    form = college.call("POST", f"/api/forms/{form['id']}/approve", {"note": "مراجع"}, expect=200)[1]
    check(form["step"] == "Dean", "اعتمدت لجنة الكلية وانتقل إلى العميد")
    form = dean.call("POST", f"/api/forms/{form['id']}/approve", {"note": None}, expect=200)[1]
    check(form["step"] == "Quality", "اعتمد العميد وانتقل إلى الجودة المركزية")
    form = quality.call("POST", f"/api/forms/{form['id']}/approve", {"note": None}, expect=200)[1]
    check(form["status"] == "Approved" and form["step"] == "Done", "الاعتماد النهائي من الجودة المركزية")
    check(len([e for e in form["events"] if e["action"] == "Approved"]) == 3, "سجل النموذج يحفظ كل خطوات الاعتماد")
    status, err = entry.call("PUT", f"/api/forms/{form['id']}/values", input_for(items[0]))
    check(status == 409, "النموذج المعتمد مقفل")

    # ---------------------------------------------------------------- قسم: فصل المهام
    step("4. نموذج القسم: ممثل القسم الذي أدخل لا يراجع (SOD-C1)")
    civ_rows = deptrep.get(f"/api/forms?cycleId={cycle['id']}")
    civ_form = next((f for f in civ_rows if f["unitId"] == civ["id"]), None)
    if civ_form is None:
        check(True, "لا مؤشرات على مستوى القسم في هذه الدورة (لا نموذج قسم)")
    else:
        cf = deptrep.get(f"/api/forms/{civ_form['id']}")
        cf = fill(deptrep, cf)
        cf = deptrep.call("POST", f"/api/forms/{cf['id']}/submit", expect=200)[1]
        status, err = deptrep.call("POST", f"/api/forms/{cf['id']}/approve", {"note": None})
        check(status == 403 and str(err.get("code", "")).startswith("sod_"), f"من أدخل لا يراجع نموذجه ({err.get('code')})")

    # ---------------------------------------------------------------- الاستيراد
    step("5. الاستيراد من Excel بمعاينة ثم تطبيق كامل أو لا شيء")
    reopened = planner.call("POST", f"/api/forms/{form['id']}/reopen", {"note": "تصحيح بعد مراجعة اللجنة العليا"}, expect=200)[1]
    check(reopened["status"] == "InProgress" and reopened["step"] == "Entry", "أعيد فتح النموذج المعتمد بسبب")
    status, _ = entry.call("POST", f"/api/forms/{form['id']}/reopen", {"note": "سبب"})
    check(status == 403, "إعادة الفتح بصلاحية خاصة فقط")
    status, xlsx, ctype = download(entry, f"/api/forms/{form['id']}/template.xlsx")
    check(status == 200 and xlsx[:2] == b"PK", "نزل قالب Excel الخاص بالنموذج")
    try:
        import openpyxl
    except ImportError:
        openpyxl = None
    if openpyxl:
        wb = openpyxl.load_workbook(io.BytesIO(xlsx))
        ws = wb.active
        headers = [c.value for c in ws[1]]
        col = {h: i + 1 for i, h in enumerate(headers)}
        target_row = None
        for r in range(2, ws.max_row + 1):
            code = ws.cell(r, col["الرمز"]).value
            item = next(i for i in items if i["code"] == code)
            if not item["usesRatio"] and item["unit"].strip() != "%" and item["aggregation"] != "WeightedAverage":
                target_row = r
                break
        if target_row:
            ws.cell(target_row, col["القيمة"]).value = "١٣"  # أرقام عربية هندية
            ws.cell(target_row, col["التفسير"]).value = "تحديث من الاستيراد"
        bad = openpyxl.load_workbook(io.BytesIO(xlsx))
        bws = bad.active
        bws.cell(2, col["القيمة"]).value = "abc"
        bws.cell(2, col["البسط"]).value = "abc"
        buf, bbuf = io.BytesIO(), io.BytesIO()
        wb.save(buf)
        bad.save(bbuf)
        status, pv = upload(entry, f"/api/forms/{form['id']}/import", "form.xlsx", bbuf.getvalue())
        check(status == 200 and pv["errors"] >= 1 and not pv["canApply"], "المعاينة تكشف الخلايا غير الرقمية")
        status, err = upload(entry, f"/api/forms/{form['id']}/import", "form.xlsx", bbuf.getvalue(), query="?apply=true")
        check(status == 400 and err.get("code") == "import_errors", "ملف به أخطاء لا يطبق منه شيء")
        if target_row:
            status, pv = upload(entry, f"/api/forms/{form['id']}/import", "form.xlsx", buf.getvalue())
            check(status == 200 and pv["changes"] == 1 and pv["canApply"], "المعاينة: تغيير واحد صالح")
            status, pv = upload(entry, f"/api/forms/{form['id']}/import", "form.xlsx", buf.getvalue(), query="?apply=true")
            check(status == 200, "طبق الاستيراد")
            f2 = entry.get(f"/api/forms/{form['id']}")
            code = ws.cell(target_row, col["الرمز"]).value
            v = next(i for s in f2["sections"] for i in s["items"] if i["code"] == code)["value"]["value"]
            check(abs(v - 13) < 0.001, "القيمة المستوردة حفظت (١٣ = 13)")
    else:
        check(True, "openpyxl غير متاح؛ تخطي ملف الاستيراد")
    status, err = upload(entry, f"/api/forms/{form['id']}/import", "x.xlsx", b"not excel")
    check(status == 400, "ملف ليس Excel يرفض")

    # أعد الاعتماد كاملًا لتظهر النتائج.
    form = entry.call("POST", f"/api/forms/{form['id']}/submit", expect=200)[1]
    for who in (college, dean, quality):
        form = who.call("POST", f"/api/forms/{form['id']}/approve", {"note": None}, expect=200)[1]
    check(form["status"] == "Approved", "أعيد اعتماد النموذج بعد التصحيح")

    # ---------------------------------------------------------------- النتائج
    step("6. النتائج: القيم المعتمدة مقابل المستهدف، والنطاق، والتصدير")
    res = planner.get(f"/api/results?cycleId={cycle['id']}")
    check(res["unit"]["kind"] == "University", "مدير الخطة يرى الجامعة")
    check(res["formsApproved"] >= 1, f"النماذج المعتمدة: {res['formsApproved']}/{res['formsTotal']}")
    with_value = [r for r in res["indicators"] if r["actual"] is not None]
    check(len(with_value) > 0, f"مؤشرات لها قيمة على مستوى الجامعة: {len(with_value)}")
    check(any(h["id"] == eng["id"] for h in res["heatUnits"]), "خريطة الكليات تشمل الهندسة")
    res_eng = planner.get(f"/api/results?cycleId={cycle['id']}&unitId={eng['id']}")
    eng_vals = [r for r in res_eng["indicators"] if r["actual"] is not None]
    check(len(eng_vals) > 0, "نتائج الهندسة من نموذجها المعتمد")
    check(all(r["summary"] for r in res_eng["indicators"]), "لكل مؤشر جملة ملخص مقروءة")
    dres = dean.get(f"/api/results?cycleId={cycle['id']}")
    check(dres["unit"]["id"] == eng["id"] and all(u["kind"] != "University" for u in dres["units"]), "العميد يرى كليته لا الجامعة")
    check(not dres["canExport"], "العميد لا يصدر")
    detail = planner.get(f"/api/results/{cycle['id']}/indicators/{eng_vals[0]['indicatorId']}?unitId={eng['id']}")
    check(len(detail["trend"]) >= 1, "اتجاه المؤشر عبر الدورات")
    status, data, ctype = download(planner, f"/api/results/{cycle['id']}/export.xlsx")
    check(status == 200 and data[:2] == b"PK", "صدر كشف النتائج Excel")
    status, _, _ = download(dean, f"/api/results/{cycle['id']}/export.xlsx")
    check(status == 403, "التصدير بصلاحية خاصة فقط")

    # ---------------------------------------------------------------- مؤشر جديد
    step("7. إضافة مؤشر جديد إلى القاموس برمز تلقائي")
    name = f"مؤشر تجريبي {uuid.uuid4().hex[:5]}"
    status, _ = viewer.call("POST", "/api/kpi/indicators", {"standardCode": "S10", "nameAr": name, "unit": "%", "level": "College", "reason": None})
    check(status == 403, "مستخدم الاطلاع لا يضيف مؤشرًا")
    created = planner.call("POST", "/api/kpi/indicators", {"standardCode": "S10", "nameAr": name, "unit": "%", "level": "College", "reason": "اختبار"}, expect=200)[1]
    check(created["code"].startswith("S10-") and created["current"]["status"] == "Draft", f"أنشئ {created['code']} مسودة")
    status, err = planner.call("POST", "/api/kpi/indicators", {"standardCode": "S10", "nameAr": name, "unit": "%", "level": "College", "reason": None})
    check(status == 422 and err.get("code") == "duplicate_name", "لا يتكرر الاسم في المعيار نفسه")

    # ---------------------------------------------------------------- الإغلاق والبريد
    step("8. الإغلاق والبريد")
    status, err = planner.call("POST", f"/api/cycles/{cycle['id']}/close", {"note": ""})
    check(status == 400, "الإغلاق يطلب سببًا")
    closed = planner.call("POST", f"/api/cycles/{cycle['id']}/close", {"note": "انتهت فترة التجربة"}, expect=200)[1]
    check(closed["status"] == "Closed", "أغلقت الدورة")
    others = [f for f in entry.get(f"/api/forms?cycleId={cycle['id']}") if f["status"] != "Approved"]
    if others:
        status, _ = entry.call("PUT", f"/api/forms/{others[0]['id']}/values", {"indicatorId": items[0]["indicatorId"], "kind": "Number", "value": 1})
        check(status in (400, 409, 422), "لا إدخال بعد إغلاق الدورة")
    import time
    for _ in range(20):
        if len(glob.glob(os.path.join(mail_dir, "*.eml"))) > mails_before:
            break
        time.sleep(0.5)
    check(len(glob.glob(os.path.join(mail_dir, "*.eml"))) > mails_before, "أرسلت إشعارات البريد لمن عليه الإجراء")

    passed = sum(1 for ok, _ in RESULTS if ok)
    print(f"\n{passed}/{len(RESULTS)} passed")
    sys.exit(0 if passed == len(RESULTS) else 1)


if __name__ == "__main__":
    main()
