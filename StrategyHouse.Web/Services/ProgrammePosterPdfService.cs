using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Web.Configuration;

namespace StrategyHouse.Web.Services;

// Phase 2 — one giant A2 programme poster: Vision + Mission + Pillars + a grid of
// all 17 department map thumbnails with status badges.
public class ProgrammePosterPdfService
{
    private const string Font = "Noto Naskh Arabic";
    private const string Primary = "#194F90";
    private const string Cyan = "#46BCCD";
    private const string Green = "#009845";
    private const string Gold = "#D79A2B";

    private readonly StrategyContentService _content;
    private readonly string _periodLabel;

    public ProgrammePosterPdfService(StrategyContentService content, IOptions<StrategyContentOptions> options)
    {
        _content = content;
        _periodLabel = string.IsNullOrWhiteSpace(options.Value.PeriodLabel) ? "2026-2030" : options.Value.PeriodLabel;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public class DeptPoster
    {
        public string DeptCode { get; set; } = "";
        public string DeptName { get; set; } = "";
        public string Status { get; set; } = "locked"; // signed / pending / locked
        public byte[]? ThumbPng { get; set; }
    }

    public byte[] Generate(IReadOnlyList<Pillar> pillars, IReadOnlyList<DeptPoster> depts)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                // A2 portrait (420 × 594 mm).
                page.Size(420, 594, Unit.Millimetre);
                page.Margin(20, Unit.Millimetre);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(14));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("الاستراتيجية المؤسسية " + _periodLabel).FontSize(40).Bold().FontColor(Primary);
                            c.Item().Text("الهيئة العامة للمنافسة").FontSize(22).FontColor(Gold);
                        });
                        row.ConstantItem(160).AlignLeft().Text("GAC").FontSize(60).Bold().FontColor(Primary);
                    });
                    col.Item().PaddingTop(8).LineHorizontal(3).LineColor(Cyan);
                });

                page.Content().PaddingVertical(14).Column(col =>
                {
                    col.Spacing(14);

                    col.Item().Background(Primary).Padding(18).Column(c =>
                    {
                        c.Item().Text("الرؤية").FontColor(Cyan).Bold().FontSize(20);
                        c.Item().Text(_content.Vision.Ar).FontColor(Colors.White).FontSize(24);
                    });

                    col.Item().Background("#FAFBFC").Border(1).BorderColor(Cyan).Padding(14).Column(c =>
                    {
                        c.Item().Text("الرسالة").FontColor(Primary).Bold().FontSize(18);
                        c.Item().Text(_content.Mission.Ar).FontSize(18);
                    });

                    col.Item().Text("الركائز الاستراتيجية").FontSize(20).Bold().FontColor(Primary);
                    col.Item().Row(row =>
                    {
                        foreach (var p in pillars)
                        {
                            row.RelativeItem().Padding(4).Background(Green).Padding(10)
                                .Text(p.PillarName ?? p.PlrCode).FontColor(Colors.White).FontSize(13).AlignCenter();
                        }
                    });

                    col.Item().PaddingTop(8).Text($"خرائط الإدارات ({depts.Count})").FontSize(20).Bold().FontColor(Primary);

                    // 4-column grid of dept thumbnails.
                    const int perRow = 4;
                    for (int i = 0; i < depts.Count; i += perRow)
                    {
                        col.Item().Row(row =>
                        {
                            for (int j = 0; j < perRow; j++)
                            {
                                if (i + j < depts.Count)
                                {
                                    var d = depts[i + j];
                                    row.RelativeItem().Padding(5).Border(1).BorderColor(Cyan).Padding(6).Column(c =>
                                    {
                                        c.Item().Row(hr =>
                                        {
                                            hr.RelativeItem().Text(d.DeptName).FontSize(11).Bold().FontColor(Primary);
                                            hr.ConstantItem(70).Text(StatusBadge(d.Status)).FontSize(9)
                                                .FontColor(StatusColor(d.Status));
                                        });
                                        if (d.ThumbPng != null)
                                            c.Item().PaddingTop(4).Height(110).Image(d.ThumbPng).FitArea();
                                        else
                                            c.Item().PaddingTop(4).Height(110).Background("#EEF2F7").AlignCenter().AlignMiddle()
                                                .Text("لا توجد خريطة").FontSize(10).FontColor(Colors.Grey.Medium);
                                    });
                                }
                                else
                                {
                                    row.RelativeItem().Padding(5);
                                }
                            }
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("الهيئة العامة للمنافسة — منصة إطلاق الاستراتيجية · ").FontColor(Colors.Grey.Darken1).FontSize(11);
                    t.Span(DateTime.UtcNow.ToString("yyyy-MM-dd")).FontColor(Primary).FontSize(11);
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static string StatusBadge(string status) => status switch
    {
        "signed" => "موقّعة",
        "pending" => "قيد الإعداد",
        _ => "مقفلة",
    };

    private static string StatusColor(string status) => status switch
    {
        "signed" => Green,
        "pending" => Gold,
        _ => "#888888",
    };
}
