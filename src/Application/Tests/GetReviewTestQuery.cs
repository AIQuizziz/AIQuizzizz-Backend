using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.Tests.Dto;
using CleanArchitectureBase.Application.Tests.Service;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Tests;

[Authorize]
public class GetReviewTestQuery : IRequest<ReviewTestDto>
{
    public required Guid AttemptId { get; set; }
}

public class ReviewTestValidator : AbstractValidator<GetReviewTestQuery>
{
    public ReviewTestValidator()
    {
        RuleFor(x => x.AttemptId)
            .NotEmpty().WithMessage("AttemptId không được để trống");
    }
}

public class GetReviewTestQueryHandler : IRequestHandler<GetReviewTestQuery, ReviewTestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITestService _testService;
    private readonly IUser _user;
    
    public GetReviewTestQueryHandler(
        IApplicationDbContext context,
        ITestService testService,
        IUser user)
    {
        _context = context;
        _testService = testService;
        _user = user;       
    }
    
    public async Task<ReviewTestDto> Handle(GetReviewTestQuery rq, CancellationToken cancellationToken)
    {
        var attempt = await _context.Attempts
            .Include(x => x.User)
            .Include(x => x.Test)
            .ThenInclude(x => x!.TestGrades)
            .Where(x => x.Id.Equals(rq.AttemptId)
            && x.TimeStart <= x.TimeFinish)
            .FirstOrDefaultAsync(cancellationToken);

        if (attempt == null || attempt.Test == null)
            throw new ErrorCodeException(ErrorCodes.ATTEMPT_NOT_FOUND);
        
        // Test hoặc lớp đã bị xoá thì không cho xem lại bài làm
        var classAlive = !attempt.Test.IsDeleted
            && await _context.Classes.AnyAsync(c => c.Id == attempt.Test!.ClassId && !c.IsDeleted, cancellationToken);
        if (!classAlive)
            throw new ErrorCodeException(ErrorCodes.TEST_NOT_FOUND, "Không tìm thấy test");

        if(attempt.TimeStart > attempt.TimeFinish)
            throw new ErrorCodeException(ErrorCodes.NOT_SUBMITTED_CAN_NOT_VIEW);

        var roleInTest = await _testService.GetRoleUserInTest(attempt.Test);

        var isShowCorrectAnswer = attempt.Test.IsShowCorrectAnswerInReview;

        if (ClassShareMode.Owner == roleInTest || ClassShareMode.Teacher == roleInTest)
        {
            isShowCorrectAnswer = true;
        }
        else
        {
            if (!attempt.Test.IsAllowReviewAfterSubmit || !attempt.UserId.Equals(_user.UserId))
                throw new ErrorCodeException(ErrorCodes.STUDENT_CAN_REVIEW_THIS_TEST);

            // Chưa hết lượt và test còn mở -> không lộ đáp án (tránh xem đáp án lần 1 rồi làm lại đạt 100%)
            if (isShowCorrectAnswer)
            {
                var attemptCount = await _context.Attempts
                    .CountAsync(a => a.UserId == attempt.UserId && a.TestId == attempt.TestId, cancellationToken);
                var exhausted = attemptCount >= attempt.Test.MaxAttempt;
                var closed = attempt.Test.TimeFinish < DateTimeOffset.UtcNow;
                isShowCorrectAnswer = exhausted || closed;
            }
        }

        // Review của 1 attempt -> hiển thị điểm của chính attempt đó (không phải điểm tổng TestGrade)
        var totalPoint = attempt.Score;

        Dictionary<Guid, AttemptQuestion>? userAnswerDict = null;
        
        userAnswerDict = (await _context.AttemptQuestions
            .Where(x => x.AttemptId == attempt.Id)
            .ToListAsync(cancellationToken))
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.First());
        
        var versionQuestions = await _context.TestVersionQuestions
            .Include(x => x.Question)
            .Where(x => x.TestVersion!.Id == attempt.TestVersionId)
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);
        
        var maxPoint = await _context.TestQuestions
            .Where(x => x.TestId == attempt.TestId)
            .SumAsync(x => x.Score, cancellationToken);

        var reviewTest = new ReviewTestDto
        {
            AttemptId = attempt.Id,
            Name = attempt.Test.Name,
            StudentName = attempt.User!.FullName,
            TimeStart = attempt.TimeStart,
            TimeEnd = attempt.TimeFinish,
            Score = totalPoint,
            Status = maxPoint > 0 && attempt.Test.PassingScore / 100 <= totalPoint / maxPoint
                ? nameof(AttemptStatus.Passed) : nameof(AttemptStatus.Failed),
        };
            
        reviewTest.Questions = versionQuestions
            .OrderBy(x => x.Order)
            .Select(q =>
            {
                AttemptQuestion? ans = null;
                if (userAnswerDict != null)
                    userAnswerDict.TryGetValue(q.Question!.Id, out ans);

                return ReviewQuestionDto.Mapper.FromEntity(q.Question!, ans!=null && !ans.DataJson.Equals("[]") ? ans : null, isShowCorrectAnswer);
            })
            .ToList();

        return reviewTest;
    }
}
