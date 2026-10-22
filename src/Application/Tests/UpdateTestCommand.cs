using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.Tests.Service;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Tests;

public class CheckUpdateQuestion
{
    public List<Guid> NotUpdateQuestionIds { get; set; } = new();
    public List<Guid> UpdateQuestionIds { get; set; } = new();
}

[Authorize]
public class UpdateTestCommand : IRequest<Guid>
{
    public Guid TestId { get; set; }
    public required string Name { get; set; }
    public required int TimeLimit { get; set; }
    public required DateTime StartTime { get; set; }
    public required DateTime EndTime { get; set; }
    public required string GradeAttemptMethod { get; set; }
    public required string GradeQuestionMethod { get; set; }
    public bool IsShowCorrectAnswerInReview { get; set; }
    public bool IsAllowReviewAfterSubmit { get; set; }
    public int MaxAttempt { get; set; } = 1;
    public int NumberOfShuffles { get; set; } = 1;
    public int PassingScore { get; set; } = 0;
    public List<CreateUpdateQuestionDto> CreateUpdateQuestions { get; set; } = new ();
    public List<Guid> DeleteQuestionIds { get; set; } = new();
}

public class UpdateTestCommandValidator : AbstractValidator<UpdateTestCommand>
{
    public UpdateTestCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên bài kiểm tra không được để trống")
            .MaximumLength(200).WithMessage("Tên bài kiểm tra không được vượt quá 200 ký tự");

        RuleFor(x => x.TestId)
            .NotEmpty().WithMessage("TestId không được để trống");
        
        RuleFor(x => x.NumberOfShuffles)
            .GreaterThan(0).WithMessage("NumberOfShuffles > 0");

        RuleFor(x => x.TimeLimit)
            .GreaterThan(0).WithMessage("Thời gian làm bài phải lớn hơn 0")
            .LessThanOrEqualTo(180).WithMessage("Thời gian làm bài không được vượt quá 180 phút");  

        RuleFor(x => x.StartTime)
            .NotEmpty().WithMessage("Thời gian bắt đầu không được để trống")
            .Must(startTime => startTime > DateTime.UtcNow)
            .WithMessage("Thời gian bắt đầu phải lớn hơn thời gian hiện tại");

        RuleFor(x => x.EndTime)
            .NotEmpty().WithMessage("Thời gian kết thúc không được để trống")
            .Must((command, endTime) => endTime > command.StartTime)
            .WithMessage("Thời gian kết thúc phải lớn hơn thời gian bắt đầu");
        
        RuleFor(x => x.GradeAttemptMethod)
            .NotEmpty().WithMessage($"GradeAttemptMethod không được để trống")
            .Must(type => new[] {"LastAttempt", "HighestScore"}.Contains(type))
            .WithMessage($"Loại câu hỏi phải là LastAttempt, HighestScore");
        
        RuleFor(x => x.GradeQuestionMethod)
            .NotEmpty().WithMessage($"GradeAttemptMethod không được để trống")
            .Must(type => new[] {"Partial", "AllOrNothing"}.Contains(type))
            .WithMessage($"Loại câu hỏi phải là Partial, AllOrNothing");
        
        RuleFor(x => x.CreateUpdateQuestions)
            .Must(q => q != null && q.Count <= 100)
            .WithMessage("Bộ test không được vượt quá 100 câu");
        
        RuleFor(x => x.CreateUpdateQuestions)
            .Must(TestQuestionRules.HasDistinctQuestionIds)
            .WithMessage("Một câu hỏi chỉ được chọn một lần trong bài kiểm tra");

        RuleForEach(x => x.CreateUpdateQuestions)
            .SetValidator((command, question) => new CreateUpdateQuestionDto.QuestionCreateDtoValidator());
    }
}

