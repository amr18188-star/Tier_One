# منظومة جامعة العاصمة لإدارة الأداء الاستراتيجي — Tier 1

مبنية على أساس منظومة مركز اللغات: مصفوفة الصلاحيات بالنطاق، والتحقق بخطوتين، وسجلات MongoDB المتسلسلة، وواجهة antd.

| الطبقة | المحتوى |
|---|---|
| `src/Tier1.Domain` | الجهات، وهيكل Tier 1، والمؤشرات وإصداراتها، والمستهدفات، والتعارضات، ودورات جمع البيانات ونماذجها وأدلتها |
| `src/Tier1.Application` | الخدمات وقواعد العمل النقية (`IndicatorRules`، `DistributionRules`، `StructureRules`، `TargetRules`، `ValueRules`، `ApprovalRules`، `ResultsRules`) |
| `src/Tier1.Infrastructure` | PostgreSQL (EF Core)، وسجلات MongoDB، والبذرة المرجعية `Persistence/Seed/reference-seed.json` |
| `src/Tier1.Api` | المسارات (`Endpoints/Tier1Endpoints.cs` و`Endpoints/CollectionEndpoints.cs`) والصلاحيات والإعدادات |
| `web` | الواجهة (React + antd)، وتبنى إلى `src/Tier1.Api/wwwroot` |
| `tests/Tier1.Tests` | اختبارات الوحدة (القواعد والصلاحيات والبذرة) |
| `tests/Tier1.IntegrationTests` | اختبارات التكامل: التطبيق كله على PostgreSQL وMongoDB في حاويات مؤقتة (Testcontainers): مسار النموذج والتزامن ومصفوفة الاعتماد وإدارة الحسابات والأدوار |
| `tests/Tier1.E2E` | اختبارات شاملة على خادم يعمل (xUnit): القاموس والمستهدفات والتعارضات والهيكل والتكليف وجمع البيانات والاعتماد والاستيراد والنتائج وشجرة الخطة، وبطاقة الأداء، واختبار الحمل |
| `deploy/` | صورة Docker وملف compose للإنتاج والوكيل العكسي وتهيئة MongoDB |
| `docs/requirements.md` | وثيقة المتطلبات والقرارات |
| `docs/deployment.md` | التشغيل على خوادم الجامعة، والنسخ الاحتياطي والاسترجاع |
| `docs/user-guide.md` | دليل المستخدم لكل دور |
| `docs/audit/2026-11/` | المراجعة الشاملة (المتطلبات والأمان والمعمارية والقاعدة والواجهة وتجربة المستخدم والاختبارات) وخارطة التطوير ووصفة إضافة وحدة جديدة |
| `reference/` | ملفات المصدر (Excel) التي تستخرج منها البذرة |

## التشغيل للتطوير

يحتاج Docker Desktop فقط (و.NET SDK للخيار 2). لا سكربتات ولا إعداد مسبق: كلمات المرور والإعدادات مثبتة في `docker-compose.yml` و`docker-compose.dev.yml` و`src/Tier1.Api/appsettings.Development.json`.
القاعدتان والخادم والواجهة كلها حاويات، والمستودع مركب فيها: `dotnet watch` يعيد بناء الخادم عند كل حفظ، وVite يحدث الواجهة فورًا، فلا بناء للصور لتغيير شيفرة.

```bash
docker compose -p tier1-dev -f docker-compose.dev.yml up      # أول مرة بضع دقائق (الصور والحزم)
```

عند أول بدء تملأ حاوية `demo-data` البيانات التجريبية عبر واجهة الخادم بحسابات الأدوار (نحو 30 ثانية بعد أن يجيب الخادم): هياكل الكليات وثلاثة أرباع لعام 2027 بنماذج في كل مراحل الاعتماد.
تعمل مرة واحدة (تتخطى إن وجدت دوراتها). المولد في `tests/Tier1.TestSupport` ويشغله أمر الأدوات `--demo-data`؛ وفي الخيار 2 شغله بنفسك بعد بدء الخادم: `dotnet run --project tools/Tier1.Tools -- --demo-data`.

| | الخيار 1: الخادم داخل Docker | الخيار 2: الخادم على جهازك |
|---|---|---|
| الأمر | `docker compose -p tier1-dev -f docker-compose.dev.yml up` | `docker compose -p tier1-dev up -d` ثم `dotnet run --project src/Tier1.Api` |
| الخادم | http://localhost:5150 | http://localhost:5150 |
| الواجهة (Vite) | http://localhost:5188 | `cd web && npm run dev` ثم http://localhost:5188 |

