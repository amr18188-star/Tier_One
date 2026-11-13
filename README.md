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

## التشغيل للتطوير

يحتاج Docker Desktop فقط (و.NET SDK للخيار 2). لا سكربتات ولا إعداد مسبق: كلمات المرور والإعدادات مثبتة في `docker-compose.yml` و`docker-compose.dev.yml` و`src/Tier1.Api/appsettings.Development.json`.
القاعدتان والخادم والواجهة كلها حاويات، والمستودع مركب فيها: `dotnet watch` يعيد بناء الخادم عند كل حفظ، وVite يحدث الواجهة فورًا، فلا بناء للصور لتغيير شيفرة.

```bash
docker compose -p tier1-dev -f docker-compose.dev.yml up      # أول مرة بضع دقائق (الصور والحزم)
```

عند أول بدء يملأ الخادم نفسه البيانات التجريبية (`Seed:DemoData`، نحو 30 ثانية): هياكل الكليات وثلاثة أرباع لعام 2027 بنماذج في كل مراحل الاعتماد.
الشيفرة في `src/Tier1.Api/Startup/DemoData`، وتعمل مرة واحدة (تتخطى إن وجدت دوراتها).

| | الخيار 1: الخادم داخل Docker | الخيار 2: الخادم على جهازك |
|---|---|---|
| الأمر | `docker compose -p tier1-dev -f docker-compose.dev.yml up` | `docker compose -p tier1-dev up -d` ثم `dotnet run --project src/Tier1.Api` |
| الخادم | http://localhost:5150 | http://localhost:5150 |
| الواجهة (Vite) | http://localhost:5188 | `cd web && npm run dev` ثم http://localhost:5188 |

- القاعدتان على عنوان الجهاز: PostgreSQL `5440` (`tier1_dev` / `tier1_dev`) وMongoDB `27031` (غيّر المنفذين بالمتغيرين `POSTGRES_PORT` و`MONGO_PORT` إن كانا مشغولين). `appsettings.Development.json` يشير إليهما.
- القاعدتان والبيانات مشتركة بين الخيارين (المشروع `tier1-dev`). لا تشغل الخيارين معًا (المنفذ 5150).
- الرسائل تكتب ملفات `.eml` في `data/mail`.
- الخادم في الحاوية يستمع على كل عناوينها (`ASPNETCORE_HTTP_PORTS`)، لأن حاجز التطوير في `StartupGuards` يمنع غير loopback في `ASPNETCORE_URLS`؛ والمنفذان منشوران على `127.0.0.1` فلا ينكشفان على الشبكة.
- تُبنى الصورة من جديد (`up --build`) فقط عند تغيير `Dockerfile.dev`. لمخرجات بناء قديمة: `down` ثم `docker volume rm tier1-dev_tier1_api_out`.
- الإيقاف: `docker compose -p tier1-dev -f docker-compose.dev.yml down` (البيانات تبقى)، ومع `-v` تحذف كل البيانات ويعاد ملؤها عند البدء التالي.
- أوامر الصيانة أوامر .NET (لا سكربتات): `dotnet run --project src/Tier1.Api -- --reset-kpi --confirm` و`--load-structure --confirm` و`--backup <مجلد>` و`--restore-test <نسخة>`.
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
والنسخ الاحتياطي اليومي (`--backup`) واختبار الاسترجاع الشهري (`--restore-test`).

## إعادة بناء البيانات المرجعية

```powershell
python tools\build_reference_workbook.py   # ملف Excel للمراجعة
python tools\export_seed.py                # بذرة الخادم
```
