using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Models;

namespace StrategyHouse.Web.Services;

// Phase 13 — assembles the comprehensive executive report ViewModel from every part of the
// rollout: sessions, attendee counts, quiz attempts, the official survey (via the Phase 12
// analytics service), contribution pledges, group signatures and strategy maps.
public class ExecutiveReportService
{
    private readonly ApplicationDbContext _db;
    private readonly SurveyAnalyticsService _survey;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ExecutiveReportService(ApplicationDbContext db, SurveyAnalyticsService survey)
    {
        _db = db;
        _survey = survey;
    }

    public async Task<ExecutiveReportViewModel> BuildAsync()
    {
        var vm = new ExecutiveReportViewModel { GeneratedAt = DateTime.UtcNow };

        var sessions = await _db.StrategySessions.AsNoTracking().ToListAsync();
        var deptNames = await _db.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.DeptCode, d => d.NameAr ?? d.DeptCode);

        // ----- Overview -----
        vm.Overview.TotalSessions = sessions.Count;
        vm.Overview.TotalCompletedSessions = sessions.Count(s => s.CompletedAt != null);
        vm.Overview.TotalAttendees = sessions.Sum(s => s.AttendeeCount ?? 0);
        vm.Overview.TotalDepartmentsEngaged = sessions.Select(s => s.DeptCode).Distinct().Count();

        // ----- Department breakdown -----
        vm.DepartmentBreakdown = sessions
            .GroupBy(s => s.DeptCode)
            .Select(g => new ExecDepartmentRow
            {
                DeptCode = g.Key,
                DeptName = deptNames.TryGetValue(g.Key, out var n) ? n : g.Key,
                SessionsCount = g.Count(),
                AttendeesCount = g.Sum(s => s.AttendeeCount ?? 0),
                CompletionRate = g.Any() ? Math.Round(100.0 * g.Count(s => s.CompletedAt != null) / g.Count(), 1) : 0,
            })
            .OrderByDescending(r => r.AttendeesCount).ThenBy(r => r.DeptName)
            .ToList();

        // ----- Quiz analytics -----
        await BuildQuizAsync(vm);

        // ----- Survey analytics (reuse Phase 12 service) -----
        await BuildSurveyAsync(vm);

        // ----- Contributions -----
        await BuildContributionsAsync(vm);

        // ----- Group signatures -----
        await BuildGroupSignaturesAsync(vm, deptNames);

        // ----- Maps -----
        vm.MapsCount = await _db.DepartmentStrategyMaps.CountAsync();

