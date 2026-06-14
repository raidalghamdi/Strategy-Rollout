using System.Text;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 6 — rule-based, DB-only assistant. No external LLM calls (no internet at
// runtime). Detects one of a fixed set of Arabic intents from the normalised
// question, answers strictly from seeded strategy data, and persists every turn.
public class ChatbotService
{
    private readonly ApplicationDbContext _db;
    private readonly StrategyContentService _content;

    public ChatbotService(ApplicationDbContext db, StrategyContentService content)
    {
        _db = db;
        _content = content;
    }

    public record Answer(string Text, string Intent, int ResultCount);

    public async Task<Answer> AskAsync(string question, Guid? sessionId)
    {
        var raw = question ?? string.Empty;
        var norm = Normalize(raw);
        var result = await ResolveAsync(norm);

        _db.ChatbotConversations.Add(new ChatbotConversation
        {
            Question = raw.Length > 1000 ? raw[..1000] : raw,
            Answer = result.Text,
            SessionId = sessionId,
            MatchedIntent = result.Intent,
            ResultCount = result.ResultCount,
        });
        await _db.SaveChangesAsync();
        return result;
    }

    private async Task<Answer> ResolveAsync(string q)
    {
        // 1) Vision
        if (Has(q, "رويه", "رؤيه", "vision") || (Has(q, "رويه") && Has(q, "هيئه")))
        {
            var v = _content.Vision.Ar;
            return new Answer($"رؤية الهيئة العامة للمنافسة:\n«{v}»", "vision", 1);
        }

        // 2) Mission
        if (Has(q, "رساله", "mission"))
        {
            var m = _content.Mission.Ar;
            return new Answer($"رسالة الهيئة:\n«{m}»", "mission", 1);
        }

        // 3) Values
        if (Has(q, "قيم", "قيمه", "values"))
        {
            var vals = _content.Values.Select(x => "• " + x.Ar).ToList();
            return new Answer("قيم الهيئة:\n" + string.Join("\n", vals), "values", vals.Count);
        }

        // 4) Count of pillars
        if (HasCountWord(q) && Has(q, "ركيز", "ركائز", "محور", "محاور", "pillar"))
        {
            var n = await _db.Pillars.CountAsync();
            return new Answer($"عدد ركائز الاستراتيجية هو {n} ركائز.", "pillars_count", n);
        }

        // 5) List pillars
        if (Has(q, "ركيز", "ركائز", "محور", "محاور", "pillar"))
        {
            var ps = await _db.Pillars.OrderBy(p => p.PlrCode).Select(p => p.PillarName).ToListAsync();
            var body = string.Join("\n", ps.Select(p => "• " + p));
            return new Answer($"ركائز الاستراتيجية ({ps.Count}):\n{body}", "pillars_list", ps.Count);
        }

        // 6) Count of objectives
        if (HasCountWord(q) && Has(q, "هدف", "اهداف", "objective"))
        {
            var n = await _db.Objectives.CountAsync();
            return new Answer($"عدد الأهداف الاستراتيجية هو {n} أهداف.", "objectives_count", n);
        }

        // 7) List objectives
        if (Has(q, "هدف", "اهداف", "objective"))
        {
            var os = await _db.Objectives.OrderBy(o => o.ObjectiveCode).Select(o => o.ObjectiveName).Take(20).ToListAsync();
            var body = string.Join("\n", os.Select(o => "• " + o));
            return new Answer($"من الأهداف الاستراتيجية:\n{body}", "objectives_list", os.Count);
        }

        // 8) Count of projects
        if (HasCountWord(q) && Has(q, "مشروع", "مشاريع", "project"))
        {
            var n = await _db.Projects.CountAsync();
            return new Answer($"إجمالي عدد المشاريع هو {n} مشروعاً.", "projects_count", n);
        }

        // 9) Count of KPIs
        if (HasCountWord(q) && Has(q, "مؤشر", "موشر", "مؤشرات", "kpi"))
        {
            var n = await _db.Kpis.CountAsync();
            return new Answer($"إجمالي عدد مؤشرات الأداء هو {n} مؤشراً.", "kpis_count", n);
        }

        // 10) Count of initiatives
        if (HasCountWord(q) && Has(q, "مبادر", "initiative"))
        {
            var n = await _db.Initiatives.CountAsync();
            return new Answer($"إجمالي عدد المبادرات هو {n} مبادرة.", "initiatives_count", n);
        }

        // 11) Count of departments
        if (HasCountWord(q) && Has(q, "اداره", "ادارات", "department"))
        {
            var n = await _db.Departments.CountAsync(d => d.IsActive);
            return new Answer($"عدد الإدارات في الهيئة هو {n} إدارة.", "departments_count", n);
        }

        // 12) List departments
        if (Has(q, "اداره", "ادارات", "department"))
        {
            var ds = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.DeptCode)
                .Select(d => d.NameAr).ToListAsync();
            var body = string.Join("\n", ds.Select(d => "• " + d));
            return new Answer($"إدارات الهيئة ({ds.Count}):\n{body}", "departments_list", ds.Count);
        }

        // Free-text fallback — search names across the strategy data.
        return await SearchAsync(q);
    }

    // Best-effort free-text search across pillars/objectives/projects/KPIs by name.
    private async Task<Answer> SearchAsync(string q)
    {
        var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3).Distinct().Take(4).ToArray();
        if (terms.Length == 0)
            return Fallback();

        var hits = new List<string>();
        int count = 0;

        var pillars = await _db.Pillars.Select(p => p.PillarName).ToListAsync();
        foreach (var p in pillars.Where(p => p != null && terms.Any(t => Normalize(p!).Contains(t))))
        { hits.Add("ركيزة: " + p); count++; }

        var objectives = await _db.Objectives.Select(o => o.ObjectiveName).ToListAsync();
        foreach (var o in objectives.Where(o => o != null && terms.Any(t => Normalize(o!).Contains(t))).Take(5))
        { hits.Add("هدف: " + o); count++; }

        var inits = await _db.Initiatives.Select(i => i.InitiativeName).ToListAsync();
        foreach (var i in inits.Where(i => i != null && terms.Any(t => Normalize(i!).Contains(t))).Take(5))
        { hits.Add("مبادرة: " + i); count++; }

        var kpis = await _db.Kpis.Select(k => k.KpiName).ToListAsync();
        foreach (var k in kpis.Where(k => k != null && terms.Any(t => Normalize(k!).Contains(t))).Distinct().Take(5))
        { hits.Add("مؤشر: " + k); count++; }

        if (count == 0) return Fallback();

        var body = string.Join("\n", hits.Take(12).Select(h => "• " + h));
        return new Answer($"وجدت ما يلي في بيانات الاستراتيجية:\n{body}", "search", count);
    }

    private static Answer Fallback() => new(
        "لم أجد إجابة دقيقة. يمكنك أن تسألني عن: رؤية الهيئة، رسالتها، قيمها، عدد الركائز أو الأهداف أو المشاريع أو المؤشرات أو الإدارات.",
        "fallback", 0);

    private static bool HasCountWord(string q) =>
        Has(q, "كم", "عدد", "اجمالي", "how many", "count");

    private static bool Has(string haystack, params string[] needles)
    {
        foreach (var n in needles)
            if (haystack.Contains(Normalize(n))) return true;
        return false;
    }

    // Arabic normalisation: strip diacritics/tatweel, unify alef/ya/ta-marbuta,
    // lowercase, collapse whitespace, drop punctuation. Lets loose matching work.
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            char c = ch;
            switch (c)
            {
                case 'أ': case 'إ': case 'آ': case 'ٱ': c = 'ا'; break;
                case 'ى': c = 'ي'; break;
                case 'ة': c = 'ه'; break;
                case 'ؤ': c = 'و'; break;
                case 'ئ': c = 'ي'; break;
            }
            // Strip Arabic diacritics (harakat) and tatweel.
            if ((c >= 'ً' && c <= 'ْ') || c == 'ـ') continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (char.IsWhiteSpace(c)) sb.Append(' ');
            // else: drop punctuation
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
