using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services.Dtos;

namespace StrategyHouse.Web.Services;

// Phase 20.11 — Question Template engine for the expanded quiz bank.
//
// When the admin enables `quiz.bank.useExpanded`, the quiz no longer reads the
// 5 static curated questions from QuizQuestionsProvider; instead, it asks this
// service to generate up to 200 questions from LIVE database data
// (Pillars, Objectives, Initiatives, Projects, KPIs, Departments) using a set
// of dynamic templates. Each template knows how to:
//   1. produce a question text (filled with real entity names),
//   2. derive a CORRECT answer from the same DB, and
//   3. fabricate 3 plausible distractors from peer rows in the DB.
//
// Templates are spread across 6 domains:
//   1 — الرؤية والركائز
//   2 — الأهداف
//   3 — المبادرات
//   4 — المشاريع (filtered by user's DeptCode)
//   5 — مؤشرات الأداء (filtered by user's DeptCode)
//   9 — القطاعات والإدارات
//
// Domains 1, 2, 3, 9 are GLOBAL; domains 4 and 5 are restricted to questions
// about projects/KPIs in the user's own department. The generator returns
// a flat list of QuizQuestion records (not persisted); the QuizController
// picks 5 at random for any given attempt.
//
// CRITICAL: This service works fully offline. It only reads from the local
// database via IStrategyDataSource (which fans out to MSSQL mirror → SQLite).
// No external API calls, no LLM, no internet.
public class QuizTemplateService
{
    private readonly IStrategyDataSource _source;
    private readonly ApplicationDbContext _db;

    public QuizTemplateService(IStrategyDataSource source, ApplicationDbContext db)
    {
        _source = source;
        _db = db;
    }

    // Generates the full bank (up to ~200 templates) for the given user/dept.
    // deptCode may be null (anonymous quiz) — in that case domains 4 & 5 are skipped.
    public async Task<List<QuizQuestion>> GenerateForUserAsync(string? deptCode, CancellationToken ct = default)
    {
        var bank = new List<QuizQuestion>();

        var pillars = (await _source.GetPillarsAsync(ct))
            .Where(p => !string.IsNullOrWhiteSpace(p.Code) && !string.IsNullOrWhiteSpace(p.Name))
            .DistinctBy(p => p.Code)
            .ToList();
        var objectives = (await _source.GetObjectivesAsync(ct))
            .Where(o => !string.IsNullOrWhiteSpace(o.Code) && !string.IsNullOrWhiteSpace(o.Name))
            .DistinctBy(o => o.Code)
            .ToList();
        var initiatives = (await _source.GetInitiativesAsync(null, ct))
            .Where(i => !string.IsNullOrWhiteSpace(i.Code) && !string.IsNullOrWhiteSpace(i.Name))
            .DistinctBy(i => i.Code)
            .ToList();
        var departments = await _db.Departments.AsNoTracking()
            .Where(d => d.IsActive && !string.IsNullOrEmpty(d.NameAr))
            .ToListAsync(ct);

        // Domain 1 — Vision & Pillars (general).
        bank.AddRange(BuildDomain1(pillars));

        // Domain 2 — Objectives (general).
        bank.AddRange(BuildDomain2(objectives, pillars));

        // Domain 3 — Initiatives (general).
        bank.AddRange(BuildDomain3(initiatives, objectives));

        // Domain 4 — Projects (filtered by user's department).
        if (!string.IsNullOrEmpty(deptCode))
        {
            var deptProjects = (await _source.GetProjectsAsync(GetDeptNameAr(deptCode, departments), ct))
                .Where(p => !string.IsNullOrWhiteSpace(p.Code))
                .DistinctBy(p => p.Code)
                .ToList();
            bank.AddRange(BuildDomain4(deptProjects, initiatives, deptCode));
        }

        // Domain 5 — KPIs (filtered by user's department).
        if (!string.IsNullOrEmpty(deptCode))
        {
            var deptKpis = (await _source.GetKpisAsync(GetDeptNameAr(deptCode, departments), ct))
                .Where(k => !string.IsNullOrWhiteSpace(k.Code))
                .DistinctBy(k => k.Code)
                .ToList();
            bank.AddRange(BuildDomain5(deptKpis, objectives, deptCode));
        }

        // Domain 9 — Sectors & Departments (general).
        bank.AddRange(BuildDomain9(departments));

        return bank;
    }

