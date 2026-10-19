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

        var testVersion = await _context.TestVersions
            .Where(x => x.TestId.Equals(test.Id))
            .ToListAsync(cancellationToken);
        
        List<TestVersion> updateTestVersions = new();
        
        var versionQuestionsAllNo = await _context.TestVersionQuestions
            .Include(x => x.TestVersion)
            .ThenInclude(x => x!.Test)
            .Include(x => x.Question)
            .Where(x => x.TestVersion!.Test!.Id == rq.TestId)
            .ToListAsync(cancellationToken);
        
        var versionQuestions = versionQuestionsAllNo.Where(x => x.TestVersion!.No == 0)
            .Select(x => new {QuestionId = x.QuestionId, Question = x.Question})
            .ToList();
        
        var allExist = rq.DeleteQuestionIds
            .All(x => versionQuestions.Select(v => v.QuestionId).Contains(x));
        if (!allExist)
            throw new ErrorCodeException(ErrorCodes.QUESTION_NOT_FOUND_TO_DELETE);
        
        var updateQuestionDto = rq.CreateUpdateQuestions
            .Where(x => x.QuestionId != null &&
                        versionQuestions.Select(y => y.QuestionId).Contains(x.QuestionId.Value)
                        && !rq.DeleteQuestionIds.Contains(x.QuestionId.Value))
            .ToList();

        var updateQuestion = versionQuestions
            .Where(x => updateQuestionDto.Any(q => q.QuestionId!.Value == x.QuestionId)
            && !rq.DeleteQuestionIds.Contains(x.QuestionId))
            .Select(x => x.Question!)
            .ToList();
        
        var newQuestionDto = rq.CreateUpdateQuestions
            .Where(x => x.QuestionId == null ||
                        !versionQuestions.Select(y => y.QuestionId).Contains(x.QuestionId.Value))
            .ToList();
        
        var newQuestionIds = await _testService.QuestionAccessAndCompareForTest(newQuestionDto, cancellationToken);

        var updateQuestionIds = _testService.CheckQuestionsForUpdate(updateQuestionDto, updateQuestion);

        var deleteUpdateQuestion = versionQuestionsAllNo
            .Where(x => updateQuestionIds.UpdateQuestionIds.Contains(x.QuestionId)
                        || rq.DeleteQuestionIds.Contains(x.QuestionId))
            .ToList();
        
        var listQuestions = new List<Question>();
        
        var listTestVersionQuestions = new List<TestVersionQuestion>();

        var order = versionQuestions.Count;
        
        if (rq.NumberOfShuffles != testVersion.Count)
        {
            if (rq.NumberOfShuffles > testVersion.Count)
            {
                // Version mới copy từ danh sách câu hỏi hiện tại, bỏ các câu sẽ bị xoá/cập nhật trong request này
                // (câu cập nhật được thêm lại cho mọi version ở vòng lặp bên dưới)
                var removedIds = deleteUpdateQuestion.Select(x => x.QuestionId).ToHashSet();
                var questions = versionQuestions.Select(x => x.QuestionId).Where(id => !removedIds.Contains(id)).ToList();
                
                // Range(start, count): tạo đúng (NumberOfShuffles - hiện có) version, No tiếp nối từ 0..n-1
                var testVersions = Enumerable.Range(testVersion.Count, rq.NumberOfShuffles - testVersion.Count)
                    .Select(versionNo => new TestVersion
                    {
                        Id = Guid.NewGuid(),
                        TestId = test.Id,
                        No = versionNo
                    }).ToList();
                
                var testVersionQuestions = testVersions.SelectMany(t =>
                {
                    var shuffledIndex = Enumerable.Range(0, questions.Count).OrderBy(_ => _random.Next()).ToList();

                    return shuffledIndex.Select((index, order) => new TestVersionQuestion
                    {
                        Id = Guid.NewGuid(), QuestionId = questions[index], TestVersionId = t.Id, Order = order
                    });
                }).ToList();
                
                testVersion.AddRange(testVersions);
                
                _context.TestVersions.AddRange(testVersions);
                
                _context.TestVersionQuestions.AddRange(testVersionQuestions);
            }

            if (rq.NumberOfShuffles < testVersion.Count)
            {
                // Tách riêng danh sách version bị xoá; testVersion giữ lại các version còn dùng để thêm câu hỏi mới
                var versionsToRemove = testVersion.OrderByDescending(x => x.No).Take(testVersion.Count - rq.NumberOfShuffles).ToList();
                var removeIds = versionsToRemove.Select(y => y.Id).ToHashSet();
                
                var deleteVersionQuestions = versionQuestionsAllNo
                    .Where(x => removeIds.Contains(x.TestVersionId));
                
                _context.TestVersionQuestions.RemoveRange(deleteVersionQuestions);
                
                _context.TestVersions.RemoveRange(versionsToRemove);
                testVersion = testVersion.Where(x => !removeIds.Contains(x.Id)).ToList();
            }
        }

        var mediaMap = await _context.ResolveQuestionMediaAsync(rq.CreateUpdateQuestions, _user.UserId!.Value, cancellationToken);

        // Id câu hỏi cuối cùng theo đúng thứ tự client gửi lên (để giữ nguyên thứ tự hiển thị sau khi lưu)
        var finalQuestionIds = new List<Guid>();

        foreach (var questionDto in rq.CreateUpdateQuestions)
        {
            var question = new Question
            {
                Id = Guid.NewGuid(),
                Type = Enum.Parse<QuestionType>(questionDto.Type!),
                ExplainText = questionDto.ExplainText,
                QuestionText = questionDto.QuestionText,
                TextFormat = TextFormatHelper.Resolve(questionDto.TextFormat, TextFormat.PlainText),
                Score = questionDto.Score
            };
            question.ApplyMedia(questionDto, mediaMap);

            if (questionDto.QuestionId.HasValue &&
                updateQuestionIds.NotUpdateQuestionIds.Contains(questionDto.QuestionId!.Value))
            {
                finalQuestionIds.Add(questionDto.QuestionId!.Value);
                continue;
            }
            else if (questionDto.QuestionId.HasValue && newQuestionIds.Contains(questionDto.QuestionId!.Value))
            {
                question.Id = questionDto.QuestionId.Value;
            }
            else
            {
                question.DataJson = CreateUpdateQuestionDto.Serializer.Serialize(questionDto);
                
                listQuestions.Add(question);
            }

            finalQuestionIds.Add(question.Id);

            foreach (var version in testVersion)
            {
                var testVersionQuestion = new TestVersionQuestion
                {
                    Id = Guid.NewGuid(), QuestionId = question.Id, Order = order, TestVersionId = version.Id
                };
                
                listTestVersionQuestions.Add(testVersionQuestion);
            }
            order++;
        }
        
        // Đếm theo số câu hỏi thực tế của version gốc (No = 0) sau khi cập nhật:
        // câu giữ nguyên + câu mới/câu được cập nhật (tạo lại), không trừ DeleteQuestionIds lần nữa.
        var baseVersionId = testVersion.FirstOrDefault(x => x.No == 0)?.Id;
        var removedBaseIds = deleteUpdateQuestion
            .Where(x => x.TestVersion!.No == 0)
            .Select(x => x.QuestionId)
            .ToHashSet();
        var keptBaseCount = versionQuestions.Count(x => !removedBaseIds.Contains(x.QuestionId));
        var addedBaseCount = listTestVersionQuestions.Count(x => x.TestVersionId == baseVersionId);
        test.QuestionCount = keptBaseCount + addedBaseCount;

        // Giữ đúng thứ tự client gửi cho version gốc (câu được cập nhật không bị đẩy xuống cuối)
        var orderIndex = finalQuestionIds
            .Select((id, index) => new { id, index })
            .GroupBy(x => x.id)
            .ToDictionary(g => g.Key, g => g.First().index);
        foreach (var vq in versionQuestionsAllNo.Where(x => x.TestVersion!.No == 0 && !removedBaseIds.Contains(x.QuestionId)))
        {
            if (orderIndex.TryGetValue(vq.QuestionId, out var idx)) vq.Order = idx;
        }
        foreach (var vq in listTestVersionQuestions.Where(x => x.TestVersionId == baseVersionId))
        {
            if (orderIndex.TryGetValue(vq.QuestionId, out var idx)) vq.Order = idx;
        }
        
        _context.TestVersionQuestions.RemoveRange(deleteUpdateQuestion);
        
        _context.Questions.AddRange(listQuestions);
        
        _context.TestVersionQuestions.AddRange(listTestVersionQuestions);
        
        await _context.SaveChangesAsync(cancellationToken);
        
        return test.Id;
    }
}
