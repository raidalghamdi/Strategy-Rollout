using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 12 — measurement engine for the official 8-question survey. Implements the
// metrics/formulas from the Excel spec: Likert means + %high, MCQ choice shares, and
// open-text manual categorisation tallies. All methods return ready-to-render DTOs.
//
// Answers are stored denormalised in SurveyResponse.AnswersJson as [{qid, value}], keyed
// by the question's Guid; the service identifies the official survey by its seeded title.
public class SurveyAnalyticsService
{
    private readonly ApplicationDbContext _db;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public SurveyAnalyticsService(ApplicationDbContext db) { _db = db; }

    // ---------- DTOs ----------

    public record LikertResults(
        double Mean,
        double Median,
        int[] Distribution,   // index 0..4 → score 1..5
        double PctHigh,       // % scoring 4 or 5
        int Total);

    public record ChoiceResult(string ChoiceText, int Count, double Percent);

    public record OpenTextCategoryTally(string Category, int Count, double Percent);

    public record OpenTextResults(
        int TotalResponses,
        List<OpenTextCategoryTally> Categories,
        int UncategorizedCount,
        List<OpenTextVerbatim> RecentVerbatim);

    public record OpenTextVerbatim(Guid ResponseId, string Text, string? Category, DateTime SubmittedAt);

    // ---------- Survey resolution ----------

