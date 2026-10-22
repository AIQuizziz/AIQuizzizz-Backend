using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.Questions.Dtos;
using CleanArchitectureBase.Application.Tests.Service;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Tests;

/// <summary>
/// "Cập nhật theo bản gốc": chép nội dung hiện tại của câu gốc (bộ câu hỏi) vào câu tương ứng trong bài test.
/// Chỉ làm được trước giờ mở bài. Điểm của câu trong bài (TestQuestion.Score) giữ nguyên.
/// </summary>
[Authorize]
public class SyncTestQuestionWithSourceCommand : IRequest<QuestionResponseDto>
{
    public Guid TestId { get; set; }
    /// <summary>Id câu hỏi trong bài test (bản sao)</summary>
    public Guid QuestionId { get; set; }
}

public class SyncTestQuestionWithSourceCommandValidator : AbstractValidator<SyncTestQuestionWithSourceCommand>
{
    public SyncTestQuestionWithSourceCommandValidator()
    {
        RuleFor(x => x.TestId).NotEmpty().WithMessage("TestId không được để trống");
        RuleFor(x => x.QuestionId).NotEmpty().WithMessage("QuestionId không được để trống");
    }
}

public class SyncTestQuestionWithSourceCommandHandler : IRequestHandler<SyncTestQuestionWithSourceCommand, QuestionResponseDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITestService _testService;

    public SyncTestQuestionWithSourceCommandHandler(IApplicationDbContext context, ITestService testService)
    {
        _context = context;
        _testService = testService;
    }

    public async Task<QuestionResponseDto> Handle(SyncTestQuestionWithSourceCommand rq, CancellationToken cancellationToken)
    {
        var test = await _context.Tests
            .Where(x => x.Id == rq.TestId && x.IsDeleted == false)
            .FirstOrDefaultAsync(cancellationToken);
        if (test == null)
            throw new ErrorCodeException(ErrorCodes.TEST_NOT_FOUND, "Không tìm thấy test");

        if (!await _testService.CanViewOrEditTest(test.ClassId, cancellationToken))
            throw new ErrorCodeException(ErrorCodes.USER_NOT_HAVE_PERMISSION_IN_TEST, "Không có quyền sửa");

        if (test.TimeStart < DateTimeOffset.UtcNow
            || await _context.Attempts.AnyAsync(x => x.TestId == test.Id, cancellationToken))
            throw new ErrorCodeException(ErrorCodes.TEST_ALREADY_OPEN);

        var testQuestion = await _context.TestQuestions
            .Include(x => x.Question)
            .Where(x => x.TestId == test.Id && x.QuestionId == rq.QuestionId)
            .FirstOrDefaultAsync(cancellationToken);
        if (testQuestion == null)
            throw new ErrorCodeException(ErrorCodes.QUESTION_NOT_FOUND_TO_DELETE, "Câu hỏi không có trong bài test");

        var copy = testQuestion.Question!;
        if (copy.SourceQuestionId == null)
            throw new ErrorCodeException(ErrorCodes.SOURCE_QUESTION_NOT_FOUND, "Câu hỏi không có câu gốc");

        // Câu gốc phải còn và người dùng vẫn xem được bộ câu hỏi chứa nó
        Question source;
        try
        {
            source = (await _testService.GetSourceQuestionsForTest(new[] { copy.SourceQuestionId.Value }, cancellationToken))
                [copy.SourceQuestionId.Value];
        }
        catch (ErrorCodeException)
        {
            throw new ErrorCodeException(ErrorCodes.SOURCE_QUESTION_NOT_FOUND, "Câu gốc đã bị xoá hoặc không còn quyền truy cập");
        }

        // Dữ liệu cũ dùng chung với bài khác / bộ câu hỏi -> tách thành câu riêng của bài trước khi sửa
        var shared = copy.QuestionSetId != null
                     || await _context.TestQuestions.AnyAsync(x => x.QuestionId == copy.Id && x.TestId != test.Id, cancellationToken);
        if (shared)
        {
            var own = new Question { Id = Guid.NewGuid(), Type = source.Type, Score = copy.Score, Order = copy.Order };
            TestQuestionFactory.CopyContentFrom(own, source);
            _context.Questions.Add(own);

            _context.TestQuestions.Remove(testQuestion);
            _context.TestQuestions.Add(new TestQuestion
            {
                TestId = test.Id, QuestionId = own.Id, Score = testQuestion.Score, Order = testQuestion.Order
            });

            var versionRows = await _context.TestVersionQuestions
                .Where(x => x.TestVersion!.TestId == test.Id && x.QuestionId == copy.Id)
                .ToListAsync(cancellationToken);
            foreach (var row in versionRows)
                row.QuestionId = own.Id;

            copy = own;
        }
        else
        {
            TestQuestionFactory.CopyContentFrom(copy, source);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var dto = QuestionResponseDto.Mapper.FromEntity(copy, true, false, true);
        dto.Score = testQuestion.Score;
        dto.SourceQuestionId = source.Id;
        dto.SourceVersion = copy.SourceVersion;
        dto.LatestSourceVersion = source.Version;
        dto.SourceQuestionSetId = source.QuestionSetId;
        dto.SourceQuestionSetName = source.QuestionSet?.Name;
        return dto;
    }
}
