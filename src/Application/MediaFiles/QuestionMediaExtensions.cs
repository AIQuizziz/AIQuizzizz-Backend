using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.MediaFiles;

public static class QuestionMediaExtensions
{
    /// <summary>
    /// Kiểm tra các MediaId client gửi kèm câu hỏi và trả về map MediaId -> MediaType.
    /// Được phép gắn media nếu: là chủ media, HOẶC media đang nằm trong 1 câu hỏi mà user có quyền xem
    /// (question set public/được share, test template được share) - trường hợp copy câu hỏi.
    /// Chặn việc lấy MediaId của người khác để tự sinh presigned URL.
    /// </summary>
    public static async Task<Dictionary<Guid, MediaType>> ResolveQuestionMediaAsync(
        this IApplicationDbContext context,
        IEnumerable<CreateUpdateQuestionDto> questions,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var mediaIds = questions
            .Where(q => q.MediaId.HasValue)
            .Select(q => q.MediaId!.Value)
            .Distinct()
            .ToList();

        if (mediaIds.Count == 0)
            return new Dictionary<Guid, MediaType>();

        var medias = await context.Media
            .Where(m => mediaIds.Contains(m.Id)
                        && !m.IsDeleted
                        && m.ModerationStatus != MediaModerationStatus.Violated)
            .Select(m => new { m.Id, m.Type, m.OwnerId })
            .ToListAsync(cancellationToken);

        var notOwned = medias.Where(m => m.OwnerId != userId).Select(m => m.Id).ToList();

        var accessibleViaQuestion = notOwned.Count == 0
            ? new List<Guid>()
            : await context.Questions
                .Where(q => q.MediaId.HasValue && notOwned.Contains(q.MediaId.Value) && !q.IsDeleted)
                .Where(q =>
                    (q.QuestionSet != null && !q.QuestionSet.IsDeleted &&
                     (q.QuestionSet.VisibilityMode == QuestionSetVisibilityMode.Public ||
                      q.QuestionSet.QuestionSetUsers.Any(u => u.UserId == userId) ||
                      q.QuestionSet.FolderQuestionSets.Any(f => f.Folder != null && !f.Folder.IsDeleted &&
                                                                 f.Folder.FolderUsers.Any(u => u.UserId == userId))))
                    // Bản sao trong bài test mình quản lý: giữ được ảnh/video của câu gốc dù bộ gốc đã đổi quyền hoặc bị xoá
                    || q.TestQuestions.Any(t => t.Test != null && !t.Test.IsDeleted &&
                                                t.Test.Class!.ClassUsers.Any(u => u.UserId == userId &&
                                                    (u.ShareMode == ClassShareMode.Owner || u.ShareMode == ClassShareMode.Teacher))))
                .Select(q => q.MediaId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

        var allowed = medias
            .Where(m => m.OwnerId == userId || accessibleViaQuestion.Contains(m.Id))
            .ToDictionary(m => m.Id, m => m.Type);

        var invalid = mediaIds.Where(id => !allowed.ContainsKey(id)).ToList();
        if (invalid.Count > 0)
            throw new ErrorCodeException(ErrorCodes.MEDIA_NOT_FOUND,
                $"Media không tồn tại hoặc không có quyền sử dụng: {string.Join(", ", invalid)}");

        return allowed;
    }

    /// <summary>Gán MediaId/MediaType từ dto vào entity câu hỏi (null nếu không có media)</summary>
    public static void ApplyMedia(this Question question, CreateUpdateQuestionDto dto, IReadOnlyDictionary<Guid, MediaType> mediaMap)
    {
        if (dto.MediaId.HasValue && mediaMap.TryGetValue(dto.MediaId.Value, out var type))
        {
            question.MediaId = dto.MediaId;
            question.MediaType = type;
        }
        else
        {
            question.MediaId = null;
            question.MediaType = null;
        }
    }
}