        return vm;
    }

    private async Task BuildQuizAsync(ExecutiveReportViewModel vm)
    {
        var attempts = await _db.QuizAttempts.AsNoTracking().ToListAsync();
        var qa = vm.QuizAnalytics;
        qa.TotalAttempts = attempts.Count;
        if (attempts.Count > 0)
        {
            // Normalise each attempt to a 0-5 scale so distribution buckets are comparable.
            var scaled = attempts
                .Select(a => a.Total > 0 ? a.Score * 5.0 / a.Total : 0)
                .ToList();
            qa.AvgScore = Math.Round(scaled.Average(), 2);
            foreach (var s in scaled)
            {
                if (s <= 2) qa.Bucket0to2++;
                else if (s < 5) qa.Bucket3to4++;
                else qa.Bucket5++;
            }
        }

        // Top 3 most-missed questions across all attempts (parse AnswersJson detail rows).
        var missCount = new Dictionary<Guid, int>();
        var seenCount = new Dictionary<Guid, int>();
        foreach (var a in attempts)
        {
            List<QuizAnswerDetail>? detail;
            try { detail = JsonSerializer.Deserialize<List<QuizAnswerDetail>>(a.AnswersJson, JsonOpts); }
            catch { detail = null; }
            if (detail == null) continue;
            foreach (var d in detail)
            {
                if (d.Qid == Guid.Empty) continue;
                seenCount[d.Qid] = seenCount.GetValueOrDefault(d.Qid) + 1;
                if (!d.Correct) missCount[d.Qid] = missCount.GetValueOrDefault(d.Qid) + 1;
            }
        }

        if (missCount.Count > 0)
        {
            var topIds = missCount.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key).ToList();
            var questions = await _db.QuizQuestions.AsNoTracking()
                .Where(q => topIds.Contains(q.Id))
                .ToDictionaryAsync(q => q.Id, q => q.QuestionAr);
            foreach (var id in topIds)
            {
                int seen = seenCount.GetValueOrDefault(id);
                int miss = missCount.GetValueOrDefault(id);
                qa.Top3MostMissed.Add(new ExecMissedQuestion
                {
                    QuestionAr = questions.TryGetValue(id, out var t) ? t : id.ToString(),
                    Attempts = seen,
                    MissRate = seen > 0 ? Math.Round(100.0 * miss / seen, 1) : 0,
                });
            }
        }
    }

    private async Task BuildSurveyAsync(ExecutiveReportViewModel vm)
    {
        var survey = await _survey.GetOfficialSurveyAsync();
        if (survey == null) return;
        var questions = await _survey.GetQuestionsAsync(survey.Id);

        foreach (var q in questions)
        {
            var metric = new ExecSurveyMetric { Order = q.Order, QuestionAr = q.QuestionAr };
            switch (q.QuestionType)
            {
                case QuestionType.Likert5:
                    var l = await _survey.GetLikertResultsAsync(q.Id);
                    metric.Type = "مقياس ليكرت";
                    metric.Headline = l.Total > 0
                        ? $"المتوسط {l.Mean:0.##}/5 · نسبة العالية {l.PctHigh:0.#}% · عدد {l.Total}"
                        : "لا توجد إجابات بعد";
                    if (q.Order == 1)
                    {
                        vm.Overview.AvgSurveyClarity = l.Mean;
                        if (l.Distribution != null && l.Distribution.Length == 5)
                            vm.Overview.SurveyClarityDistribution = l.Distribution;
                    }
                    if (q.Order == 8) vm.Overview.AvgContributionCapability = l.Mean;
                    break;
                case QuestionType.MultipleChoice:
                    var ch = await _survey.GetMultipleChoiceResultsAsync(q.Id);
                    var top = ch.FirstOrDefault(c => c.Count > 0);
                    metric.Type = "اختيار من متعدد";
                    metric.Headline = top != null ? $"الأبرز: \"{top.ChoiceText}\" ({top.Percent:0.#}%)" : "لا توجد إجابات بعد";
                    break;
                case QuestionType.OpenText:
                    var o = await _survey.GetOpenTextResultsAsync(q.Id);
                    var tc = o.Categories.FirstOrDefault();
                    metric.Type = "سؤال مفتوح";
                    metric.Headline = o.TotalResponses > 0
                        ? (tc != null ? $"{o.TotalResponses} إجابة · أبرز فئة \"{tc.Category}\" ({tc.Count})" : $"{o.TotalResponses} إجابة")
                        : "لا توجد إجابات بعد";
                    break;
            }
            vm.SurveyMetrics.Add(metric);
        }

        // Average quiz score already set; survey clarity/capability set above.
    }

    private async Task BuildContributionsAsync(ExecutiveReportViewModel vm)
    {
        var pledges = await _db.ContributionPledges.AsNoTracking().ToListAsync();
        vm.Contributions.TotalPledges = pledges.Count;

        var objCodes = pledges.Where(p => p.ElementType == "OBJ").Select(p => p.ElementCode).ToList();
        var initCodes = pledges.Where(p => p.ElementType == "INIT").Select(p => p.ElementCode).ToList();

        var objNames = await _db.Objectives.AsNoTracking()
            .Where(o => objCodes.Contains(o.ObjectiveCode))
            .ToDictionaryAsync(o => o.ObjectiveCode, o => o.ObjectiveName ?? o.ObjectiveCode);
        var initNames = await _db.Initiatives.AsNoTracking()
            .Where(i => initCodes.Contains(i.InitiativeCode))
            .ToDictionaryAsync(i => i.InitiativeCode, i => i.InitiativeName ?? i.InitiativeCode);

        vm.Contributions.TopObjectives = pledges
            .Where(p => p.ElementType == "OBJ")
            .GroupBy(p => p.ElementCode)
            .Select(g => new ExecNameCount { Name = objNames.TryGetValue(g.Key, out var n) ? n : g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(5).ToList();

        vm.Contributions.TopInitiatives = pledges
            .Where(p => p.ElementType == "INIT")
            .GroupBy(p => p.ElementCode)
            .Select(g => new ExecNameCount { Name = initNames.TryGetValue(g.Key, out var n) ? n : g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(5).ToList();
    }

    private async Task BuildGroupSignaturesAsync(ExecutiveReportViewModel vm, Dictionary<string, string> deptNames)
    {
        // Group signatures are MapInkAssets with MemberId == null (Phase 13), joined to their map's dept.
        var sigs = await (from a in _db.MapInkAssets.AsNoTracking()
                          join m in _db.DepartmentStrategyMaps.AsNoTracking() on a.MapId equals m.Id
                          where a.AssetKind == "signature" && a.MemberId == null && a.IsActive
                          orderby a.CapturedAt descending
                          select new { a.TypedText, m.DeptCode, a.CapturedAt })
                         .ToListAsync();

        vm.GroupSignatures.TotalCount = sigs.Count;
        vm.GroupSignatures.RecentComments = sigs
            .Where(s => !string.IsNullOrWhiteSpace(s.TypedText))
            .Take(10)
            .Select(s => new ExecRecentComment
            {
                DeptName = deptNames.TryGetValue(s.DeptCode, out var n) ? n : s.DeptCode,
                Text = s.TypedText!,
                CapturedAt = s.CapturedAt,
            })
            .ToList();

        // Overview avg quiz score mirrors quiz analytics avg.
        vm.Overview.AvgQuizScore = vm.QuizAnalytics.AvgScore;
    }

    private class QuizAnswerDetail
    {
        public Guid Qid { get; set; }
        public int Picked { get; set; }
        public int CorrectIndex { get; set; }
        public bool Correct { get; set; }
    }
}
