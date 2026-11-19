# تنظيف تاريخ المستودع من النسخ الاحتياطية وملفات البناء (D9)

أُخرجت النسخ الاحتياطية وملفات البناء التجريبية من المستودع في الفرع `d9-repo-hygiene` ([ADR-0009](adr/0009-no-backups-in-git.md))، لكنها **باقية في تاريخه**: حوالي 22 ميجابايت من النسخ الاحتياطية، و8.6 ميجابايت من ملفات البناء. يمكن لأي من يملك نسخة من المستودع استخراجها. الفحص أثبت أنها بيانات تجريبية واختبارية فقط، فلا عجلة، لكن قرار D9 يجعل التنظيف إلزاميًا قبل النشر.

**التنظيف يعيد كتابة التاريخ:** كل commit بعد `2143403` يتغير معرفه. ينفذه **صاحب المستودع وحده، مرة واحدة، بالتنسيق مع الفريق**، بعد دمج `d9-repo-hygiene` وكل الفروع المفتوحة أو إغلاقها.

## قبل البدء

1. **أعلن تجميدًا للدفع** على المستودع لكل المشاركين، واطلب دمج الطلبات المفتوحة أو إغلاقها.
2. **تأكد أن النسخ الاحتياطية المطلوبة محفوظة خارج git** في التخزين الآمن المعتمد:
   - `نقل-المنظومة/backups/tier1.dump`
   - `نقل-المنظومة/backups/tier1_demo.dump`
   - `نقل-المنظومة/backups/mongo/`
3. **ثبّت `git filter-repo`:**
   ```bash
   pip install git-filter-repo
   ```

## الخطوات

```bash
# 1) نسخة احتياطية كاملة للمستودع كما هو (تحفظ في التخزين الآمن حتى يتأكد الجميع)
git clone --mirror https://github.com/Mohamed-Farraj/Tier1.git Tier1-before-cleanup.git

# 2) نسخة عمل نظيفة للتنظيف
git clone --mirror https://github.com/Mohamed-Farraj/Tier1.git Tier1-cleanup.git
cd Tier1-cleanup.git

# 3) إزالة المسارات من كل التاريخ
git filter-repo --invert-paths \
  --path "نقل-المنظومة/backups" \
  --path-glob "web/.scratch-*" \
  --path-glob "*.dump"

# 4) التحقق: يجب ألا يظهر شيء
git log --all --oneline -- "نقل-المنظومة/backups" "web/.scratch-*"
git rev-list --all --objects | grep -E "\.dump$|/backups/|web/\.scratch-" || echo "نظيف"

# 5) الدفع القسري لكل الفروع والوسوم (يعيد كتابة المستودع البعيد)
git push --force --mirror https://github.com/Mohamed-Farraj/Tier1.git
```

## بعد الدفع

1. **كل مشارك يحذف نسخته المحلية ويستنسخ من جديد.** الدمج أو السحب من نسخة قديمة يعيد الملفات إلى التاريخ.
2. **على GitHub:** طلبات الدمج القديمة تحتفظ بمراجعها. لإزالتها من ذاكرة GitHub المؤقتة، راسل دعم GitHub بطلب «Remove sensitive data» مع معرف المستودع.
3. **يبقى اختبار `RepositoryHygieneTests`** يمنع رجوع هذه الملفات في أي commit لاحق.
4. **تحذف النسخة `Tier1-before-cleanup.git`** بعد أن يتأكد الجميع أن المستودع يعمل، أو تحفظ في التخزين الآمن بحسب سياسة الجامعة.

## إن حدثت مشكلة

النسخة `Tier1-before-cleanup.git` من الخطوة 1 تعيد المستودع كما كان:

```bash
cd Tier1-before-cleanup.git
git push --force --mirror https://github.com/Mohamed-Farraj/Tier1.git
```
