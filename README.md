# منصة إطلاق استراتيجية هيئة الحكومة الرقمية (GAC)
## Strategy House Rollout Platform

[![Deploy on Railway](https://railway.com/button.svg)](https://railway.com/new/github)

> **One-click deploy:** Click the button above → sign in to Railway → pick this repo (`Strategy-Rollout`) → Deploy. After deploy: **Settings → Networking → Generate Domain** to get your public URL.


منصة رقمية تفاعلية لإطلاق استراتيجية الهيئة على ١٨٠ موظفاً موزّعين على ١٨ إدارة خلال أربعة أيام تشغيل (الأيام ٢ إلى ٥ فقط)، بجلسات مدتها ٩٠ دقيقة لكل إدارة كاملة.

A configurable, Arabic-first platform that runs the Strategy House rollout for the General Authority for Competition (GAC): three movements per session (Define → Build → Commit), a collaborative per-department Strategy Map, voluntary digital signatures, and a daily operational dashboard for the strategy office.

---

## معمارية المشروع | Solution Architecture

ثلاث طبقات على نمط CX_final — Clean Architecture مبسطة:

| المشروع | الدور |
|---|---|
| `StrategyHouse.Domain` | الكيانات (Entities) والتعدادات (Enums) — لا تبعيات على بنية تحتية |
| `StrategyHouse.Infrastructure` | DbContext، إعدادات EF، البذور (SeedData)، Identity |
| `StrategyHouse.Web` | ASP.NET Core 8 MVC + Razor Views، SignalR، خدمات النطاق |

---

## التقنيات | Tech stack

- **.NET 8** / **ASP.NET Core 8 MVC** (Razor Views, server-rendered)
- **Entity Framework Core 8** with provider switch:
  - **SQLite** (default for development; file: `strategy_house.db`)
  - **MySQL / MariaDB** via Pomelo (production)
- **ASP.NET Identity** (3 roles: `Admin`, `Facilitator`, `Viewer`)
- **SignalR** for the Movement 2 real-time canvas sync (`/hubs/canvas`)
- **QRCoder** for in-room QR display (baseline + survey)
- **Arabic RTL** throughout (Tajawal / Cairo fonts)

---

## الميزات | Features

### 🏛️ بيت الاستراتيجية القابل للتشكيل (Configurable Framework)
- رؤية، رسالة، قيم (٥)، ركائز (٥)، أهداف (١٣) — مبذور افتراضياً ببيت GAC ولكنه قابل للاستبدال الكامل من لوحة الإدارة.
- المنصة قابلة لإعادة الاستخدام لأي شكل بيت استراتيجية (Layer/Element data model).

### 🎯 الجلسة (٩٠ دقيقة)
- **الحركة ١ (٢٠د) — التعريف:** قياس مرجعي مجهول عبر رمز QR، ثم عرض البيت كاملاً.
- **الحركة ٢ (٤٠د) — البناء:** كل إدارة على iPad/شاشة تسحب مشاريعها/مؤشراتها/أدوارها على عناصر البيت — مزامنة مباشرة عبر SignalR.
- **الحركة ٣ (٣٠د) — الالتزام:** اختيار الالتزامات من قائمة منسقة + إضافة التزامات مخصصة + ربط كل التزام بعنصر استراتيجي + **توقيع تطوعي على iPad**.
- **إغلاق:** استبيان ٦ أسئلة، ثم رسالة الشكر/الخريطة/الاختبار صباحاً.

### 🧱 جدار الالتزامات الرقمي | Digital Commitment Wall
- متاح للحضور فقط أثناء أسبوع الإطلاق (عبر رمز الجلسة).
- يُعمَّم على الجميع تلقائياً عند اكتمال جميع الإدارات الـ ١٨.

### 📊 لوحة المؤشرات اليومية | Daily Dashboard
خمسة أسئلة تشغيلية يومية لمكتب الاستراتيجية:
1. من حضر اليوم؟ (الحضور بحسب الجلسة/الإدارة)
2. كيف وجدوا الجلسة؟ (متوسطات الاستبيان)
3. ما الالتزامات الأكثر تكراراً؟
4. هل بقيت المفاهيم؟ (نسبة الصحة في اختبار اليوم)
5. ماذا كتبوا بأقلامهم؟ (النصوص المفتوحة)

### 🗺️ خريطة رحلة رئيس الإدارة | Department Head Journey Map
وثيقة تُولَّد لكل (جلسة × إدارة) قبل اليوم بـ ٢٤ ساعة — تحدد لحظتين كلاميتين للرئيس (افتتاح + إغلاق) ضمن خط زمني للـ ٩٠ دقيقة.

### 📧 الباقة البريدية لليوم التالي | Same-day Email Package
رسالة مخصصة لكل حاضر فيها صورة خريطة إدارته + رسالة شكر + رابط الاختبار الاختياري (Arabic RTL HTML).

### 🎓 الاختبار الاختياري | Optional Quiz
٥ أسئلة، **مجهول الهوية**، نتائج إجمالية فقط (لا تُعرض هوية أحد، حتى للمنظم).

---

## القيود التشغيلية المُلتزم بها | Operational Constraints Honored

| القيد | كيف تتعامل المنصة |
|---|---|
| لا يمكن جمع كل المشاركين/الرؤساء في مكان واحد | الجدار الرقمي + الباقة البريدية يحلان محل الحدث الجماعي |
| أيام ١ و ٦ غير متاحة | الجلسات تتركز في الأيام ٢–٥ |
| لا اختلاط بين إدارات | كل إدارة بطاولة منفصلة، ولا قنوات مشتركة |
| نضج تنظيمي منخفض + موظفون جدد | الحركة ١ تعليم نقي، الحركة ٢ بناء جماعي، الحركة ٣ التزام مبسط |
| التوقيع تطوعي | لا إجبار، يُسجَّل فقط من يضغط "حفظ" |

---

## الأدوار | Roles

| الدور | الوصول |
|---|---|
| `Admin` | كامل الصلاحيات: إدارة الإطار، الإدارات، الجلسات، الالتزامات، الاستبيان، الاختبار، لوحة المؤشرات |
| `Facilitator` | مكتب الاستراتيجية: الإدارة + المؤشرات + خريطة رحلة الرئيس |
| `Viewer` | عرض فقط (مستقبلاً) |
| **الحاضر** | لا تسجيل دخول — يدخل بـ رمز الجلسة (`AccessCode`) |

---

## بدء التشغيل | Getting Started

راجع ملف **`RUN_ME.md`** في الجذر للتعليمات خطوة بخطوة.

ملخّص سريع:

```bash
cd StrategyHouse.Web
dotnet run
# الموقع: http://localhost:5080
# دخول تجريبي: admin@gac.gov.sa / Demo@123
# رمز الجلسة التجريبية: DEMO-001
```

---

## التبديل إلى MySQL | Switching to MySQL

في `StrategyHouse.Web/appsettings.json`:

```json
{
  "Database": { "Provider": "MySql" },
  "ConnectionStrings": {
    "MySql": "Server=localhost;Database=strategy_house;User=root;Password=YOUR_PASSWORD;"
  }
}
```

ثم:

```bash
dotnet ef database update --project StrategyHouse.Infrastructure --startup-project StrategyHouse.Web
```

---

## هيكل المجلدات | Folder Structure

```
StrategyHouse/
├── StrategyHouse.sln
├── README.md
├── RUN_ME.md
├── StrategyHouse.Domain/
│   ├── Entities/        (Framework, Department, Session, StrategyMap, Survey, Identity)
│   └── Enums/
├── StrategyHouse.Infrastructure/
│   └── Persistence/     (ApplicationDbContext, SeedData)
└── StrategyHouse.Web/
    ├── Program.cs
    ├── appsettings.json
    ├── Controllers/     (Home, Account, Session, Wall, Dashboard, Admin)
    ├── Hubs/            (MapCanvasHub — SignalR)
    ├── Services/        (QrService, EmailComposer, DashboardService, JourneyMapService)
    ├── Views/           (Razor Pages — all RTL Arabic)
    └── wwwroot/         (site.css, map-canvas.js, signature.js)
```

---

## ملاحظات | Notes

- نموذج البيانات يدعم أكثر من إطار (`Framework`) — يمكن تشغيل المنصة لاستراتيجيات أخرى في المستقبل.
- البيانات الاستراتيجية الفعلية المعروضة في الواجهة هي بذور مكان أصلية؛ تُعدَّل من `Admin/Framework/{id}` أو مباشرة في `SeedData.cs`.
- مدراء الإدارات الـ ١٨ المبذورون أسماء مكان (Placeholder) — استبدلها بأسماء حقيقية قبل الإنتاج.
