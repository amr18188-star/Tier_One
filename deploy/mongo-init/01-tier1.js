// تهيئة قاعدة السجلات عند أول تشغيل لحاوية MongoDB: دوران وحسابان.
// tier1LogWriter للتطبيق: إضافة وقراءة فقط، فلا يعدل حدثًا ولا يحذفه ولا يسقط مجموعة.
// tier1LogReader للمراجع الرقابي الخارجي: قراءة فقط.
const dbName = "tier1_logs";
const logs = db.getSiblingDB(dbName);

logs.createRole({
  role: "tier1LogWriter",
  privileges: [{ resource: { db: dbName, collection: "" }, actions: ["insert", "find"] }],
  roles: [],
});
logs.createRole({
  role: "tier1LogReader",
  privileges: [{ resource: { db: dbName, collection: "" }, actions: ["find"] }],
  roles: [],
});
logs.createUser({ user: "tier1_app", pwd: process.env.MONGO_APP_PASSWORD, roles: ["tier1LogWriter"] });
logs.createUser({ user: "tier1_reader", pwd: process.env.MONGO_READER_PASSWORD, roles: ["tier1LogReader"] });
