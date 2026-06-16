using System.Net;
using System.Net.Mail;

namespace StrategyHouse.Web.Services;

// Phase 13.1 — sends generated reports (PDF/PPTX/XLSX/CSV) as email attachments via SMTP.
// SMTP settings come from the "Smtp" config section; all keys are optional. When no host is
// configured the service returns a graceful failure instead of throwing, so the admin UI can
// surface a clear Arabic message rather than a 500.
public class ReportEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<ReportEmailService> _logger;

    public ReportEmailService(IConfiguration config, ILogger<ReportEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public record EmailResult(bool Sent, string Reason);

    public async Task<EmailResult> SendReportAsync(
        string toEmail, string subject, string bodyHtml,
        string fileName, byte[] attachment, string mimeType)
    {
        var host = _config["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
            return new EmailResult(false, "خدمة البريد غير مهيأة على الخادم. يرجى ضبط إعدادات SMTP أو تنزيل التقرير يدوياً.");

        if (string.IsNullOrWhiteSpace(toEmail))
            return new EmailResult(false, "يرجى إدخال بريد إلكتروني صحيح.");

        int port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
        bool enableSsl = !bool.TryParse(_config["Smtp:EnableSsl"], out var ssl) || ssl;
        var user = _config["Smtp:User"];
        var password = _config["Smtp:Password"];
        var from = _config["Smtp:From"];
        if (string.IsNullOrWhiteSpace(from)) from = "no-reply@gac.gov.sa";

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(from),
                Subject = subject,
                Body = bodyHtml,
                IsBodyHtml = true,
            };
            message.To.Add(new MailAddress(toEmail));
            using var stream = new MemoryStream(attachment);
            message.Attachments.Add(new Attachment(stream, fileName, mimeType));

            using var client = new SmtpClient(host, port) { EnableSsl = enableSsl };
            if (!string.IsNullOrWhiteSpace(user))
                client.Credentials = new NetworkCredential(user, password);

            await client.SendMailAsync(message);
            return new EmailResult(true, $"تم إرسال التقرير بنجاح إلى {toEmail}.");
        }
        catch (FormatException)
        {
            return new EmailResult(false, "صيغة البريد الإلكتروني غير صحيحة.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send report email to {Email}", toEmail);
            return new EmailResult(false, "تعذّر إرسال البريد الإلكتروني. يرجى المحاولة لاحقاً أو تنزيل التقرير يدوياً.");
        }
    }
}
