using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 4 — auto-generates an Arabic quiz bank (~100 questions) from the strategy data.
// Idempotent: skips when the bank already has >= 100 questions. All questions start
// unapproved so an admin curates them before they appear in the public pool.
public class QuizGeneratorService
{
    private readonly ApplicationDbContext _db;

    public QuizGeneratorService(ApplicationDbContext db) { _db = db; }

    public async Task<int> GenerateAllAsync()
    {
        if (await _db.QuizQuestions.CountAsync() >= 100) return 0;

        var pillars = await _db.Pillars.OrderBy(p => p.PlrCode).ToListAsync();
        var objectives = await _db.Objectives.OrderBy(o => o.ObjectiveCode).ToListAsync();
        var initiatives = await _db.Initiatives.OrderBy(i => i.InitiativeCode).ToListAsync();
        var projects = await _db.Projects.OrderBy(p => p.ProjectCode).ToListAsync();
        var kpis = await _db.Kpis.OrderBy(k => k.KpiCode).ToListAsync();
        var depts = await _db.Departments.OrderBy(d => d.DeptCode).ToListAsync();
        if (pillars.Count == 0) return 0;

        var rnd = new Random(20260614);
        var values = new[] { "الشفافية", "التعاون", "التميز", "العدالة", "الابتكار" };
        var fakeValues = new[] { "السرعة", "الربحية", "التوسع", "المنافسة الداخلية", "الهرمية", "الحصرية" };
        var qs = new List<QuizQuestion>();

        string PillarName(string? code) => pillars.FirstOrDefault(p => p.PlrCode == code)?.PillarName ?? code ?? "";

        T[] Distractors<T>(IEnumerable<T> pool, T correct, int n, Func<T, string> key)
        {
            var others = pool.Where(x => key(x) != key(correct)).OrderBy(_ => rnd.Next()).Take(n).ToList();
            return others.ToArray();
        }

        void AddMcq(string scope, string? dept, string q, List<string> opts, int correctIdx, string? expl)
        {
            qs.Add(new QuizQuestion
            {
                Scope = scope, DeptCodeFilter = dept, QuestionType = "MCQ",
                QuestionAr = q, OptionsJson = JsonSerializer.Serialize(opts),
                CorrectIndex = correctIdx, ExplanationAr = expl, IsApproved = false, IsActive = true, Source = "AutoGen",
            });
        }
        void AddTf(string statement, bool isTrue, string? expl)
        {
            qs.Add(new QuizQuestion
            {
                Scope = "General", QuestionType = "TrueFalse", QuestionAr = statement,
                OptionsJson = JsonSerializer.Serialize(new[] { "صح", "خطأ" }),
                CorrectIndex = isTrue ? 0 : 1, ExplanationAr = expl, IsApproved = false, IsActive = true, Source = "AutoGen",
            });
        }

        // Helper to shuffle options and track the correct index.
        (List<string> opts, int idx) Shuffle(string correct, IEnumerable<string> distractors)
        {
            var list = new List<string> { correct };
            list.AddRange(distractors);
            list = list.OrderBy(_ => rnd.Next()).ToList();
            return (list, list.IndexOf(correct));
        }

        // 1) Pillar for each objective (one per objective).
        foreach (var o in objectives)
        {
            var correct = PillarName(o.PlrCode);
            var distractors = Distractors(pillars, pillars.First(p => p.PlrCode == o.PlrCode), 3, p => p.PlrCode)
                .Select(p => p.PillarName ?? p.PlrCode);
            var (opts, idx) = Shuffle(correct, distractors);
            AddMcq("General", null, $"ما الركيزة المرتبطة بالهدف '{o.ObjectiveName}'؟", opts, idx, $"الهدف يتبع ركيزة {correct}.");
        }

        // 2) Which value is NOT a GAC value (8 questions).
        for (int i = 0; i < 8; i++)
        {
            var fake = fakeValues[rnd.Next(fakeValues.Length)];
            var reals = values.OrderBy(_ => rnd.Next()).Take(3);
            var (opts, idx) = Shuffle(fake, reals);
            AddMcq("General", null, "أي من القيم التالية ليست من قيم الهيئة؟", opts, idx, $"'{fake}' ليست من قيم الهيئة.");
        }

        // 3) How many objectives under a pillar (one per pillar).
        foreach (var p in pillars)
        {
            var count = objectives.Count(o => o.PlrCode == p.PlrCode);
            var candidates = new HashSet<int> { count };
            while (candidates.Count < 4) candidates.Add(Math.Max(0, count + rnd.Next(-2, 3)));
            var opts = candidates.OrderBy(_ => rnd.Next()).Select(x => x.ToString()).ToList();
            AddMcq("General", null, $"كم هدفًا تحت ركيزة '{p.PillarName}'؟", opts, opts.IndexOf(count.ToString()), $"عدد الأهداف = {count}.");
        }

        // 4) Department responsible for a parent sector (10 questions).
        var deptsWithParent = depts.Where(d => !string.IsNullOrEmpty(d.ParentSector)).ToList();
        for (int i = 0; i < 10 && deptsWithParent.Count >= 4; i++)
        {
            var d = deptsWithParent[i % deptsWithParent.Count];
            var distractors = Distractors(depts, d, 3, x => x.DeptCode).Select(x => x.NameAr ?? x.DeptCode);
            var (opts, idx) = Shuffle(d.NameAr ?? d.DeptCode, distractors);
            AddMcq("General", null, $"أي إدارة تتبع قطاع '{d.ParentSector}'؟", opts, idx, null);
        }

        // 5) Which initiative belongs to an objective (15 questions).
        var objsWithInit = objectives.Where(o => initiatives.Any(i => i.ObjectiveCode == o.ObjectiveCode)).ToList();
        for (int i = 0; i < 15 && objsWithInit.Count > 0; i++)
        {
            var o = objsWithInit[i % objsWithInit.Count];
            var belongs = initiatives.Where(x => x.ObjectiveCode == o.ObjectiveCode).OrderBy(_ => rnd.Next()).First();
            var distractors = initiatives.Where(x => x.ObjectiveCode != o.ObjectiveCode)
                .OrderBy(_ => rnd.Next()).Take(3).Select(x => x.InitiativeName ?? x.InitiativeCode);
            var (opts, idx) = Shuffle(belongs.InitiativeName ?? belongs.InitiativeCode, distractors);
            AddMcq("General", null, $"أي مبادرة تتبع للهدف '{o.ObjectiveName}'؟", opts, idx, null);
        }

        // 6) Correct type of a project (10 questions).
        for (int i = 0; i < 10 && projects.Count > 0; i++)
        {
            var pr = projects[rnd.Next(projects.Count)];
            var correct = pr.ProjectType ?? "تشغيلي";
            var (opts, idx) = Shuffle(correct, new[] { correct == "استراتيجي" ? "تشغيلي" : "استراتيجي" });
            AddMcq("General", null, $"ما النوع الصحيح للمشروع برمز '{pr.ProjectCode}'؟", opts, idx, $"المشروع من النوع {correct}.");
        }

        // 7) Which KPI belongs to a department (15 questions, Department-scoped).
        var deptKpiGroups = kpis.Where(k => k.DepartmentCode != null).GroupBy(k => k.DepartmentCode!).ToList();
        for (int i = 0; i < 15 && deptKpiGroups.Count > 0; i++)
        {
            var grp = deptKpiGroups[i % deptKpiGroups.Count];
            var deptCode = grp.Key;
            var belongs = grp.OrderBy(_ => rnd.Next()).First();
            var deptName = depts.FirstOrDefault(d => d.DeptCode == deptCode)?.NameAr ?? deptCode;
            var distractors = kpis.Where(k => k.DepartmentCode != deptCode)
                .OrderBy(_ => rnd.Next()).Take(3).Select(k => k.KpiName ?? k.KpiCode);
            var (opts, idx) = Shuffle(belongs.KpiName ?? belongs.KpiCode, distractors);
            AddMcq("Department", deptCode, $"أي مؤشر يقع تحت إدارة '{deptName}'؟", opts, idx, null);
        }

        // 8) True/False statements (24).
        var visionKeywords = new[] { "رائدة عالمياً", "الازدهار الاقتصادي", "المنافسة" };
        AddTf("تتكوّن استراتيجية الهيئة من 5 ركائز رئيسية.", pillars.Count == 5, $"عدد الركائز = {pillars.Count}.");
        AddTf("عدد الأهداف الاستراتيجية في الهيئة 13 هدفًا.", objectives.Count == 13, $"عدد الأهداف = {objectives.Count}.");
        AddTf("عدد الإدارات في الهيئة 17 إدارة.", depts.Count == 17, $"عدد الإدارات = {depts.Count}.");
        AddTf("تتضمن رؤية الهيئة عبارة 'رائدة عالمياً'.", true, null);
        AddTf("جميع مشاريع الهيئة من النوع الاستراتيجي فقط.", false, "يوجد مشاريع تشغيلية أيضاً.");
        AddTf("'الابتكار' من قيم الهيئة.", true, null);
        AddTf("'الربحية' من قيم الهيئة.", false, "الربحية ليست من القيم.");
        AddTf("ركيزة 'تمكين المنافسة' من ركائز الهيئة.", pillars.Any(p => p.PillarName == "تمكين المنافسة"), null);
        // Fill the rest with auto fact/flip about objective→pillar mapping.
        int tfMade = 8;
        foreach (var o in objectives)
        {
            if (tfMade >= 24) break;
            bool flip = rnd.Next(2) == 0;
            var statedPillar = flip
                ? (pillars.Where(p => p.PlrCode != o.PlrCode).OrderBy(_ => rnd.Next()).FirstOrDefault()?.PillarName ?? PillarName(o.PlrCode))
                : PillarName(o.PlrCode);
            bool isTrue = statedPillar == PillarName(o.PlrCode);
            AddTf($"الهدف '{o.ObjectiveName}' يتبع ركيزة '{statedPillar}'.", isTrue, $"الهدف يتبع ركيزة {PillarName(o.PlrCode)}.");
            tfMade++;
        }

        _db.QuizQuestions.AddRange(qs);
        await _db.SaveChangesAsync();
        return qs.Count;
    }
}
