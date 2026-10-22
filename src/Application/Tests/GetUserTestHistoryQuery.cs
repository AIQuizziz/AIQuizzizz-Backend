using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.Tests.Dto;
using CleanArchitectureBase.Application.Tests.Service;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Tests;

[Authorize]
public class GetUserTestHistoryQuery : IRequest<PaginatedList<HistoryTestDto>>
{
    public required Guid TestId { get; set; }
    public Guid? UserId { get; set; }
    public bool? IsPassed { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class GetUserTestHistoryQueryValidator : AbstractValidator<GetUserTestHistoryQuery>
{
    public GetUserTestHistoryQueryValidator()
    {
        RuleFor(x => x.TestId)
            .NotEmpty().WithMessage("ClassId ko đc rỗng");
        
        RuleFor(x => x.UserId)
            .Must(id => id == null || id != Guid.Empty)
            .WithMessage("UserId không được là GUID rỗng khi được cung cấp");
        
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Kích thước trang phải từ 1 đến 100");
            
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Số trang phải lớn hơn hoặc bằng 1");
    }
}

public class GetUserTestHistoryQueryHandler : IRequestHandler<GetUserTestHistoryQuery, PaginatedList<HistoryTestDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITestService _testService;
    private readonly IUser _user;
    
    public GetUserTestHistoryQueryHandler(
        IApplicationDbContext context,
        ITestService testService,
        IUser user)
    {
        _context = context;
        _testService = testService;
        _user = user;
    }
    
    /// <summary>
    /// The function retrieves a user's test attempt history for a specific test, filtered by pass/fail status, and returns a paginated list of attempt details
    /// </summary>
    /// <param name="rq">Request contains TestId, IsPassed, PageNumber, and PageSize information</param>
    /// <param name="cancellationToken">Token to cancel the task</param>
    public async Task<PaginatedList<HistoryTestDto>> Handle(GetUserTestHistoryQuery rq, CancellationToken cancellationToken)
    {
        var test = await _context.Tests.Where(x => x.Id.Equals(rq.TestId)).FirstOrDefaultAsync(cancellationToken);
        if (test == null)
            throw new ErrorCodeException(ErrorCodes.TEST_NOT_FOUND, "Không tìm thấy bài test");

        var userId = _user.UserId;
        
        var isUserInClass = await _testService.CanViewHistoryOfTest(test.ClassId, cancellationToken);
        if (!isUserInClass)
            throw new ErrorCodeException(ErrorCodes.NOT_FOUND_USER_IN_CLASS, "User không có trong lớp");

        var isLecturerOrOwnerInClass = await _testService.CanViewOrEditTest(test.ClassId, cancellationToken);
        
        if (isLecturerOrOwnerInClass)
        {
            if (rq.UserId != null && rq.UserId != Guid.Empty)
                userId = rq.UserId.Value;
        }
        
        var totalScore = _context.TestQuestions
            .Where(x => x.TestId == rq.TestId)
            .Sum(x => x.Score);
        // tránh chia cho 0 trong SQL khi tổng điểm = 0
        var safeTotalScore = totalScore > 0 ? totalScore : 1;
            
        var attempts = _context.Attempts
            .Include(x => x.User)
            .Include(x => x.Test)
            .Where(x => x.TestId == rq.TestId && x.UserId == userId
            && x.TimeStart <= x.TimeFinish)
            .OrderByDescending(x => x.TimeFinish)
            .Select(x => new HistoryTestDto
            {
                AttemptId = x.Id,
                StudentName = x.User != null ? x.User.FullName : null,
                StudentEmail = x.User != null ? x.User.Email : null,
                Score = x.Score,
                Status = x.Test!.PassingScore/100 <= x.Score/safeTotalScore ? nameof(AttemptStatus.Passed) : nameof(AttemptStatus.Failed),
                TimeStart = x.TimeStart,
                TimeSubmit = x.TimeFinish,
                CanReview = x.Test.IsAllowReviewAfterSubmit || isLecturerOrOwnerInClass
            });
        
        if (rq.IsPassed != null)
            attempts = attempts
                .Where(x => x.Status!.Equals(rq.IsPassed.Value ? nameof(AttemptStatus.Passed) : nameof(AttemptStatus.Failed)));

        return await PaginatedList<HistoryTestDto>.CreateAsync(
            attempts.AsQueryable(),
            rq.PageNumber,
            rq.PageSize
        );
    }
}
