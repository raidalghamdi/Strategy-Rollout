using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Configuration;
using StrategyHouse.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Register the bundled Arabic font for QuestPDF (falls back gracefully if missing).
var arabicFontPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "fonts", "NotoNaskhArabic-Regular.ttf");
if (File.Exists(arabicFontPath))
{
    using var fontStream = File.OpenRead(arabicFontPath);
    QuestPDF.Drawing.FontManager.RegisterFont(fontStream);
}
// Phase 12 — register Cairo for the survey final-report PDF (GAC brand typeface).
var cairoFontPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "fonts", "cairo", "Cairo-Variable.ttf");
if (File.Exists(cairoFontPath))
{
    using var cairoStream = File.OpenRead(cairoFontPath);
    QuestPDF.Drawing.FontManager.RegisterFont(cairoStream);
}
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Database — SQLite primary store for the app (sessions, CMS, surveys, mirror tables).
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var conn = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=strategy_house.db";
    options.UseSqlite(conn);
});

// Phase 16 / 19.8 — optional external MSSQL (Option A schema). The ExternalDbContext
// is now registered whenever a connection string is supplied, INDEPENDENT of the
// UseExternalDb flag. This lets the flag be toggled at runtime (it is reloadable JSON;
// see IOptionsMonitor<FeaturesOptions> below) and take effect immediately — every
// consumer gates live behaviour on the flag via IConfiguration, so flipping it on
// activates the already-registered context with no server restart. When no connection
// string is configured the context is left unregistered (injected as null) exactly as
// before, so the app runs entirely on the local SQLite context.
builder.Services.Configure<FeaturesOptions>(builder.Configuration.GetSection("Features"));
var externalConn = builder.Configuration.GetConnectionString("ExternalMssql");
if (!string.IsNullOrWhiteSpace(externalConn))
{
    builder.Services.AddDbContext<ExternalDbContext>(options => options.UseSqlServer(externalConn));
}
builder.Services.AddMemoryCache();
builder.Services.AddScoped<DepartmentDirectoryService>();
// Phase 17 — read services for the Option A strategy tables (external MSSQL).
// When UseExternalDb is off these return empty lists with a logged warning.
builder.Services.AddScoped<PillarsService>();
builder.Services.AddScoped<ObjectivesService>();
builder.Services.AddScoped<KpisService>();
builder.Services.AddScoped<InitiativesService>();
builder.Services.AddScoped<ProjectsService>();
// Phase 19.5 — MSSQL→SQLite mirror push + resilient strategy data provider
// (live MSSQL → SQLite mirror → dummy fallback).
builder.Services.AddScoped<IMssqlMirrorService, MssqlMirrorService>();
builder.Services.AddScoped<IStrategyDataProvider, StrategyDataProvider>();
// Phase 19.7 — external MSSQL connection diagnostics (startup probe + admin endpoint).
builder.Services.AddScoped<ExternalDbDiagnostics>();

// Identity — three roles: Admin, Facilitator, Viewer.
builder.Services
    .AddIdentity<AppUser, IdentityRole<int>>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 4;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";

    // API calls (e.g. the auth-gated chatbot) should get a 401/403 instead of an
    // HTML login redirect so the client can handle the rejection cleanly.
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

// MVC + Razor
builder.Services.AddControllersWithViews()
    .AddRazorRuntimeCompilation();

// Larger request bodies for base64 PNG ink/signature uploads (Phase 3).
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(o =>
    o.Limits.MaxRequestBodySize = 25_000_000);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 25_000_000;
    o.ValueLengthLimit = 25_000_000;
});

// App services
builder.Services.Configure<StrategyContentOptions>(builder.Configuration.GetSection("StrategyContent"));
builder.Services.AddSingleton<StrategyContentService>();
// Phase 9 — mini CMS (admin-editable page text), cached in memory.
builder.Services.AddSingleton<PageContentService>();
builder.Services.AddScoped<QrService>();
builder.Services.AddScoped<AccessCodeService>();
builder.Services.AddScoped<StrategyMapPdfService>();
// Phase 2 — leadership analytics
builder.Services.AddScoped<CoverageService>();
builder.Services.AddScoped<PledgeAggregateService>();
builder.Services.AddScoped<ProgrammePosterPdfService>();
// Phase 4 — assessment
builder.Services.AddScoped<QuizGeneratorService>();
builder.Services.AddScoped<SurveyReportPdfService>();
// Phase 12 — official survey analytics + final report
builder.Services.AddScoped<SurveyAnalyticsService>();
builder.Services.AddScoped<SurveyFinalReportPdfService>();
// Phase 19.21 (Fix 4) — strategy-data executive report (External entities → xlsx + summary)
builder.Services.AddScoped<StrategyDataReportService>();
// Phase 13 — executive report
builder.Services.AddScoped<ExecutiveReportService>();
builder.Services.AddScoped<ExecutiveReportPdfDocument>();
// Phase 13.1 — multi-format export (PowerPoint + Excel) and email delivery
builder.Services.AddScoped<ExecutiveReportExcelBuilder>();
builder.Services.AddScoped<ExecutiveReportPowerPointBuilder>();
builder.Services.AddScoped<SurveyReportExcelBuilder>();
builder.Services.AddScoped<SurveyReportPowerPointBuilder>();
builder.Services.AddScoped<ReportEmailService>();
// Phase 6 — DB-only chatbot
builder.Services.AddScoped<ChatbotService>();

