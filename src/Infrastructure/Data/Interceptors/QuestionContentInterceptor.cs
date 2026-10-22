using CleanArchitectureBase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitectureBase.Infrastructure.Data.Interceptors;

/// <summary>
/// Giữ ContentHash / Version của câu hỏi đúng ở mọi đường ghi (sửa bộ câu hỏi, nhập file, extension, bài test...):
///  - câu mới: tính ContentHash
///  - câu bị sửa: nội dung đổi (hash khác) thì cập nhật hash; câu thuộc bộ câu hỏi tăng Version
/// Bản sao trong bài test (QuestionSetId = null) không tăng Version: phiên bản nguồn nằm ở SourceVersion.
/// </summary>
public class QuestionContentInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateQuestions(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        UpdateQuestions(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public static void UpdateQuestions(DbContext? context)
    {
        if (context == null) return;

        foreach (var entry in context.ChangeTracker.Entries<Question>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            var question = entry.Entity;
            var hash = QuestionContent.Hash(question);
            if (hash == question.ContentHash) continue;

            // Câu cũ chưa có hash (trước khi có cơ chế version): chỉ ghi nhận hash, không biết nội dung trước đó để tăng version
            if (entry.State == EntityState.Modified && question.ContentHash != null && question.QuestionSetId != null)
                question.Version++;

            if (question.Version < 1)
                question.Version = 1;
            question.ContentHash = hash;
        }
    }
}
