// File: Data/Configurations/PlanConfiguration.cs

using CleanArchitectureBase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitectureBase.Infrastructure.Data.Configurations;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        //Cấu hình thuộc tính
        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Price)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(p => p.Duration)
            .IsRequired();
            
        builder.Property(p => p.Unit)
            .IsRequired();

        builder.Property(p => p.CanLearn)
            .IsRequired();

        builder.Property(p => p.CanOpenTest)
            .IsRequired();

        builder.Property(p => p.CanCopyOrImportQuestionSet)
            .IsRequired();

        builder.Property(p => p.IsDeleted)
            .HasDefaultValue(false);
    }
}


public class PlanPriceHistoryConfiguration : IEntityTypeConfiguration<PlanPriceHistory>
{
    public void Configure(EntityTypeBuilder<PlanPriceHistory> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .IsRequired();

        builder.Property(p => p.PlanId)
            .IsRequired();

        builder.Property(p => p.Price)
            .IsRequired();

        builder.Property(p => p.DateStart)
            .IsRequired();

        builder.Property(p => p.DateFinish);


        builder.HasOne(p => p.Plan)
            .WithMany(p => p.PriceHistories)
            .HasForeignKey(p => p.PlanId)
            .OnDelete(DeleteBehavior.Restrict); 

    }
}


