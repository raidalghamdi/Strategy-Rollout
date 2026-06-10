using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

/// <summary>
/// Composes the same-day email package: thank-you (per-attendee, Arabic) +
/// Department Strategy Map (image/HTML) + optional quiz link. In v1 the
/// composer renders the HTML and returns it; production-time SMTP wiring can
/// be added by configuring an IEmailSender in Program.cs.
/// </summary>
public class EmailComposer
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;

    public EmailComposer(ApplicationDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<EmailPackage> ComposeForAttendeeAsync(int attendeeId)
    {
        var attendee = await _db.SessionAttendees
            .Include(a => a.Department)
            .Include(a => a.Session)
            .FirstAsync(a => a.Id == attendeeId);

        var map = await _db.StrategyMaps
            .Include(m => m.Commitments).ThenInclude(c => c.LinkedElement)
            .Include(m => m.Signatures)
            .FirstOrDefaultAsync(m => m.SessionId == attendee.SessionId && m.DepartmentId == attendee.DepartmentId);

        var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5000";
        var quizUrl = $"{baseUrl}/Session/Quiz?code={attendee.Session?.AccessCode}";
        var wallUrl = $"{baseUrl}/Wall?code={attendee.Session?.AccessCode}";

        var subject = "بيت الاستراتيجية - شكرًا لمشاركتك";
        var body = $@"<!DOCTYPE html>
<html dir='rtl' lang='ar'>
<head><meta charset='utf-8'>
<style>
  body {{ font-family: 'Tajawal', 'Segoe UI', sans-serif; background: #f5f7fa; color: #1f3b4d; padding: 24px; }}
  .card {{ background: #fff; max-width: 640px; margin: auto; border-radius: 8px; padding: 32px; box-shadow: 0 2px 8px rgba(0,0,0,0.06); }}
  h1 {{ color: #1B5E7F; margin-top: 0; }}
  .map {{ background: #E8F4F8; border-radius: 6px; padding: 16px; margin: 16px 0; }}
  .commitment {{ background: #fff; border-right: 4px solid #1B5E7F; padding: 10px 14px; margin: 8px 0; }}
  .cta {{ display: inline-block; background: #1B5E7F; color: #fff !important; padding: 10px 20px; border-radius: 6px; text-decoration: none; margin: 8px 4px; }}
</style></head>
<body>
  <div class='card'>
    <h1>شكرًا {attendee.FullNameAr}</h1>
    <p>نشكرك على مشاركتك في جلسة <b>{attendee.Session?.TitleAr}</b> أمس.
    وجود إدارة <b>{attendee.Department?.NameAr}</b> في الجلسة أضاف قيمة حقيقية للنقاش.</p>
    <h3>خريطة إدارتك الاستراتيجية</h3>
    <div class='map'>
      {RenderMapSummary(map, attendee.Department!)}
    </div>
    <p>
      <a class='cta' href='{wallUrl}'>شاهد جدار البيت الاستراتيجي</a>
      <a class='cta' href='{quizUrl}'>اختبار تفاعلي قصير (اختياري)</a>
    </p>
    <p style='color:#6b7c89; font-size: 12px;'>سيتم تسليم النسخة المطبوعة من الخريطة لإدارتك لاحقًا.</p>
  </div>
</body>
</html>";
        return new EmailPackage(attendee.Email ?? "", subject, body);
    }

    private string RenderMapSummary(StrategyMap? map, Department dept)
    {
        if (map == null)
            return $"<p>خريطة إدارة <b>{dept.NameAr}</b> قيد التجهيز.</p>";

        var commitmentsHtml = string.Join("", map.Commitments.Select(c =>
            $"<div class='commitment'>{System.Net.WebUtility.HtmlEncode(c.TextAr)}" +
            (c.LinkedElement != null ? $" <span style='color:#6b7c89;font-size:12px;'>→ {System.Net.WebUtility.HtmlEncode(c.LinkedElement.NameAr)}</span>" : "") +
            "</div>"));

        var sigCount = map.Signatures.Count;
        return $@"<p><b>{System.Net.WebUtility.HtmlEncode(dept.NameAr)}</b></p>
                  <p>التزامات الفريق:</p>{commitmentsHtml}
                  <p style='color:#6b7c89;font-size:12px;'>عدد التواقيع: {sigCount}</p>";
    }
}

public record EmailPackage(string To, string Subject, string HtmlBody);
