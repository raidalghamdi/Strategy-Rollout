using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 19.7 — diagnostics for the external MSSQL connection. The Phase 17
// ExternalDbContext and the Phase 19.5 push button silently fail when the
// warehouse is unreachable, leaving the operator with no signal. This service
// runs a bounded CanConnect probe, masks the password, categorises the failure
// and emits an Arabic hint. Reused by both the startup probe (Railway logs) and
// the GET /Admin/ExternalData/TestConnection JSON endpoint.

public class ExternalDbDiagnosticResult
{
    public bool UseExternalDb { get; set; }
    public string ConnectionStringMasked { get; set; } = "";
    public bool CanConnect { get; set; }
    public string? ErrorMessage { get; set; }
    public string ErrorCategory { get; set; } = "unknown";
    public string ArabicHint { get; set; } = "";
    public long LatencyMs { get; set; }
    public string? ServerVersion { get; set; }
}

public class ExternalDbDiagnostics
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly IConfiguration _config;
    private readonly ExternalDbContext? _external;
    private readonly ILogger<ExternalDbDiagnostics> _log;

    public ExternalDbDiagnostics(
        IConfiguration config,
        ILogger<ExternalDbDiagnostics> log,
        ExternalDbContext? external = null)
    {
        _config = config;
        _log = log;
        _external = external;
    }

    public async Task<ExternalDbDiagnosticResult> TestAsync(CancellationToken ct = default)
    {
        var useExternal = _config.GetValue<bool>("Features:UseExternalDb");
        var rawConn = _config.GetConnectionString("ExternalMssql") ?? "";

        var result = new ExternalDbDiagnosticResult
        {
            UseExternalDb = useExternal,
            ConnectionStringMasked = MaskConnectionString(rawConn),
        };

        if (!useExternal)
        {
            result.ErrorCategory = "config";
            result.ArabicHint = "العلم UseExternalDb معطل في الإعدادات. عدّل appsettings.json وأعد تشغيل التطبيق.";
            return result;
        }

        if (_external == null)
        {
            result.ErrorCategory = "config";
            result.ErrorMessage = "ExternalDbContext is not registered (connection string is empty or the flag was off at startup).";
            result.ArabicHint = "سلسلة الاتصال فارغة أو لم يُسجَّل السياق عند بدء التشغيل. تحقق من ConnectionStrings:ExternalMssql ثم أعد التشغيل.";
            return result;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);
            var token = cts.Token;

            result.CanConnect = await _external.Database.CanConnectAsync(token);

            if (result.CanConnect)
            {
                result.ServerVersion = await TryGetServerVersionAsync(token);
                result.ArabicHint = "الاتصال ناجح بقاعدة البيانات الخارجية.";
            }
            else
            {
                // CanConnect returned false without throwing — treat as a generic
                // network/reachability failure so the operator gets a useful hint.
                result.ErrorMessage = "CanConnectAsync returned false (server unreachable or refused the connection).";
                Categorize(result, "could not open a connection");
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            var full = FlattenException(ex);
            result.CanConnect = false;
            result.ErrorMessage = full;
            // Categorise on the OUTERMOST exception message per the diagnostic spec.
            Categorize(result, ex.Message);
            result.LatencyMs = sw.ElapsedMilliseconds;
            return result;
        }

        sw.Stop();
        result.LatencyMs = sw.ElapsedMilliseconds;
        return result;
    }

    private async Task<string?> TryGetServerVersionAsync(CancellationToken ct)
    {
        try
        {
            var versions = await _external!.Database
                .SqlQueryRaw<string>("SELECT @@VERSION AS Value")
                .ToListAsync(ct);
            return versions.Count > 0 ? versions[0] : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Connected to external MSSQL but SELECT @@VERSION failed.");
            return null;
        }
    }

    // Categorise using the OUTERMOST message text. Order matters: auth/database
    // are checked before the broad network match so a "login failed" or "cannot
    // open database" is not swallowed by a generic connection phrase.
    public static void Categorize(ExternalDbDiagnosticResult result, string message)
    {
        var m = (message ?? "").ToLowerInvariant();

        if (m.Contains("login failed"))
        {
            result.ErrorCategory = "auth";
            result.ArabicHint = "بيانات الاعتماد غير صحيحة (اسم المستخدم أو كلمة المرور)";
            return;
        }
        if (m.Contains("cannot open database") || m.Contains("database does not exist"))
        {
            result.ErrorCategory = "database";
            result.ArabicHint = "اسم قاعدة البيانات غير موجود أو المستخدم لا يملك صلاحية الوصول";
            return;
        }
        if (m.Contains("certificate") || m.Contains("ssl") || m.Contains("encryption"))
        {
            result.ErrorCategory = "ssl";
            result.ArabicHint = "مشكلة شهادة SSL. أضف TrustServerCertificate=True;Encrypt=False إلى سلسلة الاتصال";
            return;
        }
        if (m.Contains("format") || m.Contains("keyword not supported") || m.Contains("invalid connection string"))
        {
            result.ErrorCategory = "config";
            result.ArabicHint = "صيغة سلسلة الاتصال غير صحيحة. الصيغة المتوقعة: Server=hostname,1433;Database=name;User Id=user;Password=pass;TrustServerCertificate=True;Encrypt=False;Connection Timeout=10;";
            return;
        }
        if (m.Contains("network-related") || m.Contains("could not open a connection") || m.Contains("transport-level error"))
        {
            result.ErrorCategory = "network";
            result.ArabicHint = "تعذر الوصول للخادم. تحقق من اسم الخادم/المنفذ وأن جدار الحماية يسمح بالاتصال من Railway (IP outbound)";
            return;
        }
        if (m.Contains("timeout") || m.Contains("timed out"))
        {
            result.ErrorCategory = "timeout";
            result.ArabicHint = "انتهت مهلة الاتصال. تحقق من أن الخادم متاح من الإنترنت العام (وليس فقط الشبكة الداخلية)";
            return;
        }

        result.ErrorCategory = "unknown";
        result.ArabicHint = "خطأ غير معروف. راجع السجل الكامل في Railway logs";
    }

    // Flattens an exception and all of its inner exceptions into one string so
    // the full chain is visible in Railway logs / the JSON response.
    public static string FlattenException(Exception ex)
    {
        var parts = new List<string>();
        var current = (Exception?)ex;
        var depth = 0;
        while (current != null && depth < 10)
        {
            parts.Add(current.GetType().Name + ": " + current.Message);
            current = current.InnerException;
            depth++;
        }
        return string.Join(" --> ", parts);
    }

    // Masks the Password / Pwd value in a connection string so logs never leak it.
    public static string MaskConnectionString(string? conn)
    {
        if (string.IsNullOrWhiteSpace(conn)) return "";

        var segments = conn.Split(';');
        for (var i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            var eq = seg.IndexOf('=');
            if (eq <= 0) continue;
            var key = seg.Substring(0, eq).Trim().ToLowerInvariant();
            if (key == "password" || key == "pwd")
            {
                segments[i] = seg.Substring(0, eq) + "=***";
            }
        }
        return string.Join(";", segments);
    }
}
