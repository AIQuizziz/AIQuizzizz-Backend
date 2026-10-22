using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.MediaFiles;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.Tests.Service;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Tests;

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
        var existingByQuestion = testQuestions.ToDictionary(x => x.QuestionId);

        if (!rq.DeleteQuestionIds.All(existingByQuestion.ContainsKey))
            throw new ErrorCodeException(ErrorCodes.QUESTION_NOT_FOUND_TO_DELETE);

        var sentIds = rq.CreateUpdateQuestions.Where(x => x.QuestionId.HasValue).Select(x => x.QuestionId!.Value).ToHashSet();
        // Id được gửi lại trong danh sách thì giữ (không xoá)
        var deleteIds = rq.DeleteQuestionIds.Where(id => !sentIds.Contains(id)).ToHashSet();

        // Câu mới chọn từ bộ câu hỏi: kiểm tra quyền và lấy câu gốc
        var sources = await _testService.GetSourceQuestionsForTest(
            sentIds.Where(id => !existingByQuestion.ContainsKey(id)), cancellationToken);

        // Dữ liệu cũ (thời còn mẫu đề thi): câu có thể đang dùng chung với bài khác / bộ câu hỏi -> không sửa tại chỗ
        var existingIds = existingByQuestion.Keys.ToList();
        var sharedIds = (await _context.TestQuestions
                .Where(x => existingIds.Contains(x.QuestionId) && x.TestId != test.Id)
                .Select(x => x.QuestionId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Concat(testQuestions.Where(x => x.Question!.QuestionSetId != null).Select(x => x.QuestionId))
            .ToHashSet();

        var mediaMap = await _context.ResolveQuestionMediaAsync(rq.CreateUpdateQuestions, _user.UserId!.Value, cancellationToken);

        var listQuestions = new List<Question>();
        // Bản gốc mới theo đúng thứ tự client gửi lên
        var finalQuestions = new List<(Guid QuestionId, float Score)>();

        foreach (var questionDto in rq.CreateUpdateQuestions)
        {
            if (questionDto.QuestionId.HasValue && existingByQuestion.TryGetValue(questionDto.QuestionId.Value, out var row))
            {
                var current = row.Question!;
                if (!sharedIds.Contains(current.Id))
                {
                    // Câu của riêng bài này: sửa tại chỗ (interceptor cập nhật ContentHash)
                    TestQuestionFactory.Apply(current, questionDto, mediaMap);
                    finalQuestions.Add((current.Id, questionDto.Score));
                }
                else if (TestQuestionFactory.HashOf(questionDto, mediaMap) == QuestionContent.Hash(current))
                {
                    finalQuestions.Add((current.Id, questionDto.Score));
                }
                else
                {
                    var copy = TestQuestionFactory.Create(questionDto, mediaMap, source: null);
                    copy.SourceQuestionId = current.SourceQuestionId;
                    copy.SourceVersion = current.SourceVersion;
                    listQuestions.Add(copy);
                    finalQuestions.Add((copy.Id, questionDto.Score));
                }
                continue;
            }

            var source = questionDto.QuestionId.HasValue ? sources[questionDto.QuestionId.Value] : null;
            var question = TestQuestionFactory.Create(questionDto, mediaMap, source);
            listQuestions.Add(question);
            finalQuestions.Add((question.Id, questionDto.Score));
        }

        // Câu đang có mà client không gửi lên và không xoá: giữ lại, xếp sau
        finalQuestions.AddRange(testQuestions
            .Where(x => !sentIds.Contains(x.QuestionId) && !deleteIds.Contains(x.QuestionId))
            .Select(x => (x.QuestionId, x.Score)));

        var finalIds = finalQuestions.Select(x => x.QuestionId).ToList();
        var finalIdSet = finalIds.ToHashSet();

        // Câu bị bỏ khỏi bài: gỡ khỏi bản gốc; bản sao riêng của bài thì xoá mềm
        foreach (var removed in testQuestions.Where(x => !finalIdSet.Contains(x.QuestionId)))
        {
            _context.TestQuestions.Remove(removed);
            if (!sharedIds.Contains(removed.QuestionId))
                removed.Question!.IsDeleted = true;
        }

        // Cập nhật bản gốc: sửa tại chỗ dòng còn dùng (tránh xoá rồi thêm lại trùng unique TestId + QuestionId)
        for (var index = 0; index < finalQuestions.Count; index++)
        {
            var (questionId, score) = finalQuestions[index];
            if (existingByQuestion.TryGetValue(questionId, out var row))
            {
                row.Score = score;
                row.Order = index;
                row.Question!.Order = index;
            }
            else
            {
                _context.TestQuestions.Add(new TestQuestion
                {
                    TestId = test.Id, QuestionId = questionId, Score = score, Order = index
                });
            }
        }
        foreach (var question in listQuestions)
            question.Order = finalIds.IndexOf(question.Id);

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