//Cấu hình bảng trung gian giữa người dùng và Plan
public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.Property(us => us.DateStart)
            .IsRequired();

        builder.Property(us => us.DateFinish)
            .IsRequired();

        builder.Property(us => us.IsActive)
            .HasDefaultValue(false);

        builder.HasOne(us => us.User)
            .WithMany(u => u.UserSubscriptions)
            .HasForeignKey(us => us.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(us => us.Plan)
            .WithMany()
            .HasForeignKey(us => us.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.HasKey(q => q.Id);

        builder.Property(q => q.Type)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => (QuestionType)Enum.Parse(typeof(QuestionType), v));

        // builder.HasQueryFilter(x => !x.IsDeleted);

        builder.Property(q => q.QuestionText)
            .HasMaxLength(5000);

        builder.Property(q => q.ExplainText)
            .HasMaxLength(5000);

        builder.Property(q => q.TextFormat)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => (TextFormat)Enum.Parse(typeof(TextFormat), v, true));

        builder.Property(q => q.Score)
            .HasColumnType("numeric(5,2)")
            .HasDefaultValue(0);

        builder.Property(q => q.DataJson)
            .IsRequired()
            .HasColumnType("json");

        builder.Ignore(q => q.Data);

        builder.Property(q => q.MediaType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(q => q.Version)
            .HasDefaultValue(1);

        // SHA-256 hex (QuestionContent.Hash)
        builder.Property(q => q.ContentHash)
            .HasMaxLength(64);

        // Bản sao trong bài test -> câu gốc trong bộ câu hỏi (câu gốc chỉ xoá mềm nên không cascade)
        builder.HasOne(q => q.SourceQuestion)
            .WithMany()
            .HasForeignKey(q => q.SourceQuestionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(q => q.SourceQuestionId);

        //Một question thuộc về một QuestionSet
        builder.HasOne(q => q.QuestionSet)
            .WithMany(qs => qs.Questions)
            .HasForeignKey(q => q.QuestionSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuestionSetConfiguration : IEntityTypeConfiguration<QuestionSet>
{
    public void Configure(EntityTypeBuilder<QuestionSet> builder)
    {
        builder.Property(qs => qs.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(qs => qs.Description)
            .HasMaxLength(500);

        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.Property(qs => qs.VisibilityMode)
            .HasConversion(
                v => v.ToString(),
                v => (QuestionSetVisibilityMode)Enum.Parse(typeof(QuestionSetVisibilityMode), v));

        builder.Property(qs => qs.QuestionCount)
            .HasDefaultValue(0);

        //Một QuestionSet có nhiều question
        builder.HasMany(qs => qs.Questions)
            .WithOne(q => q.QuestionSet)
            .HasForeignKey(q => q.QuestionSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuestionSetRatingConfiguration : IEntityTypeConfiguration<QuestionSetRating>
{

    public void Configure(EntityTypeBuilder<QuestionSetRating> builder)
    {
        builder.HasKey(qsr => new
        {
            qsr.QuestionSetId,
            qsr.CreatedBy
        });
        
        builder.Property(qsr => qsr.Rating)
            .IsRequired();
            
            
        builder.HasOne(qsr => qsr.QuestionSet)
            .WithMany(qs => qs.QuestionSetRatings) 
            .HasForeignKey(qsr => qsr.QuestionSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}




public class QuestionSetTagConfiguration : IEntityTypeConfiguration<QuestionSetTag>
{
    public void Configure(EntityTypeBuilder<QuestionSetTag> builder)
    {
        //composite key
        builder.HasKey(qst => new
        {
            qst.TagId,
            qst.QuestionSetId
        });

        builder.HasOne(qst => qst.Tag)
            .WithMany(t => t.QuestionSetTags)
            .HasForeignKey(qst => qst.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(qst => qst.QuestionSet)
            .WithMany(qs => qs.QuestionSetTags)
            .HasForeignKey(qst => qst.QuestionSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(500);
    }
}

//Bảng trung gian giữa người dùng và bộ câu hỏi
public class QuestionSetUserConfiguration : IEntityTypeConfiguration<QuestionSetUser>
{

    public void Configure(EntityTypeBuilder<QuestionSetUser> builder)
    {
        builder.HasKey(qsu => new
        {
            qsu.UserId,
            qsu.QuestionSetId
        });

        builder.Property(qsu => qsu.ShareMode)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => (QuestionSetUserShareMode)Enum.Parse(typeof(QuestionSetUserShareMode), v));

        builder.HasOne(qsu => qsu.User)
            .WithMany(u => u.QuestionSetUsers)
            .HasForeignKey(qsu => qsu.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(qsu => qsu.QuestionSet)
            .WithMany(qs => qs.QuestionSetUsers)
            .HasForeignKey(qsu => qsu.QuestionSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserQuestionSetAccessHistoryConfigutation : IEntityTypeConfiguration<UserQuestionSetAccessHistory>
{

    public void Configure(EntityTypeBuilder<UserQuestionSetAccessHistory> builder)
    {
        builder.HasKey(qsu => new
        {
            qsu.UserId,
            qsu.QuestionSetId
        });
        
        builder.HasOne(x => x.User)
            .WithMany(u => u.AccessHistories)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.QuestionSet)
            .WithMany(qs => qs.AccessHistories)
            .HasForeignKey(x => x.QuestionSetId)
            .OnDelete(DeleteBehavior.Restrict);
        ;
    }
}

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Content)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(c => c.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasOne(c => c.Question)
            .WithMany(q => q.Comments)
            .HasForeignKey(c => c.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.CreatedByUser)
            .WithMany(u => u.Comments)
            .HasForeignKey(c => c.CreatedBy).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.ParentComment)
            .WithMany(c => c.ChildComments)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referencing relationship for comments
        builder.HasMany(c => c.ChildComments)
            .WithOne(c => c.ParentComment)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class UserQuestionSetHistoryConfiguration : IEntityTypeConfiguration<UserQuestionSetHistory>
{
    public void Configure(EntityTypeBuilder<UserQuestionSetHistory> builder)
    {
        builder.HasKey(h => new { h.UserId, h.QuestionId }); 

        builder.Property(uqsh => uqsh.IsCorrect)
            .IsRequired();

        builder.HasOne(uqsh => uqsh.User)
            .WithMany(u => u.UserQuestionSetHistories)
            .HasForeignKey(uqsh => uqsh.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(uqsh => uqsh.Question)
            .WithMany(q => q.UserQuestionSetHistories)
            .HasForeignKey(uqsh => uqsh.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.Property(f => f.Name)
            .IsRequired()
            .HasMaxLength(200);
    }
}

public class FolderQuestionSetConfiguration : IEntityTypeConfiguration<FolderQuestionSet>
{
    public void Configure(EntityTypeBuilder<FolderQuestionSet> builder)
    {
        builder.HasKey(fqs => new
        {
            fqs.FolderId,
            fqs.QuestionSetId
        });

        builder.HasOne(fqs => fqs.Folder)
            .WithMany(f => f.FolderQuestionSets)
            .HasForeignKey(fqs => fqs.FolderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(fqs => fqs.QuestionSet)
            .WithMany(qs => qs.FolderQuestionSets)
            .HasForeignKey(fqs => fqs.QuestionSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
public class FolderUserConfiguration : IEntityTypeConfiguration<FolderUser>
{
    public void Configure(EntityTypeBuilder<FolderUser> builder)
    {
        builder.HasKey(fu => new
        {
            fu.UserId,
            fu.FolderId
        });

        builder.Property(fu => fu.ShareMode)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => (FolderShareMode)Enum.Parse(typeof(FolderShareMode), v));

        builder.HasOne(fu => fu.User)
            .WithMany(u => u.FolderUsers)
            .HasForeignKey(fu => fu.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(fu => fu.Folder)
            .WithMany(f => f.FolderUsers)
            .HasForeignKey(fu => fu.FolderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TestConfiguration : IEntityTypeConfiguration<Test>
{
    public void Configure(EntityTypeBuilder<Test> builder)
    {
        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.TimeStart)
            .IsRequired();

        builder.Property(t => t.TimeFinish)
            .IsRequired();

        builder.Property(t => t.TimeLimit)
            .IsRequired();
        builder.Property(t => t.QuestionCount)
            .IsRequired();

        builder.Property(t => t.PassingScore)
            .IsRequired()
            .HasColumnType("numeric(5,2)");

        builder.Property(t => t.GradeAttemptMethod)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => (GradeAttemptMethod)Enum.Parse(typeof(GradeAttemptMethod), v));

        builder.Property(t => t.GradeQuestionMethod)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => (GradeQuestionMethod)Enum.Parse(typeof(GradeQuestionMethod), v));

        builder.Property(t => t.ClassId)
            .IsRequired();

        builder.HasOne(t => t.Class)
            .WithMany(c => c.Tests)
            .HasForeignKey(t => t.ClassId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(t => t.IsShowCorrectAnswerInReview)
            .HasDefaultValue(false);

        builder.Property(t => t.IsDeleted)
            .HasDefaultValue(false);
    }
}

public class TestVersionConfiguration : IEntityTypeConfiguration<TestVersion>
{
    public void Configure(EntityTypeBuilder<TestVersion> builder)
    {
        builder.Property(tv => tv.No)
            .IsRequired();

        builder.HasOne(tv => tv.Test)
            .WithMany(t => t.TestVersions)
            .HasForeignKey(tv => tv.TestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TestQuestionConfiguration : IEntityTypeConfiguration<TestQuestion>
{
    public void Configure(EntityTypeBuilder<TestQuestion> builder)
    {
        builder.HasKey(tq => tq.Id);

        // cùng kiểu với Question.Score
        builder.Property(tq => tq.Score)
            .HasColumnType("numeric(5,2)")
            .HasDefaultValue(0);

        builder.Property(tq => tq.Order)
            .IsRequired();

        // mỗi câu hỏi chỉ xuất hiện một lần trong bản gốc của một bài test
        builder.HasIndex(tq => new { tq.TestId, tq.QuestionId }).IsUnique();

        builder.HasOne(tq => tq.Test)
            .WithMany(t => t.TestQuestions)
            .HasForeignKey(tq => tq.TestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(tq => tq.Question)
            .WithMany(q => q.TestQuestions)
            .HasForeignKey(tq => tq.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TestVersionQuestionConfiguration : IEntityTypeConfiguration<TestVersionQuestion>
{
    public void Configure(EntityTypeBuilder<TestVersionQuestion> builder)
    {
        builder.HasKey(tvq => tvq.Id);

        builder.Property(tvq => tvq.Order)
            .IsRequired();

        builder.HasOne(tvq => tvq.TestVersion)
            .WithMany(tv => tv.TestVersionQuestions)
            .HasForeignKey(tvq => tvq.TestVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(tvq => tvq.Question)
            .WithMany(q => q.TestVersionQuestions)
            .HasForeignKey(tvq => tvq.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AttemptConfiguration : IEntityTypeConfiguration<Attempt>
{
    public void Configure(EntityTypeBuilder<Attempt> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.TimeStart)
            .IsRequired();

        builder.Property(a => a.TimeFinish)
            .IsRequired();

        builder.Property(a => a.Score)
            .IsRequired()
            .HasColumnType("numeric(9,2)");

        builder.HasOne(a => a.Test)
            .WithMany(t => t.Attempts)
            .HasForeignKey(a => a.TestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.TestVersion)
            .WithMany(tv => tv.Attempts)
            .HasForeignKey(a => a.TestVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.User)
            .WithMany(u => u.Attempts)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AttemptQuestionConfiguration : IEntityTypeConfiguration<AttemptQuestion>
{
    public void Configure(EntityTypeBuilder<AttemptQuestion> builder)
    {
        builder.HasKey(aq => aq.Id);

        builder.Property(aq => aq.Order)
            .HasDefaultValue(0);

        builder.Property(aq => aq.Score)
            .HasColumnType("numeric(9,2)");

        // 1 câu hỏi chỉ có 1 bản ghi trả lời trong 1 attempt (chặn trùng khi autosave song song)
        builder.HasIndex(aq => new { aq.AttemptId, aq.QuestionId }).IsUnique();

        builder.Property(aq => aq.DataJson)
            .IsRequired()
            .HasColumnType("json");

        builder.Ignore(aq => aq.Data);

        builder.HasOne(aq => aq.Attempt)
            .WithMany(a => a.AttemptQuestions)
            .HasForeignKey(aq => aq.AttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(aq => aq.Question)
            .WithMany(q => q.AttemptQuestions)
            .HasForeignKey(aq => aq.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ClassConfiguration : IEntityTypeConfiguration<Class>
{
    public void Configure(EntityTypeBuilder<Class> builder)
    {
        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasMany(c => c.ClassQuestionSets)
            .WithOne(cqs => cqs.Class)
            .HasForeignKey(cqs => cqs.ClassId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ClassUserConfiguration : IEntityTypeConfiguration<ClassUser>
{
    public void Configure(EntityTypeBuilder<ClassUser> builder)
    {
        builder.HasKey(cu => new
        {
            cu.ClassId,
            cu.UserId
        });

        builder.Property(cu => cu.ShareMode)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => (ClassShareMode)Enum.Parse(typeof(ClassShareMode), v));

        builder.HasOne(cu => cu.Class)
            .WithMany(c => c.ClassUsers)
            .HasForeignKey(cu => cu.ClassId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cu => cu.User)
            .WithMany(u => u.ClassUsers)
            .HasForeignKey(cu => cu.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ClassQuestionSetConfiguration : IEntityTypeConfiguration<ClassQuestionSet>
{
    public void Configure(EntityTypeBuilder<ClassQuestionSet> builder)
    {
        builder.HasKey(cqs => new
        {
            cqs.ClassId,
            cqs.QuestionSetId
        });

        builder.HasOne(cqs => cqs.Class)
            .WithMany(c => c.ClassQuestionSets)
            .HasForeignKey(cqs => cqs.ClassId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cqs => cqs.QuestionSet)
            .WithMany(qs => qs.ClassQuestionSets)
            .HasForeignKey(cqs => cqs.QuestionSetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ClassInvitationConfiguration : IEntityTypeConfiguration<ClassInvitation>
{
    public void Configure(EntityTypeBuilder<ClassInvitation> builder)
    {
        builder.Property(ci => ci.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(ci => ci.TimeStart)
            .IsRequired();

        builder.Property(ci => ci.TimeEnd)
            .IsRequired();

        builder.Property(ci => ci.IsDeleted)
            .HasDefaultValue(false);

        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasOne(ci => ci.Class)
            .WithMany(c => c.ClassInvitations)
            .HasForeignKey(ci => ci.ClassId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ClassInvitationUserConfiguration : IEntityTypeConfiguration<ClassInvitationUser>
{
    public void Configure(EntityTypeBuilder<ClassInvitationUser> builder)
    {
        builder.Property(ciu => ciu.TimeJoin)
            .IsRequired();

        builder.HasOne(ciu => ciu.User)
            .WithMany(u => u.ClassInvitationUsers)
            .HasForeignKey(ciu => ciu.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ciu => ciu.ClassInvitation)
            .WithMany()
            .HasForeignKey(ciu => ciu.ClassInvitationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TestGradeConfiguration : IEntityTypeConfiguration<TestGrade>
{

    public void Configure(EntityTypeBuilder<TestGrade> builder)
    {
        builder.HasKey(tg => tg.Id);

        builder.Property(tg => tg.TestId).IsRequired();
        builder.Property(tg => tg.UserId).IsRequired();
        builder.Property(tg => tg.Score).HasColumnType("numeric(9,2)").IsRequired();

        // mỗi user chỉ có 1 điểm tổng / test
        builder.HasIndex(tg => new { tg.UserId, tg.TestId }).IsUnique();

        //Nối với bảng Test
        builder.HasOne(tg => tg.Test)
            .WithMany(t => t.TestGrades) //Navigation nối ngược từ Test về TestGrade
            .HasForeignKey(tg => tg.TestId)
            .OnDelete(DeleteBehavior.Cascade);

        //Nối với bảng User
        builder.HasOne(tg => tg.User)
            .WithMany(u => u.TestGrades) // nếu User không có navigation property ngược
            .HasForeignKey(tg => tg.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}


public class TransactionConfigutation : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(t => t.Id);
        
        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict); 
            
        builder.HasIndex(t => t.PaymentId).IsUnique();
    }
}

public class SystemSettingConfigutation : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.HasKey(t => t.Id);
    }
}



