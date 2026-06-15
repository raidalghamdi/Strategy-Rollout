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
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Database — SQLite (dev) / MySQL (production), provider-switchable via appsettings.
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (provider.Equals("MySql", StringComparison.OrdinalIgnoreCase))
    {
        var conn = builder.Configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException("MySql connection string missing.");
        options.UseMySql(conn, ServerVersion.AutoDetect(conn));
    }
    else
    {
        var conn = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=strategy_house.db";
        options.UseSqlite(conn);
    }
});

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
    var quiz = scope.ServiceProvider.GetRequiredService<QuizGeneratorService>();
    await AssessmentSeeder.RunAsync(db, quiz);

    // Phase 10 — optional one-time quiz reset on startup (controlled by Quiz:ResetOnStartup).
    // Wipes all attempts + questions and reseeds the 5 demo questions. Default false.
    if (builder.Configuration.GetValue<bool>("Quiz:ResetOnStartup"))
        await AssessmentSeeder.ResetQuizAsync(db);

    // Phase 10.2 — unconditional safety net: if the bank is empty for any reason
    // (a stale reset flag, a deploy that wiped without reseeding), restore the 5 demo
    // questions so /Quiz/Start never renders an empty card. Idempotent.
    await AssessmentSeeder.EnsureDemoQuizAsync(db);

    // Phase 6 — predefined department roster (default-checked attendees in stage 1).
    await RosterSeeder.RunAsync(db);

    // Phase 5 — one-time signature backfill: flip pending signature ink to Approved
    // and regenerate signed-map PDFs so existing maps show their signatures.
    var pdf = scope.ServiceProvider.GetRequiredService<StrategyMapPdfService>();
    await SignatureBackfill.RunAsync(db, pdf);
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
