using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.Plans.Service;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.Tests.Service;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Tests;

[Authorize]
public class CreateTestCommand : IRequest<Guid>
{
    public required string Name { get; set; }
    public required Guid ClassId { get; set; }
    public required int TimeLimit { get; set; }
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    public required string GradeAttemptMethod { get; set; }
    public required string GradeQuestionMethod { get; set; }
    public bool IsShowCorrectAnswerInReview { get; set; }
    public bool IsAllowReviewAfterSubmit { get; set; }
    public int NumberOfShuffles { get; set; } = 1;
    public int MaxAttempt { get; set; } = 1;
    public int PassingScore { get; set; } = 0;
    public List<CreateUpdateQuestionDto> Questions { get; set; } = new ();
}

public class CreateTestCommandValidator : AbstractValidator<CreateTestCommand>
{
    public CreateTestCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên bài kiểm tra không được để trống")
            .MaximumLength(200).WithMessage("Tên bài kiểm tra không được vượt quá 200 ký tự");
        
        RuleFor(x => x.PassingScore)
            .GreaterThanOrEqualTo(0)
            .NotEmpty().WithMessage("PassingScore không được để trống");
            // .LessThan(100).WithMessage("PassingScore không được vượt quá 100%");

        RuleFor(x => x.ClassId)
            .NotEmpty().WithMessage("ClassId không được để trống");
        
        RuleFor(x => x.NumberOfShuffles)
            .NotEmpty().WithMessage("NumberOfShuffles không được để trống")
            .LessThanOrEqualTo(10).WithMessage("Số lần shuffles tối đa 10")
            .GreaterThan(0).WithMessage("Số lần shuffles tối thiểu 1");

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
        
        RuleFor(x => x.Questions)
            .Must(TestQuestionRules.HasDistinctQuestionIds)
            .WithMessage("Một câu hỏi chỉ được chọn một lần trong bài kiểm tra");

        RuleForEach(x => x.Questions)
            .SetValidator((command, question) => new CreateUpdateQuestionDto.QuestionCreateDtoValidator());
    }
}

public class CreateTestCommandHandler : IRequestHandler<CreateTestCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ITestService _testService;
    private readonly IPlanService _planService;
    private readonly IUser _user;
    private readonly Random _random = new();
    
    public CreateTestCommandHandler(
        IApplicationDbContext context,
        ITestService testService,
        IPlanService planService,
        IUser user)
    {
        _context = context;
        _testService = testService;
        _planService = planService;
        _user = user;
    }

    /// <summary>
    /// The function creates a new test for a class, including its questions and version, and returns the test ID
    /// </summary>
    /// <param name="rq">Request contains ClassId, Name, GradeAttemptMethod, GradeQuestionMethod, EndTime, StartTime, TimeLimit, MaxAttempt, PassingScore, IsShowCorrectAnswerInReview, and Questions information</param>
    /// <param name="cancellationToken">Token to cancel the task</param>
    public async Task<Guid> Handle(CreateTestCommand rq, CancellationToken cancellationToken)
    {
        var classById = await _context.Classes
            .Where(x => x.Id == rq.ClassId)
            .FirstOrDefaultAsync(cancellationToken);
        if (classById == null)
            throw new ErrorCodeException(ErrorCodes.CLASS_NOTFOUND, "Không tìm thấy lớp");
        
        var checkPlan = await _planService.CanOpenTest(_user.UserId!.Value);
        if (!checkPlan)
            throw new ErrorCodeException(ErrorCodes.PLAN_REQUIRE_PLAN);
        
        var isLecturerOrOwnerInClass = await _testService.CanCreateTest(rq.ClassId);
        if (!isLecturerOrOwnerInClass)
            throw new ErrorCodeException(ErrorCodes.NOT_FOUND_TEACHER_OR_OWNER_IN_CLASS, "Không phải lecturer hoặc owner của class");

        var test = new Test
        {
            Id = Guid.NewGuid(),
            Name = rq.Name,
            ClassId = rq.ClassId,
            GradeAttemptMethod = Enum.Parse<GradeAttemptMethod>(rq.GradeAttemptMethod),
            GradeQuestionMethod = Enum.Parse<GradeQuestionMethod>(rq.GradeQuestionMethod),
            TimeFinish = rq.EndTime,
            TimeStart = rq.StartTime,
            TimeLimit = rq.TimeLimit,
            MaxAttempt = rq.MaxAttempt,
            PassingScore = rq.PassingScore,
            IsShowCorrectAnswerInReview = rq.IsShowCorrectAnswerInReview,
            IsAllowReviewAfterSubmit = rq.IsAllowReviewAfterSubmit,
            QuestionCount = 0
        };

        if (rq.Questions.Count > 100)
            throw new ErrorCodeException(ErrorCodes.NUMBER_OF_QUESTION_EXCEED_LIMIT,
                "Số lượng câu hỏi không được vượt quá 100");

        var validQuestionIds = await _testService.QuestionAccessAndCompareForTest(rq.Questions, cancellationToken);

        var questionsToAdd = new List<Question>();

        var mediaMap = await _context.ResolveQuestionMediaAsync(rq.Questions, _user.UserId!.Value, cancellationToken);

        var questionIds = rq.Questions.Select((q, index) =>
        {
            if (q.QuestionId.HasValue && validQuestionIds.Contains(q.QuestionId.Value))
                return q.QuestionId.Value;

            var question = new Question
            {
                Id = Guid.NewGuid(),
                Type = Enum.Parse<QuestionType>(q.Type!),
                QuestionText = q.QuestionText,
                ExplainText = q.ExplainText,
                TextFormat = TextFormatHelper.Resolve(q.TextFormat, TextFormat.PlainText),
                Score = q.Score,
                Order = index,
                DataJson = CreateUpdateQuestionDto.Serializer.Serialize(q)
            };
            question.ApplyMedia(q, mediaMap);
            questionsToAdd.Add(question);
            return question.Id;
        }).ToList();
        
        // Bản gốc của bài test: thứ tự client gửi lên + điểm của từng câu trong bài này
        var testQuestions = questionIds.Select((questionId, index) => new TestQuestion
        {
            TestId = test.Id,
            QuestionId = questionId,
            Score = rq.Questions[index].Score,
            Order = index
        }).ToList();

        var testVersions = Enumerable.Range(0, rq.NumberOfShuffles).Select(versionNo => new TestVersion
        {
            Id = Guid.NewGuid(),
            TestId = test.Id,
            No = versionNo
        }).ToList();

        var testVersionQuestions = TestVersionBuilder.Build(questionIds, testVersions, _random);

        test.QuestionCount = questionIds.Count;

        _context.Tests.Add(test);

        _context.Questions.AddRange(questionsToAdd);

        _context.TestQuestions.AddRange(testQuestions);

        _context.TestVersions.AddRange(testVersions);

        _context.TestVersionQuestions.AddRange(testVersionQuestions);

        await _context.SaveChangesAsync(cancellationToken);

        return test.Id;
    }
}
