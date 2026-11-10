# -*- coding: utf-8 -*-
"""
اختبار حمل: مستخدمون متزامنون (100 افتراضيًا) يكررون ما يفعله المستخدم الحقيقي في الدورة: الصفحة الأولى، وقائمة نماذجه،
وفتح نموذج، والنتائج، والقاموس. يقيس زمن الاستجابة (الوسيط وp95 والأقصى) ونسبة الأخطاء لكل مسار.

الحسابات التجريبية قليلة، فكل مستخدم افتراضي يدخل بجلسة مستقلة لأحد الحسابات (عدة جلسات للحساب الواحد مسموحة).
    python tests/load/load_test.py --users 100 --seconds 60
المعيار المقبول (وثيقة المتطلبات): p95 أقل من ثانيتين ولا أخطاء خادم.
"""
import argparse
import os
import random
import statistics
import sys
import threading
import time
from collections import defaultdict

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "e2e"))
from scenario_test import Client  # noqa: E402

ACCOUNTS = ["planner", "quality", "dean", "college", "entry", "deptrep", "viewer", "vp", "president", "committee"]

timings: dict[str, list[float]] = defaultdict(list)
errors: dict[str, int] = defaultdict(int)
lock = threading.Lock()


def timed(client: Client, name: str, path: str):
    start = time.perf_counter()
    status, data = client._raw("GET", path)
    elapsed = time.perf_counter() - start
    with lock:
        timings[name].append(elapsed)
        if status >= 500:
            errors[name] += 1
    return status, data


def user_loop(index: int, shared: dict, ready: threading.Barrier):
    email = f"{ACCOUNTS[index % len(ACCOUNTS)]}@tier1.local"
    client = None
    for attempt in range(5):
        try:
            client = Client(email)
            break
        except Exception:  # noqa: BLE001 — الدخول المتزامن قد يصطدم بحد المعدل؛ يعاد بعد مهلة.
            time.sleep(1 + attempt)
    ready.wait()
    until = shared["until"]
    if client is None:
        with lock:
            errors["login"] += 1
        return
    while time.time() < until:
        timed(client, "home", "/api/home")
        status, forms = timed(client, "forms", "/api/forms")
        if status == 200 and forms:
            timed(client, "form", f"/api/forms/{random.choice(forms)['id']}")
        timed(client, "results", "/api/results")
        timed(client, "indicators", "/api/kpi/indicators")
        time.sleep(random.uniform(0.5, 2.0))  # زمن تفكير المستخدم بين الصفحات


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--users", type=int, default=100)
    parser.add_argument("--seconds", type=int, default=60)
    args = parser.parse_args()

    print(f"Logging in {args.users} virtual users…")
    shared: dict = {}
    # المدة تبدأ بعد دخول الجميع: الحاجز يضبط موعد الانتهاء مرة واحدة لحظة اكتماله.
    ready = threading.Barrier(args.users + 1, action=lambda: shared.update(until=time.time() + args.seconds))
    threads = [threading.Thread(target=user_loop, args=(i, shared, ready), daemon=True) for i in range(args.users)]
    for t in threads:
        t.start()
    ready.wait()
    print(f"Running for {args.seconds}s…")
    for t in threads:
        t.join(timeout=args.seconds + 120)

    total = sum(len(v) for v in timings.values())
    print(f"\n{'route':<12}{'requests':>10}{'median ms':>12}{'p95 ms':>10}{'max ms':>10}{'5xx':>6}")
    worst_p95 = 0.0
    for name in ["home", "forms", "form", "results", "indicators"]:
        values = sorted(timings.get(name, []))
        if not values:
            continue
        p95 = values[min(len(values) - 1, int(len(values) * 0.95))]
        worst_p95 = max(worst_p95, p95)
        print(f"{name:<12}{len(values):>10}{statistics.median(values) * 1000:>12.0f}{p95 * 1000:>10.0f}{values[-1] * 1000:>10.0f}{errors.get(name, 0):>6}")
    failed = sum(errors.values())
    print(f"\n{total} requests, {total / args.seconds:.1f} req/s, worst p95 {worst_p95 * 1000:.0f} ms, server errors {failed}")
    ok = worst_p95 < 2.0 and failed == 0
    print("PASS" if ok else "FAIL")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