var app = builder.Build();

// Seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
    await SeedData.RunAsync(db, userManager, roleManager);

    // Phase 4 — programme survey (quiz auto-seed removed in Phase 5; admin-controlled).
    // Phase 19.8 — period label comes from StrategyContent:PeriodLabel (default 2026-2030).
    var quiz = scope.ServiceProvider.GetRequiredService<QuizGeneratorService>();
    var periodLabel = builder.Configuration.GetValue<string>("StrategyContent:PeriodLabel") ?? "2026-2030";
    await AssessmentSeeder.RunAsync(db, quiz, periodLabel);

    // Phase 12 — replace the survey bank with the 8 official questions (idempotent via hash).
    await Phase12SurveySeeder.SeedAsync(db);

    // Phase 14 — seed editable home-page CMS keys (idempotent; never overwrites edits).
    await Phase14HomeContentSeeder.SeedAsync(db);

    // Phase 10 — optional one-time quiz reset on startup (controlled by Quiz:ResetOnStartup).
    // Wipes all attempts + questions and reseeds the 5 demo questions. Default false.
    if (builder.Configuration.GetValue<bool>("Quiz:ResetOnStartup"))
        await AssessmentSeeder.ResetQuizAsync(db);

    // Phase 10.2 — unconditional safety net: if the bank is empty for any reason
    // (a stale reset flag, a deploy that wiped without reseeding), restore the 5 demo
    // questions so /Quiz/Start never renders an empty card. Idempotent.
    await AssessmentSeeder.EnsureDemoQuizAsync(db);

    // Phase 6 — predefined department roster (default-checked attendees in stage 1).
    // Phase 19.8 — shuffle seed is config-driven (StrategyContent:RandomSeed); null → Random.Shared.
    var rosterSeed = builder.Configuration.GetValue<int?>("StrategyContent:RandomSeed");
    await RosterSeeder.RunAsync(db, rosterSeed);

    // Phase 5 — one-time signature backfill: flip pending signature ink to Approved
    // and regenerate signed-map PDFs so existing maps show their signatures.
    var pdf = scope.ServiceProvider.GetRequiredService<StrategyMapPdfService>();
    await SignatureBackfill.RunAsync(db, pdf);

    // Phase 19.7 — when UseExternalDb is true, probe the external MSSQL on startup
    // and log the FULL exception chain (password masked) at Warning so Railway logs
    // reveal why the connection fails. Never throws: the app must keep running on
    // the mirror/dummy fallback even when the warehouse is unreachable.
    if (builder.Configuration.GetValue<bool>("Features:UseExternalDb"))
    {
        var startupLog = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("ExternalDbStartupProbe");
        try
        {
            var diag = scope.ServiceProvider.GetRequiredService<ExternalDbDiagnostics>();
            var probe = await diag.TestAsync();
            if (probe.CanConnect)
            {
                startupLog.LogInformation(
                    "External MSSQL probe OK in {LatencyMs}ms. Conn={Masked} Version={Version}",
                    probe.LatencyMs, probe.ConnectionStringMasked, probe.ServerVersion);
            }
            else
            {
                startupLog.LogWarning(
                    "External MSSQL probe FAILED ({Category}) in {LatencyMs}ms. Conn={Masked} Error={Error} Hint={Hint}",
                    probe.ErrorCategory, probe.LatencyMs, probe.ConnectionStringMasked,
                    probe.ErrorMessage, probe.ArabicHint);
            }
        }
        catch (Exception ex)
        {
            startupLog.LogWarning(ex,
                "External MSSQL startup probe threw unexpectedly; continuing on mirror/dummy fallback.");
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
