using System.Reflection;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Domain.Common;
using CleanArchitectureBase.Domain.Entities;
using CleanArchitectureBase.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitectureBase.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<UserAccount,
    ApplicationRole,
    Guid,
    ApplicationUserClaim,
    ApplicationUserRole,
    ApplicationUserLogin,
    ApplicationRoleClaim,
    ApplicationUserToken
>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<TodoList> TodoLists => Set<TodoList>();

    public DbSet<TodoItem> TodoItems => Set<TodoItem>();

    public DbSet<RefreshToken> RefreshTokens { get; set; }

    public DbSet<User> DomainUsers => Set<User>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<UserSubscription> UserSubscriptions => Set<UserSubscription>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionSet> QuestionSets => Set<QuestionSet>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<UserQuestionSetHistory> UserQuestionSetHistories => Set<UserQuestionSetHistory>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FolderQuestionSet> FolderQuestionSets => Set<FolderQuestionSet>();
    public DbSet<FolderUser> FolderUsers => Set<FolderUser>();
    public DbSet<Test> Tests => Set<Test>();
    public DbSet<TestVersion> TestVersions => Set<TestVersion>();
    public DbSet<TestQuestion> TestQuestions => Set<TestQuestion>();
    public DbSet<TestVersionQuestion> TestVersionQuestions => Set<TestVersionQuestion>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptQuestion> AttemptQuestions => Set<AttemptQuestion>();
    public DbSet<Class> Classes => Set<Class>();
    public DbSet<ClassUser> ClassUsers => Set<ClassUser>();
    public DbSet<ClassQuestionSet> ClassQuestionSets => Set<ClassQuestionSet>();
    public DbSet<ClassInvitation> ClassInvitations => Set<ClassInvitation>();
    public DbSet<ClassInvitationUser> ClassInvitationUsers => Set<ClassInvitationUser>();
    public DbSet<TestGrade> TestGrades => Set<TestGrade>();
    public DbSet<QuestionSetUser> QuestionSetUsers => Set<QuestionSetUser>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<QuestionSetTag> QuestionSetTags => Set<QuestionSetTag>();
    public DbSet<UserQuestionSetAccessHistory> UserQuestionSetAccessHistories => Set<UserQuestionSetAccessHistory>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<PlanPriceHistory> PlanPriceHistories => Set<PlanPriceHistory>();
    public DbSet<QuestionSetRating> QuestionSetRatings => Set<QuestionSetRating>();
    public DbSet<SystemSetting> SystemSettings  => Set<SystemSetting>();
    public DbSet<Media> Media => Set<Media>();
    public DbSet<UserViolation> UserViolations => Set<UserViolation>();
    public DbSet<ExtensionSetting> ExtensionSettings => Set<ExtensionSetting>();

    // public override DbSet<ApplicationUserRole> UserRoles { get; set; }
    // public override DbSet<ApplicationRole> Roles { get; set; }
    // public override DbSet<ApplicationUserClaim> UserClaims { get; set; }
    // public override DbSet<ApplicationUserLogin> UserLogins { get; set; }
    // public override DbSet<ApplicationUserToken> UserTokens { get; set; }
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(CleanArchitectureBase.Domain.Common.BaseAuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                builder.Entity(entityType.ClrType)
                    .HasOne(typeof(User), nameof(BaseAuditableEntity.CreatedByUser))
                    .WithMany()
                    .HasForeignKey(nameof(BaseAuditableEntity.CreatedBy))
                    .OnDelete(DeleteBehavior.Restrict);
            }
        }

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
