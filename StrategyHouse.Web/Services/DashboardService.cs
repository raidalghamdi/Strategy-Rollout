using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

/// <summary>
/// Builds the daily summary dashboard for the strategy office. Covers the five
/// operational questions: attendance, survey averages, top commitments,
/// quiz retention, and flagged open-text comments.
/// </summary>
public class DashboardService
{
    private readonly ApplicationDbContext _db;
    public DashboardService(ApplicationDbContext db) { _db = db; }

    public async Task<DailySummary> BuildAsync(DateTime day)
    {
        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);

        // 1. Attendance — sessions of the day, planned vs attended
        var sessions = await _db.Sessions
            .Where(s => s.ScheduledAt >= dayStart && s.ScheduledAt < dayEnd)
            .Include(s => s.Attendees).ThenInclude(a => a.Department)
            .Include(s => s.SessionDepartments).ThenInclude(sd => sd.Department)
            .ToListAsync();
        var attendance = sessions.Select(s => new AttendanceRow(
            s.Id, s.TitleAr,
            string.Join("، ", s.SessionDepartments.Select(sd => sd.Department?.NameAr ?? "")),
            s.Attendees.Count,
            s.Attendees.Count(a => a.Attended))).ToList();

        // 2. Survey averages — for today's sessions
        var sessionIds = sessions.Select(s => s.Id).ToList();
        var surveyAnswers = await _db.SurveyAnswers
            .Include(a => a.SurveyQuestion)
            .Include(a => a.SurveyResponse)
            .Where(a => a.SurveyResponse != null && sessionIds.Contains(a.SurveyResponse.SessionId))
            .ToListAsync();
        var surveyAverages = surveyAnswers
            .Where(a => a.SurveyQuestion != null && a.RatingValue.HasValue)
            .GroupBy(a => new { a.SurveyQuestionId, a.SurveyQuestion!.TextAr })
            .Select(g => new SurveyAverageRow(g.Key.TextAr, g.Average(x => x.RatingValue ?? 0), g.Count()))
            .ToList();

        // 3. Top commitments — across today's session Maps
        var commitments = await _db.MapCommitments
            .Include(c => c.LinkedElement)
            .Include(c => c.StrategyMap)
            .Where(c => c.StrategyMap != null && sessionIds.Contains(c.StrategyMap.SessionId))
            .ToListAsync();
        var topCommitments = commitments
            .GroupBy(c => c.TextAr)
            .Select(g => new CommitmentRow(g.Key, g.Count(), string.Join("، ", g.Select(x => x.LinkedElement?.NameAr ?? "").Distinct())))
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToList();

        // 4. Quiz retention — from PRIOR day's attendees (quiz arrives next morning)
        var priorStart = dayStart.AddDays(-1);
        var priorEnd = dayStart;
        var priorSessionIds = await _db.Sessions
            .Where(s => s.ScheduledAt >= priorStart && s.ScheduledAt < priorEnd)
            .Select(s => s.Id).ToListAsync();
        var quizAnswers = await _db.QuizAnswers
            .Include(a => a.QuizQuestion)
            .Include(a => a.QuizResponse)
            .Where(a => a.QuizResponse != null && priorSessionIds.Contains(a.QuizResponse.SessionId))
            .ToListAsync();
        var quizRetention = quizAnswers
            .Where(a => a.QuizQuestion != null)
            .GroupBy(a => a.QuizQuestion!.TextAr)
            .Select(g => new QuizRetentionRow(g.Key, g.Count(), g.Count(x => x.IsCorrect)))
            .ToList();

        // 5. Open-text feedback flagged
        var openText = surveyAnswers
            .Where(a => !string.IsNullOrWhiteSpace(a.OpenText))
            .OrderByDescending(a => a.SurveyResponse!.SubmittedAt)
            .Take(20)
            .Select(a => new OpenTextRow(a.SurveyQuestion?.TextAr ?? "", a.OpenText ?? ""))
            .ToList();

        return new DailySummary(day, attendance, surveyAverages, topCommitments, quizRetention, openText);
    }
}

public record DailySummary(
    DateTime Day,
    List<AttendanceRow> Attendance,
    List<SurveyAverageRow> SurveyAverages,
    List<CommitmentRow> TopCommitments,
    List<QuizRetentionRow> QuizRetention,
    List<OpenTextRow> OpenText);

public record AttendanceRow(int SessionId, string Title, string Departments, int Planned, int Attended);
public record SurveyAverageRow(string Question, double Average, int Responses);
public record CommitmentRow(string Text, int Count, string LinkedTo);
public record QuizRetentionRow(string Question, int Answers, int Correct);
public record OpenTextRow(string Question, string Text);