    private static string? GetDeptNameAr(string deptCode, List<Department> departments)
        => departments.FirstOrDefault(d => d.DeptCode == deptCode)?.NameAr;

    // ----- Domain 1: Vision & Pillars (target ~25 questions) -----
    private static List<QuizQuestion> BuildDomain1(List<StrategyPillarDto> pillars)
    {
        var list = new List<QuizQuestion>();
        if (pillars.Count == 0) return list;
        var rnd = new Random(101);

        // Template 1.1 — pillar count
        list.Add(Mcq(1, $"كم عدد الركائز الاستراتيجية الرئيسية للهيئة؟",
            ShuffleWithCorrect(rnd, pillars.Count.ToString(),
                new[] { (pillars.Count + 1).ToString(), Math.Max(1, pillars.Count - 1).ToString(), (pillars.Count + 2).ToString() }),
            "تُحدَّد الركائز الاستراتيجية وفق البيت الاستراتيجي للهيئة."));

        // Template 1.2 — each pillar: "is X a strategic pillar?" Yes (one per pillar).
        foreach (var p in pillars.Take(5))
        {
            var distractorPool = PoolDistractors(p.Name, 3, FakePillarNames(pillars));
            list.Add(Mcq(1, $"أيٌّ من التالي يُعدّ ركيزة استراتيجية للهيئة؟",
                ShuffleWithCorrect(rnd, p.Name, distractorPool),
                $"«{p.Name}» إحدى الركائز الاستراتيجية المعتمدة."));
        }

        // Template 1.3 — what is the highest element in the strategy house?
        list.Add(Mcq(1, "ما العنصر الأعلى في البيت الاستراتيجي الذي تتصل به كل المبادرات في النهاية؟",
            ShuffleWithCorrect(rnd, "الرؤية", new[] { "المشروع", "المؤشر", "الميزانية" }),
            "كل مبادرة ترتبط بهدف وركيزة وصولاً للرؤية."));

        // Template 1.4 — order chain
        list.Add(Mcq(1, "ما الترتيب الصحيح لسلسلة الربط من دورك حتى الرؤية؟",
            ShuffleWithCorrect(rnd,
                "المبادرة ← الهدف ← الركيزة ← الرؤية",
                new[] {
                    "الرؤية ← المبادرة ← الهدف ← الركيزة",
                    "الهدف ← الرؤية ← الركيزة ← المبادرة",
                    "الركيزة ← المبادرة ← الرؤية ← الهدف",
                }),
            "تبدأ السلسلة من المبادرة وتنتهي عند الرؤية."));

        // Template 1.5 — pillar with most objectives (synthetic, requires objectives — we skip here)
        // Template 1.6 — true/false: every pillar has at least one objective
        list.Add(TrueFalse(1, "تتفرع الركائز الاستراتيجية إلى أهداف، وكل ركيزة تضم عدداً من الأهداف.", true,
            "نعم؛ كل ركيزة تتفرع إلى عدد من الأهداف الاستراتيجية."));

        // Template 1.7 — the strategy house is read from top → bottom?
        list.Add(TrueFalse(1, "يُقرأ البيت الاستراتيجي من الأعلى (الرؤية) إلى الأسفل (الأهداف والمبادرات).", true,
            "البيت يبدأ بالرؤية في الأعلى ثم الركائز ثم الأهداف ثم المبادرات."));

        // Template 1.8 — pillar pick from pairs (negative form)
        if (pillars.Count >= 2)
        {
            foreach (var p in pillars.Skip(1).Take(3))
            {
                var fakes = FakePillarNames(pillars).Where(n => n != p.Name).Take(3).ToArray();
                list.Add(Mcq(1, $"أيٌّ من التالي ليس من الركائز الاستراتيجية للهيئة؟",
                    ShuffleWithCorrect(rnd, fakes[0], new[] { p.Name, pillars[0].Name, pillars[^1].Name }),
                    "العناصر المذكورة في الإجابات الثلاث ركائز معتمدة، أمّا الإجابة الصحيحة فهي عنوان غير رسمي."));
                break;
            }
        }

        // Template 1.9 — vision purpose
        list.Add(Mcq(1, "ما الغاية الأساسية من رؤية الهيئة؟",
            ShuffleWithCorrect(rnd,
                "توجيه الجهود الاستراتيجية على المدى البعيد",
                new[] {
                    "تحديد ميزانية كل إدارة",
                    "اختيار المتعاقدين",
                    "إعداد التقارير التشغيلية اليومية",
                }),
            "الرؤية ترسم الوجهة بعيدة المدى وتوجّه كل القرارات الاستراتيجية."));

        // Template 1.10 — does pillar X belong to strategy of GAC? variants per pillar
        foreach (var p in pillars.Take(Math.Min(5, pillars.Count)))
        {
            list.Add(TrueFalse(1, $"ركيزة «{p.Name}» إحدى الركائز الاستراتيجية للهيئة العامة للمنافسة.", true));
        }

        return list;
    }

