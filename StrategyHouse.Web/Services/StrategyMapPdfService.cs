using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StrategyHouse.Domain.Entities;

namespace StrategyHouse.Web.Services;

// Generates the print-ready department strategy map PDF (A3 landscape, Arabic RTL).
public class StrategyMapPdfService
{
    private const string Font = "Noto Naskh Arabic";
    private const string Primary = "#194F90";
    private const string Cyan = "#46BCCD";
    private const string Green = "#009845";
    private const string Gold = "#D79A2B";

    private readonly StrategyContentService _content;

    public StrategyMapPdfService(StrategyContentService content)
    {
        _content = content;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(
        DepartmentStrategyMap map,
        Department department,
        IReadOnlyList<SessionMember> members,
        IReadOnlyList<ContributionPledge> pledges,
        IReadOnlyList<Pillar> pillars,
        IReadOnlyList<Kpi> kpis,
        IReadOnlyList<Project> projects,
        IReadOnlyList<MapInkAsset>? inkAssets = null)
    {
        var deptName = department.NameAr ?? department.DeptCode;
        var date = (map.SignedAt ?? map.CreatedAt).ToString("yyyy-MM-dd");

        inkAssets ??= new List<MapInkAsset>();
        // Approved & active ink per section kind, and per signing member.
        List<MapInkAsset> SectionInk(string kind) => inkAssets
            .Where(a => a.AssetKind == kind && a.ModerationStatus == "Approved" && a.IsActive && a.PngBlob != null)
            .ToList();
        bool HasPendingInk(string kind) => inkAssets
            .Any(a => a.AssetKind == kind && a.ModerationStatus == "Pending");
        var sigByMember = inkAssets
            .Where(a => a.AssetKind == "signature" && a.MemberId != null
                        && a.ModerationStatus == "Approved" && a.IsActive && a.PngBlob != null)
            .GroupBy(a => a.MemberId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CapturedAt).First().PngBlob!);

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A3.Landscape());
                page.Margin(28);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("خريطة الاستراتيجية الإدارية").FontSize(22).Bold().FontColor(Primary);
                            c.Item().Text(deptName).FontSize(16).FontColor(Gold);
                        });
                        row.ConstantItem(120).AlignLeft().Column(c =>
                        {
                            c.Item().Text("GAC").FontSize(28).Bold().FontColor(Primary);
                            c.Item().Text(date).FontSize(10).FontColor(Colors.Grey.Darken1);
                        });
                    });
                    col.Item().PaddingTop(4).LineHorizontal(2).LineColor(Cyan);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(10);

                    // Vision banner
                    col.Item().Background(Primary).Padding(10).Column(c =>
                    {
                        c.Item().Text("الرؤية").FontColor(Cyan).Bold().FontSize(12);
                        c.Item().Text(_content.Vision.Ar).FontColor(Colors.White).FontSize(14);
                    });

                    // Mission
                    col.Item().Background("#FAFBFC").Border(1).BorderColor(Cyan).Padding(8).Column(c =>
                    {
                        c.Item().Text("الرسالة").FontColor(Primary).Bold();
                        c.Item().Text(_content.Mission.Ar);
                    });

                    // Values row (between Mission and Pillars)
                    if (_content.Values.Any())
                    {
                        var valueColors = new[] { Cyan, Green, Gold, "#9DC41A", Primary };
                        col.Item().Row(row =>
                        {
                            for (var vi = 0; vi < _content.Values.Count; vi++)
                            {
                                var color = valueColors[vi % valueColors.Length];
                                row.RelativeItem().Padding(3).Background(color).Padding(6)
                                    .AlignCenter().Text(_content.Values[vi].Ar).FontColor(Colors.White).Bold().FontSize(10);
                            }
                        });
                    }

                    // Pillars row
                    col.Item().Row(row =>
                    {
                        foreach (var p in pillars)
                        {
                            row.RelativeItem().Padding(3).Background(Green).Padding(6)
                                .Text(p.PillarName ?? p.PlrCode).FontColor(Colors.White).FontSize(10);
                        }
                    });

                    // KPIs + Projects grid
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"مؤشرات الإدارة ({kpis.Count})").Bold().FontColor(Primary);
                            foreach (var k in kpis.Take(20))
                                c.Item().Text("• " + (k.KpiName ?? k.KpiCode)).FontSize(9);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"مشاريع الإدارة ({projects.Count})").Bold().FontColor(Primary);
                            foreach (var pr in projects.Take(20))
                                c.Item().Text("• " + (pr.ProjectName ?? pr.ProjectCode)).FontSize(9);
                        });
                    });

                    // Pledges
                    col.Item().Column(c =>
                    {
                        c.Item().Text($"المساهمة ({pledges.Count})").Bold().FontColor(Gold);
                        foreach (var pl in pledges)
                            c.Item().Text($"• [{pl.ElementType}] {pl.ElementCode} — {pl.ContributionKind}").FontSize(9);
                    });

                    // Text sections — typed text on top, approved ink underneath.
                    // The commitment section is optional: only render when filled (FIX 11).
                    var commitmentFilled = !string.IsNullOrWhiteSpace(map.CommitmentsText)
                        || SectionInk("commitment").Count > 0;
                    col.Item().Row(row =>
                    {
                        void Section(string title, string color, string? text, string kind)
                        {
                            row.RelativeItem().Padding(3).Border(1).BorderColor(color).Padding(6).Column(c =>
                            {
                                c.Item().Text(title).Bold().FontColor(color);
                                c.Item().Text(text ?? "—").FontSize(9);
                                foreach (var a in SectionInk(kind))
                                    c.Item().PaddingTop(4).Height(90).Image(a.PngBlob!).FitArea();
                                if (HasPendingInk(kind))
                                    c.Item().PaddingTop(4).Background("#FFF6E5").Padding(4)
                                        .Text("في انتظار الاعتماد").FontSize(8).FontColor(Gold);
                            });
                        }
                        Section("الآراء", Primary, map.OpinionsText, "opinion");
                        Section("الأمنيات", Green, map.WishesText, "wish");
                        if (commitmentFilled)
                            Section("خطوات بسيطة سنبدأ بها", Gold, map.CommitmentsText, "commitment");
                    });

                    // Signatures
                    col.Item().PaddingTop(6).Column(c =>
                    {
                        c.Item().Text("التواقيع").Bold().FontColor(Primary);
                        c.Item().Row(row =>
                        {
                            foreach (var m in members)
                            {
                                row.RelativeItem().Padding(3).Column(mc =>
                                {
                                    if (sigByMember.TryGetValue(m.Id, out var sigPng))
                                        mc.Item().Height(40).AlignRight().Image(sigPng).FitArea();
                                    else
                                        mc.Item().Text(m.TypedSignature ?? m.NameAr).FontSize(12).Italic();
                                    mc.Item().LineHorizontal(1).LineColor(Colors.Grey.Medium);
                                    mc.Item().Text($"{m.NameAr} — {m.Role}").FontSize(8).FontColor(Colors.Grey.Darken1);
                                });
                            }
                        });
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("الهيئة العامة للمنافسة — ").FontColor(Colors.Grey.Darken1).FontSize(9);
                    t.Span("منصة إطلاق الاستراتيجية").FontColor(Primary).FontSize(9);
                });
            });
        });

        return doc.GeneratePdf();
    }
}