- القاعدتان على عنوان الجهاز: PostgreSQL `5440` (`tier1_dev` / `tier1_dev`) وMongoDB `27031` (غيّر المنفذين بالمتغيرين `POSTGRES_PORT` و`MONGO_PORT` إن كانا مشغولين). `appsettings.Development.json` يشير إليهما.
- القاعدتان والبيانات مشتركة بين الخيارين (المشروع `tier1-dev`). لا تشغل الخيارين معًا (المنفذ 5150).
- الرسائل تكتب ملفات `.eml` في `data/mail`.
- الخادم في الحاوية يستمع على كل عناوينها (`ASPNETCORE_HTTP_PORTS`)، لأن حاجز التطوير في `StartupGuards` يمنع غير loopback في `ASPNETCORE_URLS`؛ والمنفذان منشوران على `127.0.0.1` فلا ينكشفان على الشبكة.
- البناء المحلي (`dotnet build` على جهازك) يكتب في `artifacts/` بجذر المستودع لا في مجلدات المشاريع، فلا يمس مراقب `dotnet watch` في الحاوية. وإن توقف المراقب عادت حاوية الخادم وحدها (`restart: on-failure`).
- تُبنى الصورة من جديد (`up --build`) فقط عند تغيير `Dockerfile.dev`. لمخرجات بناء قديمة: `down` ثم `docker volume rm tier1-dev_tier1_api_out`.
- الإيقاف: `docker compose -p tier1-dev -f docker-compose.dev.yml down` (البيانات تبقى)، ومع `-v` تحذف كل البيانات ويعاد ملؤها عند البدء التالي.
- أوامر الصيانة في مشروع الأدوات `tools/Tier1.Tools`، خارج الخادم (ARCH-4، [ADR-0015](docs/adr/0015-tools-project.md)): `dotnet run --project tools/Tier1.Tools -- --reset-kpi --confirm` و`--load-structure --confirm` و`--backup <مجلد>` و`--restore-test <نسخة>` و`--data-fixes` و`--demo-data` و`--growth-data`. بلا وسائط يعرض القائمة كلها.
  الخادم نفسه لا ينفذ إلا `--migrate` و`--create-superadmin`.
- هذا للتطوير فقط؛ الإنتاج في `deploy/` (راجع `docs/deployment.md`).

الحسابات التجريبية `*@tier1.local` بكلمة المرور `Tier1-Demo-Pass1` (من `Seed__DemoPassword` في `docker-compose.dev.yml`).

| الحساب | الدور |
|---|---|
| `planner@` | مدير متابعة الخطة |
| `committee@` | عضو اللجنة العليا لـ Tier 1 |
| `president@` | رئيس الجامعة |
| `dean@` و`college@` و`entry@` | عميد ولجنة ومدخل بيانات كلية الهندسة (نطاق `ENG`) |
| `deptrep@` | ممثل قسم المدنية (نطاق `ENG-CIV`) |
| `vp@` و`viewer@` و`quality@` | نائب الرئيس، اطلاع، الجودة |
| `sa1@` و`sysadmin@` و`auditor@` | المدير الأعلى، مسؤول النظام، المراجع الرقابي |

### نسخة محلية لتتبّع الفلو

```bash
docker compose -p tier1-flow -f docker-compose.flow.yml up -d
```

