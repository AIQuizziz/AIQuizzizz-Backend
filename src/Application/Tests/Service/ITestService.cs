using System.Linq.Dynamic.Core;
using CleanArchitectureBase.Application.Classes.Service;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.QuestionSets.Services;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Tests.Service;

public interface ITestService
{
    Task<Dictionary<Guid, Question>> GetSourceQuestionsForTest(IEnumerable<Guid> questionIds, CancellationToken cancellationToken);
    Task<bool> CanViewOrEditTest(Guid classId, CancellationToken cancellationToken);
    Task<bool> CanViewHistoryOfTest(Guid testId, CancellationToken cancellationToken);
    Task<bool> CanCreateTest(Guid classId);
    Task TryCheckCanAttemptTest(Test test);
    Task TryCheckCanSubmitTest(Test test);
    Task<ClassShareMode?> GetRoleUserInTest(Test test);
    Task AutoSubmit(Guid attemptId);
}

public class TestService : ITestService
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly IIdentityService _identityService;
    private readonly IQuestionSetService _questionSetService;

    public TestService(
        IApplicationDbContext context,
        IUser user,
        IIdentityService identityService,
        IQuestionSetService questionSetService)
    {
        _context = context;
        _user = user;
        _identityService = identityService;
        _questionSetService = questionSetService;
    }

    /// <summary>
    /// Câu hỏi có sẵn được chọn vào bài test phải là câu (chưa xoá) của bộ câu hỏi mà người dùng xem được:
    /// của mình, được chia sẻ, công khai, trong lớp hoặc trong thư mục được chia sẻ. Trả về câu gốc theo Id.
    /// </summary>
    public async Task<Dictionary<Guid, Question>> GetSourceQuestionsForTest(IEnumerable<Guid> questionIds,
        CancellationToken cancellationToken)
    {
        var ids = questionIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, Question>();

        var sources = await _context.Questions
            .Include(q => q.QuestionSet)
            .Where(q => ids.Contains(q.Id) && !q.IsDeleted && q.QuestionSetId != null && !q.QuestionSet!.IsDeleted)
            .ToListAsync(cancellationToken);

        var viewableSets = new HashSet<Guid>();
        foreach (var questionSet in sources.Select(q => q.QuestionSet!).DistinctBy(qs => qs.Id))
        {
            if (await _questionSetService.CanUserViewQuestionSet(_user.UserId, questionSet))
                viewableSets.Add(questionSet.Id);
        }

        var allowed = sources
            .Where(q => viewableSets.Contains(q.QuestionSetId!.Value))
            .ToDictionary(q => q.Id);

        var invalid = ids.Where(id => !allowed.ContainsKey(id)).ToList();
        if (invalid.Count > 0)
        {
            throw new ErrorCodeException(new Dictionary<string, string[]>
            {
                {
                    ErrorCodes.USER_NOT_ACCESS_TO_QUESTION_SET,
                    invalid.Select(id => $"Question {id} không có quyền truy cập hoặc không tồn tại").ToArray()
                }
            });
        }

        return allowed;
    }

    public async Task<bool> CanViewOrEditTest(Guid classId, CancellationToken cancellationToken)
    {
        if (await _identityService.IsInAnyRoleAsync(_user.UserId!.Value, Domain.Constants.Roles.Administrator,
                Domain.Constants.Roles.Moderator))
            return true;
        
        var isLecturerOrOwnerInClass = await _context.ClassUsers
            .Where(u => u.UserId == _user.UserId && u.ClassId == classId &&
                        (ClassShareMode.Owner.Equals(u.ShareMode) || ClassShareMode.Teacher.Equals(u.ShareMode)))
            .FirstOrDefaultAsync(cancellationToken);
        if (isLecturerOrOwnerInClass == null)
            return false;
        
        return true;
    }

    public async Task<bool> CanViewHistoryOfTest(Guid classId, CancellationToken cancellationToken)
    {
        var user = await _context.ClassUsers
            .Where(u => u.UserId == _user.UserId && u.ClassId == classId)
            .FirstOrDefaultAsync(cancellationToken);
        
        if (user == null)
            return false;
        
        return true;
    }

    public async Task<bool> CanCreateTest(Guid classId)
    {
        var user = await _context.ClassUsers
            .Where(u => u.UserId == _user.UserId && u.ClassId == classId &&
                        (ClassShareMode.Owner.Equals(u.ShareMode) || ClassShareMode.Teacher.Equals(u.ShareMode)))
            .FirstOrDefaultAsync();
        if (user == null) return false;

        return true;
    }

    public async Task TryCheckCanAttemptTest(Test test)
    {
        if (test.TimeStart > DateTime.UtcNow)
            throw new ErrorCodeException(ErrorCodes.NOT_YET_TIME_TO_OPEN_TEST, "Chưa đến thời gian mở test");
        
        if (test.TimeFinish < DateTime.UtcNow)
            throw new ErrorCodeException(ErrorCodes.TEST_IS_OVERDUE, "Hết hạn làm bài");
        
        var student = await _context.ClassUsers
            .Where(u => u.UserId == _user.UserId && u.ClassId == test.ClassId)
            .FirstOrDefaultAsync();

        if (student == null)
            throw new ErrorCodeException(ErrorCodes.NOT_FOUND_STUDENT_IN_CLASS, "Chỉ student trong lớp mới có thể attempt test");
    }

    public async Task TryCheckCanSubmitTest(Test test)
    {
        if (test.TimeStart > DateTime.UtcNow)
            throw new ErrorCodeException(ErrorCodes.NOT_YET_TIME_TO_OPEN_TEST, "Chưa đến thời gian mở test");
        
        if (test.TimeFinish.AddSeconds(10) < DateTime.UtcNow)
            throw new ErrorCodeException(ErrorCodes.TEST_IS_OVERDUE, "Hết hạn làm bài");
        
        var student = await _context.ClassUsers
            .Where(u => u.UserId == _user.UserId && u.ClassId == test.ClassId)
            .FirstOrDefaultAsync();

        if (student == null)
            throw new ErrorCodeException(ErrorCodes.NOT_FOUND_STUDENT_IN_CLASS, "Chỉ student trong lớp mới có thể submit test");
    }

    public async Task<ClassShareMode?> GetRoleUserInTest(Test test)
    {
        var user = await _context.ClassUsers
            .Where(x => x.UserId.Equals(_user.UserId) && x.ClassId.Equals(test.ClassId))
            .FirstOrDefaultAsync();

        if (user == null)
            throw new ErrorCodeException(ErrorCodes.NOT_FOUND_USER_IN_CLASS);
        
        return user.ShareMode;
    }

    public async Task AutoSubmit(Guid attemptId)
    {
        var attempt = await _context.Attempts
            .Where(x => x.Id.Equals(attemptId))
            .FirstOrDefaultAsync();
        if (attempt == null)
            throw new ErrorCodeException(ErrorCodes.ATTEMPT_NOT_FOUND);
        
        attempt.TimeFinish = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(new CancellationToken());
        
        Console.WriteLine("Auto submit success");
    }
}
