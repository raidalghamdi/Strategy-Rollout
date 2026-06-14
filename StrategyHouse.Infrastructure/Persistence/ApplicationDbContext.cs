using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;

namespace StrategyHouse.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<AppUser, IdentityRole<int>, int>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    // Strict strategy schema — Pillars → Objectives → Kpis / Initiatives → Projects (+ Departments lookup)
    public DbSet<Pillar> Pillars => Set<Pillar>();
    public DbSet<Objective> Objectives => Set<Objective>();
    public DbSet<Kpi> Kpis => Set<Kpi>();
    public DbSet<Initiative> Initiatives => Set<Initiative>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Department> Departments => Set<Department>();

    // Phase 1 — Department Strategy Journey
    public DbSet<DepartmentAccessCode> DepartmentAccessCodes => Set<DepartmentAccessCode>();
    public DbSet<StrategySession> StrategySessions => Set<StrategySession>();
    public DbSet<SessionMember> SessionMembers => Set<SessionMember>();
    public DbSet<ContributionPledge> ContributionPledges => Set<ContributionPledge>();
    public DbSet<DepartmentStrategyMap> DepartmentStrategyMaps => Set<DepartmentStrategyMap>();
    public DbSet<MapInkAsset> MapInkAssets => Set<MapInkAsset>();
    public DbSet<ModerationAuditLog> ModerationAuditLogs => Set<ModerationAuditLog>();

    // Phase 4 — Assessment (quiz bank + surveys)
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<SurveyQuestion> SurveyQuestions => Set<SurveyQuestion>();
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();

    // Phase 6 — predefined roster + chatbot conversation log
    public DbSet<DepartmentRoster> DepartmentRoster => Set<DepartmentRoster>();
    public DbSet<ChatbotConversation> ChatbotConversations => Set<ChatbotConversation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Data annotations carry FK/column config. Relax delete behaviour on the
        // denormalised PLR_Code shortcuts so they don't clash with the primary chain.
        b.Entity<Kpi>()
            .HasOne(k => k.Pillar)
            .WithMany(p => p.Kpis)
            .HasForeignKey(k => k.PlrCode)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Kpi>()
            .HasOne(k => k.Objective)
            .WithMany(o => o.Kpis)
            .HasForeignKey(k => k.ObjectiveCode)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Project>()
            .HasOne(p => p.Pillar)
            .WithMany(pl => pl.Projects)
            .HasForeignKey(p => p.PlrCode)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Project>()
            .HasOne(p => p.Initiative)
            .WithMany(i => i.Projects)
            .HasForeignKey(p => p.InitiativeCode)
            .OnDelete(DeleteBehavior.Restrict);

        // Phase 1 journey FKs — no cascade deletes, keep all history.
        b.Entity<SessionMember>()
            .HasOne(m => m.Session)
            .WithMany(s => s.Members)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<ContributionPledge>()
            .HasOne(p => p.Session)
            .WithMany()
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<DepartmentStrategyMap>()
            .HasOne(m => m.Session)
            .WithMany()
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<MapInkAsset>()
            .HasOne(a => a.Map)
            .WithMany()
            .HasForeignKey(a => a.MapId)
            .OnDelete(DeleteBehavior.Restrict);

        // Phase 3 — index ink assets by the member who authored a signature.
        b.Entity<MapInkAsset>().HasIndex(a => a.MemberId);

        // Phase 4 — Assessment FKs. No cascade deletes; preserve all responses.
        b.Entity<SurveyQuestion>()
            .HasOne(q => q.Survey)
            .WithMany(s => s.Questions)
            .HasForeignKey(q => q.SurveyId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<SurveyResponse>()
            .HasOne(r => r.Survey)
            .WithMany()
            .HasForeignKey(r => r.SurveyId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Survey>().HasIndex(s => s.PublicToken).IsUnique();
        b.Entity<QuizQuestion>().HasIndex(q => new { q.Scope, q.IsApproved, q.IsActive });

        // Phase 6
        b.Entity<DepartmentRoster>().HasIndex(r => new { r.DeptCode, r.IsActive });
        b.Entity<ChatbotConversation>().HasIndex(c => c.AskedAt);
    }
}
