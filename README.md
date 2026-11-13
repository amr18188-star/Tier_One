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
| `tests/e2e/scenario_test.py` | اختبار شامل على خادم يعمل: القاموس والمستهدفات والتعارضات والهيكل |
| `tests/e2e/collection_test.py` | اختبار شامل لجمع البيانات: الدورة والنماذج والأدلة ومسار الاعتماد والاستيراد والنتائج |
| `tests/load/load_test.py` | اختبار الحمل (100 مستخدم متزامن) |
| `deploy/` | صورة Docker وملف compose للإنتاج والوكيل العكسي وتهيئة MongoDB |
| `docs/requirements.md` | وثيقة المتطلبات والقرارات |
| `docs/deployment.md` | التشغيل على خوادم الجامعة، والنسخ الاحتياطي والاسترجاع |
| `docs/user-guide.md` | دليل المستخدم لكل دور |
| `reference/` و`tools/` | القاموس المستخرج من ملفي الخطة وأدوات توليده |

## التشغيل للتطوير (بلا Docker)

يتطلب .NET SDK 10 وNode 24 وPython 3، وPostgreSQL 18 وMongoDB مثبتين على الجهاز.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\dev-setup.ps1   # الأسرار والإعدادات (مرة واحدة)
powershell -ExecutionPolicy Bypass -File scripts\dev-db.ps1      # قاعدة خاصة بالمشروع على 127.0.0.1:5440
cd web; npm install; npx vite build; cd ..
dotnet run --project src\Tier1.Api                                # http://localhost:5150
```

قاعدة المشروع نسخة مستقلة في `data\pgdata` لا تمس خدمة PostgreSQL المثبتة ولا تحتاج كلمة مرور `postgres`.
إيقافها: `scripts\dev-db.ps1 stop`. الحسابات التجريبية `*@tier1.local` بكلمة المرور `T1_DEMO_PASSWORD` في `.env`.

إذا كانت قاعدة `tier1` ومستخدم `tier1_dev` موجودين بالفعل على PostgreSQL المثبت في الجهاز، ضع `T1_POSTGRES_PORT=5432` في `.env` ثم شغّل `scripts\dev-setup.ps1` من جديد. في هذه الحالة لا تحتاج تشغيل `scripts\dev-db.ps1`؛ سيستخدم التطبيق السيرفر الموجود في pgAdmin، وكذلك `scripts\demo.ps1`.
اسم قاعدة سجلات MongoDB يضبط بـ `T1_MONGO_DB` في `.env` (الافتراضي `tier1_logs`). سلسلة السجلات تبدأ من جديد مع كل قاعدة `tier1` فارغة، فاختر اسمًا جديدًا بعد كل تفريغ ثم شغّل `scripts\dev-setup.ps1`.

| الحساب | الدور |
|---|---|
| `planner@` | مدير متابعة الخطة |
| `committee@` | عضو اللجنة العليا لـ Tier 1 |
| `president@` | رئيس الجامعة |
| `dean@` و`college@` و`entry@` | عميد ولجنة ومدخل بيانات كلية الهندسة (نطاق `ENG`) |
| `deptrep@` | ممثل قسم المدنية (نطاق `ENG-CIV`) |
| `vp@` و`viewer@` و`quality@` | نائب الرئيس، اطلاع، الجودة |
| `sa1@` و`sysadmin@` و`auditor@` | المدير الأعلى، مسؤول النظام، المراجع الرقابي |

### النسخة التجريبية بالبيانات (للعرض والتجربة)

قاعدة منفصلة `tier1_demo` على http://localhost:5160 مملوءة ببيانات واقعية مولدة: ثلاث دورات، ونماذج في كل مراحل الاعتماد، ونتائج ومقارنات.
الحسابات وما ينتظر كل منها في [docs/demo.md](docs/demo.md).

```powershell
powershell -ExecutionPolicy Bypass -File scripts\demo.ps1 -Reset   # قاعدة فارغة، ويبقى الخادم يعمل
python tools\demo_data.py                                          # في نافذة أخرى: يملؤها (نحو 40 ثانية، Python 3.10 فأحدث)
```

`demo.ps1` ينشئ `tier1_demo` على منفذ `T1_POSTGRES_PORT`، فيحتاج حساب `tier1_dev` صلاحية CREATEDB على ذلك السيرفر، أو قاعدة `tier1_demo` ينشئها مسؤول القاعدة من pgAdmin مملوكة لـ `tier1_dev`.

**على جهاز التطوير الحالي (11 نوفمبر 2026):** البيانات التجريبية محملة في قاعدة التطوير نفسها `tier1` على 127.0.0.1:5432، وتعرض على http://localhost:5150. لا توجد قاعدة على 5440.
لإعادة ملئها من الصفر: أوقف الخادم، واحذف المخططات `adm, dc, iam, kpi, logs, org` من `tier1` (بعد نسخة احتياطية)، وغيّر `T1_MONGO_DB` إلى اسم جديد، وانقل `data\checkpoints` جانبًا، ثم شغّل `scripts\dev-setup.ps1` والخادم، وبعده:
`$env:T1_BASE='http://localhost:5150'; py -3.12 tools\demo_data.py`.
اختبارات `tests/e2e` تضيف بياناتها (دورات تجريبية) إلى القاعدة نفسها.

## الاختبارات

```powershell
dotnet test tests\Tier1.Tests
cd web; npx tsc -b --noEmit; npx vitest run; cd ..
python tests\e2e\scenario_test.py      # والخادم يعمل
python tests\e2e\collection_test.py
python tests\load\load_test.py --users 100 --seconds 60
```

الاختبارات الشاملة تتكرر على القاعدة نفسها دون إعادة تهيئتها. البريد في التطوير يكتب ملفات `.eml` في `data\mail`.

## الإنتاج

راجع [docs/deployment.md](docs/deployment.md): Docker Compose أو IIS، والشهادات، وأوامر `--migrate` و`--mongo-setup` و`--create-superadmin`،
والنسخ الاحتياطي اليومي (`scriptsackup.ps1`) واختبار الاسترجاع الشهري (`scripts
estore-test.ps1`).

## إعادة بناء البيانات المرجعية

```powershell
python tools\build_reference_workbook.py   # ملف Excel للمراجعة
python tools\export_seed.py                # بذرة الخادم
```
