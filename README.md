# منظومة جامعة العاصمة لإدارة الأداء الاستراتيجي — Tier 1

مبنية على أساس منظومة مركز اللغات: مصفوفة الصلاحيات بالنطاق، والتحقق بخطوتين، وسجلات MongoDB المتسلسلة، وواجهة antd.

| الطبقة | المحتوى |
|---|---|
| `src/Tier1.Domain` | الجهات، وهيكل Tier 1، والمؤشرات وإصداراتها، والمستهدفات، والتعارضات |
| `src/Tier1.Application` | الخدمات وقواعد العمل النقية (`IndicatorRules`، `DistributionRules`، `StructureRules`، `TargetRules`) |
| `src/Tier1.Infrastructure` | PostgreSQL (EF Core)، وسجلات MongoDB، والبذرة المرجعية `Persistence/Seed/reference-seed.json` |
| `src/Tier1.Api` | المسارات (`Endpoints/Tier1Endpoints.cs`) والصلاحيات والإعدادات |
| `web` | الواجهة (React + antd)، وتبنى إلى `src/Tier1.Api/wwwroot` |
| `tests/Tier1.Tests` | اختبارات الوحدة (القواعد والصلاحيات والبذرة) |
| `tests/e2e/scenario_test.py` | اختبار شامل على خادم يعمل بأدوار متعددة |
| `docs/requirements.md` | وثيقة المتطلبات والقرارات |
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
python tests\e2e\scenario_test.py    # والخادم يعمل
```

## إعادة بناء البيانات المرجعية

```powershell
python tools\build_reference_workbook.py   # ملف Excel للمراجعة
python tools\export_seed.py                # بذرة الخادم
```