افتح [واجهة تتبّع الفلو](http://127.0.0.1:5189). تعمل النسخة ببيانات وملفات منفصلة عن `tier1-dev`.
تبدأ بجامعة العاصمة وكليتي الهندسة والحاسبات والذكاء الاصطناعي وأقسامهما التجريبية.
تستخدم شجرة الخطة المرجعية كاملة بمؤشراتها الـ92 ومستهدفاتها المعتمدة، وبمستويات الجمع وقواعد التجميع الأصلية للجامعة والكليات والأقسام.
تبدأ الدورات والنماذج فارغة حتى يمكن إنشاء دورة واحدة ومتابعتها من أول خطوة. يمكن رفع وثيقة الخطة من حساب مدير متابعة الخطة.

كل الحسابات أدناه على نطاق `@tier1.local` وبكلمة مرور التطوير المذكورة أعلاه.

| الحساب | الخطوة |
|---|---|
| `sa1` و`sa2` | حسابا المدير الأعلى |
| `president` | رئيس الجامعة بنفس صلاحيات النسخة الأصلية |
| `planner` | إدارة الخطة وإنشاء الدورة وفتحها |
| `entry` | إدخال المؤشرات وإرسال نموذج الكلية |
| `college` ثم `dean` ثم `quality` | مراجعة لجنة الكلية، واعتماد العميد، ثم الاعتماد النهائي من الجودة المركزية |
| `entry.cs` و`college.cs` و`dean.cs` | مدخل بيانات الحاسبات، ولجنة الكلية، والعميد |
| `deptrep` و`rep.ele` و`rep.mec` و`rep.se` و`rep.ai` | ممثلو أقسام الهندسة والحاسبات |
| `entry.uni` | إدخال مؤشرات مستوى الجامعة |
| `tier1` | مراجعة النتائج ونقل القيم المعتمدة إلى مؤشرات الجامعة |
| `committee` | اعتماد تعريفات المؤشرات والمستهدفات الجديدة عند تجربة تعديل الخطة |

من [شاشة الدخول](http://127.0.0.1:5189/login) أو الشريط العلوي بعد الدخول، افتح «تبويبات الأدوار (تطوير)» واختر حسابًا. يفتح كل دور في تبويب على عنوان محلي مستقل مثل `entry.localhost:5189`، ويظهر بريده تلقائيًا في نموذج الدخول. لكل عنوان جلسة منفصلة في المتصفح نفسه، وكل التبويبات تعمل على قاعدة بيانات الفلو نفسها.
يتاح الخيار في وضع تطوير Flow Compose. لإخفائه، اضبط `FLOW_ROLE_TABS=false` ثم أعد تشغيل Compose بأمر `up -d`.

أوقف النسخة بالأمر `docker compose -p tier1-flow -f docker-compose.flow.yml down` لحفظ تقدمك وتشغيلها لاحقًا.

## الاختبارات

```bash
dotnet test tests/Tier1.Tests                                  # اختبارات الوحدة (بلا خادم)
dotnet test tests/Tier1.IntegrationTests                       # التكامل: يلزم Docker فقط (حاويات مؤقتة تحذف بعدها)
cd web && npm run typecheck && npm run lint && npm test && cd ..   # الواجهة
dotnet test tests/Tier1.E2E                                    # شاملة: والخادم يعمل (docker compose ... up)
```

**CI** (`.github/workflows/ci.yml`) يشغل كل ذلك مع كل دفع وطلب دمج:
- البناء بالتحذيرات أخطاءً، واختبارات الوحدة، وفحص حزم NuGet المعروفة الثغرات.
- اختبارات التكامل على حاويات.
- الواجهة: الأنواع، وESLint (قواعد hooks وإتاحة JSX، والتحذيرات لا تزيد على سقف `--max-warnings`)، وvitest، و`npm audit` لحزم التشغيل، والبناء.
- فحص الأسرار (gitleaks بإعدادات `.gitleaks.toml`).
- الاختبارات الشاملة على بيئة `docker-compose.e2e.yml`، بـ `T1_E2E_REQUIRE_SERVER=1` فيفشل التشغيل إن غاب الخادم بدل أن تتخطى الاختبارات نفسها.

**عقد API** في `docs/api/openapi.json` (يولده الخادم في التطوير على `/openapi/v1.json`). اختبار `OpenApiContractTests` يفشل إن تغير مسار أو معامل أو شكل طلب أو استجابة دون تحديثه. لقبول تغيير مقصود: `T1_UPDATE_OPENAPI=1 dotnet test tests/Tier1.IntegrationTests --filter OpenApiContract`، ثم `npm run gen:api` في `web` لتوليد أنواع الواجهة (`src/api/schema.d.ts`)، ثم أضف الملفين مع التغيير. الواجهة تقارن أنواعها المكتوبة بالمولدة في `src/api/contract.check.ts`، فيفشل `npm run typecheck` إن أرسل الخادم ما لا تقرؤه الشاشات.

اختبارات تفاعل الواجهة تعمل في jsdom بتعليق `// @vitest-environment jsdom` أول الملف (مثال: `web/src/shared/DecisionModal.test.tsx`)، وبقية الاختبارات في بيئة node.

الاختبارات الشاملة (`tests/Tier1.E2E`) تتخطى نفسها إن لم يوجد خادم، وتقرأ `T1_BASE` (الافتراضي http://localhost:5150) و`T1_DEMO_PASSWORD` (الافتراضي `Tier1-Demo-Pass1`).
تتكرر على القاعدة نفسها دون إعادة تهيئتها، وتعمل واحدًا بعد الآخر لأنها تشترك في الخادم والحسابات. وتعمل على الهيكل الرسمي أو التجريبي (تسمي الكليات برموزها التجريبية ENG وCS وCOM).

- **بطاقة الأداء** (`ScorecardTests`) تحتاج قاعدة جديدة بالكليات التجريبية (`Seed__Structure=false`، ودون تشغيل `--demo-data`) لأنها تملأ كل النماذج بنفسها: `T1_E2E_SCORECARD=1 dotnet test tests/Tier1.E2E --filter Scorecard`.
- **الحمل** (`LoadTests`): `T1_E2E_LOAD=1 T1_LOAD_USERS=100 T1_LOAD_SECONDS=60 dotnet test tests/Tier1.E2E --filter Load`. المقبول: p95 أقل من ثانيتين ولا أخطاء خادم.
  - التقرير يذكر مجمع اتصالات PostgreSQL في الخادم أثناء التشغيل (`/api/logs/database`): أعلى عدد اتصالات مستعملة، والطلبات المنتظرة.
  - **بحجم النمو المتوقع** (نحو 250 جهة و300 مؤشر و20 دورة): بعد البيانات التجريبية شغّل أمر الأدوات `--growth-data` على البيئة نفسها، ثم اختبار الحمل.
    على بيئة E2E يعمل داخل شبكتها:
    ```bash
    docker compose -p tier1-e2e -f docker-compose.e2e.yml run --rm --no-deps \
      -e "ConnectionStrings__Tier1=Host=postgres;Port=5432;Database=tier1;Username=tier1_dev;Password=tier1_dev" \
      -e "ConnectionStrings__Mongo=mongodb://mongo:27017/tier1_logs" -e "DataProtection__KeysPath=/tmp/keys" \
      demo-data dotnet run --project tools/Tier1.Tools --no-launch-profile -- --growth-data --base http://api:5150
    ```
    المؤشرات والدورات تُنشأ عبر الواجهة البرمجية (دورات تجريبية `growth-NN` لا تأخذ ربعًا رسميًا). أما قيم النماذج وحالات اعتمادها فتكتب مباشرة في القاعدة، بعد فحصها بقواعد القيم نفسها، لأن الهيكل الرسمي بلا حسابات إدخال. لذلك لا تدخل سجل التدقيق. للتطوير فقط، وإعادة تشغيله لا تكرر شيئًا.
- البريد في التطوير يكتب ملفات `.eml` في `data/mail`.

## الإنتاج

راجع [docs/deployment.md](docs/deployment.md): Docker Compose أو IIS، والشهادات، وأمري الخادم `--migrate` و`--create-superadmin`،
وأوامر صورة الأدوات (`--mongo-setup` و`--load-structure` و`--apply-data-fixes`)، والنسخ الاحتياطي اليومي (`--backup`) واختبار الاسترجاع الشهري (`--restore-test`).

اسم الدخول بريد جامعي من نطاق معتمد أو نطاق فرعي منه. يدير مسؤول النظام النطاقات (جامعة العاصمة وجامعة حلوان أولًا) وقيود عناوين IP، العامة ولكل حساب، من الإعدادات وتفاصيل الحساب، بلا تغيير في الشيفرة ([ADR-0016](docs/adr/0016-university-access.md)).

فترة الخطة (افتراضيًا من 1 سبتمبر 2026 إلى 31 أغسطس 2031، دورات ربع سنوية) يحددها المدير الأعلى من «الإعدادات ← فترة خطة Tier 1»، فتتبعها الأرباع وسنوات المستهدفات في كل الشاشات ([ADR-0017](docs/adr/0017-college-targets-structure-activities-plan-period.md)).

كل طلب إنشاء أو رفع أو قرار من الواجهة يحمل مفتاح منع التكرار (`Idempotency-Key`). إعادة إرساله بعد انقطاع الرد تعيد الرد الأول ولا تكرر العمل ([ADR-0018](docs/adr/0018-idempotency-keys.md)).

## إعادة بناء البيانات المرجعية

البذرة ملفات JSON مضمنة في الخادم (`src/Tier1.Infrastructure/Persistence/Seed`)، وتستخرج من ملفي Excel بأمرين في مشروع الأدوات (لا بايثون):

```bash
dotnet run --project tools/Tier1.Tools -- --import-structure "مسار/الهيكل.xlsx"   # university-structure.json
dotnet run --project tools/Tier1.Tools -- --import-kpi "مسار/KPI.xlsx"            # kpi-framework.json
```

يكتبان الملف في `Seed/` ويحفظان نسخة المصدر في `reference/`. أما `reference-seed.json` (الهيكل المرجعي القديم) فهو المصدر نفسه ويعدل يدويًا.

على قاعدة جديدة، يحمّل البذر العادي هيكل جهات جامعة العاصمة (`university-structure.json`) مع الركائز والمعايير والمؤشرات ومستهدفاتها (`kpi-framework.json`). يعمل الهيكل الرسمي من دون الحسابات التجريبية، ويمكن تعطيله عبر `Seed__Structure=false` لقواعد الاختبار. القواعد القائمة لا تتغير تلقائيًا؛ أمر `--load-structure --confirm` يضيف أو يحدّث الجهات حسب الكود ويترك الجهات غير الواردة في الملف دون حذف.
