using System.Linq.Dynamic.Core;
using CleanArchitectureBase.Application.Classes.Service;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Tests.Service;

public interface ITestService
{
    Task<List<Guid>> QuestionAccessAndCompareForTest(List<CreateUpdateQuestionDto> question, CancellationToken cancellationToken);
    CheckUpdateQuestion CheckQuestionsForUpdate(List<CreateUpdateQuestionDto> questionDtos, List<Question> questions);
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

    public TestService(
        IApplicationDbContext context,
        IUser user,
        IIdentityService identityService)
    {
        _context = context;
        _user = user;
        _identityService = identityService;
    }

    public async Task<List<Guid>> QuestionAccessAndCompareForTest(List<CreateUpdateQuestionDto> question,
        CancellationToken cancellationToken)
    {
        var questionSetPairs = question
            .Where(q => q.QuestionId.HasValue && q.QuestionId != Guid.Empty)
            .Select(q => q.QuestionId)
            .ToList();

        if (questionSetPairs.Count == 0) return new List<Guid>();

        var validQuestionInTestTemplate = await _context.TestTemplateQuestions
            .Include(x => x.Question)
            .Include(x => x.TestTemplate)
            .ThenInclude(x => x!.TestTemplateUsers)
            .Include(qs => qs.TestTemplate)
            .ThenInclude(x => x!.FolderTestTemplates)
            .ThenInclude(x => x.Folder)
            .ThenInclude(x => x!.FolderUsers)
            .Where(q => questionSetPairs.Contains(q.QuestionId))
            .Where(q =>
                q.TestTemplate!.TestTemplateUsers.Any(tu => tu.UserId == _user.UserId)
                || q.TestTemplate!.FolderTestTemplates.Any(ft =>
                    ft.Folder!.FolderUsers.Any(fu => fu.UserId == _user.UserId)))
            .Distinct()
            .ToListAsync(cancellationToken);


        var invalidQuestion = questionSetPairs
            .Where(q => !validQuestionInTestTemplate.Any(v => v.QuestionId.Equals(q!.Value)))
            .ToList();

        if (invalidQuestion.Count > 0)
        {
            var errors = new Dictionary<string, string[]>
            {
                {
                    ErrorCodes.USER_NOT_ACCESS_TO_QUESTION_SET, invalidQuestion
                        .Select(q => $"Question {q!.Value} không có quyền truy cập hoặc không tồn tại")
                        .ToArray()
                }
            };

            throw new ErrorCodeException(errors);
        }
        
        return validQuestionInTestTemplate
            .Select(x => new { x.Question, MatchingQuestion = question.First(q => q.QuestionId!.Equals(x.QuestionId)) })
            // Chỉ so nội dung: điểm của câu trong bài test lưu ở TestQuestion, đổi điểm không cần tạo câu mới
            .Where(x => CreateUpdateQuestionDto.Compare.CompareQuestion(x.Question!, x.MatchingQuestion))
            .Select(x => x.Question!.Id)
            .ToList();
    }
    
    public CheckUpdateQuestion CheckQuestionsForUpdate(List<CreateUpdateQuestionDto> questionDtos,List<Question> questions)
    {
        if (questionDtos.Count == 0 || questionDtos.Count == 0) return new CheckUpdateQuestion();

        var notUpdateQuestionIds = questions
            .Where(x =>
                CreateUpdateQuestionDto.Compare.CompareQuestion(x, questionDtos.First(q => q.QuestionId!.Equals(x.Id))))
            .Select(x => x.Id)
            .ToList();

        var updateQuestionIds = questionDtos
            .Where(x => !notUpdateQuestionIds.Contains(x.QuestionId!.Value))
            .Select(x => x.QuestionId!.Value)
            .ToList();

        return new CheckUpdateQuestion
        {
            UpdateQuestionIds = updateQuestionIds,
            NotUpdateQuestionIds = notUpdateQuestionIds
        };
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
