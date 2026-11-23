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
   - **مفتاح بصمة الرقم القومي** بالطريقة نفسها في `T1_PERSONAL_DATA_KEY` ([ADR-0021](adr/0021-account-personal-data.md)). لا يغيَّر بعد إنشاء أول حساب: تغييره يبطل منع تكرار الرقم القومي.
   - الأرقام القومية نفسها تشفَّر بمفاتيح حماية البيانات (`/data/keys`، بشهادة `DP_CERT_FILE`). ضياع هذه المفاتيح يجعل الأرقام المحفوظة غير مقروءة، فأدخلها في النسخ الاحتياطي مع الشهادة.
3. **التشغيل الأول**:
   ```bash
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d postgres mongo
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env build app
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env run --rm app --migrate
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env build tools
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env run --rm tools --load-structure --confirm --operator "<اسم المسؤول المعتمد>"
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env run --rm \
     -e ConnectionStrings__MongoAdmin="mongodb://tier1_root:<MONGO_ROOT_PASSWORD>@mongo:27017/?authSource=admin" tools --mongo-setup
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env run --rm app --create-superadmin admin@capu.edu.eg "مدير المنظومة"
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
   ```
   كلمة المرور المؤقتة للمدير الأعلى تطبع مرة واحدة، وتغير عند أول دخول مع تفعيل التحقق بخطوتين.
   الخادم (`app`) لا ينفذ إلا `--migrate` و`--create-superadmin`؛ بقية الأوامر في خدمة `tools`، صورة منفصلة (`tier1-tools`) بالإعدادات والمجلدات نفسها،
   تعمل بـ `run --rm` فقط ولا تبدأ مع `up` ([ADR-0015](adr/0015-tools-project.md)). إن أعطي الخادم أمرًا منها رفض وسمى الأداة.
4. **التحقق**: `https://tier1.capu.edu.eg/health/ready` يعيد `"status": "ready"`.

**التحديث**: `git pull`، ثم `build app tools`، ثم `run --rm app --migrate`، ثم `up -d app`. الترحيل لا يجري تلقائيًا عند بدء الإنتاج عمدًا.

### البيانات في الإنتاج ([ADR-0010](adr/0010-production-seed.md))

- **`--migrate`** يرحل المخطط، ويضيف ما ينقص من أدوار الكتالوج، ويضيف البيانات المرجعية على قاعدة فارغة فقط. لا يغير بيانات قائمة.
- **إصلاحات البيانات** تغير صلاحيات أدوار قائمة مرة واحدة، فلا تطبق تلقائيًا في الإنتاج. يعرضها `--migrate` في آخره، وتطبق بموافقة المسؤول المعتمد:
  ```bash
  docker compose -f deploy/docker-compose.yml --env-file deploy/.env run --rm tools --data-fixes
  docker compose -f deploy/docker-compose.yml --env-file deploy/.env run --rm tools --apply-data-fixes --confirm --operator "<اسم المسؤول المعتمد>"
  ```
  كل إصلاح يطبق في معاملته، ويسجل في سجل التدقيق باسم من وافق وعدد ما تغير. وإعادة التشغيل لا تعيد ما طبق.
- **الهيكل الرسمي للجامعة** يحمل بـ `--load-structure --confirm --operator`، في الإنتاج على قاعدة بلا جهات فقط (التهيئة الأولى)، ويسجل في سجل التدقيق.
- **`--reset-kpi`** يمحو بيانات المؤشرات كلها، فهو **مرفوض في الإنتاج**. جربه على نسخة تجريبية.
- **الخادم يرفض البدء خارج التطوير** إن كان `Database:MigrateOnStartup` أو `Seed:Structure` أو `Seed:AutoDataFixes` مفعلًا.

## الخيار الثاني: Windows Server وIIS

1. ثبّت ASP.NET Core Hosting Bundle 10 وPostgreSQL 18 وMongoDB 8 (مع حسابي `tier1_app` و`tier1_reader` كما في `deploy/mongo-init/01-tier1.js`).
2. على جهاز البناء:
   ```powershell
   cd web; npm ci; npx vite build; cd ..
   dotnet publish src\Tier1.Api -c Release -o publish
   dotnet publish tools\Tier1.Tools -c Release -o publish-tools
   ```
