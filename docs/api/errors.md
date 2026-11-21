# عقد أخطاء الواجهة البرمجية (BE-6)

كل خطأ من `/api` بشكل واحد، أيًا كان من كتبه: معالج الأخطاء، أو المصادقة، أو CSRF، أو حد المعدل، أو المسار غير الموجود.

```json
{
  "status": 400,
  "code": "invalid_email",
  "title": "البريد الإلكتروني غير صالح.",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "errors": [{ "field": "email", "code": "invalid_email", "message": "البريد الإلكتروني غير صالح." }]
}
```

- **`code`** رمز ثابت بالإنجليزية تتعامل معه الواجهة. لا يتغير بتغير اللغة.
- **`title`** رسالة للمستخدم بلغة الطلب (`Accept-Language`).
- **`traceId`** هو نفسه في رأس `X-Trace-Id` لكل رد، ويذكره المستخدم للدعم الفني.
- **`errors`** الحقول التي يعلّمها النموذج:
  - `field` اسم الحقل في JSON الطلب. العنصر في قائمة يكتب بموضعه: `items[2].value`.
  - تكون القائمة فارغة إن كان الخطأ عن الطلب كله.
  - الواجهة تعلّم الحقل تحته (`useFieldErrors` و`markFieldErrors` في `web/src/shared/useFieldErrors.ts`). وإن لم يكن الحقل في النموذج المفتوح عرضت الرسالة العامة.

## الحالات

| الحالة | المعنى | ماذا يفعل المستخدم | أمثلة |
|---|---|---|---|
| 400 | مدخل غير صالح: الطلب لا يقرأ، أو قيمة حقل مرفوضة | يصحح الحقل ثم يعيد الإرسال | `bad_request`، `invalid_body`، `invalid_value`، `text_too_long`، `reason_required`، `invalid_email`، `email_domain_not_allowed`، `invalid_domain`، `invalid_ip`، `invalid_plan_period`، `end_before_start`، `invalid_progress`، `csrf`، `invalid_idempotency_key` ([ADR-0018](../adr/0018-idempotency-keys.md)) |
| 401 | لا جلسة أو انتهت | يدخل من جديد | `unauthenticated` |
| 403 | لا صلاحية، أو قاعدة فصل المهام | يطلب الصلاحية ممن يملكها | `forbidden`، `sod_*`، `ip_not_allowed` (عنوان IP غير مسموح، [ADR-0016](../adr/0016-university-access.md)) |
| 404 | غير موجود، أو خارج نطاق المستخدم (لا يكشف وجوده) | — | `not_found` |
| 409 | تعارض: عدّله غيره، أو سبقه طلب للعنصر نفسه، أو مكرر | يحدّث الصفحة ثم يعيد المحاولة | `conflict`، `quarter_taken`، `duplicate_code`، `duplicate_domain`، `duplicate_ip`، `request_in_progress` (طلب بالمفتاح نفسه ما زال جاريًا أو نتيجته غير معروفة، [ADR-0018](../adr/0018-idempotency-keys.md)) |
| 413، 415 | الملف أكبر من المسموح، أو الجسم ليس JSON | يختار ملفًا أصغر، أو يصحح الطلب | `file_too_large`، `json_required` |
| 422 | طلب صالح ترفضه قاعدة عمل أو حالة العنصر | لا تصحيح للحقول؛ الإجراء غير متاح الآن | `cycle_not_open`، `not_under_review`، `form_locked`، `ip_lockout`، `plan_period_orphans_cycles` و`plan_period_only` ([ADR-0017](../adr/0017-college-targets-structure-activities-plan-period.md))، `idempotency_key_reused` (المفتاح نفسه لطلب مختلف، [ADR-0018](../adr/0018-idempotency-keys.md)) |
| 423 | تجاوز محاولات رمز التحقق بخطوتين، فأقفل مؤقتًا | ينتظر ثم يعيد المحاولة | `mfa_locked` |
| 428 | الكتابة بلا إصدار العنصر الذي فتحه المستخدم (الإصدار إلزامي) | يحدّث الصفحة ثم يعيد المحاولة | `version_required` |
| 429 | طلبات كثيرة | ينتظر قليلًا | `rate_limited` |
| 500 | خطأ غير متوقع في الخادم (يسجل بمرجعه) | يعيد المحاولة، ويبلغ الدعم بالمرجع | `server_error` |

## القواعد

1. **الرمز الواحد له حالة واحدة دائمًا.** يفرض ذلك اختبار `ErrorContractTests` بقراءة كل `new DomainException` في الشيفرة.
2. **400 أم 422:** إن كان التصحيح في حقل يكتبه المستخدم فهو 400، ويسمي الحقل إن كان حقلًا واحدًا. وإن كان الطلب سليمًا لكن القاعدة أو الحالة لا تسمح به فهو 422.
3. **تسمية الحقل:**
   - في الخادم: `new DomainException(...) { Field = "email" }`.
   - `Require.Reason` و`Require.Optional` تسمي الحقل من الوسيط نفسه (`req.DecisionRef` يصير `decisionRef`).
   - أخطاء قراءة JSON تسمي الحقل من مساره، وقيمة enum غير المعرفة في الاستعلام تسمي اسم المعامل.