    public async Task<Survey?> GetOfficialSurveyAsync()
        => await _db.Surveys.Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.TitleAr == Phase12SurveySeeder.SurveyTitle);

    public async Task<List<SurveyQuestion>> GetQuestionsAsync(Guid surveyId)
        => await _db.SurveyQuestions.Where(q => q.SurveyId == surveyId).OrderBy(q => q.Order).ToListAsync();

    // Parse all responses for a survey into qid→value dictionaries (cached per call).
    private async Task<List<(Guid ResponseId, DateTime SubmittedAt, Dictionary<string, string> Answers)>> LoadAnswersAsync(Guid surveyId)
    {
        var responses = await _db.SurveyResponses.Where(r => r.SurveyId == surveyId)
            .OrderByDescending(r => r.SubmittedAt).ToListAsync();
        var list = new List<(Guid, DateTime, Dictionary<string, string>)>();
        foreach (var r in responses)
        {
            Dictionary<string, string> map;
            try
            {
                var arr = JsonSerializer.Deserialize<List<AnswerItem>>(r.AnswersJson, JsonOpts) ?? new();
                map = arr.Where(a => !string.IsNullOrWhiteSpace(a.Value))
                         .GroupBy(a => a.Qid).ToDictionary(g => g.Key, g => g.First().Value!);
            }
            catch { map = new(); }
            list.Add((r.Id, r.SubmittedAt, map));
        }
        return list;
    }

    // ---------- Likert ----------

    public async Task<LikertResults> GetLikertResultsAsync(Guid questionId)
    {
        var q = await _db.SurveyQuestions.FindAsync(questionId);
        if (q == null) return new LikertResults(0, 0, new int[5], 0, 0);
        var answers = await LoadAnswersAsync(q.SurveyId);
        var key = questionId.ToString();

        var nums = answers
            .Select(a => a.Answers.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : (int?)null)
            .Where(n => n is >= 1 and <= 5).Select(n => n!.Value).ToList();

        var dist = new int[5];
        foreach (var n in nums) dist[n - 1]++;
        int total = nums.Count;
        double mean = total > 0 ? nums.Average() : 0;
        double median = 0;
        if (total > 0)
        {
            var sorted = nums.OrderBy(n => n).ToList();
            median = total % 2 == 1 ? sorted[total / 2] : (sorted[total / 2 - 1] + sorted[total / 2]) / 2.0;
        }
        double pctHigh = total > 0 ? 100.0 * nums.Count(n => n >= 4) / total : 0;
        return new LikertResults(Math.Round(mean, 2), median, dist, Math.Round(pctHigh, 1), total);
    }

    // ---------- Multiple choice ----------

    public async Task<List<ChoiceResult>> GetMultipleChoiceResultsAsync(Guid questionId)
    {
        var q = await _db.SurveyQuestions.FindAsync(questionId);
        if (q == null) return new();
        var answers = await LoadAnswersAsync(q.SurveyId);
        var key = questionId.ToString();

        var picks = answers.Select(a => a.Answers.TryGetValue(key, out var v) ? v : null)
            .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();
        int total = picks.Count;

        var declared = JsonSerializer.Deserialize<List<string>>(q.OptionsJson ?? "[]") ?? new();
        var counts = picks.GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());

        // Include all declared choices (even zero) plus any unexpected free values.
        var keys = declared.Concat(counts.Keys).Distinct().ToList();
        return keys.Select(c => new ChoiceResult(c, counts.TryGetValue(c, out var n) ? n : 0,
                total > 0 ? Math.Round(100.0 * (counts.TryGetValue(c, out var m) ? m : 0) / total, 1) : 0))
            .OrderByDescending(r => r.Count).ThenBy(r => r.ChoiceText).ToList();
    }

    // ---------- Open text ----------

    public async Task<OpenTextResults> GetOpenTextResultsAsync(Guid questionId)
    {
        var q = await _db.SurveyQuestions.FindAsync(questionId);
        if (q == null) return new OpenTextResults(0, new(), 0, new());
        var answers = await LoadAnswersAsync(q.SurveyId);
        var key = questionId.ToString();

        var assignments = await _db.OpenTextCategoryAssignments
            .Where(a => a.SurveyQuestionId == questionId)
            .ToDictionaryAsync(a => a.SurveyResponseId, a => a.Category);

        var withText = answers
            .Where(a => a.Answers.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            .Select(a => (a.ResponseId, a.SubmittedAt, Text: a.Answers[key],
                Category: assignments.TryGetValue(a.ResponseId, out var c) ? c : null))
            .ToList();

        int total = withText.Count;
        int uncategorized = withText.Count(x => string.IsNullOrEmpty(x.Category));

        var cats = withText.Where(x => !string.IsNullOrEmpty(x.Category))
            .GroupBy(x => x.Category!)
            .Select(g => new OpenTextCategoryTally(g.Key, g.Count(),
                total > 0 ? Math.Round(100.0 * g.Count() / total, 1) : 0))
            .OrderByDescending(c => c.Count).ToList();

        var recent = withText.Take(10)
            .Select(x => new OpenTextVerbatim(x.ResponseId, x.Text, x.Category, x.SubmittedAt)).ToList();

        return new OpenTextResults(total, cats, uncategorized, recent);
    }

    // All text answers for the categorisation page (not just recent 10).
    public async Task<List<OpenTextVerbatim>> GetAllOpenTextAsync(Guid questionId)
    {
        var q = await _db.SurveyQuestions.FindAsync(questionId);
        if (q == null) return new();
        var answers = await LoadAnswersAsync(q.SurveyId);
        var key = questionId.ToString();
        var assignments = await _db.OpenTextCategoryAssignments
            .Where(a => a.SurveyQuestionId == questionId)
            .ToDictionaryAsync(a => a.SurveyResponseId, a => a.Category);

        return answers
            .Where(a => a.Answers.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            .Select(a => new OpenTextVerbatim(a.ResponseId, a.Answers[key],
                assignments.TryGetValue(a.ResponseId, out var c) ? c : null, a.SubmittedAt))
            .ToList();
    }

    public async Task<List<string>> GetCategoriesAsync(Guid questionId)
        => await _db.SurveyQuestionCategories.Where(c => c.SurveyQuestionId == questionId)
            .OrderBy(c => c.Order).Select(c => c.Name).ToListAsync();

    private class AnswerItem
    {
        public string Qid { get; set; } = "";
        public string? Value { get; set; }
    }
}
