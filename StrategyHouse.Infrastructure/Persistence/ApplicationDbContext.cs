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
    public DbSet<TeamValueSelection> TeamValueSelections => Set<TeamValueSelection>(); // Phase 16
    public DbSet<DepartmentStrategyMap> DepartmentStrategyMaps => Set<DepartmentStrategyMap>();
    public DbSet<MapInkAsset> MapInkAssets => Set<MapInkAsset>();
    public DbSet<ModerationAuditLog> ModerationAuditLogs => Set<ModerationAuditLog>();

    // Phase 4 — Assessment (quiz bank + surveys)
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<SurveyQuestion> SurveyQuestions => Set<SurveyQuestion>();
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();

    // Phase 12 — open-text categorisation for the official survey
    public DbSet<SurveyQuestionCategory> SurveyQuestionCategories => Set<SurveyQuestionCategory>();
    public DbSet<OpenTextCategoryAssignment> OpenTextCategoryAssignments => Set<OpenTextCategoryAssignment>();

    // Phase 6 — predefined roster + chatbot conversation log
    public DbSet<DepartmentRoster> DepartmentRoster => Set<DepartmentRoster>();
    public DbSet<ChatbotConversation> ChatbotConversations => Set<ChatbotConversation>();

    // Phase 9 — mini CMS for admin-editable page text
    public DbSet<PageContent> PageContents => Set<PageContent>();

    // Phase 18 — redesigned journey capture tables (opening reflection + role contribution)
    public DbSet<OpeningReflection> OpeningReflections => Set<OpeningReflection>();
    public DbSet<RoleContribution> RoleContributions => Set<RoleContribution>();

    // Phase 20 — journey administration audit trail + selective stage-reset log
    public DbSet<JourneyAuditLog> JourneyAuditLogs => Set<JourneyAuditLog>();
    public DbSet<JourneyStageReset> JourneyStageResets => Set<JourneyStageReset>();

    // Phase 19.5 — local SQLite mirror of the external MSSQL strategy warehouse,
    // populated by the admin push action and read as a fallback when MSSQL is down.
    public DbSet<MirrorPillar> MirrorPillars => Set<MirrorPillar>();
    public DbSet<MirrorObjective> MirrorObjectives => Set<MirrorObjective>();
    public DbSet<MirrorKpi> MirrorKpis => Set<MirrorKpi>();
    public DbSet<MirrorInitiative> MirrorInitiatives => Set<MirrorInitiative>();
    public DbSet<MirrorProject> MirrorProjects => Set<MirrorProject>();
    public DbSet<MirrorMetadata> MirrorMetadata => Set<MirrorMetadata>();

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

        // Phase 19.24 — Department FK on Projects + KPIs.
        // The Department_Code column already exists in SQLite and is fully populated
        // (220/220 Projects, 134/134 KPIs, 0 orphans). This relationship configuration
        // only teaches EF how to JOIN; it does NOT change the database schema.
        // No HasIndex() because that would require a migration.
        b.Entity<Project>()
            .HasOne(p => p.Department)
            .WithMany()
            .HasForeignKey(p => p.DepartmentCode)
            .HasPrincipalKey(d => d.DeptCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        b.Entity<Kpi>()
            .HasOne(k => k.Department)
            .WithMany()
            .HasForeignKey(k => k.DepartmentCode)
            .HasPrincipalKey(d => d.DeptCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

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

        // Phase 16 — team value selection. No cascade; keep history like other journey rows.
        b.Entity<TeamValueSelection>()
            .HasOne(t => t.Session)
            .WithMany()
            .HasForeignKey(t => t.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<TeamValueSelection>().HasIndex(t => t.SessionId);

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

        // Phase 12 — categorisation tables. Cascade from question (definitions) and
        // response (data) so a reseed/wipe cleans up cleanly.
        b.Entity<SurveyQuestionCategory>()
            .HasOne(c => c.SurveyQuestion)
            .WithMany(q => q.Categories)
            .HasForeignKey(c => c.SurveyQuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<OpenTextCategoryAssignment>()
            .HasOne(a => a.SurveyResponse)
            .WithMany()
            .HasForeignKey(a => a.SurveyResponseId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<OpenTextCategoryAssignment>()
            .HasOne(a => a.SurveyQuestion)
            .WithMany()
            .HasForeignKey(a => a.SurveyQuestionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<OpenTextCategoryAssignment>()
            .HasIndex(a => new { a.SurveyResponseId, a.SurveyQuestionId }).IsUnique();

        b.Entity<Survey>().HasIndex(s => s.PublicToken).IsUnique();
        b.Entity<QuizQuestion>().HasIndex(q => new { q.Scope, q.IsApproved, q.IsActive });

        // Phase 6
        b.Entity<DepartmentRoster>().HasIndex(r => new { r.DeptCode, r.IsActive });
        b.Entity<ChatbotConversation>().HasIndex(c => c.AskedAt);

        // Phase 18 — index the redesigned-journey capture tables by session for fast reads.
        b.Entity<OpeningReflection>().HasIndex(r => r.SessionId);
        b.Entity<RoleContribution>().HasIndex(r => r.SessionId);

        // Phase 20 — StrategySession owner FK (test-data scoping). No cascade: deleting a
        // user must never delete journey history. Optional; legacy rows have null owner.
        b.Entity<StrategySession>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(s => s.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Phase 20 — audit + reset logs, indexed for the admin filters.
        b.Entity<JourneyAuditLog>().HasIndex(a => a.CreatedAt);
        b.Entity<JourneyAuditLog>().HasIndex(a => a.ActionType);
        b.Entity<JourneyStageReset>().HasIndex(r => r.DeptCode);

        // Phase 19.5 — index mirror tables by their warehouse codes for chain lookups.
        b.Entity<MirrorPillar>().HasIndex(p => p.PlrCode);
        b.Entity<MirrorObjective>().HasIndex(o => o.ObjectiveCode);
        b.Entity<MirrorObjective>().HasIndex(o => o.PlrCode);
        b.Entity<MirrorKpi>().HasIndex(k => k.KpiCode);
        b.Entity<MirrorKpi>().HasIndex(k => k.ObjectiveCode);
        b.Entity<MirrorInitiative>().HasIndex(i => i.InitiativeCode);
        b.Entity<MirrorInitiative>().HasIndex(i => i.ObjectiveCode);
        b.Entity<MirrorProject>().HasIndex(p => p.ProjectCode);
        b.Entity<MirrorProject>().HasIndex(p => p.InitiativeCode);
    }
}
