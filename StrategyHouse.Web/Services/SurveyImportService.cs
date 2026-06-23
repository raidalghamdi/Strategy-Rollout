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
//
// Input format (long: one row per (respondent × question)):
//   Survey | Email | Name (= question text) | Answer | Notes | Suggestions | SubmittionDateTime
//
// We group rows by Email + SubmittionDateTime into one SurveyResponse, match each
// row's question text against the active survey's SurveyQuestions, and append every
// response — no dedup check (user requested "add all every time"). Analytics pages
// recompute on every render from SurveyResponses, so uploads surface immediately.
public class SurveyImportService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SurveyImportService> _log;

    public SurveyImportService(ApplicationDbContext db, ILogger<SurveyImportService> log)
    {
        _db = db;
        _log = log;
    }

    // -- Column lookup table (header → index). Matches the sample file exactly but is
    //    case/whitespace-insensitive and tolerates alternative header names. --
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
        // Pre-built responses, ready to apply. We stash them between Preview and Apply via
        // a session token (caller can also re-parse — both are safe).
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

    // Normalize question text for matching. Excel sometimes injects "_x000D_" (carriage
    // returns) and stray whitespace; we strip both. Comparison is case-insensitive on the
    // collapsed Arabic text.
    private static string Norm(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var x = s.Replace("_x000D_", " ").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        x = Regex.Replace(x, @"\s+", " ").Trim();
        // Drop common leading section markers ("القسم الأول: …") so the body text matches
        // even when one side carries the section label and the other doesn't.
        x = Regex.Replace(x, @"^القسم[^:：]+[:：]\s*", "", RegexOptions.CultureInvariant);
        // Strip trailing punctuation that may differ between Excel and DB strings.
        x = x.TrimEnd('.', '،', '؟', '?', ' ');
        return x;
    }

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

        // 2) Open workbook and find header row on the first sheet.
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

        // 3) Build question-text → SurveyQuestion lookup (normalized).
        var qByNorm = new Dictionary<string, SurveyQuestion>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in preview.TargetSurvey.Questions)
        {
            var key = Norm(q.QuestionAr);
            if (!qByNorm.ContainsKey(key)) qByNorm[key] = q;
        }

        // 4) Walk data rows, group by (email + submitted-at). Substring matching falls back
        //    to longest-common-substring across DB questions when the file/DB texts differ
        //    only in length (we trim section labels on both sides via Norm).
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
            var answer = Get("answer");
            var notes = Get("notes");
            var suggest = Get("suggest");
            var submittedRaw = Get("submitted");

            if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(fileQ) && string.IsNullOrWhiteSpace(answer))
                continue; // blank row

            // "NULL" cells become empty strings.
            if (string.Equals(notes, "NULL", StringComparison.OrdinalIgnoreCase)) notes = "";
            if (string.Equals(suggest, "NULL", StringComparison.OrdinalIgnoreCase)) suggest = "";
            if (string.Equals(answer, "NULL", StringComparison.OrdinalIgnoreCase)) answer = "";

            // Concatenate Notes + Suggestions into a single freetext value.
            var notesCombined = string.Join(" — ",
                new[] { notes, suggest }.Where(s => !string.IsNullOrWhiteSpace(s)));

            totalRows++;

            // Match question.
            var normFile = Norm(fileQ);
            SurveyQuestion? match = null;
            if (qByNorm.TryGetValue(normFile, out var direct))
            {
                match = direct;
            }
            else
            {
                // Loose match: does the file text *contain* a DB question (or vice versa)?
                foreach (var (k, q) in qByNorm)
                {
                    if (k.Length < 12) continue;
                    if (normFile.Contains(k, StringComparison.Ordinal) || k.Contains(normFile, StringComparison.Ordinal))
                    {
                        match = q;
                        break;
                    }
                }
            }

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

            DateTime submittedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(submittedRaw))
            {
                if (DateTime.TryParse(submittedRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
                    submittedAt = parsed;
                else if (double.TryParse(submittedRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var oa))
                    submittedAt = DateTime.FromOADate(oa);
            }

            // Group key: email + minute-precision submitted time. Some platforms emit slightly
            // different sub-second values for rows from the same respondent.
            var groupKey = email + "|" + submittedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (!grouped.TryGetValue(groupKey, out var pr))
            {
                pr = new PreparedResponse
                {
                    Email = email,
                    SubmittedAt = submittedAt,
                };
                grouped[groupKey] = pr;
            }
            pr.Answers.Add(new PreparedAnswer
            {
                QuestionId = match.Id,
                Answer = answer,
                Notes = string.IsNullOrWhiteSpace(notesCombined) ? null : notesCombined,
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

    // Apply: re-parses the workbook then inserts every prepared response. No dedup,
    // matches user's stated "add all every time" behavior.
    public async Task<ApplyResult> ApplyAsync(Stream xlsxStream, Guid? surveyIdOverride, CancellationToken ct)
    {
        var preview = await AnalyzeAsync(xlsxStream, surveyIdOverride, ct);
        if (preview.Error != null || preview.TargetSurvey == null)
            throw new InvalidOperationException(preview.Error ?? "تعذّر تحليل الملف.");

        int inserted = 0;
        foreach (var pr in preview.Prepared)
        {
            var answersJson = JsonSerializer.Serialize(pr.Answers.Select(a => new
            {
                qid = a.QuestionId,
                answer = a.Answer,
                notes = a.Notes,
            }));
            var resp = new SurveyResponse
            {
                Id = Guid.NewGuid(),
                SurveyId = preview.TargetSurvey.Id,
                RespondentName = pr.Email,            // best available identifier in the file
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
