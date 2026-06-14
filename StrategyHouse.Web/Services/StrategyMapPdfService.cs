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
        IReadOnlyList<Project> projects)
    {
        var deptName = department.NameAr ?? department.DeptCode;
        var date = (map.SignedAt ?? map.CreatedAt).ToString("yyyy-MM-dd");

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
                        c.Item().Text($"تعهدات المساهمة ({pledges.Count})").Bold().FontColor(Gold);
                        foreach (var pl in pledges)
                            c.Item().Text($"• [{pl.ElementType}] {pl.ElementCode} — {pl.ContributionKind}").FontSize(9);
                    });

                    // Three text sections
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Padding(3).Border(1).BorderColor(Cyan).Padding(6).Column(c =>
                        {
                            c.Item().Text("الآراء").Bold().FontColor(Primary);
                            c.Item().Text(map.OpinionsText ?? "—").FontSize(9);
                        });
                        row.RelativeItem().Padding(3).Border(1).BorderColor(Green).Padding(6).Column(c =>
                        {
                            c.Item().Text("الأمنيات").Bold().FontColor(Green);
                            c.Item().Text(map.WishesText ?? "—").FontSize(9);
                        });
                        row.RelativeItem().Padding(3).Border(1).BorderColor(Gold).Padding(6).Column(c =>
                        {
                            c.Item().Text("الالتزامات").Bold().FontColor(Gold);
                            c.Item().Text(map.CommitmentsText ?? "—").FontSize(9);
                        });
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
