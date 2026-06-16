using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.PptxSlideHelper;
using P = DocumentFormat.OpenXml.Presentation;

namespace StrategyHouse.Web.Services;

// Phase 13.1 — branded .pptx export of the comprehensive executive report. Seven slides:
// title, overview KPIs, departments, quiz, survey highlights, contributions, signatures.
// GAC navy/gold, white text on navy, RTL paragraphs, no embedded charts (text only).
public class ExecutiveReportPowerPointBuilder
{
    private PptxSlideHelper _h = null!;

    public byte[] Build(ExecutiveReportViewModel m)
    {
        _h = new PptxSlideHelper();
        using var ms = new MemoryStream();
        using (var doc = PresentationDocument.Create(ms, DocumentFormat.OpenXml.PresentationDocumentType.Presentation))
        {
            var (pres, layout) = Init(doc);

            TitleSlide(pres, layout, m);
            OverviewSlide(pres, layout, m);
            DepartmentsSlide(pres, layout, m);
            QuizSlide(pres, layout, m);
            SurveySlide(pres, layout, m);
            ContributionsSlide(pres, layout, m);
            SignaturesSlide(pres, layout, m);

            pres.Presentation!.Save();
        }
        return ms.ToArray();
    }

    private void TitleSlide(PresentationPart pres, SlideLayoutPart layout, ExecutiveReportViewModel m)
    {
        var shapes = new List<OpenXmlElement>
        {
            _h.Rect(0L, 3300000L, SlideWidth, 70000L, Gold),
            _h.TextBox(1000000L, 2200000L, 10200000L, 1100000L, new[]
            {
                ("التقرير التنفيذي الشامل", 44, true, White),
            }, center: true),
            _h.TextBox(1000000L, 3500000L, 10200000L, 900000L, new[]
            {
                ("بناء البيت الاستراتيجي — رحلة الإدارات", 22, false, Gold),
                ("الهيئة العامة للمنافسة", 18, false, "CFE2F0"),
                ("تاريخ الإصدار: " + m.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd"), 14, false, "CFE2F0"),
            }, center: true),
        };
        AddSlide(pres, layout, Navy, shapes);
    }

    private void OverviewSlide(PresentationPart pres, SlideLayoutPart layout, ExecutiveReportViewModel m)
    {
        var kpis = new (string Label, string Value)[]
        {
            ("إجمالي الجلسات", m.Overview.TotalSessions.ToString()),
            ("إجمالي الحضور", m.Overview.TotalAttendees.ToString()),
            ("الإدارات المشاركة", m.Overview.TotalDepartmentsEngaged.ToString()),
            ("متوسط الاختبار", $"{m.Overview.AvgQuizScore:0.##}/5"),
            ("وضوح الاستراتيجية", m.Overview.AvgSurveyClarity > 0 ? $"{m.Overview.AvgSurveyClarity:0.##}/5" : "—"),
            ("تواقيع الفرق", m.GroupSignatures.TotalCount.ToString()),
        };
        var shapes = ContentHeader("النظرة العامة");
        long cardW = 3500000L, cardH = 1500000L, gapX = 250000L, gapY = 300000L;
        long startX = SlideWidth - 600000L - cardW, startY = 1700000L;
        for (int i = 0; i < kpis.Length; i++)
        {
            int row = i / 3, colIdx = i % 3;
            long x = startX - colIdx * (cardW + gapX);
            long y = startY + row * (cardH + gapY);
            shapes.Add(_h.Rect(x, y, cardW, cardH, LightNavy));
            shapes.Add(_h.TextBox(x, y + 150000L, cardW, cardH - 150000L, new[]
            {
                (kpis[i].Value, 32, true, Gold),
                (kpis[i].Label, 14, false, White),
            }, center: true));
        }
        AddSlide(pres, layout, Navy, shapes);
    }

    private void DepartmentsSlide(PresentationPart pres, SlideLayoutPart layout, ExecutiveReportViewModel m)
    {
        var shapes = ContentHeader("توزيع الإدارات");
        var lines = new List<(string, int, bool, string)>
        {
            ("الإدارة — الجلسات — الحضور — نسبة الإكمال", 16, true, Gold),
        };
        if (m.DepartmentBreakdown.Count == 0)
            lines.Add(("لا توجد بيانات إدارات بعد.", 16, false, White));
        foreach (var d in m.DepartmentBreakdown.Take(10))
            lines.Add(($"{d.DeptName} — {d.SessionsCount} — {d.AttendeesCount} — {d.CompletionRate:0.#}%", 14, false, White));
        shapes.Add(_h.TextBox(600000L, 1700000L, SlideWidth - 1200000L, 4500000L, lines));
        AddSlide(pres, layout, Navy, shapes);
    }

    private void QuizSlide(PresentationPart pres, SlideLayoutPart layout, ExecutiveReportViewModel m)
    {
        var qa = m.QuizAnalytics;
        var shapes = ContentHeader("تحليلات الاختبار");
        var lines = new List<(string, int, bool, string)>
        {
            ($"إجمالي المحاولات: {qa.TotalAttempts} · المتوسط: {qa.AvgScore:0.##} / 5", 18, true, Gold),
            ($"منخفض (0-2): {qa.Bucket0to2}", 16, false, White),
            ($"متوسط (3-4): {qa.Bucket3to4}", 16, false, White),
            ($"ممتاز (5): {qa.Bucket5}", 16, false, White),
        };
        if (qa.Top3MostMissed.Count > 0)
        {
            lines.Add(("أكثر الأسئلة صعوبة:", 16, true, Gold));
            foreach (var q in qa.Top3MostMissed)
                lines.Add(($"• {q.QuestionAr} — نسبة الخطأ {q.MissRate:0.#}%", 13, false, White));
        }
        shapes.Add(_h.TextBox(600000L, 1700000L, SlideWidth - 1200000L, 4500000L, lines));
        AddSlide(pres, layout, Navy, shapes);
    }

    private void SurveySlide(PresentationPart pres, SlideLayoutPart layout, ExecutiveReportViewModel m)
    {
        var shapes = ContentHeader("أبرز مؤشرات الاستبيان");
        var lines = new List<(string, int, bool, string)>();
        var top3 = m.SurveyMetrics.OrderBy(x => x.Order).Take(3).ToList();
        if (top3.Count == 0)
            lines.Add(("لا توجد بيانات استبيان بعد.", 16, false, White));
        foreach (var s in top3)
        {
            lines.Add(($"س{s.Order}: {s.QuestionAr}", 16, true, Gold));
            lines.Add(($"{s.Type} · {s.Headline}", 13, false, White));
        }
        shapes.Add(_h.TextBox(600000L, 1700000L, SlideWidth - 1200000L, 4500000L, lines));
        AddSlide(pres, layout, Navy, shapes);
    }

    private void ContributionsSlide(PresentationPart pres, SlideLayoutPart layout, ExecutiveReportViewModel m)
    {
        var shapes = ContentHeader("أبرز المساهمات");
        var lines = new List<(string, int, bool, string)>
        {
            ($"إجمالي التعهدات: {m.Contributions.TotalPledges}", 18, true, Gold),
            ("أبرز الأهداف:", 16, true, White),
        };
        if (m.Contributions.TopObjectives.Count == 0) lines.Add(("—", 14, false, White));
        foreach (var o in m.Contributions.TopObjectives.Take(5))
            lines.Add(($"• {o.Name} ({o.Count})", 14, false, White));
        lines.Add(("أبرز المبادرات:", 16, true, White));
        if (m.Contributions.TopInitiatives.Count == 0) lines.Add(("—", 14, false, White));
        foreach (var i in m.Contributions.TopInitiatives.Take(5))
            lines.Add(($"• {i.Name} ({i.Count})", 14, false, White));
        shapes.Add(_h.TextBox(600000L, 1700000L, SlideWidth - 1200000L, 4500000L, lines));
        AddSlide(pres, layout, Navy, shapes);
    }

    private void SignaturesSlide(PresentationPart pres, SlideLayoutPart layout, ExecutiveReportViewModel m)
    {
        var shapes = ContentHeader("تواقيع الفرق والخاتمة");
        var lines = new List<(string, int, bool, string)>
        {
            ($"إجمالي تواقيع الفرق: {m.GroupSignatures.TotalCount}", 18, true, Gold),
            ($"الخرائط الاستراتيجية: {m.MapsCount}", 16, false, White),
        };
        foreach (var c in m.GroupSignatures.RecentComments.Take(3))
            lines.Add(($"• {c.DeptName}: {c.Text}", 13, false, White));
        lines.Add(("شكراً لكل الإدارات على مشاركتها في بناء البيت الاستراتيجي.", 16, true, Gold));
        shapes.Add(_h.TextBox(600000L, 1700000L, SlideWidth - 1200000L, 4500000L, lines));
        AddSlide(pres, layout, Navy, shapes);
    }

    private List<OpenXmlElement> ContentHeader(string title) => new()
    {
        _h.Rect(0L, 0L, SlideWidth, 1300000L, LightNavy),
        _h.Rect(0L, 1300000L, SlideWidth, 50000L, Gold),
        _h.TextBox(600000L, 350000L, SlideWidth - 1200000L, 800000L, new[] { (title, 28, true, White) }),
    };
}
