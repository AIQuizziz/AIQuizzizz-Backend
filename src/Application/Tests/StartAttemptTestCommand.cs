using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.Tests.Dto;
using CleanArchitectureBase.Application.Tests.Service;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Tests;

[Authorize]
public class StartAttemptTestCommand : IRequest<AttemptDetailDto>
{
    /// <summary>
    /// Id of the test the student chose to take
    /// </summary>
    public Guid TestId { get; set; }
}

public class StartAttemptTestCommandHandler : IRequestHandler<StartAttemptTestCommand, AttemptDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITestService _testService;
    private readonly IUser _user;
    private readonly IHangFireService _hangFireService;
    
    public StartAttemptTestCommandHandler(
        IApplicationDbContext context,
        ITestService testService, IUser user,
        IHangFireService hangFireService)
    {
        _context = context;
        _testService = testService;
        _user = user;
        _hangFireService = hangFireService;       
    }
    
    /// <summary>
    /// The function creates test attempts for students and returns the test questions
    /// </summary>
    /// <param name="rq">Request contains TestId information</param>
    /// <param name="cancellationToken">Token to cancel the task</param>
    public async Task<AttemptDetailDto> Handle(StartAttemptTestCommand rq, CancellationToken cancellationToken)
    {
        var test = await _context.Tests
            .Where(x => x.Id == rq.TestId && x.IsDeleted == false)
            .FirstOrDefaultAsync(cancellationToken);
        if (test == null)
            throw new ErrorCodeException(ErrorCodes.TEST_NOT_FOUND, "Bài test không tồn tại");
        
        await _testService.TryCheckCanAttemptTest(test);

        // Owner/Teacher của lớp quản lý bài kiểm tra, không làm bài như học viên (tránh tạo attempt làm sai thống kê)
        var roleInTest = await _testService.GetRoleUserInTest(test);
        if (roleInTest == ClassShareMode.Owner || roleInTest == ClassShareMode.Teacher)
            throw new ErrorCodeException(ErrorCodes.NOT_FOUND_STUDENT_IN_CLASS, "Chỉ student trong lớp mới có thể attempt test");
        
        Dictionary<Guid, AttemptQuestion>? userAnswerDict = null;
        
        var attempted = await _context.Attempts
            .Where(x => x.UserId == _user.UserId && x.TestId == rq.TestId
                                                 && (x.TimeFinish < x.TimeStart &&
                                                     x.TimeStart <= DateTime.UtcNow))
            .FirstOrDefaultAsync(cancellationToken);

        // Attempt đang làm nhưng đã quá giờ (job auto-submit bị trễ/lỗi) -> chốt lại, không cho làm tiếp
        if (attempted != null && attempted.TimeStart.AddMinutes(test.TimeLimit) < DateTimeOffset.UtcNow)
        {
            attempted.TimeFinish = attempted.TimeStart.AddMinutes(test.TimeLimit);
            await _context.SaveChangesAsync(cancellationToken);
            attempted = null;
        }

        double timeRemaining = 0;

        if (attempted != null)
        {
                userAnswerDict = (await _context.AttemptQuestions
                .Where(x => x.AttemptId == attempted.Id)
                .ToListAsync(cancellationToken))
                .GroupBy(a => a.QuestionId)
                .ToDictionary(g => g.Key, g => g.First());
                
                timeRemaining = Math.Floor(
                    (test.TimeLimit - (DateTime.UtcNow - attempted.TimeStart).TotalMinutes) * 100
                ) / 100;
        }
        
        var attemptDetail = new AttemptDetailDto
        {
            AttemptId = attempted?.Id ?? Guid.NewGuid(),
            Name = test.Name,
            TimeStart = attempted != null ? attempted.TimeStart : DateTime.UtcNow,
            TimeEnd = attempted != null ? attempted.TimeFinish : DateTime.UtcNow.AddMinutes(-test.TimeLimit),
            TimeLimit = test.TimeLimit,
            // TimeRemaining được tính ở cuối hàm
        };
        
        var testVersionId = attempted?.TestVersionId ?? Guid.Empty;
        
        if (attempted == null || attempted.TimeFinish >= attempted.TimeStart)
        {
            var attemptUser = _context.Attempts
                .Where(x => x.UserId == _user.UserId && x.TestId == rq.TestId)
                .ToList();
            if (attemptUser.Count >= test.MaxAttempt)
                throw new ErrorCodeException(ErrorCodes.MAX_ATTEMPT_IN_THIS_TEST, "Đã hết lượt làm bài");
            
            var testVersion = await _context.TestVersions
                .Where(x => x.Test!.Id == test.Id)
                .OrderBy(_ => Guid.NewGuid())
                .FirstOrDefaultAsync(cancellationToken);
            
            if (testVersion == null)
            {
                throw new ErrorCodeException(ErrorCodes.TEST_NOT_FOUND);
            }
            
            var newAttempt = new Attempt
            {
                Id = attemptDetail.AttemptId,
                TestVersionId = testVersion!.Id,
                TimeStart = DateTime.UtcNow,
                TimeFinish = DateTime.UtcNow.AddMinutes(-test.TimeLimit),
                Score = 0,
                UserId = _user.UserId ?? Guid.Empty,
                TestId = rq.TestId
            };

            testVersionId = testVersion.Id;
            
            timeRemaining = test.TimeLimit;

            await _hangFireService.AutoSubmitTest(newAttempt.Id, test.TimeLimit);
            
            _context.Attempts.Add(newAttempt);
        }
        
        var userGrade = await _context.TestGrades
            .Where(x => x.UserId == _user.UserId && x.TestId == test.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (userGrade == null)
        {
            userGrade = new TestGrade
            {
                Id = Guid.NewGuid(),
                Score = 0,
                TestId = test.Id,
                UserId = _user.UserId!.Value,
            };
            
            _context.TestGrades.Add(userGrade);
        }
        
        // Không cho đồng hồ vượt quá giờ đóng bài test
        var minutesUntilTestClose = Math.Floor((test.TimeFinish - DateTimeOffset.UtcNow).TotalMinutes * 100) / 100;
        attemptDetail.TimeRemaining = Math.Max(0, Math.Min(timeRemaining, minutesUntilTestClose));
        
        var versionQuestions = await _context.TestVersionQuestions
            .Include(x => x.Question)
            .Where(x => x.TestVersion!.Id == testVersionId)
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);
            
        var scoreByQuestion = await _context.TestQuestions
            .Where(x => x.TestId == test.Id)
            .ToDictionaryAsync(x => x.QuestionId, x => x.Score, cancellationToken);

        attemptDetail.Questions = versionQuestions
            .OrderBy(x => x.Order)
            .Select(q =>
            {
                AttemptQuestion? ans = null;
                if (userAnswerDict != null)
                    userAnswerDict.TryGetValue(q.Question!.Id, out ans);

                var dto = QuestionAttemptDetailDto.Mapper.FromEntity(q.Question!, ans!=null && !ans.DataJson.Equals("[]") ? ans : null);
                if (scoreByQuestion.TryGetValue(q.QuestionId, out var score))
                    dto.Score = score;
                return dto;
            })
            .ToList();
        
        attemptDetail.QuestionCount = versionQuestions.Count();
        
        await _context.SaveChangesAsync(cancellationToken);
        
        return attemptDetail;
    }
}
