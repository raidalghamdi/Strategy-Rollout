using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 20.18 — uploaded Excel results → SurveyResponses + automatic analytics.
// Phase 20.20 — three critical fixes after first real-world upload returned an empty
// PDF (25 responses logged but every metric 0):
//   1) AnswersJson now uses field name "value" (not "answer") so SurveyAnalyticsService
//      can read it back. AnswerItem in SurveyAnalyticsService is { Qid, Value }.
//   2) Smarter question matching: section markers ("القسم الأول: …") are dropped on
//      BOTH sides, then we try exact → contains → word-Jaccard (≥ 0.55) fallback.
//   3) Likert text answers ("مطلع بشكل كامل", "بدرجة عالية جداً") are mapped to 1..5,
//      and the free-text placeholder ":أضف اجابة في الحقل المخصص" is replaced with
//      the row's Notes/Suggestions value so OpenText questions don't store the
//      placeholder as the answer.
public class SurveyImportService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SurveyImportService> _log;

    public SurveyImportService(ApplicationDbContext db, ILogger<SurveyImportService> log)
    {
        _db = db;
        _log = log;
    }

    private static readonly Dictionary<string, string[]> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["survey"]     = new[] { "Survey", "Survey Title", "Survey Name", "الاستبيان" },
        ["email"]      = new[] { "Email", "RespondentEmail", "البريد", "البريد الإلكتروني" },
        ["question"]   = new[] { "Name", "Question", "QuestionText", "السؤال", "نص السؤال" },
        ["answer"]     = new[] { "Answer", "Response", "الإجابة", "Choice" },
        ["notes"]      = new[] { "Notes", "Notes/Extra", "الملاحظات", "FreeText" },
        ["suggest"]    = new[] { "Suggestions", "Suggestion", "الاقتراحات" },
        ["submitted"]  = new[] { "SubmittionDateTime", "SubmittedAt", "SubmissionDateTime", "Timestamp", "تاريخ" },
    };

    public sealed class ImportPreview
    {
        public Survey? TargetSurvey { get; set; }
        public string? Error { get; set; }
        public int TotalRows { get; set; }
        public int UniqueRespondents { get; set; }
        public int MatchedQuestions { get; set; }
        public int UnmatchedQuestionCount { get; set; }
        public List<string> UnmatchedQuestions { get; set; } = new();
        public List<RespondentSummary> Respondents { get; set; } = new();
        public List<QuestionMatch> QuestionMatches { get; set; } = new();
        public List<PreparedResponse> Prepared { get; set; } = new();
    }

    public sealed class RespondentSummary
    {
        public string Email { get; set; } = "";
        public int AnswerCount { get; set; }
        public DateTime SubmittedAt { get; set; }
    }

    public sealed class QuestionMatch
    {
        public string FileQuestion { get; set; } = "";
        public Guid? MatchedQuestionId { get; set; }
        public string? MatchedQuestionText { get; set; }
        public int RowCount { get; set; }
    }

    public sealed class PreparedResponse
    {
        public string Email { get; set; } = "";
        public DateTime SubmittedAt { get; set; }
        public List<PreparedAnswer> Answers { get; set; } = new();
    }

    public sealed class PreparedAnswer
    {
        public Guid QuestionId { get; set; }
        public string Answer { get; set; } = "";
        public string? Notes { get; set; }
    }

    // ---- normalization ----------------------------------------------------

    // Strip "_x000D_", CR/LF, collapse spaces, then DROP any leading "القسم … :" marker.
    private static string Norm(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var x = s.Replace("_x000D_", " ").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        x = Regex.Replace(x, @"\s+", " ").Trim();
        // Drop any leading "القسم <anything> : " — covers Arabic colon ":" or "："
        x = Regex.Replace(x, @"^القسم[^:：]+[:：]\s*", "", RegexOptions.CultureInvariant);
        // Some files put leading punctuation on cell values (".منع …"); strip it.
        x = x.TrimStart('.', '،', '؟', '?', ' ', ':');
        x = x.TrimEnd('.', '،', '؟', '?', ' ');
        return x;
    }

    // Token set for Jaccard similarity. Removes punctuation, drops tokens shorter than 2 chars.
    private static HashSet<string> Tokens(string s)
    {
        var stripped = Regex.Replace(Norm(s), @"[\p{P}\p{S}]", " ");
        return stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var inter = a.Intersect(b).Count();
        var union = a.Count + b.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }

    // ---- Likert text → 1..5 mapping ---------------------------------------

    private static readonly Dictionary<string, int> LikertMap = new(StringComparer.Ordinal)
    {
        // High-end
        ["بدرجة عالية جداً"] = 5,
        ["بدرجة عالية جدا"]  = 5,
        ["مطلع بشكل كامل"]   = 5,
        ["موافق بشدة"]       = 5,
        // 4
        ["بدرجة عالية"]      = 4,
        ["مطلع بشكل جيد"]    = 4,
        ["موافق"]            = 4,
        // 3
        ["بدرجة متوسطة"]     = 3,
        ["مطلع بشكل إلى حد ما"] = 3,
        ["محايد"]            = 3,
        // 2
        ["بدرجة منخفضة"]     = 2,
        ["مطلع بشكل محدود"]  = 2,
        ["غير موافق"]        = 2,
        // 1
        ["بدرجة منخفضة جداً"] = 1,
        ["بدرجة منخفضة جدا"]  = 1,
        ["غير مطلع"]          = 1,
        ["غير موافق بشدة"]    = 1,
    };

    // Convert any Likert cell value to "1".."5" or "" (empty -> ignored downstream).
    private static string LikertNormalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var v = raw.Trim().TrimEnd('.', '،', '؟', '?', ' ').TrimStart('.', '،', ' ', ':');
        // Already a number 1..5?
        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 5)
            return n.ToString(CultureInfo.InvariantCulture);
        // Try the table.
        if (LikertMap.TryGetValue(v, out var mapped))
            return mapped.ToString(CultureInfo.InvariantCulture);
        // Fuzzy: longest matching key.
        foreach (var (k, score) in LikertMap.OrderByDescending(kv => kv.Key.Length))
        {
            if (v.Contains(k, StringComparison.Ordinal)) return score.ToString(CultureInfo.InvariantCulture);
        }
        return "";
    }

    // The Excel template uses a literal placeholder for open-text questions
    // (":أضف اجابة في الحقل المخصص"). Treat that as an empty answer.
    private static bool IsPlaceholder(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return true;
        var n = v.Trim().TrimStart(':', ' ', '.', '،').TrimEnd(':', ' ', '.', '،');
        return n.Contains("أضف اجابة", StringComparison.Ordinal)
            || n.Contains("أضف إجابة", StringComparison.Ordinal)
            || n.Contains("اضف اجابة", StringComparison.Ordinal)
            || n.Contains("اضف إجابة", StringComparison.Ordinal);
    }

    // Clean MCQ cell value: drop leading punctuation, trim whitespace. Used both
    // for matching against OptionsJson and for storing the canonical choice text.
    private static string CleanChoice(string v) => Norm(v);

    // ---- main analyze -----------------------------------------------------

    public async Task<ImportPreview> AnalyzeAsync(Stream xlsxStream, Guid? surveyIdOverride, CancellationToken ct)
    {
        var preview = new ImportPreview();

        // 1) Resolve target survey: explicit > sole-active > error.
        if (surveyIdOverride.HasValue)
        {
            preview.TargetSurvey = await _db.Surveys
                .Include(s => s.Questions)
                .FirstOrDefaultAsync(s => s.Id == surveyIdOverride.Value, ct);
            if (preview.TargetSurvey == null)
            {
                preview.Error = "تعذّر العثور على الاستبيان المحدّد.";
                return preview;
            }
        }
        else
        {
            var active = await _db.Surveys
                .Include(s => s.Questions)
                .Where(s => s.IsActive)
                .ToListAsync(ct);
            if (active.Count == 0)
            {
                preview.Error = "لا يوجد استبيان نشط في النظام. فعّل استبياناً واحداً ثم أعد المحاولة.";
                return preview;
            }
            if (active.Count > 1)
            {
                preview.Error = $"يوجد {active.Count} استبيانات نشطة. حدّد الاستبيان المستهدف صراحةً.";
                return preview;
            }
            preview.TargetSurvey = active[0];
        }

        // 2) Open workbook.
        using var wb = new XLWorkbook(xlsxStream);
        var ws = wb.Worksheets.First();
        var headerRow = ws.FirstRowUsed();
        if (headerRow == null)
        {
            preview.Error = "الملف فارغ.";
            return preview;
        }

        var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in headerRow.CellsUsed())
        {
            var header = (c.GetString() ?? "").Trim();
            foreach (var kv in HeaderAliases)
            {
                if (kv.Value.Any(a => string.Equals(a, header, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!colIndex.ContainsKey(kv.Key)) colIndex[kv.Key] = c.Address.ColumnNumber;
                }
            }
        }

        string[] required = { "email", "question", "answer" };
        var missing = required.Where(r => !colIndex.ContainsKey(r)).ToList();
        if (missing.Count > 0)
        {
            preview.Error = "أعمدة مفقودة في الملف: " + string.Join("، ", missing.Select(m =>
                m switch { "email" => "Email", "question" => "Name/Question", "answer" => "Answer", _ => m }));
            return preview;
        }

        // 3) Build DB-side question lookups: by normalized text + tokens (for fuzzy).
        var qIndex = preview.TargetSurvey.Questions
            .OrderBy(q => q.Order)
            .Select(q => (Q: q, Norm: Norm(q.QuestionAr), Tokens: Tokens(q.QuestionAr)))
            .ToList();
        var qByNorm = qIndex.ToDictionary(x => x.Norm, x => x.Q, StringComparer.OrdinalIgnoreCase);

        SurveyQuestion? MatchQuestion(string fileQ)
        {
            var nf = Norm(fileQ);
            if (string.IsNullOrEmpty(nf)) return null;
            // a) exact post-normalization
            if (qByNorm.TryGetValue(nf, out var direct)) return direct;
            // b) substring either way
            foreach (var (q, n, _) in qIndex)
            {
                if (n.Length < 10) continue;
                if (nf.Contains(n, StringComparison.Ordinal) || n.Contains(nf, StringComparison.Ordinal))
                    return q;
            }
            // c) Jaccard ≥ 0.55 → best DB question
            var fileToks = Tokens(fileQ);
            var best = qIndex
                .Select(x => (x.Q, Score: Jaccard(fileToks, x.Tokens)))
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();
            return best.Score >= 0.55 ? best.Q : null;
        }

        // 4) Walk rows.
        var grouped = new Dictionary<string, PreparedResponse>(StringComparer.OrdinalIgnoreCase);
        var matchByFileQ = new Dictionary<string, QuestionMatch>(StringComparer.Ordinal);
        var unmatched = new HashSet<string>(StringComparer.Ordinal);
        int totalRows = 0;

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
        for (int rowNum = headerRow.RowNumber() + 1; rowNum <= lastRow; rowNum++)
        {
            ct.ThrowIfCancellationRequested();
            var row = ws.Row(rowNum);
            string Get(string key) => colIndex.TryGetValue(key, out var ix)
                ? (row.Cell(ix).GetString() ?? "").Trim()
                : "";

            var email = Get("email");
            var fileQ = Get("question");
            var rawAnswer = Get("answer");
            var notes = Get("notes");
            var suggest = Get("suggest");
            var submittedRaw = Get("submitted");

            if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(fileQ) && string.IsNullOrWhiteSpace(rawAnswer))
                continue;

            if (string.Equals(notes, "NULL", StringComparison.OrdinalIgnoreCase)) notes = "";
            if (string.Equals(suggest, "NULL", StringComparison.OrdinalIgnoreCase)) suggest = "";
            if (string.Equals(rawAnswer, "NULL", StringComparison.OrdinalIgnoreCase)) rawAnswer = "";

            totalRows++;

            var match = MatchQuestion(fileQ);
            if (!matchByFileQ.TryGetValue(fileQ, out var qm))
            {
                qm = new QuestionMatch { FileQuestion = fileQ };
                if (match != null)
                {
                    qm.MatchedQuestionId = match.Id;
                    qm.MatchedQuestionText = match.QuestionAr;
                }
                matchByFileQ[fileQ] = qm;
            }
            qm.RowCount++;

            if (match == null)
            {
                unmatched.Add(fileQ);
                continue;
            }

            // ---- transform answer per question type ----
            string finalAnswer;
            string? finalNotes;
            // Combine Notes + Suggestions once.
            var notesCombined = string.Join(" — ",
                new[] { notes, suggest }.Where(s => !string.IsNullOrWhiteSpace(s)));
            finalNotes = string.IsNullOrWhiteSpace(notesCombined) ? null : notesCombined;

            switch (match.Type)
            {
                case "Likert5":
                    finalAnswer = LikertNormalize(rawAnswer);
                    break;
                case "Text":
                    // The template puts a placeholder in Answer and the actual text in
                    // Notes/Suggestions. Promote the freetext to the answer in that case.
                    if (IsPlaceholder(rawAnswer))
                    {
                        finalAnswer = notesCombined ?? "";
                        // notes already promoted; don't duplicate.
                        finalNotes = null;
                    }
                    else
                    {
                        finalAnswer = rawAnswer.Trim();
                    }
                    break;
                case "MCQ":
                default:
                    finalAnswer = CleanChoice(rawAnswer);
                    break;
            }

            if (string.IsNullOrWhiteSpace(finalAnswer) && string.IsNullOrWhiteSpace(finalNotes))
                continue; // nothing to store

            DateTime submittedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(submittedRaw))
            {
                if (DateTime.TryParse(submittedRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
                    submittedAt = parsed;
                else if (double.TryParse(submittedRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var oa))
                    submittedAt = DateTime.FromOADate(oa);
            }

            var groupKey = email + "|" + submittedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (!grouped.TryGetValue(groupKey, out var pr))
            {
                pr = new PreparedResponse { Email = email, SubmittedAt = submittedAt };
                grouped[groupKey] = pr;
            }
            pr.Answers.Add(new PreparedAnswer
            {
                QuestionId = match.Id,
                Answer = finalAnswer,
                Notes = finalNotes,
            });
        }

        preview.TotalRows = totalRows;
        preview.UniqueRespondents = grouped.Count;
        preview.MatchedQuestions = matchByFileQ.Values.Count(m => m.MatchedQuestionId.HasValue);
        preview.UnmatchedQuestions = unmatched.OrderBy(x => x).ToList();
        preview.UnmatchedQuestionCount = preview.UnmatchedQuestions.Count;
        preview.QuestionMatches = matchByFileQ.Values.OrderByDescending(m => m.RowCount).ToList();
        preview.Respondents = grouped.Values
            .Select(g => new RespondentSummary
            {
                Email = g.Email,
                AnswerCount = g.Answers.Count,
                SubmittedAt = g.SubmittedAt,
            })
            .OrderBy(r => r.SubmittedAt)
            .ToList();
        preview.Prepared = grouped.Values.ToList();
        return preview;
    }

    public sealed class ApplyResult
    {
        public int AddedResponses { get; set; }
        public Guid? TargetSurveyId { get; set; }
        public string? TargetSurveyTitle { get; set; }
    }

    public async Task<ApplyResult> ApplyAsync(Stream xlsxStream, Guid? surveyIdOverride, CancellationToken ct)
    {
        var preview = await AnalyzeAsync(xlsxStream, surveyIdOverride, ct);
        if (preview.Error != null || preview.TargetSurvey == null)
            throw new InvalidOperationException(preview.Error ?? "تعذّر تحليل الملف.");

        int inserted = 0;
        foreach (var pr in preview.Prepared)
        {
            // Phase 20.20 — Critical: SurveyAnalyticsService.AnswerItem expects { Qid, Value }
            // (NOT { qid, answer }). Use lowercase property names; the deserialiser is
            // configured with PropertyNameCaseInsensitive=true so this is read back fine.
            var answersJson = JsonSerializer.Serialize(pr.Answers.Select(a => new
            {
                qid   = a.QuestionId.ToString(),
                value = a.Answer,
                notes = a.Notes,
            }));
            var resp = new SurveyResponse
            {
                Id = Guid.NewGuid(),
                SurveyId = preview.TargetSurvey.Id,
                RespondentName = pr.Email,
                AnswersJson = answersJson,
                SubmittedAt = pr.SubmittedAt,
                ClientFingerprint = "import:xlsx",
            };
            _db.SurveyResponses.Add(resp);
            inserted++;
        }
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("SurveyImport: inserted {N} responses into survey {Sid} ({Title}).",
            inserted, preview.TargetSurvey.Id, preview.TargetSurvey.TitleAr);
        return new ApplyResult
        {
            AddedResponses = inserted,
            TargetSurveyId = preview.TargetSurvey.Id,
            TargetSurveyTitle = preview.TargetSurvey.TitleAr,
        };
    }
}