3. انسخ `publish` إلى `C:\inetpub\tier1`، وأنشئ موقعًا بمجموعة تطبيقات «No Managed Code»، وربط HTTPS بالشهادة.
4. الإعدادات في `appsettings.Production.json` بجوار التطبيق (أو متغيرات بيئة لمجموعة التطبيقات) بالمفاتيح نفسها التي في `deploy/docker-compose.yml`،
   مع `Files:Root` و`DataProtection:KeysPath` و`MongoLogs:CheckpointDirectory` على قرص بيانات خارج مجلد الموقع (مثل `D:\Tier1Data\files`).
   على Windows تحمى مفاتيح البيانات بـ DPAPI فلا تلزم شهادة PFX.
5. `Network:TrustedProxies` = عنوان IIS نفسه (`127.0.0.1`) لأن ANCM يمرر الطلب محليًا.
6. الأدوات في مجلد خارج الموقع (مثل `C:\Tier1Tools`) من `publish-tools`، بالإعدادات نفسها (`appsettings.Production.json` بجوارها، أو متغيرات البيئة)، و`DOTNET_ENVIRONMENT=Production`.
   الترحيل من مجلد التطبيق: `dotnet Tier1.Api.dll --migrate`، ثم من مجلد الأدوات `dotnet Tier1.Tools.dll --load-structure --confirm --operator "<الاسم>"` (أول مرة) ثم `--mongo-setup` (بحساب الإدارة مؤقتًا)،
   ثم `dotnet Tier1.Api.dll --create-superadmin`. وبعد كل تحديث: `dotnet Tier1.Api.dll --migrate` ثم `dotnet Tier1.Tools.dll --data-fixes` و`--apply-data-fixes --confirm --operator` إن وجد معلق.

## ما يرفض الخادم البدء بدونه خارج التطوير

الخادم يفحص إعداداته عند البدء (`StartupGuards`) ويتوقف برسالة واضحة إن: كان التحقق بخطوتين موقوفًا، أو الحسابات التجريبية مفعلة،
أو الكوكيز بلا `__Host-` وSecure، أو لا يوجد خادم بريد، أو مفتاح السلسلة ناقص، أو `Files:Root` ليس مسارًا مطلقًا، أو لا وكيل موثوق.

## قبل كل نشر: لا نسخ احتياطية في المستودع ([ADR-0009](adr/0009-no-backups-in-git.md))