    // ----- Domain 2: Objectives (target ~25 questions) -----
    private static List<QuizQuestion> BuildDomain2(List<StrategyObjectiveDto> objectives, List<StrategyPillarDto> pillars)
    {
        var list = new List<QuizQuestion>();
        if (objectives.Count == 0) return list;
        var rnd = new Random(202);

        // Template 2.1 — objective count
        list.Add(Mcq(2, "كم عدد الأهداف الاستراتيجية المعتمدة في البيت الاستراتيجي؟",
            ShuffleWithCorrect(rnd, objectives.Count.ToString(),
                new[] { (objectives.Count + 2).ToString(), Math.Max(1, objectives.Count - 2).ToString(), (objectives.Count + 5).ToString() }),
            "العدد الفعلي للأهداف يُحسب من الأهداف النشطة في البيت الاستراتيجي."));

        // Template 2.2 — objective → pillar mapping (per objective sample)
        var pillarByCode = pillars.ToDictionary(p => p.Code, p => p.Name);
        var sampleObjs = objectives.OrderBy(_ => rnd.Next()).Take(Math.Min(15, objectives.Count)).ToList();
        foreach (var o in sampleObjs)
        {
            if (string.IsNullOrEmpty(o.PillarCode) || !pillarByCode.TryGetValue(o.PillarCode, out var pillarName)) continue;
            var distractors = pillarByCode.Values.Where(n => n != pillarName).Take(3).ToArray();
            if (distractors.Length < 3) continue;
            list.Add(Mcq(2, $"تحت أي ركيزة استراتيجية يندرج الهدف «{o.Name}»؟",
                ShuffleWithCorrect(rnd, pillarName, distractors),
                $"الهدف «{o.Name}» يندرج تحت ركيزة «{pillarName}»."));
        }

        // Template 2.3 — true/false: objectives bridge daily work to strategy
        list.Add(TrueFalse(2, "تربط الأهداف الاستراتيجية العمل اليومي للإدارات بالاستراتيجية المؤسسية.", true,
            "الأهداف هي حلقة الوصل بين العمليات اليومية والرؤية الاستراتيجية."));

        // Template 2.4 — diff between objective and initiative
        list.Add(Mcq(2, "ما الفرق الجوهري بين الهدف الاستراتيجي والمبادرة؟",
            ShuffleWithCorrect(rnd,
                "الهدف نتيجة استراتيجية مرغوبة، والمبادرة وسيلة لتحقيقها",
                new[] {
                    "الهدف عمل تشغيلي قصير، والمبادرة خطة طويلة",
                    "الهدف يقاس بالنقد، والمبادرة بالوقت",
                    "لا فرق بينهما",
                }),
            "الهدف يصف النتيجة، أما المبادرة فهي الإجراء التنفيذي لتحقيق الهدف."));

        // Template 2.5 — for each pillar, count of objectives
        var pillarObjCount = objectives
            .Where(o => !string.IsNullOrEmpty(o.PillarCode))
            .GroupBy(o => o.PillarCode!)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var p in pillars.Take(Math.Min(5, pillars.Count)))
        {
            if (!pillarObjCount.TryGetValue(p.Code, out var c) || c == 0) continue;
            list.Add(Mcq(2, $"كم عدد الأهداف الاستراتيجية المرتبطة بركيزة «{p.Name}»؟",
                ShuffleWithCorrect(rnd, c.ToString(),
                    new[] { (c + 1).ToString(), Math.Max(1, c - 1).ToString(), (c + 2).ToString() }),
                null));
        }

