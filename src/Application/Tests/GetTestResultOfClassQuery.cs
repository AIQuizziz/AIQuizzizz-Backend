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
public class GetTestResultOfClassQuery : IRequest<PaginatedList<ResultTestOfClassDto>>
{
    public required Guid TestId { get; set; }
    public string? StudentName { get; set; }
    public bool? IsPassed { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class GetTestResultOfClassQueryValidator : AbstractValidator<GetTestResultOfClassQuery>
{
    public GetTestResultOfClassQueryValidator()
    {
        RuleFor(x => x.TestId)
            .NotEmpty().WithMessage("ClassId ko đc rỗng");
        
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Kích thước trang phải từ 1 đến 100");
            
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Số trang phải lớn hơn hoặc bằng 1");
    }
}

public class GetTestResultOfClassQueryHandler : IRequestHandler<GetTestResultOfClassQuery, PaginatedList<ResultTestOfClassDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITestService _testService;
    
    public GetTestResultOfClassQueryHandler(IApplicationDbContext context, ITestService testService)
    {
        _context = context;
        _testService = testService;
    }
    
    public async Task<PaginatedList<ResultTestOfClassDto>> Handle(GetTestResultOfClassQuery rq, CancellationToken cancellationToken)
    {
        var test = await _context.Tests.Where(x => x.Id.Equals(rq.TestId)).FirstOrDefaultAsync(cancellationToken);
        if (test == null)
            throw new ErrorCodeException(ErrorCodes.TEST_NOT_FOUND, "Không tìm thấy bài test");

        var canView = await _testService.CanViewOrEditTest(test.ClassId, cancellationToken);
        if (!canView)
            throw new ErrorCodeException(ErrorCodes.USER_NOT_HAVE_PERMISSION_IN_TEST, "Không có quyền xem");

        var totalScore = _context.TestQuestions
            .Where(x => x.TestId == rq.TestId)
            .Sum(x => x.Score);
        // tránh chia cho 0 trong SQL khi tổng điểm = 0
        var safeTotalScore = totalScore > 0 ? totalScore : 1;
        
        var resultTest = _context.TestGrades
            .Include(x => x.User)
            .Include(x => x.Test)
            .Where(x => x.TestId == rq.TestId && x.Test!.IsDeleted == false
            && (rq.StudentName == null || x.User!.FullName!.ToLower().Contains(rq.StudentName.ToLower())))
            .Select(x => new ResultTestOfClassDto
            {
                StudentId = x.UserId,
                StudentName = x.User!.FullName,
                StudentEmail = x.User!.Email,
                Score = x.Score,
                Status = x.Score/safeTotalScore >= x.Test!.PassingScore/100
                    ? nameof(AttemptStatus.Passed)
                    : nameof(AttemptStatus.Failed)
            });

        if (rq.IsPassed != null)
        {
            resultTest = resultTest
                .Where(x => x.Status!.Equals(rq.IsPassed.Value ? nameof(AttemptStatus.Passed) : nameof(AttemptStatus.Failed)));
        }
        
        return await PaginatedList<ResultTestOfClassDto>.CreateAsync(
            resultTest.AsQueryable(),
            rq.PageNumber,
            rq.PageSize
            ) ;
    }
}
