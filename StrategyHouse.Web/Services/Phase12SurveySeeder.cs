using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 17 — the 8 official survey questions are now defined in the hard-coded
// SurveyQuestionsProvider (single authoritative source, like QuizQuestionsProvider).
// This seeder only MATERIALISES those static definitions into the local SQLite
// Survey/SurveyQuestion rows so that responses, analytics and the final report —
// which all key off stable question rows — keep working. Questions are no longer
// editable from the admin UI. Idempotent: a stable hash of the definition set is
// stored in PageContents; on startup, if the live bank's hash != target hash, the
// old questions, choices, responses and category data are wiped and re-inserted.
public static class Phase12SurveySeeder
{
    public const string SurveyTitle = SurveyQuestionsProvider.SurveyTitle;
    private const string HashKey = "survey.seed.hash";

    // Definitions come from the hard-coded provider (Phase 17 single source of truth).
    public static List<SurveyQuestionsProvider.QDef> Definitions() => SurveyQuestionsProvider.Definitions();

    public static string LegacyType(QuestionType t) => SurveyQuestionsProvider.LegacyType(t);

    public static async Task SeedAsync(ApplicationDbContext db)
    {
        var defs = Definitions();
        var initiativeChoices = await ResolveInitiativeChoicesAsync(db);
        var target = ComputeHash(defs, initiativeChoices);

        var current = await db.PageContents.FirstOrDefaultAsync(p => p.Key == HashKey);
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.TitleAr == SurveyTitle);

        // Already current and the survey exists → nothing to do.
        if (survey != null && current?.ValueAr == target) return;

        await ApplyAsync(db, defs, initiativeChoices, target);
    }

    // Forced reseed (admin "إعادة بذر الاستبيان" button): wipe + reinsert unconditionally.
    public static async Task ReseedAsync(ApplicationDbContext db)
    {
        var defs = Definitions();
        var initiativeChoices = await ResolveInitiativeChoicesAsync(db);
        var target = ComputeHash(defs, initiativeChoices);
        await ApplyAsync(db, defs, initiativeChoices, target);
    }

    private static async Task ApplyAsync(ApplicationDbContext db, List<SurveyQuestionsProvider.QDef> defs, string[] initiativeChoices, string targetHash)
    {
        // Phase 12 replaces the survey: retire any other active survey so respondents
        // only see the official 8-question bank.
        await db.Surveys.Where(s => s.TitleAr != SurveyTitle && s.IsActive)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.IsActive, false));

        // Find or create the official survey.
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.TitleAr == SurveyTitle);
        if (survey == null)
        {
            survey = new Survey
            {
                TitleAr = SurveyTitle,
                DescriptionAr = SurveyQuestionsProvider.SurveyDescription,
                Audience = "Public",
                IsActive = true,
                PublicToken = await UniqueTokenAsync(db),
            };
            db.Surveys.Add(survey);
            await db.SaveChangesAsync();
        }

        // Wipe old questions of this survey + their categories + this survey's responses
        // and any category assignments. Responses are deleted (acceptable per plan) since
        // they answer a now-removed question bank.
        var oldQuestionIds = await db.SurveyQuestions.Where(q => q.SurveyId == survey.Id).Select(q => q.Id).ToListAsync();
        if (oldQuestionIds.Count > 0)
            await db.OpenTextCategoryAssignments.Where(a => oldQuestionIds.Contains(a.SurveyQuestionId)).ExecuteDeleteAsync();
        await db.SurveyResponses.Where(r => r.SurveyId == survey.Id).ExecuteDeleteAsync();
        // SurveyQuestionCategories cascade from the question delete.
        await db.SurveyQuestions.Where(q => q.SurveyId == survey.Id).ExecuteDeleteAsync();

        // Insert the 8 new questions.
        foreach (var d in defs)
        {
            var choices = d.ChoicesFromInitiatives ? initiativeChoices : d.Choices;
            var q = new SurveyQuestion
            {
                SurveyId = survey.Id,
                Order = d.N,
                QuestionType = d.Type,
                Type = LegacyType(d.Type),
                QuestionAr = d.Text,
                MeasurementMetric = d.Metric,
                MeasurementFormula = d.Formula,
                IsRequired = d.Type != QuestionType.OpenText,
                OptionsJson = choices is { Length: > 0 } ? JsonSerializer.Serialize(choices) : null,
            };
            db.SurveyQuestions.Add(q);
            await db.SaveChangesAsync();

            if (d.Categories is { Length: > 0 })
            {
                int order = 1;
                foreach (var cat in d.Categories)
                    db.SurveyQuestionCategories.Add(new SurveyQuestionCategory { SurveyQuestionId = q.Id, Name = cat, Order = order++ });
            }
        }
        await db.SaveChangesAsync();

        // Record the hash.
        var row = await db.PageContents.FirstOrDefaultAsync(p => p.Key == HashKey);
        if (row == null)
            db.PageContents.Add(new PageContent { Key = HashKey, ValueAr = targetHash, UpdatedAt = DateTime.UtcNow });
        else { row.ValueAr = targetHash; row.UpdatedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync();
    }

    // Top strategic initiatives by code order, for Q6 choices. Falls back to a generic
    // set when the Initiative table is empty (fresh DB before strategy seed).
    private static async Task<string[]> ResolveInitiativeChoicesAsync(ApplicationDbContext db)
    {
        var names = await db.Initiatives
            .Where(i => i.InitiativeName != null && i.InitiativeName != "")
            .OrderBy(i => i.InitiativeCode)
            .Select(i => i.InitiativeName!)
            .Take(8)
            .ToListAsync();

        if (names.Count >= 2) return names.ToArray();

        return SurveyQuestionsProvider.FallbackInitiativeChoices();
    }

    private static string ComputeHash(List<SurveyQuestionsProvider.QDef> defs, string[] initiativeChoices)
    {
        var sb = new StringBuilder();
        sb.Append("v1|").Append(SurveyTitle).Append('|');
        foreach (var d in defs)
        {
            sb.Append(d.N).Append(':').Append((int)d.Type).Append(':').Append(d.Text).Append('|');
            var choices = d.ChoicesFromInitiatives ? initiativeChoices : d.Choices;
            if (choices != null) sb.Append("c=").Append(string.Join(",", choices)).Append('|');
            if (d.Categories != null) sb.Append("k=").Append(string.Join(",", d.Categories)).Append('|');
        }
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static async Task<string> UniqueTokenAsync(ApplicationDbContext db)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var bytes = RandomNumberGenerator.GetBytes(16);
            var sb = new StringBuilder(16);
            foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
            var token = sb.ToString();
            if (!await db.Surveys.AnyAsync(s => s.PublicToken == token)) return token;
        }
        return Guid.NewGuid().ToString("N")[..16];
    }
}
