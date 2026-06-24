using System.Net;
using System.Net.Mail;

namespace StrategyHouse.Web.Services;

// Phase 20.27 — minimal SMTP email sender for password reset / first-time setup links.
// Configuration lives in appsettings.{Environment}.json under the "Smtp" section.
// On Office365 use smtp.office365.com:587 with STARTTLS and a mailbox username/password.
// On internal Exchange, point Host at e.g. mail.gac.gov.sa.
//
// If the configuration is missing or marked Enabled=false, calls are no-ops and just
// log the message; this lets development run without a real SMTP server.
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
    bool IsConfigured { get; }
}

public class SmtpOptions
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "smtp.office365.com";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "no-reply@gac.gov.sa";
    public string FromName { get; set; } = "منصة الاستراتيجية - GAC";
}

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _opt;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(SmtpOptions opt, ILogger<SmtpEmailSender> log)
    {
        _opt = opt;
        _log = log;
    }

    public bool IsConfigured =>
        _opt.Enabled
        && !string.IsNullOrWhiteSpace(_opt.Host)
        && !string.IsNullOrWhiteSpace(_opt.FromAddress);

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _log.LogWarning("SMTP not configured — would have sent to {To} subject={Subject}", toEmail, subject);
            return;
        }

        try
        {
            using var msg = new MailMessage
            {
                From = new MailAddress(_opt.FromAddress, _opt.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
                SubjectEncoding = System.Text.Encoding.UTF8,
                BodyEncoding = System.Text.Encoding.UTF8,
            };
            msg.To.Add(new MailAddress(toEmail));

            using var client = new SmtpClient(_opt.Host, _opt.Port)
            {
                EnableSsl = _opt.UseStartTls,
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };
            if (!string.IsNullOrEmpty(_opt.Username))
                client.Credentials = new NetworkCredential(_opt.Username, _opt.Password);

            await client.SendMailAsync(msg, ct);
            _log.LogInformation("Sent email to {To} subject={Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send email to {To} subject={Subject}", toEmail, subject);
            throw;
        }
    }
}
