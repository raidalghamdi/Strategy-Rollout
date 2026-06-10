using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;

namespace StrategyHouse.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<AppUser, IdentityRole<int>, int>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    // Framework engine
    public DbSet<Framework> Frameworks => Set<Framework>();
    public DbSet<FrameworkLayer> FrameworkLayers => Set<FrameworkLayer>();
    public DbSet<FrameworkElement> FrameworkElements => Set<FrameworkElement>();

    // Departments
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DepartmentProject> DepartmentProjects => Set<DepartmentProject>();
    public DbSet<DepartmentKpi> DepartmentKpis => Set<DepartmentKpi>();
    public DbSet<DepartmentRole> DepartmentRoles => Set<DepartmentRole>();

    // Sessions
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionDepartment> SessionDepartments => Set<SessionDepartment>();
    public DbSet<SessionAttendee> SessionAttendees => Set<SessionAttendee>();

    // Maps
    public DbSet<StrategyMap> StrategyMaps => Set<StrategyMap>();
    public DbSet<MapPlacement> MapPlacements => Set<MapPlacement>();
    public DbSet<MapCommitment> MapCommitments => Set<MapCommitment>();
    public DbSet<MapSignature> MapSignatures => Set<MapSignature>();
    public DbSet<CommitmentTemplate> CommitmentTemplates => Set<CommitmentTemplate>();

    // Surveys + Quiz + Baseline
    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<SurveyQuestion> SurveyQuestions => Set<SurveyQuestion>();
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();
    public DbSet<SurveyAnswer> SurveyAnswers => Set<SurveyAnswer>();
    public DbSet<BaselineResponse> BaselineResponses => Set<BaselineResponse>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizResponse> QuizResponses => Set<QuizResponse>();
    public DbSet<QuizAnswer> QuizAnswers => Set<QuizAnswer>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<FrameworkElement>()
            .HasOne(e => e.ParentElement)
            .WithMany()
            .HasForeignKey(e => e.ParentElementId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<MapPlacement>()
            .HasOne(p => p.FrameworkElement)
            .WithMany()
            .HasForeignKey(p => p.FrameworkElementId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<MapCommitment>()
            .HasOne(c => c.LinkedElement)
            .WithMany()
            .HasForeignKey(c => c.LinkedElementId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<DepartmentProject>()
            .HasOne(p => p.DefaultElement)
            .WithMany()
            .HasForeignKey(p => p.DefaultElementId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<DepartmentKpi>()
            .HasOne(k => k.DefaultElement)
            .WithMany()
            .HasForeignKey(k => k.DefaultElementId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<CommitmentTemplate>()
            .HasOne(c => c.SuggestedElement)
            .WithMany()
            .HasForeignKey(c => c.SuggestedElementId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<Session>().HasIndex(s => s.AccessCode).IsUnique();
        b.Entity<Framework>().HasIndex(f => f.IsActive);
    }
}