        return list;
    }

    // ----- Domain 3: Initiatives (target ~25 questions) -----
    private static List<QuizQuestion> BuildDomain3(List<StrategyInitiativeDto> initiatives, List<StrategyObjectiveDto> objectives)
    {
        var list = new List<QuizQuestion>();
        if (initiatives.Count == 0) return list;
        var rnd = new Random(303);

        // Template 3.1 — initiative count
        list.Add(Mcq(3, "كم عدد المبادرات الاستراتيجية المسجّلة على مستوى الهيئة؟",
            ShuffleWithCorrect(rnd, initiatives.Count.ToString(),
                new[] { (initiatives.Count + 5).ToString(), Math.Max(1, initiatives.Count - 5).ToString(), (initiatives.Count + 10).ToString() }),
            null));

        // Template 3.2 — initiative → objective mapping
        var objByCode = objectives.ToDictionary(o => o.Code, o => o.Name);
        var sampleInits = initiatives.OrderBy(_ => rnd.Next()).Take(Math.Min(15, initiatives.Count)).ToList();
        foreach (var ini in sampleInits)
        {
            if (string.IsNullOrEmpty(ini.ObjectiveCode) || !objByCode.TryGetValue(ini.ObjectiveCode, out var objName)) continue;
            var distractors = objByCode.Values.Where(n => n != objName).OrderBy(_ => rnd.Next()).Take(3).ToArray();
            if (distractors.Length < 3) continue;
            list.Add(Mcq(3, $"تحت أي هدف استراتيجي تندرج المبادرة «{ini.Name}»؟",
                ShuffleWithCorrect(rnd, objName, distractors),
                $"«{ini.Name}» تندرج تحت هدف «{objName}»."));
        }

        // Template 3.3 — what links daily work to strategy
        list.Add(Mcq(3, "ما الذي يربط عمل إدارتك اليومي بالاستراتيجية المؤسسية؟",
            ShuffleWithCorrect(rnd, "المبادرات والأهداف",
                new[] { "عدد الاجتماعات", "حجم الميزانية فقط", "الموقع الجغرافي" }),
            "المبادرات والأهداف هي جسر الربط بين العمل اليومي والاستراتيجية."));

        // Template 3.4 — initiatives are…
        list.Add(Mcq(3, "ما الوصف الأنسب للمبادرة الاستراتيجية؟",
            ShuffleWithCorrect(rnd,
                "حزمة عمل مُحدّدة لتحقيق هدف استراتيجي",
                new[] {
                    "اجتماع دوري بين الإدارات",
                    "تقرير شهري للأداء",
                    "نشاط ترفيهي للموظفين",
                }),
            "المبادرة حزمة منظّمة من الأنشطة لتحقيق هدف استراتيجي."));

        // Template 3.5 — objective → initiative count
        var initsByObj = initiatives
            .Where(i => !string.IsNullOrEmpty(i.ObjectiveCode))
            .GroupBy(i => i.ObjectiveCode!)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var o in objectives.OrderBy(_ => rnd.Next()).Take(Math.Min(5, objectives.Count)))
        {
            if (!initsByObj.TryGetValue(o.Code, out var c) || c == 0) continue;
            list.Add(Mcq(3, $"كم عدد المبادرات الاستراتيجية المرتبطة بالهدف «{o.Name}»؟",
                ShuffleWithCorrect(rnd, c.ToString(),
                    new[] { (c + 1).ToString(), Math.Max(1, c - 1).ToString(), (c + 2).ToString() }),
                null));
        }

        return list;
    }

    // ----- Domain 4: Projects (filtered by user's department, target ~20 questions) -----
    private static List<QuizQuestion> BuildDomain4(List<StrategyProjectDto> projects, List<StrategyInitiativeDto> allInitiatives, string deptCode)
    {
        var list = new List<QuizQuestion>();
        if (projects.Count == 0) return list;
        var rnd = new Random(404);

        // Template 4.1 — project count per dept
        list.Add(Mcq(4, "كم عدد المشاريع المسجّلة في إدارتك حالياً؟", deptCode,
            ShuffleWithCorrect(rnd, projects.Count.ToString(),
                new[] { (projects.Count + 1).ToString(), Math.Max(1, projects.Count - 1).ToString(), (projects.Count + 3).ToString() }),
            "العدد محسوب من المشاريع النشطة على إدارتك."));

        // Template 4.2 — each project → initiative
        var initByCode = allInitiatives.ToDictionary(i => i.Code, i => i.Name);
        var sample = projects.OrderBy(_ => rnd.Next()).Take(Math.Min(10, projects.Count)).ToList();
        foreach (var pr in sample)
        {
            if (string.IsNullOrEmpty(pr.InitiativeCode) || !initByCode.TryGetValue(pr.InitiativeCode, out var iniName)) continue;
            var distractors = initByCode.Values.Where(n => n != iniName).OrderBy(_ => rnd.Next()).Take(3).ToArray();
            if (distractors.Length < 3) continue;
            list.Add(Mcq(4, $"تحت أي مبادرة يندرج المشروع «{pr.Name}»؟", deptCode,
                ShuffleWithCorrect(rnd, iniName, distractors),
                $"«{pr.Name}» مرتبط بمبادرة «{iniName}»."));
        }

        // Template 4.3 — projects link via?
        list.Add(Mcq(4, "كيف يرتبط كل مشروع بالبيت الاستراتيجي؟", deptCode,
            ShuffleWithCorrect(rnd,
                "عبر مبادرة وهدف وركيزة وصولاً إلى الرؤية",
                new[] {
                    "عبر تقرير سنوي فقط",
                    "عبر اجتماع المدير العام",
                    "بدون ارتباط مباشر",
                }),
            "كل مشروع يرتبط بمبادرة تابعة لهدف تحت ركيزة وصولاً للرؤية."));

        return list;
    }

    // ----- Domain 5: KPIs (filtered by user's department, target ~20 questions) -----
    private static List<QuizQuestion> BuildDomain5(List<StrategyKpiDto> kpis, List<StrategyObjectiveDto> objectives, string deptCode)
    {
        var list = new List<QuizQuestion>();
        if (kpis.Count == 0) return list;
        var rnd = new Random(505);

        // Template 5.1 — KPI count per dept
        list.Add(Mcq(5, "كم عدد مؤشّرات الأداء المسجّلة في إدارتك؟", deptCode,
            ShuffleWithCorrect(rnd, kpis.Count.ToString(),
                new[] { (kpis.Count + 1).ToString(), Math.Max(1, kpis.Count - 1).ToString(), (kpis.Count + 2).ToString() }),
            null));

        // Template 5.2 — KPI → objective
        var objByCode = objectives.ToDictionary(o => o.Code, o => o.Name);
        var sample = kpis.OrderBy(_ => rnd.Next()).Take(Math.Min(10, kpis.Count)).ToList();
        foreach (var k in sample)
        {
            if (string.IsNullOrEmpty(k.ObjectiveCode) || !objByCode.TryGetValue(k.ObjectiveCode, out var objName)) continue;
            var distractors = objByCode.Values.Where(n => n != objName).OrderBy(_ => rnd.Next()).Take(3).ToArray();
            if (distractors.Length < 3) continue;
            list.Add(Mcq(5, $"يقيس المؤشّر «{k.Name}» أداء أي هدف استراتيجي؟", deptCode,
                ShuffleWithCorrect(rnd, objName, distractors),
                $"المؤشّر «{k.Name}» يقيس هدف «{objName}»."));
        }

        // Template 5.3 — what is a KPI
        list.Add(Mcq(5, "ما الوصف الأنسب لمؤشّر الأداء الرئيسي (KPI)؟", deptCode,
            ShuffleWithCorrect(rnd,
                "أداة قياس كميّة لتقدّم تحقيق هدف استراتيجي",
                new[] {
                    "تقرير سنوي توضيحي",
                    "اسم لجنة داخلية",
                    "مستند تخطيطي فقط",
                }),
            "المؤشّر أداة قياس كميّة لمتابعة تقدّم الأهداف."));

        return list;
    }

    // ----- Domain 9: Sectors & Departments (target ~10 questions) -----
    private static List<QuizQuestion> BuildDomain9(List<Department> departments)
    {
        var list = new List<QuizQuestion>();
        if (departments.Count == 0) return list;
        var rnd = new Random(909);

        // Template 9.1 — total active dept count
        var count = departments.Count;
        list.Add(Mcq(9, "كم عدد الإدارات النشطة في الهيكل التنظيمي للهيئة؟",
            ShuffleWithCorrect(rnd, count.ToString(),
                new[] { (count + 2).ToString(), Math.Max(1, count - 2).ToString(), (count + 5).ToString() }),
            null));

        // Template 9.2 — dept → sector
        var sample = departments.Where(d => !string.IsNullOrEmpty(d.ParentSector)).OrderBy(_ => rnd.Next()).Take(8).ToList();
        var allSectors = departments.Select(d => d.ParentSector).Where(s => !string.IsNullOrEmpty(s)).Distinct().Cast<string>().ToList();
        foreach (var d in sample)
        {
            var sector = d.ParentSector!;
            var distractors = allSectors.Where(s => s != sector).OrderBy(_ => rnd.Next()).Take(3).ToArray();
            if (distractors.Length < 3) continue;
            list.Add(Mcq(9, $"تتبع إدارة «{d.NameAr}» إلى أي قطاع؟",
                ShuffleWithCorrect(rnd, sector, distractors),
                $"إدارة «{d.NameAr}» تتبع قطاع «{sector}»."));
        }

        // Template 9.3 — how many sectors total
        var sectorCount = allSectors.Count;
        if (sectorCount > 0)
        {
            list.Add(Mcq(9, "كم عدد القطاعات الرئيسية في الهيئة؟",
                ShuffleWithCorrect(rnd, sectorCount.ToString(),
                    new[] { (sectorCount + 1).ToString(), Math.Max(1, sectorCount - 1).ToString(), (sectorCount + 2).ToString() }),
                null));
        }

        return list;
    }

    // ===== Helpers =====

    private static QuizQuestion Mcq(int domain, string text, (string[] opts, int correct) shuffled, string? expl = null)
        => Mcq(domain, text, deptCode: null, shuffled, expl);

    private static QuizQuestion Mcq(int domain, string text, string? deptCode, (string[] opts, int correct) shuffled, string? expl = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Scope = (domain == 4 || domain == 5) ? "Department" : "General",
            DeptCodeFilter = (domain == 4 || domain == 5) ? deptCode : null,
            QuestionType = "MCQ",
            QuestionAr = text,
            OptionsJson = JsonSerializer.Serialize(shuffled.opts),
            CorrectIndex = shuffled.correct,
            ExplanationAr = expl,
            IsApproved = true,
            IsActive = true,
            Source = $"Template:{domain}",
        };

    private static QuizQuestion TrueFalse(int domain, string text, bool answer, string? expl = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Scope = "General",
            DeptCodeFilter = null,
            QuestionType = "TrueFalse",
            QuestionAr = text,
            OptionsJson = JsonSerializer.Serialize(new[] { "صحيح", "خطأ" }),
            CorrectIndex = answer ? 0 : 1,
            ExplanationAr = expl,
            IsApproved = true,
            IsActive = true,
            Source = $"Template:{domain}",
        };

    // Shuffles options around the correct answer; returns (options[], correctIndex).
    private static (string[] opts, int correct) ShuffleWithCorrect(Random rnd, string correctAnswer, IEnumerable<string> distractors)
    {
        var all = new List<string> { correctAnswer };
        all.AddRange(distractors.Where(d => !string.Equals(d, correctAnswer, StringComparison.Ordinal)).Take(3));
        // Pad if fewer than 3 distractors were available.
        while (all.Count < 4) all.Add("—");
        var shuffled = all.OrderBy(_ => rnd.Next()).ToArray();
        var idx = Array.IndexOf(shuffled, correctAnswer);
        if (idx < 0) idx = 0; // fallback
        return (shuffled, idx);
    }

    // Pads distractor pool from peer names when fewer than 3 real distractors exist.
    private static string[] PoolDistractors(string correct, int count, IEnumerable<string> peers)
    {
        var pool = peers.Where(n => !string.Equals(n, correct, StringComparison.Ordinal)).Take(count).ToList();
        var generic = new[] { "العمليات اليومية", "التواصل الإداري", "المراسلات الرسمية", "الإجراءات المالية" };
        var gi = 0;
        while (pool.Count < count && gi < generic.Length)
        {
            if (!string.Equals(generic[gi], correct, StringComparison.Ordinal)) pool.Add(generic[gi]);
            gi++;
        }
        return pool.ToArray();
    }

    // Generic plausible-but-fake pillar names used when we need a distractor that is
    // NOT one of the real pillars (for negative-form questions).
    private static string[] FakePillarNames(IEnumerable<StrategyPillarDto> realPillars)
    {
        var realSet = new HashSet<string>(realPillars.Select(p => p.Name), StringComparer.Ordinal);
        var generic = new[] {
            "التشغيل اليومي",
            "النفقات السنوية",
            "المراسلات الإدارية",
            "الأنشطة الاجتماعية",
            "المهام الفرعية",
        };
        return generic.Where(n => !realSet.Contains(n)).ToArray();
    }
}
