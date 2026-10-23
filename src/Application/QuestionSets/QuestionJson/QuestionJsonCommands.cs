using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.Plans.Service;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.QuestionSets.Services;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.QuestionSets.QuestionJson;

/// <summary>
/// Xuất câu hỏi của bộ ra JSON công khai (<see cref="QuestionJsonFormat"/>). Cần quyền sửa bộ; người không phải chủ bộ
/// còn cần gói cho phép sao chép bộ câu hỏi (xuất rồi nhập lại tương đương sao chép). Ảnh / video không được xuất.
/// </summary>
[Authorize]
public class ExportQuestionSetJsonQuery : IRequest<QuestionJsonFile>
{
    public Guid QuestionSetId { get; set; }
}

public class ExportQuestionSetJsonQueryHandler : IRequestHandler<ExportQuestionSetJsonQuery, QuestionJsonFile>
{
    private readonly IApplicationDbContext _context;
    private readonly IQuestionSetService _questionSetService;
    private readonly IPlanService _planService;
    private readonly IUser _user;

    public ExportQuestionSetJsonQueryHandler(IApplicationDbContext context, IQuestionSetService questionSetService,
        IPlanService planService, IUser user)
    {
        _context = context;
        _questionSetService = questionSetService;
        _planService = planService;
        _user = user;
    }

    public async Task<QuestionJsonFile> Handle(ExportQuestionSetJsonQuery request, CancellationToken cancellationToken)
    {
        var userId = _user.UserId!.Value;
        var questionSet = await _questionSetService.GetActiveQuestionSet(request.QuestionSetId, cancellationToken)
                          ?? throw new ErrorCodeException(ErrorCodes.QUESTION_SET_NOT_FOUND);

        if (!await _questionSetService.CanUserEditQuestionSet(userId, questionSet.Id))
            throw new ErrorCodeException(ErrorCodes.COMMON_FORBIDDEN, "Bạn không có quyền xuất bộ câu hỏi này");

        var isOwner = await _context.QuestionSetUsers.AnyAsync(u =>
            u.QuestionSetId == questionSet.Id && u.UserId == userId && u.ShareMode == QuestionSetUserShareMode.Owner, cancellationToken);
        if (!isOwner && !await _planService.CanCopyOrImportQuestionSet(userId))
            throw new ErrorCodeException(ErrorCodes.PLAN_REQUIRE_PLAN, "Gói hiện tại không cho phép xuất bộ câu hỏi của người khác");

        var questions = await _context.Questions
            .Where(q => q.QuestionSetId == questionSet.Id && !q.IsDeleted)
            .OrderBy(x => x.Order).ThenBy(x => x.Created).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var tags = await _context.QuestionSetTags
            .Where(t => t.QuestionSetId == questionSet.Id && t.Tag != null)
            .Select(t => t.Tag!.Name)
            .ToListAsync(cancellationToken);

        return new QuestionJsonFile
        {
            ExportedAt = DateTimeOffset.UtcNow,
            QuestionSet = new QuestionSetJsonInfo
            {
                Name = questionSet.Name,
                Description = string.IsNullOrWhiteSpace(questionSet.Description) ? null : questionSet.Description,
                Tags = tags.Count == 0 ? null : tags
            },
            Questions = questions
                .Select(q => QuestionJsonFormat.FromDto(CreateUpdateQuestionDto.Deserializer.Deserialize(q)))
                .ToList()
        };
    }
}

public class ImportQuestionJsonResultDto
{
    /// <summary>Tên / mô tả / thẻ của bộ nếu JSON có (client dùng để điền sẵn khi tạo bộ mới)</summary>
    public QuestionSetJsonInfo? QuestionSet { get; set; }
    public int Total { get; set; }
    public List<ImportedJsonQuestionDto> ValidQuestions { get; set; } = new();
    public List<ImportedJsonQuestionDto> InvalidQuestions { get; set; } = new();
}

/// <summary>
/// Đọc + kiểm tra JSON câu hỏi (không lưu gì). Trả câu hợp lệ / không hợp lệ kèm lý do để người dùng chọn câu đưa vào
/// trang tạo / sửa bộ câu hỏi, giống nhập Excel. Quy tắc kiểm tra dùng chung với lúc lưu.
/// </summary>
[Authorize]
public class ImportQuestionJsonCommand : IRequest<ImportQuestionJsonResultDto>
{
    public string? Json { get; set; }
}

public class ImportQuestionJsonCommandValidator : AbstractValidator<ImportQuestionJsonCommand>
{
    public ImportQuestionJsonCommandValidator()
    {
        RuleFor(x => x.Json)
            .MaximumLength(QuestionJsonFormat.MaxJsonLength)
            .WithMessage($"Nội dung JSON quá lớn (tối đa {QuestionJsonFormat.MaxJsonLength / 1_000_000} triệu ký tự)");
    }
}

public class ImportQuestionJsonCommandHandler : IRequestHandler<ImportQuestionJsonCommand, ImportQuestionJsonResultDto>
{
    public Task<ImportQuestionJsonResultDto> Handle(ImportQuestionJsonCommand request, CancellationToken cancellationToken)
    {
        var parsed = QuestionJsonParser.Parse(request.Json);
        return Task.FromResult(new ImportQuestionJsonResultDto
        {
            QuestionSet = parsed.QuestionSet,
            Total = parsed.Questions.Count,
            ValidQuestions = parsed.Questions.Where(q => q.Errors.Count == 0).ToList(),
            InvalidQuestions = parsed.Questions.Where(q => q.Errors.Count > 0).ToList()
        });
    }
}
