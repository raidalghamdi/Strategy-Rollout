using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Models;

namespace StrategyHouse.Web.Services;

// Phase 13/14 — assembles the comprehensive executive report ViewModel from every part of
// the rollout: sessions, attendee counts, quiz attempts, the official survey (via the Phase
// 12 analytics service), contribution pledges, group signatures and strategy maps. Phase 14
// adds five leadership-analytics dimensions (alignment, culture, risks, maturity,
// recommendations) and richer detail on the existing six sections.
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

    public Task<ExecutiveReportViewModel> BuildAsync() => BuildAsync(ExecReportSections.AllSelected());

    public async Task<ExecutiveReportViewModel> BuildAsync(ExecReportSections sections)
    {
        var vm = new ExecutiveReportViewModel { GeneratedAt = DateTime.UtcNow, Sections = sections };

        var sessions = await _db.StrategySessions.AsNoTracking().ToListAsync();
        var activeDepts = await _db.Departments.AsNoTracking().Where(d => d.IsActive)
            .ToDictionaryAsync(d => d.DeptCode, d => d.NameAr ?? d.DeptCode);
        // Fall back to all departments for name lookup (a session dept may be inactive).
        var deptNames = await _db.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.DeptCode, d => d.NameAr ?? d.DeptCode);

        // ----- Overview -----
        vm.Overview.TotalSessions = sessions.Count;
        vm.Overview.TotalCompletedSessions = sessions.Count(s => s.CompletedAt != null);
        vm.Overview.TotalAttendees = sessions.Sum(s => s.AttendeeCount ?? 0);
        var engagedCodes = sessions.Select(s => s.DeptCode).Distinct().ToHashSet();
        vm.Overview.TotalDepartmentsEngaged = engagedCodes.Count;
        vm.Overview.TotalDepartments = activeDepts.Count;
        vm.Overview.CompletionPercentage = sessions.Count > 0
            ? Math.Round(100.0 * vm.Overview.TotalCompletedSessions / sessions.Count, 1) : 0;
        var completedSessions = sessions.Where(s => s.CompletedAt != null).ToList();
        vm.Overview.AvgCompletionMinutes = completedSessions.Count > 0
            ? Math.Round(completedSessions.Average(s => (s.CompletedAt!.Value - s.StartedAt).TotalMinutes), 1) : 0;
        if (sessions.Count > 0)
        {
            vm.Overview.SessionsFrom = sessions.Min(s => s.StartedAt);
            vm.Overview.SessionsTo = sessions.Max(s => s.LastActivityAt ?? s.CompletedAt ?? s.StartedAt);
        }
        vm.Overview.NotEngagedDepartments = activeDepts
            .Where(kv => !engagedCodes.Contains(kv.Key))
            .Select(kv => kv.Value).OrderBy(n => n).ToList();

        // ----- Department breakdown (ranked) -----
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
            .OrderByDescending(r => r.AttendeesCount).ThenByDescending(r => r.SessionsCount).ThenBy(r => r.DeptName)
            .ToList();
        for (int i = 0; i < vm.DepartmentBreakdown.Count; i++) vm.DepartmentBreakdown[i].Rank = i + 1;

        await BuildQuizAsync(vm);
        await BuildSurveyAsync(vm);
        await BuildContributionsAsync(vm, deptNames);
        await BuildGroupSignaturesAsync(vm, deptNames);
        await BuildTeamValuesAsync(vm);

        vm.MapsCount = await _db.DepartmentStrategyMaps.CountAsync();

        // Phase 20.33 (Comment 4) — build the three-level detail tables
        await BuildThreeLevelDetailAsync(vm, sessions, deptNames);

        // ----- Phase 14 leadership analytics (depend on the above) -----
        await BuildLeadershipAlignmentAsync(vm);
        BuildLeadershipCulture(vm, sessions, deptNames);
        await BuildLeadershipRisksAsync(vm, deptNames);
        BuildLeadershipMaturity(vm);
        BuildLeadershipRecommendations(vm);

        return vm;
    }

    private async Task BuildQuizAsync(ExecutiveReportViewModel vm)
    {
        var attempts = await _db.QuizAttempts.AsNoTracking().ToListAsync();
        var qa = vm.QuizAnalytics;
        qa.TotalAttempts = attempts.Count;
        if (attempts.Count > 0)
        {
            var scaled = attempts.Select(a => a.Total > 0 ? a.Score * 5.0 / a.Total : 0).ToList();
            qa.AvgScore = Math.Round(scaled.Average(), 2);
            foreach (var s in scaled)
            {
                if (s <= 2) qa.Bucket0to2++;
                else if (s < 5) qa.Bucket3to4++;
                else qa.Bucket5++;
            }
        }

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

        if (seenCount.Count > 0)
        {
            var allIds = seenCount.Keys.ToList();
            var questions = await _db.QuizQuestions.AsNoTracking()
                .Where(q => allIds.Contains(q.Id))
                .ToDictionaryAsync(q => q.Id, q => q.QuestionAr);

            // Phase 20.32: drop orphan question IDs (re-seeded questions leave old attempt rows
            // pointing to deleted GUIDs). Only show rows whose question text we can resolve.
            var resolvedIds = allIds.Where(id => questions.ContainsKey(id) && !string.IsNullOrWhiteSpace(questions[id])).ToList();

            ExecMissedQuestion Row(Guid id)
            {
                int seen = seenCount.GetValueOrDefault(id);
                int miss = missCount.GetValueOrDefault(id);
                return new ExecMissedQuestion
                {
                    QuestionAr = questions[id],
                    Attempts = seen,
                    MissRate = seen > 0 ? Math.Round(100.0 * miss / seen, 1) : 0,
                };
            }

            qa.Top3MostMissed = resolvedIds
                .Select(Row).OrderByDescending(r => r.MissRate).ThenByDescending(r => r.Attempts)
                .Take(3).ToList();
            qa.Top3Strongest = resolvedIds
                .Select(Row).OrderBy(r => r.MissRate).ThenByDescending(r => r.Attempts)
                .Take(3).ToList();
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
                    metric.LikertMean = l.Mean;
                    metric.LikertPctHigh = l.PctHigh;
                    metric.LikertTotal = l.Total;
                    if (l.Distribution is { Length: 5 }) metric.LikertDistribution = l.Distribution;
                    metric.Headline = l.Total > 0
                        ? $"المتوسط {l.Mean:0.##}/5 · نسبة العالية {l.PctHigh:0.#}% · عدد {l.Total}"
                        : "لا توجد إجابات بعد";
                    if (q.Order == 1)
                    {
                        vm.Overview.AvgSurveyClarity = l.Mean;
                        if (l.Distribution is { Length: 5 }) vm.Overview.SurveyClarityDistribution = l.Distribution;
                    }
                    if (q.Order == 8) vm.Overview.AvgContributionCapability = l.Mean;
                    break;
                case QuestionType.MultipleChoice:
                    var ch = await _survey.GetMultipleChoiceResultsAsync(q.Id);
                    metric.Type = "اختيار من متعدد";
                    metric.Choices = ch.Select(c => new ExecChoiceShare { Text = c.ChoiceText, Count = c.Count, Percent = c.Percent }).ToList();
                    var top = ch.FirstOrDefault(c => c.Count > 0);
                    metric.Headline = top != null ? $"الأبرز: \"{top.ChoiceText}\" ({top.Percent:0.#}%)" : "لا توجد إجابات بعد";
                    break;
                case QuestionType.OpenText:
                    var o = await _survey.GetOpenTextResultsAsync(q.Id);
                    metric.Type = "سؤال مفتوح";
                    metric.OpenTextTotal = o.TotalResponses;
                    metric.OpenTextUncategorized = o.UncategorizedCount;
                    metric.OpenTextCategories = o.Categories.Select(c => new ExecNameCount { Name = c.Category, Count = c.Count }).ToList();
                    var tc = o.Categories.FirstOrDefault();
                    metric.Headline = o.TotalResponses > 0
                        ? (tc != null ? $"{o.TotalResponses} إجابة · أبرز فئة \"{tc.Category}\" ({tc.Count})" : $"{o.TotalResponses} إجابة")
                        : "لا توجد إجابات بعد";
                    break;
            }
            vm.SurveyMetrics.Add(metric);
        }
    }

    private async Task BuildContributionsAsync(ExecutiveReportViewModel vm, Dictionary<string, string> deptNames)
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

        vm.Contributions.ByDepartment = pledges
            .GroupBy(p => p.DeptCode)
            .Select(g => new ExecNameCount { Name = deptNames.TryGetValue(g.Key, out var n) ? n : g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ToList();
    }

    private async Task BuildGroupSignaturesAsync(ExecutiveReportViewModel vm, Dictionary<string, string> deptNames)
    {
        var sigs = await (from a in _db.MapInkAssets.AsNoTracking()
                          join m in _db.DepartmentStrategyMaps.AsNoTracking() on a.MapId equals m.Id
                          where a.AssetKind == "signature" && a.MemberId == null && a.IsActive
                          orderby a.CapturedAt descending
                          select new { a.TypedText, m.DeptCode, a.CapturedAt })
                         .ToListAsync();

        vm.GroupSignatures.TotalCount = sigs.Count;
        var comments = sigs.Where(s => !string.IsNullOrWhiteSpace(s.TypedText)).ToList();
        vm.GroupSignatures.RecentComments = comments
            .Take(10)
            .Select(s => new ExecRecentComment
            {
                DeptName = deptNames.TryGetValue(s.DeptCode, out var n) ? n : s.DeptCode,
                Text = s.TypedText!,
                CapturedAt = s.CapturedAt,
            })
            .ToList();

        vm.GroupSignatures.TopKeywords = TopKeywords(comments.Select(c => c.TypedText!), 10);

        vm.Overview.AvgQuizScore = vm.QuizAnalytics.AvgScore;
    }

    // ----- Phase 16 team value selections -----
    private async Task BuildTeamValuesAsync(ExecutiveReportViewModel vm)
    {
        var selections = await _db.TeamValueSelections.AsNoTracking().ToListAsync();
        vm.TeamValues.TotalSelections = selections.Count;
        vm.TeamValues.ByValue = selections
            .GroupBy(s => s.SelectedValueText)
            .Select(g => new ExecNameCount { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .ToList();
    }

    // ----- Phase 14 leadership: alignment -----
    private async Task BuildLeadershipAlignmentAsync(ExecutiveReportViewModel vm)
    {
        var pledges = await _db.ContributionPledges.AsNoTracking().ToListAsync();
        var al = vm.LeadershipAlignment;

        var pillars = await _db.Pillars.AsNoTracking().ToListAsync();
        var objToPlr = await _db.Objectives.AsNoTracking()
            .Where(o => o.PlrCode != null)
            .ToDictionaryAsync(o => o.ObjectiveCode, o => o.PlrCode!);
        var initToObj = await _db.Initiatives.AsNoTracking()
            .Where(i => i.ObjectiveCode != null)
            .ToDictionaryAsync(i => i.InitiativeCode, i => i.ObjectiveCode!);

        string? PillarOf(Domain.Entities.ContributionPledge p)
        {
            switch (p.ElementType)
            {
                case "OBJ":
                    return objToPlr.TryGetValue(p.ElementCode, out var plr) ? plr : null;
                case "INIT":
                    if (initToObj.TryGetValue(p.ElementCode, out var obj) && objToPlr.TryGetValue(obj, out var plr2)) return plr2;
                    return null;
                default:
                    return null;
            }
        }

        var byPillar = new Dictionary<string, int>();
        int mapped = 0;
        foreach (var p in pledges)
        {
            var plr = PillarOf(p);
            if (plr == null) continue;
            byPillar[plr] = byPillar.GetValueOrDefault(plr) + 1;
            mapped++;
        }
        al.TotalContributions = mapped;

        al.PillarShares = pillars
            .OrderBy(p => p.PlrCode)
            .Select(p => new ExecPillarShare
            {
                PillarCode = p.PlrCode,
                PillarName = p.PillarName ?? p.PlrCode,
                Count = byPillar.GetValueOrDefault(p.PlrCode),
                Percent = mapped > 0 ? Math.Round(100.0 * byPillar.GetValueOrDefault(p.PlrCode) / mapped, 1) : 0,
            })
            .ToList();

        foreach (var ps in al.PillarShares.Where(ps => ps.Percent < 10))
        {
            al.Gaps.Add($"ركيزة \"{ps.PillarName}\" تلقّت {ps.Percent:0.#}% فقط من المساهمات.");
            al.Recommendations.Add($"يُوصى بتوجيه مزيد من التركيز نحو ركيزة \"{ps.PillarName}\" في الجلسات القادمة.");
        }
        if (al.Gaps.Count == 0 && al.PillarShares.Any(p => p.Count > 0))
            al.Recommendations.Add("توزيع المساهمات متوازن نسبياً عبر الركائز — يُوصى بالحفاظ على هذا التوازن.");
    }

    // ----- Phase 14 leadership: culture & engagement -----
    private void BuildLeadershipCulture(ExecutiveReportViewModel vm, List<Domain.Entities.StrategySession> sessions, Dictionary<string, string> deptNames)
    {
        var cu = vm.LeadershipCulture;

        cu.DepartmentParticipation = vm.DepartmentBreakdown
            .Select(d => new ExecDeptParticipation
            {
                DeptName = d.DeptName,
                Attendees = d.AttendeesCount,
                RatioKnown = false,
                ParticipationRatio = d.AttendeesCount,
            })
            .OrderByDescending(d => d.Attendees).ToList();

        // Sentiment of group comments (simple Arabic keyword classification).
        foreach (var c in vm.GroupSignatures.RecentComments)
        {
            switch (Sentiment(c.Text))
            {
                case 1: cu.PositiveComments++; break;
                case -1: cu.NegativeComments++; break;
                default: cu.NeutralComments++; break;
            }
        }

        // Team-spirit composite (0..100): blends engagement breadth, clarity satisfaction and
        // positive-comment ratio, each normalised to 0..1.
        double engagement = vm.Overview.TotalDepartments > 0
            ? (double)vm.Overview.TotalDepartmentsEngaged / vm.Overview.TotalDepartments : 0;
        double clarity = vm.Overview.AvgSurveyClarity / 5.0;
        int totalComments = cu.PositiveComments + cu.NeutralComments + cu.NegativeComments;
        double positivity = totalComments > 0 ? (double)cu.PositiveComments / totalComments : 0.5;
        cu.TeamSpiritScore = Math.Round(100.0 * (0.4 * engagement + 0.4 * clarity + 0.2 * positivity), 1);
        cu.TeamSpiritLabel = cu.TeamSpiritScore >= 75 ? "مرتفع" : cu.TeamSpiritScore >= 50 ? "متوسط" : "منخفض";
    }

    // ----- Phase 14 leadership: risks & opportunities -----
    private async Task BuildLeadershipRisksAsync(ExecutiveReportViewModel vm, Dictionary<string, string> deptNames)
    {
        var ri = vm.LeadershipRisks;
        var survey = await _survey.GetOfficialSurveyAsync();
        if (survey != null)
        {
            var questions = await _survey.GetQuestionsAsync(survey.Id);
            var q4 = questions.FirstOrDefault(q => q.Order == 4);
            var q7 = questions.FirstOrDefault(q => q.Order == 7);

            if (q4 != null)
            {
                var o = await _survey.GetOpenTextResultsAsync(q4.Id);
                ri.TopChallenges = o.Categories.Take(6)
                    .Select(c => new ExecCategorisedItem { Category = c.Category, Count = c.Count, Percent = c.Percent }).ToList();
            }
            if (q7 != null)
            {
                var o = await _survey.GetOpenTextResultsAsync(q7.Id);
                ri.TopOpportunities = o.Categories.Take(6)
                    .Select(c => new ExecCategorisedItem { Category = c.Category, Count = c.Count, Percent = c.Percent }).ToList();
            }
        }

        // Risk heatmap: departments whose group comments most frequently mention challenge words.
        var sigs = await (from a in _db.MapInkAssets.AsNoTracking()
                          join m in _db.DepartmentStrategyMaps.AsNoTracking() on a.MapId equals m.Id
                          where a.AssetKind == "signature" && a.MemberId == null && a.IsActive
                                && a.TypedText != null
                          select new { a.TypedText, m.DeptCode })
                         .ToListAsync();
        var heat = new Dictionary<string, int>();
        foreach (var s in sigs)
        {
            if (MentionsChallenge(s.TypedText!))
                heat[s.DeptCode] = heat.GetValueOrDefault(s.DeptCode) + 1;
        }
        ri.RiskHeatmap = heat.OrderByDescending(kv => kv.Value)
            .Select(kv => new ExecNameCount { Name = deptNames.TryGetValue(kv.Key, out var n) ? n : kv.Key, Count = kv.Value })
            .ToList();

        if (ri.TopChallenges.Count > 0)
            ri.Recommendations.Add($"أبرز تحدٍّ متكرر: \"{ri.TopChallenges[0].Category}\" — يُوصى بمعالجته ضمن خطة التنفيذ.");
        if (ri.TopOpportunities.Count > 0)
            ri.Recommendations.Add($"أبرز فرصة مذكورة: \"{ri.TopOpportunities[0].Category}\" — يُوصى باستثمارها مبكراً.");
        if (ri.RiskHeatmap.Count > 0)
            ri.Recommendations.Add($"إدارة \"{ri.RiskHeatmap[0].Name}\" هي الأكثر ذكراً للتحديات — يُوصى بدعم إضافي.");
    }

    // ----- Phase 14 leadership: organisational maturity -----
    private void BuildLeadershipMaturity(ExecutiveReportViewModel vm)
    {
        var ma = vm.LeadershipMaturity;
        // Composite per the whole event (we lack per-dept survey/quiz breakdown here, so the
        // composite uses event-level means as the baseline and ranks departments by their
        // engagement relative to it). The composite is on a 0..5 scale.
        double quiz = vm.Overview.AvgQuizScore;          // already 0..5
        double clarity = vm.Overview.AvgSurveyClarity;   // 0..5
        double capability = vm.Overview.AvgContributionCapability; // 0..5
        double baseComposite = new[] { quiz, clarity, capability }.Where(x => x > 0).DefaultIfEmpty(0).Average();

        int maxAtt = vm.DepartmentBreakdown.Count > 0 ? Math.Max(1, vm.DepartmentBreakdown.Max(d => d.AttendeesCount)) : 1;
        foreach (var d in vm.DepartmentBreakdown)
        {
            // Nudge the event baseline by the department's engagement (attendees + completion).
            double engagementFactor = 0.5 * (d.AttendeesCount / (double)maxAtt) + 0.5 * (d.CompletionRate / 100.0);
            double score = Math.Round(Math.Clamp(baseComposite * (0.7 + 0.6 * engagementFactor), 0, 5), 2);
            string tier = score >= 4 ? "ناضجة" : score >= 3 ? "متطورة" : "بحاجة دعم";
            ma.Departments.Add(new ExecDeptMaturity { DeptName = d.DeptName, Score = score, Tier = tier });
        }
        ma.Departments = ma.Departments.OrderByDescending(d => d.Score).ToList();
        ma.MatureCount = ma.Departments.Count(d => d.Tier == "ناضجة");
        ma.DevelopingCount = ma.Departments.Count(d => d.Tier == "متطورة");
        ma.NeedsSupportCount = ma.Departments.Count(d => d.Tier == "بحاجة دعم");

        if (ma.MatureCount > 0) ma.Recommendations.Add($"{ma.MatureCount} إدارة ناضجة — يُوصى بإشراكها كمراجع ومُلهِم لبقية الإدارات.");
        if (ma.DevelopingCount > 0) ma.Recommendations.Add($"{ma.DevelopingCount} إدارة متطورة — يُوصى ببرامج تعزيز لرفعها لمستوى النضج.");
        if (ma.NeedsSupportCount > 0) ma.Recommendations.Add($"{ma.NeedsSupportCount} إدارة بحاجة دعم — يُوصى بجلسات توعوية مكثّفة ومتابعة قريبة.");
    }

    // ----- Phase 14 leadership: auto recommendations -----
    private void BuildLeadershipRecommendations(ExecutiveReportViewModel vm)
    {
        var recs = vm.LeadershipRecommendations;

        foreach (var dept in vm.Overview.NotEngagedDepartments.Take(3))
            recs.Add($"إدارة \"{dept}\" لم تشارك بعد — يُوصى بجدولة جلسة استراتيجية لها.");

        foreach (var ps in vm.LeadershipAlignment.PillarShares.Where(p => p.Percent < 10 && p.Count >= 0).Take(2))
            recs.Add($"ركيزة \"{ps.PillarName}\" تلقّت {ps.Percent:0.#}% فقط من المساهمات — قد تحتاج تركيزاً أكبر.");

        if (vm.Overview.AvgSurveyClarity > 0 && vm.Overview.AvgSurveyClarity < 3.5)
            recs.Add($"متوسط وضوح الاستراتيجية {vm.Overview.AvgSurveyClarity:0.##}/5 — يُوصى بحملة توعوية إضافية.");
        if (vm.Overview.AvgContributionCapability > 0 && vm.Overview.AvgContributionCapability < 3.5)
            recs.Add($"متوسط القدرة على المساهمة {vm.Overview.AvgContributionCapability:0.##}/5 — يُوصى بتمكين الفرق وتوضيح أدوارها.");
        if (vm.QuizAnalytics.TotalAttempts > 0 && vm.QuizAnalytics.AvgScore < 3.5)
            recs.Add($"متوسط الاختبار {vm.QuizAnalytics.AvgScore:0.##}/5 — يُوصى بإعادة عرض النقاط المعرفية الأضعف.");

        if (vm.LeadershipCulture.NegativeComments > vm.LeadershipCulture.PositiveComments && vm.LeadershipCulture.NegativeComments > 0)
            recs.Add("نبرة التعليقات تميل للسلبية — يُوصى بمراجعة ملاحظات الفرق ومعالجة دواعي القلق.");

        if (vm.Overview.CompletionPercentage < 70 && vm.Overview.TotalSessions > 0)
            recs.Add($"نسبة إكمال الجلسات {vm.Overview.CompletionPercentage:0.#}% — يُوصى بمتابعة الجلسات غير المكتملة.");

        if (recs.Count == 0)
            recs.Add("المؤشرات العامة إيجابية — يُوصى بالحفاظ على الزخم وتوثيق أفضل الممارسات.");

        // Cap at 8 per the spec.
        if (recs.Count > 8) vm.LeadershipRecommendations = recs.Take(8).ToList();
    }

    // ----- Phase 20.33 (Comment 4) — three-level detail -----

    private async Task BuildThreeLevelDetailAsync(
        ExecutiveReportViewModel vm,
        List<Domain.Entities.StrategySession> sessions,
        Dictionary<string, string> deptNames)
    {
        // Load departments with their sector
        var depts = await _db.Departments.AsNoTracking().ToListAsync();
        var deptSector = depts.ToDictionary(d => d.DeptCode, d => d.ParentSector ?? "");

        // --- Individual level: roster members with session info ---
        var roster = await _db.DepartmentRoster.AsNoTracking().Where(r => r.IsActive).ToListAsync();

        // Build a map: deptCode → list of completed sessions (for avg time)
        var sessionsByDept = sessions
            .GroupBy(s => s.DeptCode)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var member in roster.OrderBy(r => r.DeptCode).ThenBy(r => r.NameAr))
        {
            var deptSessions = sessionsByDept.TryGetValue(member.DeptCode, out var ss) ? ss : new();
            var completed = deptSessions.Where(s => s.CompletedAt != null).ToList();
            double? avgTime = completed.Count > 0
                ? completed.Average(s => (s.CompletedAt!.Value - s.StartedAt).TotalMinutes)
                : null;

            vm.IndividualRows.Add(new Models.ExecIndividualRow
            {
                Email = member.Email ?? "",
                FullNameAr = member.NameAr,
                DeptCode = member.DeptCode,
                DeptName = deptNames.TryGetValue(member.DeptCode, out var dn) ? dn : member.DeptCode,
                SectorName = deptSector.TryGetValue(member.DeptCode, out var sec) ? sec : "",
                Completed = deptSessions.Any(s => s.CompletedAt != null),
                CompletedAt = completed.OrderByDescending(s => s.CompletedAt).FirstOrDefault()?.CompletedAt,
                CompletionMinutes = avgTime.HasValue ? Math.Round(avgTime.Value, 1) : null,
            });
        }

        // --- Department level (V2 with sector + avg time) ---
        vm.DepartmentRowsV2 = sessions
            .GroupBy(s => s.DeptCode)
            .Select(g =>
            {
                var completed = g.Where(s => s.CompletedAt != null).ToList();
                double avgMin = completed.Count > 0
                    ? Math.Round(completed.Average(s => (s.CompletedAt!.Value - s.StartedAt).TotalMinutes), 1)
                    : 0;
                return new Models.ExecDepartmentRowV2
                {
                    DeptCode = g.Key,
                    DeptName = deptNames.TryGetValue(g.Key, out var n) ? n : g.Key,
                    SectorName = deptSector.TryGetValue(g.Key, out var sec) ? sec : "",
                    SessionsCount = g.Count(),
                    AttendeesCount = g.Sum(s => s.AttendeeCount ?? 0),
                    CompletionRate = g.Any() ? Math.Round(100.0 * g.Count(s => s.CompletedAt != null) / g.Count(), 1) : 0,
                    AvgCompletionMinutes = avgMin,
                };
            })
            .OrderByDescending(r => r.AttendeesCount).ThenBy(r => r.DeptName)
            .ToList();

        // --- Sector level ---
        var sectorRows = new Dictionary<string, Models.ExecSectorRow>();
        foreach (var row in vm.DepartmentRowsV2)
        {
            var sector = string.IsNullOrEmpty(row.SectorName) ? "إدارات مستقلة" : row.SectorName;
            if (!sectorRows.TryGetValue(sector, out var sr))
            {
                sr = new Models.ExecSectorRow { SectorName = sector };
                sectorRows[sector] = sr;
            }
            sr.SessionsCount += row.SessionsCount;
            sr.AttendeesCount += row.AttendeesCount;
            sr.DeptNames.Add(row.DeptName);
        }
        // Compute completion rate and avg time at sector level from raw sessions
        foreach (var (sectorName, sr) in sectorRows)
        {
            var deptCodes = depts
                .Where(d => (string.IsNullOrEmpty(d.ParentSector) ? "إدارات مستقلة" : d.ParentSector) == sectorName)
                .Select(d => d.DeptCode).ToHashSet();
            var sectorSessions = sessions.Where(s => deptCodes.Contains(s.DeptCode)).ToList();
            if (sectorSessions.Count > 0)
                sr.CompletionRate = Math.Round(100.0 * sectorSessions.Count(s => s.CompletedAt != null) / sectorSessions.Count, 1);
            var sectorCompleted = sectorSessions.Where(s => s.CompletedAt != null).ToList();
            sr.AvgCompletionMinutes = sectorCompleted.Count > 0
                ? Math.Round(sectorCompleted.Average(s => (s.CompletedAt!.Value - s.StartedAt).TotalMinutes), 1)
                : 0;
        }
        vm.SectorRows = sectorRows.Values.OrderByDescending(r => r.AttendeesCount).ThenBy(r => r.SectorName).ToList();
    }

    // ----- small text utilities -----

    private static readonly string[] PositiveWords =
        { "ممتاز", "رائع", "جيد", "مفيد", "ملهم", "شكر", "نجاح", "تطور", "إيجاب", "متحمس", "فخور", "تعاون", "واضح", "مبدع", "سعد" };
    private static readonly string[] NegativeWords =
        { "صعب", "ضعف", "مشكلة", "تحدي", "تحدّي", "نقص", "غموض", "قلق", "تأخر", "بطيء", "محبط", "سلبي", "عقبة", "غير واضح", "صعوبة" };
    private static readonly string[] ChallengeWords =
        { "تحدي", "تحدّي", "صعوبة", "صعب", "عقبة", "مشكلة", "نقص", "محدودية", "غموض", "موارد", "ميزانية", "وقت" };

    private static int Sentiment(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        int pos = PositiveWords.Count(w => text.Contains(w));
        int neg = NegativeWords.Count(w => text.Contains(w));
        if (pos > neg) return 1;
        if (neg > pos) return -1;
        return 0;
    }

    private static bool MentionsChallenge(string text)
        => !string.IsNullOrWhiteSpace(text) && ChallengeWords.Any(text.Contains);

    private static readonly HashSet<string> Stopwords = new()
    {
        "في", "من", "على", "إلى", "عن", "مع", "هذا", "هذه", "ذلك", "التي", "الذي", "أن", "إن",
        "كان", "قد", "ما", "لا", "ثم", "أو", "و", "أيضا", "كل", "بعض", "هو", "هي", "نحن", "هم",
        "the", "and", "for", "with", "our", "are", "was", "this", "that",
    };

    private static List<ExecNameCount> TopKeywords(IEnumerable<string> texts, int take)
    {
        var counts = new Dictionary<string, int>();
        foreach (var t in texts)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            var cleaned = new string(t.Select(ch => char.IsLetter(ch) ? ch : ' ').ToArray());
            foreach (var w in cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var word = w.Trim();
                if (word.Length < 3 || Stopwords.Contains(word)) continue;
                counts[word] = counts.GetValueOrDefault(word) + 1;
            }
        }
        return counts.Where(kv => kv.Value > 1)
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
            .Take(take)
            .Select(kv => new ExecNameCount { Name = kv.Key, Count = kv.Value })
            .ToList();
    }

    private class QuizAnswerDetail
    {
        public Guid Qid { get; set; }
        public int Picked { get; set; }
        public int CorrectIndex { get; set; }
        public bool Correct { get; set; }
    }
}
