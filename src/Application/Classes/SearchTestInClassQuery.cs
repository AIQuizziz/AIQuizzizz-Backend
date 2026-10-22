using CleanArchitectureBase.Application.Classes.Service;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Classes;

public class SearchTestResultDto
{
    public required Guid TestId { get; set; }
    public required string Name { get; set; }
    public int? NumberOfQuestions { get; set; }
    public required int TimeLimit { get; set; }
    public DateTimeOffset TimeStart { get; set; }
    public double RelativeTime { get; set; }
    public int? NumberOfCompletion { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset TimeFinish { get; set; }
    public int MaxAttempt { get; set; }
    /// <summary>Số lượt đã làm của user hiện tại</summary>
    public int UserAttemptCount { get; set; }
    /// <summary>User đang có một lượt làm dở (được phép tiếp tục kể cả khi đã hết lượt)</summary>
    public bool HasInProgressAttempt { get; set; }
}   

[Authorize]
public class SearchTestInClassQuery : IRequest<PaginatedList<SearchTestResultDto>>
{
    /// <summary>
    /// Id of the class want to retrieve tests
    /// </summary>
    public required Guid ClassId { get; set; }
    public string? TestName { get; set; }
    public string? Status { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 5;
}

public class SearchTestInClassQueryValidator : AbstractValidator<SearchTestInClassQuery>
{
    public SearchTestInClassQueryValidator()
    {
        RuleFor(v => v.ClassId)
            .NotEmpty().WithMessage("ClassId không được để trống");
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Số trang phải lớn hơn hoặc bằng 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Kích thước trang phải từ 1 đến 100");
        
        RuleFor(x => x.Status)
            .Must(mode => new[] {"Active", "Completed", "Upcoming"}.Contains(mode) || string.IsNullOrEmpty(mode))
            .WithMessage($"SharedMode phải là Active, Completed, Upcoming");
    }
}

public class SearchTestInClassQueryHandler : IRequestHandler<SearchTestInClassQuery, PaginatedList<SearchTestResultDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IClassService _classService;
    private readonly IUser _user;
    
    public SearchTestInClassQueryHandler(
        IApplicationDbContext context,
        IClassService classService,
        IUser user)
    {
        _context = context;
        _classService = classService;
        _user = user;
    }
    
    /// <summary>
    /// The function searches for tests in a class based on test name and status, returning a paginated list of test details
    /// </summary>
    /// <param name="rq">Request contains ClassId, TestName, Status, PageNumber, and PageSize information</param>
    /// <param name="cancellationToken">Token to cancel the task</param>
    public async Task<PaginatedList<SearchTestResultDto>> Handle(SearchTestInClassQuery rq, CancellationToken cancellationToken)
    {
        var classById = await _context.Classes
            .Where(x => x.Id == rq.ClassId)
            .FirstOrDefaultAsync(cancellationToken);
        if (classById == null)
            throw new ErrorCodeException(ErrorCodes.CLASS_NOTFOUND, "Không tìm thấy lớp");
        
        var isUserInClass = await _classService.IsUserInClass(rq.ClassId);
        if (!isUserInClass)
            throw new ErrorCodeException(ErrorCodes.NOT_FOUND_USER_IN_CLASS, "User không có trong lớp");
        
        var tests = await _context.Tests
            .Where(x => x.ClassId.Equals(rq.ClassId) && x.IsDeleted == false
            && (string.IsNullOrEmpty(rq.TestName) || x.Name.ToLower().Contains(rq.TestName.ToLower())))
            .ToListAsync(cancellationToken);
        
        switch (rq.Status)
        {
            case nameof(TestStatus.Active):
                tests = tests.Where(x => x.TimeStart <= DateTime.UtcNow && x.TimeFinish >= DateTime.UtcNow).ToList();
                break;
            case nameof(TestStatus.Completed):
                tests = tests.Where(x => x.TimeFinish < DateTime.UtcNow).ToList();
                break;
            case nameof(TestStatus.Upcoming):
                tests = tests.Where(x => x.TimeStart > DateTime.UtcNow).ToList();
                break;
        }
        
        var testIds = tests.Select(t => t.Id).ToList();
        
        var versionQuestionCounts = await _context.TestQuestions
            .Where(tq => testIds.Contains(tq.TestId))
            .GroupBy(tq => tq.TestId)
            .Select(g => new { TestId = g.Key, QuestionCount = g.Count() })
            .ToDictionaryAsync(
                x => x.TestId,
                x => new { x.QuestionCount },
                cancellationToken);
        
        var attemptCounts = await _context.Attempts
            .Where(a => testIds.Contains(a.TestId))
            .GroupBy(a => a.TestId)
            .Select(g => new 
            { 
                TestId = g.Key, 
                Count = g.Select(a => a.UserId).Distinct().Count() 
            })
            .ToDictionaryAsync(
                x => x.TestId, 
                x => x.Count, 
                cancellationToken
            );

        var myAttempts = (await _context.Attempts
                .Where(a => testIds.Contains(a.TestId) && a.UserId == _user.UserId)
                .Select(a => new { a.TestId, a.TimeStart, a.TimeFinish })
                .ToListAsync(cancellationToken))
            .GroupBy(a => a.TestId)
            .ToDictionary(g => g.Key, g => new
            {
                Count = g.Count(),
                InProgress = g.Any(a => a.TimeFinish < a.TimeStart)
            });

        var results = tests.Select(test =>
        {
            var questionCount = versionQuestionCounts.ContainsKey(test.Id)
                ? versionQuestionCounts[test.Id]
                : new { QuestionCount = 0 };

            var completionCount = attemptCounts.ContainsKey(test.Id) ? attemptCounts[test.Id] : 0;

            return new SearchTestResultDto
            {
                TestId = test.Id,
                Name = test.Name,
                NumberOfQuestions = questionCount.QuestionCount,
                NumberOfCompletion = completionCount,
                Status = test.TimeStart > DateTime.UtcNow ? TestStatus.Upcoming.ToString()
                    : test.TimeFinish < DateTime.UtcNow ? TestStatus.Completed.ToString()
                    : TestStatus.Active.ToString(),
                TimeLimit = test.TimeLimit,
                RelativeTime = Math.Floor((DateTime.UtcNow - test.TimeStart).TotalHours),
                TimeStart = test.TimeStart,
                TimeFinish = test.TimeFinish,
                MaxAttempt = test.MaxAttempt,
                UserAttemptCount = myAttempts.TryGetValue(test.Id, out var mine) ? mine.Count : 0,
                HasInProgressAttempt = myAttempts.TryGetValue(test.Id, out var mine2) && mine2.InProgress,
            };
        });

        return PaginatedList<SearchTestResultDto>.Create(
            results.ToList(),
            rq.PageNumber,
            rq.PageSize
        );
    }
}
