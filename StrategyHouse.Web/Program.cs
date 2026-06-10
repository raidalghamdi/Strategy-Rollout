using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Hubs;
using StrategyHouse.Web.Services;

var builder = WebApplication.CreateBuilder(args);

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
});

// MVC + Razor + SignalR
builder.Services.AddControllersWithViews()
    .AddRazorRuntimeCompilation();
builder.Services.AddSignalR();

// App services
builder.Services.AddScoped<EmailComposer>();
builder.Services.AddScoped<QrService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<JourneyMapService>();

var app = builder.Build();

// Seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
    await SeedData.RunAsync(db, userManager, roleManager);
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

app.MapHub<MapCanvasHub>("/hubs/canvas");

app.Run();