- **اختبار `RepositoryHygieneTests` ينجح** (يشغل مع `dotnet test`): لا نسخ احتياطية ولا تصديرات قواعد ولا ملفات بناء في git.
- **النسخ الاحتياطية** (`pg_dump`، وتصدير MongoDB) في التخزين الآمن المعتمد خارج المستودع، لا في git ولا في مجلد مشترك عام.
- **تاريخ المستودع نُظّف** مرة واحدة من النسخ القديمة بحسب [repo-history-cleanup.md](repo-history-cleanup.md). قبل أول نشر للإنتاج، تأكد من تنفيذه.

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
dotnet Tier1.Tools.dll --backup E:\Backups\Tier1 --mongo-uri "mongodb://tier1_reader:<pw>@127.0.0.1/tier1_logs?authSource=tier1_logs"
dotnet Tier1.Tools.dll --restore-test E:\Backups\Tier1\tier1-YYYYMMDD-HHMMSS
```

- النسخة: `pg_dump` بصيغة مخصصة، و`mongodump` للسجلات، وأرشيف للأدلة والمفاتيح ونقاط التثبيت، وملف بصمات SHA-256.
- الجدولة: يوميًا الثانية صباحًا من Task Scheduler (أو cron في الخيار الأول بـ `docker compose exec postgres pg_dump ...`)، والاحتفاظ 30 يومًا افتراضيًا.
- **اختبار الاسترجاع شهريًا** بأمر `--restore-test`: يتحقق من البصمات، ويسترجع في قاعدة مؤقتة، ويقارن عدد السجلات بالأصلية، ثم يحذفها.
- نسخة خارج الخادم (قرص آخر أو مبنى آخر) لكل نسخة أسبوعية على الأقل.

## الاحتفاظ بالبيانات

مهمة الاحتفاظ (`RetentionJob`) تعمل كل ست ساعات، وتحذف على دفعات من 5000 صف:

| ما يحذف | متى | الإعداد (الافتراضي) |
|---|---|---|
| الجلسات المنتهية أو الملغاة | بعد مدتها | `Retention:SessionDays` (30) |
| نسخة قيد التدقيق في `logs.outbox` | بعد وصولها إلى MongoDB وثبوت سلامة سلسلة يومها في التحقق الليلي | `Retention:OutboxDays` (30) |
| البريد المرسل في `adm.mail_outbox` | بعد إرساله | `Retention:MailSentDays` (180) |
| البريد الفاشل نهائيًا | بعد فشله | `Retention:MailFailedDays` (365) |
| حدث عمل في `int.outbox_events` وسجل معالجاته | بعد أن تعالجه كل معالجاته (الحدث الفاشل يبقى للفحص) | `Retention:EventDays` (30) |
| إشعار في صندوق المستخدم `notify.inbox` | بعد إنشائه، مقروءًا أو لا | `Retention:InboxDays` (365) |
| محاولة دخول في `iam.login_attempts` | بعد تسجيلها | `Retention:LoginAttemptDays` (90) |
| عداد دخول في `iam.login_counters` انتهت نافذته وحجبه | بعد يوم | — |
| ملف في `Files:Root` لا مستند له (رفع فشل حفظه)، أو كتابة ناقصة `.part` | بعد ساعة | — |

قيود التدقيق نفسها لا تحذف: سجلها الدائم في MongoDB بسلسلة البصمات.

## حماية الدخول من التخمين ([ADR-0014](adr/0014-shared-login-attempts.md))

عدادات المحاولات الفاشلة في PostgreSQL، تتشاركها كل نسخ الخادم، فلا يتجاوزها التنقل بين النسخ. لا يحتاج النشر بنسخة واحدة شيئًا جديدًا، ولا تحتاج النسخ المتعددة Redis.

| الحد | الإعداد (الافتراضي) | ما يحدث عند بلوغه |
|---|---|---|
| حساب واحد من عنوان واحد | `Auth:MaxFailedLogins` (5) خلال `Auth:LockoutMinutes` (15) | يحجب العنوان عن هذا الحساب 15 دقيقة، ويدخل صاحبه من مكان آخر |
| كل الحسابات من عنوان واحد (والبريد غير الموجود) | `Auth:MaxFailedLoginsPerAddress` (100) خلال `Auth:AddressWindowMinutes` (15) | يحجب العنوان `Auth:AddressBlockMinutes` (15)، إلا عن حساب دخل منه بنجاح خلال `Auth:KnownAddressDays` (30) |
| حساب واحد من كل العناوين | `Auth:MaxFailedLoginsPerAccount` (20) | يقفل الحساب `Auth:LockoutMinutes` |
| رمز التحقق بخطوتين | `Auth:MaxMfaFailures` (5) | يقفل التحقق `Auth:MfaLockoutMinutes` |

- الرد في كل حجب هو رد كلمة المرور الخطأ وبزمنه، فلا يكشف وجود الحساب ولا الحجب.
- كل محاولة (نجاح أو فشل أو حجب) تسجل في `iam.login_attempts` بسببها، ببصمة البريد لا بالبريد نفسه، ومعها قيد في السجل الأمني.
- حد العنوان مرتفع لأن شبكة الجامعة قد تخرج من عنوان واحد. خفضه إن كانت عناوين المستخدمين متفرقة.
- خلف وسيط عكسي يجب تعريفه في `Network:TrustedProxies`، وإلا رأى الخادم عنوان الوسيط لكل الطلبات فاشتركت فيه كل الحسابات.
- حد المعدل في الذاكرة (`RateLimiting:LoginPerMinute`) يبقى خط دفاع أول لكل نسخة، والعدادات المشتركة هي الضمان عبر النسخ.

## اتصالات قاعدة البيانات

- **لكل نسخة من الخادم مجمع اتصالات بحد صريح:** `Database:MaxPoolSize` (الافتراضي 30)، إلا إن حدده نص الاتصال (`Maximum Pool Size`). والاتصال الخامل يعاد بعد دقيقة.
- **الحساب:** عدد النسخ × `MaxPoolSize` + الأدوات والإدارة (نحو 10) أقل من `max_connections` في PostgreSQL (الافتراضي 100).
  - مثال: نسختان × 30 + 10 = 70.
- **لماذا لا يرفع الحد:**
  - كان الحد الافتراضي لـ Npgsql (100) يساوي حد PostgreSQL. في اختبار الحمل أخذت نسخة واحدة كل اتصالات القاعدة، وأبقتها خاملة دقائق بعده، فرفضت القاعدة غيرها ("too many clients").
  - الاتصالات فوق عدد أنوية القاعدة لا تسرع شيئًا: الاستعلامات تنتظر المعالج داخل PostgreSQL بدل أن تنتظر اتصالًا في المجمع.
  - على بيانات النمو (100 مستخدم، 40 ثانية) بلغ المجمع 30 من 30 دون أي طلب منتظر.
- **المراقبة:** `/api/logs/database` (لمن يطلع على السجل التقني) يعرض الاتصالات المستعملة والمنتظرة وذروتها، وتقرير اختبار الحمل يذكرها.
## البريد الجامعي وقيود عناوين IP ([ADR-0016](adr/0016-university-access.md))

- **اسم الدخول هو البريد الجامعي.** يقبل البريد من نطاق جامعة معتمد أو أي نطاق فرعي منه: `capu.edu.eg` يقبل `eng.capu.edu.eg` و`med.capu.edu.eg`، ولا يقبل `xcapu.edu.eg` ولا `capu.edu.eg.evil.com`.
- **النطاقات بيانات لا شيفرة.** يضيف الترحيل أول مرة جامعة العاصمة (`capu.edu.eg`) وجامعة حلوان (`helwan.edu.eg`). وبعدها يديرها مسؤول النظام من «الإعدادات ← نطاقات البريد الجامعي المعتمدة»: إضافة جامعة، وتعديل، وتفعيل أو إيقاف، وحذف. ولا يلزم نشر جديد.
- **إيقاف نطاق أو حذفه لا يمس الحسابات.** تبقى الحسابات القائمة تدخل كما هي، ويمنع فقط إنشاء حساب جديد بالنطاق وتغيير بريد إليه. وتعرض الشاشة عدد الحسابات بالنطاق قبل التغيير، فتنقل بريدها إلى نطاق معتمد من تعديل الحساب أو توقف.
- **أول مدير أعلى** (`--create-superadmin`) يجب أن يكون بريده من نطاق معتمد.
- **قيود عناوين IP:**
  - قائمة عامة في «الإعدادات ← قيود عناوين IP العامة»، وقائمة لكل حساب في تبويب «قيود عناوين IP» في تفاصيله.
  - كل قائمة عناوين ونطاقات CIDR، IPv4 وIPv6، ولها مفتاح تشغيل.
  - يقبل الحساب إن كان عنوانه في القائمة العامة المشغلة، أو في قائمته المشغلة. وإن لم تسر عليه أي قائمة (موقفة أو فارغة) فالدخول بلا قيد.
  - تفحص عند الدخول بعد التحقق من كلمة المرور، ثم في كل طلب. والرفض 403 `ip_not_allowed` برسالة تذكر العنوان، ويسجل في السجل الأمني (`auth.ip_denied`) وفي محاولات الدخول.
  - يرفض الخادم تغيير القائمة العامة إن كان سيمنع المسؤول الذي يغيرها من عنوانه الحالي (`ip_lockout`). أضف عنوانك أولًا.
- **عنوان العميل الحقيقي:** يقرأ من `X-Forwarded-For` من الوكيل المعرّف في `Network:TrustedProxies` وحده، ولا يوثق برأس يرسله العميل مباشرة. فتأكد أن عنوان الوكيل العكسي (Caddy أو IIS) مضبوط، وإلا ظهر عنوان الوكيل نفسه لكل المستخدمين.
- **الصلاحية:** `adm.security` (مسؤول النظام كاملة، والمراجع اطلاعًا). وعلى قاعدة قائمة تصل بإصلاح البيانات `catalog:adm.security`، يعرضه `--migrate` ويطبق بأمر الأدوات `--apply-data-fixes --confirm --operator`.

## العنوان العام

`App:PublicUrl` إلزامي خارج التطوير: عنوان https الذي يفتح منه المستخدمون المنظومة. روابط الإشعارات في البريد تقود إليه، ويرفض الخادم البدء بدونه أو بعنوان http أو localhost (BE-9).

## المراقبة

- `GET /health/live`: التطبيق يعمل.
- `GET /health/ready`: 503 إن تعذر PostgreSQL وحده. وفيما عداه 200 بحالة `ready` أو `degraded`، مع كل فحص وحالته وسببه دون أرقام:
  - `mongo`: قاعدة السجلات غير متاحة (القيود تنتظر في `logs.outbox`).
  - `audit`: قيود لم تصل MongoDB منذ أكثر من `Health:AuditBacklogMinutes` (15).
  - `events`: حدث فشل نهائيًا في آخر يوم، أو معلق أكثر من `Health:EventBacklogMinutes` (15).
  - `mail`: بريد فشل نهائيًا في آخر يوم (صفحة «البريد الصادر»).
  - اربط التنبيه بحالة `degraded`، لا بالرمز 503 وحده.
- الإشعارات تكتب بعد ثبوت العملية من أحداث `int.outbox_events`. حدث بقي معلقًا طويلًا أو فشل بعد عشر محاولات يعني أن الإشعارات متوقفة: راجع `last_error` فيه.
- سجلات التدقيق والأمان والوصول في MongoDB، ويعرضها المراجع الرقابي من صفحة «السجلات»، والتحقق الليلي من سلامة السلسلة يرسل نقطة تثبيت بالبريد.
- البريد الصادر في جدول `adm.mail_outbox` بحالته ومحاولاته، وتعرضه صفحة «البريد الصادر» لمن يملك `adm.mail` (مسؤول النظام افتراضيًا) مع إعادة إرسال الإشعار الفاشل؛ التذكيرات اليومية بالنماذج القريبة من موعدها تبدأ بعد الثامنة صباحًا بتوقيت القاهرة.

## فترة الخطة وهيكل الكلية ([ADR-0017](adr/0017-college-targets-structure-activities-plan-period.md))

- **فترة الخطة إعداد لا شيفرة.** الافتراضي من 1 سبتمبر 2026 إلى 31 أغسطس 2031: خمس سنوات خطة (2027–2031) وعشرون دورة ربع سنوية.
  - يغيرها المدير الأعلى وحده من «الإعدادات ← فترة خطة Tier 1» بسبب مكتوب، بتعليمات وزارة التعليم العالي.
  - الفترة سنوات كاملة من سنة إلى عشر.
  - يرفض التغيير إن ترك دورة رسمية خارج الفترة.
- **الترحيلات** `CollegeManagementStructure` و`InitiativeActivities` يطبقها `--migrate` كالمعتاد.
  - الأول يضيف «إدارة الكلية» (العميد والوكلاء الثلاثة) ومسئول الجودة إلى هياكل الكليات القائمة.
  - الثاني ينشئ جدول أنشطة المبادرات.
- **الأدوار:** المواقع الجديدة بلا دور مقترح. ينشئ المدير الأعلى أدوار العميد والوكلاء ومسئول الجودة من مصفوفة الصلاحيات ويسندها.
