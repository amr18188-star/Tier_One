# دليل التشغيل على خوادم الجامعة (On-Premises)

المنظومة تطبيق واحد (ASP.NET Core يخدم الواجهة والـ API) مع قاعدتين: PostgreSQL للبيانات، وMongoDB لسجلات التدقيق المتسلسلة.
خياران للتشغيل؛ الأول أسهل في التحديث والثاني لمن يعتمد Windows Server وIIS.

## المتطلبات

| البند | الحد الأدنى للتجربة على الجامعة كلها |
|---|---|
| الخادم | 4 أنوية، 8 جيجابايت ذاكرة، 100 جيجابايت قرص (الأدلة تنمو بحسب الرفع) |
| نظام التشغيل | Ubuntu 24.04 مع Docker، أو Windows Server 2022 |
| قواعد البيانات | PostgreSQL 18 وMongoDB 8 (حاويات في الخيار الأول) |
| الشبكة | اسم داخلي مثل `tier1.capu.edu.eg` وشهادة TLS له، وخادم بريد SMTP |

اختبار الحمل (`LoadTests` في `tests/Tier1.E2E`) على جهاز تطوير عادي: 100 مستخدم متزامن، 219 طلبًا في الثانية، وp95 أقل من نصف ثانية بلا أخطاء.

## الخيار الأول: Docker Compose (موصى به)

```bash
git clone <repo> /opt/tier1 && cd /opt/tier1
cp deploy/.env.example deploy/.env     # املأ كل القيم (كلمات مرور عشوائية طويلة)
```

1. **الشهادات**: ضع `tier1.crt` و`tier1.key` في `TLS_CERT_DIR`، وشهادة PFX لحماية المفاتيح في `DP_CERT_FILE`:
   ```bash
   openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj "/CN=tier1-dataprotection" -keyout dp.key -out dp.crt
   openssl pkcs12 -export -inkey dp.key -in dp.crt -out /etc/tier1/dp.pfx -passout pass:<DP_CERT_PASSWORD>
   ```
2. **مفتاح السلسلة**: `openssl rand -base64 32` في `T1_CHAIN_KEY`، واحفظ نسخة منه خارج الخادم (خزنة الجامعة).
3. **التشغيل الأول**:
   ```bash
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d postgres mongo
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env build app
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env run --rm app --migrate
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env run --rm \
     -e ConnectionStrings__MongoAdmin="mongodb://tier1_root:<MONGO_ROOT_PASSWORD>@mongo:27017/?authSource=admin" app --mongo-setup
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env run --rm app --create-superadmin admin@capu.edu.eg "مدير المنظومة"
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
   ```
   كلمة المرور المؤقتة للمدير الأعلى تطبع مرة واحدة، وتغير عند أول دخول مع تفعيل التحقق بخطوتين.
4. **التحقق**: `https://tier1.capu.edu.eg/health/ready` يعيد `ready`.

**التحديث**: `git pull`، ثم `build app`، ثم `run --rm app --migrate`، ثم `up -d app`. الترحيل لا يجري تلقائيًا عند بدء الإنتاج عمدًا.

## الخيار الثاني: Windows Server وIIS

1. ثبّت ASP.NET Core Hosting Bundle 10 وPostgreSQL 18 وMongoDB 8 (مع حسابي `tier1_app` و`tier1_reader` كما في `deploy/mongo-init/01-tier1.js`).
2. على جهاز البناء:
   ```powershell
   cd web; npm ci; npx vite build; cd ..
   dotnet publish src\Tier1.Api -c Release -o publish
   ```
3. انسخ `publish` إلى `C:\inetpub\tier1`، وأنشئ موقعًا بمجموعة تطبيقات «No Managed Code»، وربط HTTPS بالشهادة.
4. الإعدادات في `appsettings.Production.json` بجوار التطبيق (أو متغيرات بيئة لمجموعة التطبيقات) بالمفاتيح نفسها التي في `deploy/docker-compose.yml`،
   مع `Files:Root` و`DataProtection:KeysPath` و`MongoLogs:CheckpointDirectory` على قرص بيانات خارج مجلد الموقع (مثل `D:\Tier1Data\files`).
   على Windows تحمى مفاتيح البيانات بـ DPAPI فلا تلزم شهادة PFX.
