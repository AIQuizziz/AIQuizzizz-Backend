using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.QuestionSets.Services;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Extension;

/// <summary>
/// Xoá một thẻ (nút Hoàn tác trên thông báo và nút Xoá trong Side Panel của extension). Chỉ xoá được câu hỏi loại LanguageFlashcard
/// mà người dùng có quyền sửa bộ chứa nó; không đụng tới các phần khác của bộ (mô tả, tag, câu hỏi khác).
/// </summary>
[Authorize]
public class UndoCaptureFlashcardCommand : IRequest
{
    public Guid QuestionId { get; set; }
}

public class UndoCaptureFlashcardCommandValidator : AbstractValidator<UndoCaptureFlashcardCommand>
{
    public UndoCaptureFlashcardCommandValidator()
    {
        RuleFor(x => x.QuestionId).NotEmpty();
    }
}

public class UndoCaptureFlashcardCommandHandler : IRequestHandler<UndoCaptureFlashcardCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly IQuestionSetService _questionSetService;

    public UndoCaptureFlashcardCommandHandler(
        IApplicationDbContext context, IUser user, IQuestionSetService questionSetService)
    {
        _context = context;
        _user = user;
        _questionSetService = questionSetService;
    }

    public async Task Handle(UndoCaptureFlashcardCommand request, CancellationToken cancellationToken)
    {
        var question = await _context.Questions
            .FirstOrDefaultAsync(q => q.Id == request.QuestionId && !q.IsDeleted && q.Type == QuestionType.LanguageFlashcard, cancellationToken)
            ?? throw new ErrorCodeException(ErrorCodes.COMMON_NOT_FOUND, "Không tìm thấy thẻ");

        if (question.QuestionSetId is not { } setId
            || !await _questionSetService.CanUserEditQuestionSet(_user.UserId!.Value, setId))
            throw new ErrorCodeException(ErrorCodes.COMMON_FORBIDDEN);

        var set = await _context.QuestionSets.FirstAsync(q => q.Id == setId, cancellationToken);
        question.IsDeleted = true;
        set.QuestionCount = Math.Max(0, set.QuestionCount - 1);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
