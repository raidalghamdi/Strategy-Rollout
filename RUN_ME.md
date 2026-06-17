# تشغيل المنصة محلياً | Running the Platform Locally

## المتطلبات | Prerequisites

- **.NET 8 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- SQLite يعمل افتراضياً بدون أي إعداد. (اختياري) **Microsoft SQL Server** كمستودع خارجي للقراءة فقط.

تحقّق من النسخة:
```bash
dotnet --version
# يجب أن يطبع: 8.0.x
```

---

## ١. التشغيل السريع (SQLite — افتراضي) | Quick start

```bash
cd StrategyHouse.Web
dotnet restore
dotnet run
```

سيُنشئ هذا قاعدة بيانات SQLite في `StrategyHouse.Web/strategy_house.db` ويبذرها تلقائياً ببيانات GAC الكاملة.

افتح المتصفح: **http://localhost:5080** (أو المنفذ الذي يطبعه `dotnet run`).

---

## ٢. الحسابات والبيانات المبذورة | Seeded Accounts & Data

### حساب المدير | Admin user
- البريد: `admin@gac.gov.sa`
- كلمة المرور: `Demo@123`

### الأدوار | Roles
- `Admin` — كامل الصلاحيات (المدير الافتراضي يحملها)
- `Facilitator` — مكتب الاستراتيجية
- `Viewer` — مشاهد فقط

### الجلسة التجريبية | Demo session
- العنوان: **جلسة تجريبية — إدارة الموارد البشرية**
- **رمز الجلسة (AccessCode): `DEMO-001`**
- الإدارات المربوطة: ٣ إدارات تجريبية
- الحضور: ٩ مشاركين

### المحتوى | Content
- **بيت الاستراتيجية الافتراضي** — رؤية، رسالة، ٥ قيم، ٥ ركائز، ١٣ هدف (مكان قابل للتعديل).
- **١٨ إدارة** بأسماء مكان (DEPT-01 ... DEPT-18).
- **٨ التزامات** في القائمة المنسقة.
- **استبيان نهاية الجلسة**: ٦ أسئلة.
- **اختبار ما بعد الجلسة**: ٥ أسئلة (مجهول، نتائج إجمالية).

---

## ٣. المسارات الرئيسية | Key Routes

### للحضور (لا يحتاج تسجيل دخول) | For Attendees (no login)
- `/` — الصفحة الرئيسية
- `/Session/Console?code=DEMO-001` — طاولة الجلسة (لمكتب الاستراتيجية أثناء الجلسة)
- `/Session/Baseline?code=DEMO-001` — القياس المرجعي (الحركة ١)
- `/Session/Map?code=DEMO-001&departmentId=1` — لوحة بناء الخريطة (الحركة ٢)
- `/Session/Commit?code=DEMO-001&departmentId=1` — اختيار الالتزامات + التوقيع (الحركة ٣)
- `/Session/Survey?code=DEMO-001` — استبيان نهاية الجلسة
- `/Session/Quiz?code=DEMO-001` — الاختبار الاختياري (صباح اليوم التالي)
- `/Wall?code=DEMO-001` — جدار الالتزامات لجلسة محددة

### لمكتب الاستراتيجية (يحتاج تسجيل دخول) | For Strategy Office (login required)
- `/Admin` — لوحة الإدارة
- `/Admin/Frameworks` — أطر الاستراتيجية
- `/Admin/Departments` — قائمة الإدارات الـ١٨
- `/Admin/Sessions` — قائمة الجلسات + إنشاء جلسة جديدة
- `/Admin/Commitments` — قائمة الالتزامات المقترحة
- `/Admin/Journey?sessionId=1&departmentId=1` — خريطة رحلة رئيس الإدارة
- `/Dashboard` — لوحة المؤشرات اليومية (٥ أسئلة تشغيلية)
- `/Wall` — كامل الجدار (بدون رمز)

---

## ٤. سيناريو تجربة كامل خلال ٥ دقائق | 5-Minute Walkthrough

1. **افتح** `http://localhost:5080/`.
2. **سجّل دخول** كمدير: `admin@gac.gov.sa` / `Demo@123`.
3. **افتح طاولة الجلسة**: من الصفحة الرئيسية أدخل `DEMO-001`، أو اذهب مباشرة إلى `/Session/Console?code=DEMO-001`.
4. **شغّل الحركة ١** — افتح `/Session/Baseline?code=DEMO-001` (أو امسح رمز QR من شاشة الطاولة) وأرسل قياساً مرجعياً.
5. **شغّل الحركة ٢** — افتح `/Session/Map?code=DEMO-001&departmentId=1` واسحب عناصر الإدارة (مشاريع/مؤشرات/أدوار) على الركائز والأهداف.
6. **شغّل الحركة ٣** — افتح `/Session/Commit?code=DEMO-001&departmentId=1`، اختر التزامات، أضف توقيعاً تطوعياً، ثم اضغط "إنهاء وتثبيت".
7. **شاهد الجدار** — افتح `/Wall?code=DEMO-001`.
8. **شاهد لوحة المؤشرات** — `/Dashboard` (بحساب الإدارة).
9. **شاهد خريطة رحلة الرئيس** — `/Admin/Journey?sessionId=1&departmentId=1`.

---

## ٥. إعادة تهيئة قاعدة البيانات | Resetting the DB

لحذف SQLite وإعادة البذر:

```bash
cd StrategyHouse.Web
rm strategy_house.db
dotnet run
```

---

## ٦. نشر إنتاجي | Production Deploy

```bash
dotnet publish StrategyHouse.Web -c Release -o ./publish
# انسخ ./publish + appsettings.json إلى الخادم
# شغّل: dotnet StrategyHouse.Web.dll
```

تذكّر:
- استخدم HTTPS خلف عكسي proxy (nginx).
- بدّل قيم `App:BaseUrl` و `App:NoReplyEmail` لقيم الإنتاج.

---

## ٧. استكشاف الأخطاء | Troubleshooting

| المشكلة | الحل |
|---|---|
| `dotnet: command not found` | ثبّت .NET 8 SDK من [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0) |
| المنفذ مشغول | عدّل `--urls http://localhost:PORT` في `dotnet run` |
| SQLite "disk I/O error" | تأكد أن مسار `strategy_house.db` قابل للكتابة |
| لا تظهر العناصر العربية بشكل صحيح | تأكد أن المتصفح يدعم UTF-8 (افتراضياً نعم) — والخط مُحمّل من Google Fonts |
| SignalR لا يتصل | الميزة اختيارية، اللوحة تعمل بدونه (تحفظ محلياً فقط عبر POST) |

---

## ٨. الترخيص والملاحظات | License & Notes

- المحتوى الاستراتيجي المبذور (الرؤية، الرسالة، القيم، الركائز، الأهداف، الإدارات) **بيانات مكان قابلة للاستبدال**.
- استبدلها بالمحتوى الحقيقي قبل الإنتاج من `/Admin/Framework/1` أو من `SeedData.cs`.
- المنصة مفتوحة الهيكل (Open Source) داخل الهيئة. أي تعديل يجب أن يحترم القيود التشغيلية الموثقة في `README.md`.