5. `Network:TrustedProxies` = عنوان IIS نفسه (`127.0.0.1`) لأن ANCM يمرر الطلب محليًا.
6. الترحيل وإنشاء المدير الأعلى من مجلد التطبيق: `dotnet Tier1.Api.dll --migrate` ثم `--mongo-setup` (بحساب الإدارة مؤقتًا) ثم `--create-superadmin`.

## ما يرفض الخادم البدء بدونه خارج التطوير

الخادم يفحص إعداداته عند البدء (`StartupGuards`) ويتوقف برسالة واضحة إن: كان التحقق بخطوتين موقوفًا، أو الحسابات التجريبية مفعلة،
أو الكوكيز بلا `__Host-` وSecure، أو لا يوجد خادم بريد، أو مفتاح السلسلة ناقص، أو `Files:Root` ليس مسارًا مطلقًا، أو لا وكيل موثوق.

## التحقق بخطوتين ([ADR-0007](adr/0007-mfa-by-capability.md))

1. **أدوار الأمان الثابتة يلزمها التحقق دائمًا:** `Auth:MfaRoles` في `appsettings.json` (المدير الأعلى، ومسؤول النظام، والمراجع الرقابي، ورئيس الجامعة، واللجنة العليا، ومدير متابعة الخطة). تغييرها من إعدادات الخادم فقط، لا من الشاشة.
2. **المفتاح «المصادقة الثنائية لكل من يعتمد أو يقرر أو يدير الصلاحيات»** في «إعدادات النظام»:
   - يشمل عند تشغيله كل من يملك فعليًا إجراء قرار، أو تعديل القيم الرسمية للجامعة، أو إدارة الحسابات والأدوار والصلاحيات وإعدادات النظام: الجودة، ومسئول ملف Tier 1، والعمداء، ولجان الكليات، وممثلي الأقسام، والأدوار المخصصة والاستثناءات الفردية.
   - يغيره **المدير الأعلى وحده** بسبب مكتوب يسجل في سجل التدقيق.
   - **موقوف افتراضيًا عند النشر.**
3. **قبل تشغيل المفتاح:**
   - أبلغ مستخدمي الكليات أنهم سيطلب منهم عند دخولهم التالي تثبيت تطبيق مصادقة (Microsoft أو Google Authenticator) ومسح الرمز.
   - جلساتهم المفتوحة غير المتحقق منها تتوقف عن الإجراءات حتى يدخلوا من جديد.
   - يسري المفتاح خلال 30 ثانية من تغييره.

## النسخ الاحتياطي والاسترجاع

```bash
# الاتصالات من إعدادات الخادم نفسها (ConnectionStrings:Tier1 وMongo وFiles:Root وDataProtection:KeysPath وMongoLogs:CheckpointDirectory)؛
# يلزم تثبيت pg_dump وpg_restore وmongodump، أو --pg-bin لمجلد أدوات PostgreSQL.
dotnet Tier1.Api.dll --backup E:\Backups\Tier1 --mongo-uri "mongodb://tier1_reader:<pw>@127.0.0.1/tier1_logs?authSource=tier1_logs"
dotnet Tier1.Api.dll --restore-test E:\Backups\Tier1	ier1-YYYYMMDD-HHMMSS
```

- النسخة: `pg_dump` بصيغة مخصصة، و`mongodump` للسجلات، وأرشيف للأدلة والمفاتيح ونقاط التثبيت، وملف بصمات SHA-256.
- الجدولة: يوميًا الثانية صباحًا من Task Scheduler (أو cron في الخيار الأول بـ `docker compose exec postgres pg_dump ...`)، والاحتفاظ 30 يومًا افتراضيًا.
- **اختبار الاسترجاع شهريًا** بأمر `--restore-test`: يتحقق من البصمات، ويسترجع في قاعدة مؤقتة، ويقارن عدد السجلات بالأصلية، ثم يحذفها.
- نسخة خارج الخادم (قرص آخر أو مبنى آخر) لكل نسخة أسبوعية على الأقل.

## المراقبة

- `GET /health/live` (التطبيق يعمل) و`GET /health/ready` (PostgreSQL متاح).
- سجلات التدقيق والأمان والوصول في MongoDB، ويعرضها المراجع الرقابي من صفحة «السجلات»، والتحقق الليلي من سلامة السلسلة يرسل نقطة تثبيت بالبريد.
- البريد الصادر في جدول `adm.mail_outbox` بحالته ومحاولاته؛ التذكيرات اليومية بالنماذج القريبة من موعدها تبدأ بعد الثامنة صباحًا بتوقيت القاهرة.
