// Phase 20.35 — One-time migration of built-in keyword dictionaries from
// OpenTextAutoCategorizer.cs source code into editable DB rows
// (SurveyQuestionCategories.KeywordsJson).
//
// After this seeder runs once successfully:
//   * Each existing category row gets its keyword list populated
//   * The IsBuiltin flag is flipped to true so the UI knows the row is
//     part of the platform's official "آلية القياس" rule sheet
//   * Subsequent edits in /Admin/Survey/CategoryManager become the source
//     of truth — the C# dictionaries in OpenTextAutoCategorizer remain only
//     as a fallback for the rare case where the DB has zero keywords.
//
// Idempotency: the seeder only writes to a category row if its KeywordsJson
// is currently "[]" / null / empty. That way, once an admin edits keywords,
// re-deploying the app never silently overwrites their changes.

using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure;
using StrategyHouse.Infrastructure.Persistence;
using System.Text.Json;

namespace StrategyHouse.Web.Services;

public static class CategoryKeywordsSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        // Map: questionOrder -> (categoryName -> keyword[])
        // Keep order-of-priority intact (most-specific category first) by matching
        // the same order used in OpenTextAutoCategorizer.cs.
        var rules = new Dictionary<int, List<(string Cat, string[] Kws)>>
        {
            [4] = new()
            {
                ("أنظمة وتقنية", new[] {
                    "تقني", "تقنيه", "تقنيا", "تكنولوجي", "رقمي", "رقمنه",
                    "نظام", "انظمه", "النظم", "اتمته", "اتمتت", "اتمتة",
                    "منصه", "منصات", "برامج", "تطبيق", "البنيه التقنيه", "بنيه تقنيه",
                    "البنيه التحتيه", "بنيه تحتيه", "البنيه التحتي", "بنيه تحتي",
                    "البنيه التحتيه الالكترونيه", "الاسواق الرقميه", "اسواق رقميه",
                    "ذكاء اصطناعي", "ai", "بيانات", "قاعده بيانات",
                }),
                ("ثقافة مؤسسية", new[] {
                    "ثقافه", "ثقافه مؤسسيه", "ثقافيه", "وعي", "الوعي", "ادراك",
                    "وعي المستفيد", "زياده وعي", "زيادة وعي",
                    "الاستراتيجيه حاضره", "عكس الاستراتيجيه", "السلوك", "سلوك",
                    "فعاليات", "في جميع الفعاليات",
                    "مقاومه التغيير", "مقاومه التغير", "ممانعه", "روح الفريق",
                    "بيئه العمل", "بيئه عمل", "تعاون داخلي", "تواصل داخلي",
                    "ولاء", "انتماء",
                }),
                ("موارد بشرية", new[] {
                    "موارد بشريه", "موارد البشريه", "بشري", "كوادر", "كفاءات",
                    "تدريب", "تطوير الموظف", "موظف", "موظفين", "استقطاب",
                    "عدد الموظفين", "نقص الموظفين", "نقص الكوادر",
                    "خبرات", "مهارات", "رواتب", "حوافز", "تأهيل",
                }),
                ("تشريعية", new[] {
                    "تشريع", "تشريعات", "تشريعي", "قانون", "قوانين", "قانوني",
                    "لائحه", "لوائح", "نظام المنافسه", "العقوبات", "صلاحيات قانونيه",
                }),
                ("تنظيمية", new[] {
                    "تنظيمي", "تنظيميه", "اجراءات", "اجراء", "بيروقراطيه", "بيروقراطي",
                    "هيكل", "هيكله", "هيكليه", "تنسيق", "تكامل", "حوكمه",
                    "صلاحيات", "تفويض", "ادارات", "ادارة العمل",
                    "تعاون مع القطاع", "القطاع الخاص", "الجهات الخارجيه",
                    "شراكات", "تنسيق مع", "تكامل مع",
                }),
                ("أخرى", new[] {
                    "ميزانيه", "ميازنيه", "ميازنه", "المياازنيه", "الماليه",
                    "تمويل", "تمويلي", "موارد ماليه",
                    "حملات تسويقيه", "تسويق", "تسويقي", "اعلامي", "اعلام",
                    "المنشات", "الاسواق", "رقابه الاسواق", "رقابه",
                    "الاحتكار", "احتكار", "المنافسه عادله", "منافسه عادله",
                }),
            },
            [5] = new()
            {
                ("شفافية", new[] { "شفافيه", "شفافيت", "شفاف", "وضوح", "افصاح", "مصداقيه" }),
                ("تعاون",   new[] { "تعاون", "تعاوني", "شراكه", "عمل جماعي", "فريق واحد", "تكاتف" }),
                ("تميز",    new[] { "تميز", "تميُّز", "التميز", "اتقان", "جوده", "كفاءه", "تطوير" }),
                ("عدالة",   new[] { "عداله", "عادل", "مساواه", "انصاف", "نزاهه" }),
                ("ابتكار",  new[] { "ابتكار", "ابداع", "ابداعي", "تجديد", "ابتكاري", "تطوير مبتكر" }),
                ("قيمة معتمدة", new[] { "شفافيه", "تعاون", "تميز", "عداله", "ابتكار" }),
                ("قيمة جديدة", new[] {
                    "مرونه", "احترافيه", "اخلاص", "مسؤوليه", "مسؤوليه مجتمعيه",
                    "التزام", "امانه", "احترام", "رياده", "ريادي", "ولاء",
                    "انجاز", "تمكين", "ثقه", "كرامه", "قياده",
                }),
            },
            [7] = new()
            {
                ("تشريعات", new[] {
                    "تشريع", "تشريعات", "قانون", "قوانين", "لائحه", "لوائح",
                    "نظام المنافسه", "العقوبات", "تحديث لائحه",
                    "نطاق تطبيق نظام", "نطاق تطبيق", "برنامج الامتثال", "الامتثال",
                    "منع الاحتكار", "تجريم الاحتكار", "مكافحه الاحتكار",
                }),
                ("خدمات", new[] {
                    "خدمه", "خدمات", "خدمات الكترونيه", "منصه الكترونيه",
                    "قنوات خدمه", "تجربه المستفيد", "مستفيد", "مستهلك",
                    "بلاغات", "شكاوى", "بت في البلاغات",
                    "اتمته الاجراءات", "اتمته جميع الاجراءات", "تسهيل الاجراءات",
                    "القضاء على البيروقراطيه", "بيروقراطيه",
                }),
                ("بيئة عمل", new[] {
                    "بيئه عمل", "بيئه العمل", "تطوير الموظفين", "تطوير الموظف",
                    "تدريب", "كوادر", "كفاءات", "حوافز", "رفاهيه", "سعاده",
                    "تمكين الموظف", "ثقافه مؤسسيه",
                    "رفع قيمه الموظفين", "تقدير الموظفين", "تقديرهم",
                    "عدد الموظفين", "نمو الهيئه", "الاختصاصات",
                }),
                ("دور وطني", new[] {
                    "دور وطني", "ريادي", "قياده", "تأثير", "مرجعيه", "اقليمي", "عالمي",
                    "رؤيه 2030", "رؤية 2030", "رؤيه ٢٠٣٠", "اقتصاد وطني",
                    "تنافسيه", "تنافسيه السوق", "حمايه المستهلك", "حمايه المنافسه",
                    "جذب الاستثمار", "ناتج محلي", "تنميه", "استراتيجي",
                    "تحقيق الاهداف", "تحقيق الهدف", "العمل الاستباقي", "استباقي",
                    "نقله نوعيه", "نقله نوعيه في العمليات", "تطور نوعي",
                    "رائده", "رائده في", "رائده في مجال المنافسه",
                    "المنطقه العربيه", "عداله في الاسواق", "عداله الاسواق",
                    "نشر الوعي", "الوعي بسياسات المنافسه", "حملات تسويقيه",
                    "تحقيق اهدافها", "اهدافها الطموحه", "اهدافها",
                    "انخفاض عدد المخالفات", "المخالفات", "منتجات متعدده", "اسعار مختلفه",
                }),
                ("أخرى", new[] {
                    "متفائل", "تفاؤل", "امل", "التوفيق", "نتمنى التوفيق",
                    "التميز والابداع", "تميز وابداع", "تميز", "ابداع",
                    "الاحترافيه", "احترافيه", "جوده", "تطوير",
                }),
            },
        };

        // Pull every open-text question with its categories. We update categories whose
        // KeywordsJson is empty/null/"[]" so admin edits are never overwritten.
        var openQuestions = await db.SurveyQuestions
            .Include(q => q.Categories)
            .Where(q => q.Type == "Text" || q.QuestionType == StrategyHouse.Domain.Enums.QuestionType.OpenText)
            .ToListAsync(ct);

        int updatedRows = 0;
        foreach (var q in openQuestions)
        {
            if (!rules.TryGetValue(q.Order, out var ruleSet)) continue;

            foreach (var (catName, kws) in ruleSet)
            {
                // Match by exact category name; the names were seeded by Phase12SurveySeeder
                // so they line up with the dictionary keys verbatim.
                var row = q.Categories.FirstOrDefault(c => c.Name == catName);
                if (row == null) continue;

                bool isEmpty = string.IsNullOrWhiteSpace(row.KeywordsJson)
                            || row.KeywordsJson.Trim() == "[]";
                if (!isEmpty) continue; // analyst already edited — leave alone

                row.KeywordsJson = JsonSerializer.Serialize(kws);
                row.IsBuiltin = true;
                row.IsActive = true;
                updatedRows++;
            }
        }

        if (updatedRows > 0)
            await db.SaveChangesAsync(ct);
    }
}