public class UpdateTestCommandHandler : IRequestHandler<UpdateTestCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ITestService _testService;
    private readonly Random _random = new();
    private readonly IUser _user;
    
    public UpdateTestCommandHandler(
        IApplicationDbContext context,
        ITestService testService,
        IUser user)
    {
        _context = context;
        _testService = testService;
        _user = user;
    }
    
    public async Task<Guid> Handle(UpdateTestCommand rq, CancellationToken cancellationToken)
    {
        var test = await _context.Tests
            .Where(x => x.Id == rq.TestId && x.IsDeleted == false)
            .FirstOrDefaultAsync(cancellationToken);
        if (test == null)
            throw new ErrorCodeException(ErrorCodes.TEST_NOT_FOUND, "Không tìm thấy test");
            
        var canUpdate = await _testService.CanViewOrEditTest(test.ClassId, cancellationToken);
        if (!canUpdate)
            throw new ErrorCodeException(ErrorCodes.USER_NOT_HAVE_PERMISSION_IN_TEST, "Không có quyền sửa");

        if (test.TimeStart < DateTimeOffset.UtcNow)
            throw new ErrorCodeException(ErrorCodes.TEST_ALREADY_OPEN);
        
        test.Name = rq.Name;
        test.TimeFinish = rq.EndTime;
        test.TimeStart = rq.StartTime;
        test.TimeLimit = rq.TimeLimit;
        test.MaxAttempt = rq.MaxAttempt;
        test.PassingScore = rq.PassingScore;
        test.IsShowCorrectAnswerInReview = rq.IsShowCorrectAnswerInReview;
        test.IsAllowReviewAfterSubmit = rq.IsAllowReviewAfterSubmit;
        test.GradeAttemptMethod = Enum.Parse<GradeAttemptMethod>(rq.GradeAttemptMethod);
        test.GradeQuestionMethod = Enum.Parse<GradeQuestionMethod>(rq.GradeQuestionMethod);

        // Phòng thủ: chỉ sửa được trước giờ mở bài nên chưa thể có bài làm; nếu có thì không sinh lại bản trộn
        if (await _context.Attempts.AnyAsync(x => x.TestId == test.Id, cancellationToken))
            throw new ErrorCodeException(ErrorCodes.TEST_ALREADY_OPEN);

        // Bản gốc hiện tại của bài test
        var testQuestions = await _context.TestQuestions
            .Include(x => x.Question)
            .Where(x => x.TestId == test.Id)
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);
        var existingIds = testQuestions.Select(x => x.QuestionId).ToHashSet();

        if (!rq.DeleteQuestionIds.All(existingIds.Contains))
            throw new ErrorCodeException(ErrorCodes.QUESTION_NOT_FOUND_TO_DELETE);

        var deleteIds = rq.DeleteQuestionIds.ToHashSet();

        var updateQuestionDto = rq.CreateUpdateQuestions
            .Where(x => x.QuestionId.HasValue && existingIds.Contains(x.QuestionId.Value) && !deleteIds.Contains(x.QuestionId.Value))
            .ToList();
        var updateQuestion = testQuestions
            .Where(x => updateQuestionDto.Any(q => q.QuestionId!.Value == x.QuestionId))
            .Select(x => x.Question!)
            .ToList();
        var newQuestionDto = rq.CreateUpdateQuestions
            .Where(x => !x.QuestionId.HasValue || !existingIds.Contains(x.QuestionId.Value))
            .ToList();

        // Câu lấy từ mẫu đề thi mà không sửa nội dung -> dùng lại câu đó
        var reusableIds = (await _testService.QuestionAccessAndCompareForTest(newQuestionDto, cancellationToken)).ToHashSet();
        // Câu đang có trong bài mà không sửa nội dung -> giữ nguyên (đổi điểm / thứ tự chỉ cập nhật TestQuestion)
        var unchangedIds = _testService.CheckQuestionsForUpdate(updateQuestionDto, updateQuestion).NotUpdateQuestionIds.ToHashSet();

        var mediaMap = await _context.ResolveQuestionMediaAsync(rq.CreateUpdateQuestions, _user.UserId!.Value, cancellationToken);

        var listQuestions = new List<Question>();
        // Bản gốc mới theo đúng thứ tự client gửi lên
        var finalQuestions = new List<(Guid QuestionId, float Score)>();

        foreach (var questionDto in rq.CreateUpdateQuestions)
        {
            var questionId = questionDto.QuestionId;
            var keep = questionId.HasValue
                       && (unchangedIds.Contains(questionId.Value) || reusableIds.Contains(questionId.Value));

            if (!keep)
            {
                // Câu mới hoặc đã sửa nội dung: tạo câu mới, không sửa câu cũ (câu cũ có thể đang được mẫu đề thi dùng chung)
                var question = new Question
                {
                    Id = Guid.NewGuid(),
                    Type = Enum.Parse<QuestionType>(questionDto.Type!),
                    ExplainText = questionDto.ExplainText,
                    QuestionText = questionDto.QuestionText,
                    TextFormat = TextFormatHelper.Resolve(questionDto.TextFormat, TextFormat.PlainText),
                    Score = questionDto.Score,
                    DataJson = CreateUpdateQuestionDto.Serializer.Serialize(questionDto)
                };
                question.ApplyMedia(questionDto, mediaMap);
                listQuestions.Add(question);
                questionId = question.Id;
            }

            finalQuestions.Add((questionId!.Value, questionDto.Score));
        }

        // Câu đang có mà client không gửi lên và không xoá: giữ lại, xếp sau (giữ hành vi cũ)
        var sentIds = rq.CreateUpdateQuestions.Where(x => x.QuestionId.HasValue).Select(x => x.QuestionId!.Value).ToHashSet();
        finalQuestions.AddRange(testQuestions
            .Where(x => !sentIds.Contains(x.QuestionId) && !deleteIds.Contains(x.QuestionId))
            .Select(x => (x.QuestionId, x.Score)));

        // Cập nhật bản gốc: sửa tại chỗ dòng còn dùng (tránh xoá rồi thêm lại trùng unique TestId + QuestionId)
        var existingByQuestion = testQuestions.ToDictionary(x => x.QuestionId);
        var finalIds = finalQuestions.Select(x => x.QuestionId).ToList();
        var finalIdSet = finalIds.ToHashSet();

        _context.TestQuestions.RemoveRange(testQuestions.Where(x => !finalIdSet.Contains(x.QuestionId)));
        for (var index = 0; index < finalQuestions.Count; index++)
        {
            var (questionId, score) = finalQuestions[index];
            if (existingByQuestion.TryGetValue(questionId, out var row))
            {
                row.Score = score;
                row.Order = index;
            }
            else
            {
                _context.TestQuestions.Add(new TestQuestion
                {
                    TestId = test.Id, QuestionId = questionId, Score = score, Order = index
                });
            }
        }

        // Bản trộn: đủ số lượng yêu cầu, rồi sinh lại toàn bộ từ bản gốc mới
        var currentVersions = await _context.TestVersions
            .Where(x => x.TestId == test.Id)
            .ToListAsync(cancellationToken);
        var (versions, addedVersions, removedVersions) = TestVersionBuilder.Resize(test.Id, currentVersions, rq.NumberOfShuffles);

        var oldVersionQuestions = await _context.TestVersionQuestions
            .Where(x => x.TestVersion!.TestId == test.Id)
            .ToListAsync(cancellationToken);
        _context.TestVersionQuestions.RemoveRange(oldVersionQuestions);
        _context.TestVersions.RemoveRange(removedVersions);
        _context.TestVersions.AddRange(addedVersions);
        _context.TestVersionQuestions.AddRange(TestVersionBuilder.Build(finalIds, versions, _random));

        test.QuestionCount = finalIds.Count;

        _context.Questions.AddRange(listQuestions);

        await _context.SaveChangesAsync(cancellationToken);
        
        return test.Id;
    }
}
