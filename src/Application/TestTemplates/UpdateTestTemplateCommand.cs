using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.Tests.Service;
using CleanArchitectureBase.Application.TestTemplates.Service;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.TestTemplates;

[Authorize]
public class UpdateTestTemplateCommand : IRequest<Guid>
{
    public Guid TestTemplateId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<CreateUpdateQuestionDto> CreateUpdateQuestions { get; set; } = new ();
    public List<Guid> DeleteQuestionIds { get; set; } = new();
}

public class UpdateTestTemplateCommandValidator : AbstractValidator<UpdateTestTemplateCommand>
{
    public UpdateTestTemplateCommandValidator()
    {
        RuleFor(x => x.TestTemplateId)
            .NotEmpty().WithMessage("TestTemplateId không được để trống");
            
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên bộ câu hỏi không được để trống")
            .MaximumLength(200).WithMessage("Tên bộ câu hỏi không được vượt quá 200 ký tự");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Mô tả không được vượt quá 500 ký tự");

        RuleFor(x => x.CreateUpdateQuestions)
            .Must(q => q != null && q.Count <= 100)
            .WithMessage("Test template không được vượt quá 100 câu");

        RuleFor(x => x.CreateUpdateQuestions)
            .NotEmpty().WithMessage("Bộ câu hỏi phải chứa ít nhất một câu hỏi")
            .Must(questions => questions.All(IsValidQuestionType))
            .WithMessage("Một hoặc nhiều câu hỏi có loại hoặc dữ liệu không hợp lệ");

        RuleForEach(x => x.CreateUpdateQuestions)
            .SetValidator((command, question) => new CreateUpdateQuestionDto.QuestionCreateDtoValidator());
    }
    
    private bool IsValidQuestionType(CreateUpdateQuestionDto createUpdateQuestion)
    {
        bool isValid = createUpdateQuestion.Type switch
        {
            nameof(QuestionType.MultipleChoice) => createUpdateQuestion.MultipleChoices != null && createUpdateQuestion.MultipleChoices.Any(),
            nameof(QuestionType.Matching) => createUpdateQuestion.MatchingPairs != null && createUpdateQuestion.MatchingPairs.Any(),
            nameof(QuestionType.Ordering) => createUpdateQuestion.OrderingItems != null && createUpdateQuestion.OrderingItems.Any(),
            nameof(QuestionType.ShortText) => !string.IsNullOrWhiteSpace(createUpdateQuestion.ShortAnswer),
            _ => false
        };

        return isValid;
    }
}

public class UpdateTestTemplateCommandHandler : IRequestHandler<UpdateTestTemplateCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ITestTemplateService _testTemplateService;
    private readonly IUser _user;
    
    public UpdateTestTemplateCommandHandler(
        IApplicationDbContext context,
        ITestTemplateService testTemplateService,
        IUser user)
    {
        _context = context;
        _testTemplateService = testTemplateService;
        _user = user;
    }
    
    public async Task<Guid> Handle(UpdateTestTemplateCommand rq, CancellationToken cancellationToken)
    {
        var testTemplate = await _context.TestTemplates
            .Where(t => t.Id == rq.TestTemplateId && t.IsDeleted == false)
            .FirstOrDefaultAsync(cancellationToken);
        if (testTemplate == null)
            throw new ErrorCodeException(ErrorCodes.TEST_TEMPLATE_NOT_FOUND, "Không tìm thấy test template");
            
        var canUpdateSharing = await _testTemplateService.CanEditTestTemplate(rq.TestTemplateId, cancellationToken);
        if (!canUpdateSharing)
            throw new ErrorCodeException(ErrorCodes.USER_NOT_HAVE_PERMISSION_IN_TEST_TEMPLATE, "Không có quyền sửa");
        
        testTemplate.Name = rq.Name;
        if (rq.Description != null) testTemplate.Description = rq.Description;
        
        var testTemplateQuestions = await _context.TestTemplateQuestions
            .Include(x => x.Question)
            .Where(x => x.TestTemplateId == rq.TestTemplateId)
            .ToListAsync(cancellationToken);

        var updateQuestionDto = rq.CreateUpdateQuestions
            .Where(x => x.QuestionId != null &&
                        testTemplateQuestions.Select(y => y.QuestionId).Contains(x.QuestionId.Value))
            .ToList();
        
        var updateQuestion = testTemplateQuestions
            .Where(x => updateQuestionDto.Any(q => q.QuestionId!.Value == x.QuestionId))
            .Select(x => x.Question!)
            .ToList();
        
        var questionFromQuestionSet = rq.CreateUpdateQuestions
            .Where(x => x.QuestionId != null &&
                        !testTemplateQuestions.Select(y => y.QuestionId).Contains(x.QuestionId.Value))
            .ToList();
        
        await _testTemplateService.TryCheckQuestionAccessForTestTemplate(questionFromQuestionSet, cancellationToken);

        var updateQuestionIds = _testTemplateService.CheckQuestionsForUpdate(updateQuestionDto, updateQuestion);
        
        var deleteUpdateQuestion = testTemplateQuestions
            .Where(x => updateQuestionIds.UpdateQuestionIds.Contains(x.QuestionId)
                        || rq.DeleteQuestionIds.Contains(x.QuestionId))
            .ToList();
        
        var listTemplateQuestions = new List<TestTemplateQuestion>();
        
        var listQuestions = new List<Question>();
        
        var mediaMap = await _context.ResolveQuestionMediaAsync(rq.CreateUpdateQuestions, _user.UserId!.Value, cancellationToken);
        
        var questionOrder = 0;
        foreach (var questionDto in rq.CreateUpdateQuestions)
        {
            var position = questionOrder++;
            if (questionDto.QuestionId.HasValue &&
                updateQuestionIds.NotUpdateQuestionIds.Contains(questionDto.QuestionId!.Value))
            {
                // Câu không đổi nội dung: chỉ cập nhật vị trí
                var unchanged = testTemplateQuestions.FirstOrDefault(x => x.QuestionId == questionDto.QuestionId.Value)?.Question;
                if (unchanged != null) unchanged.Order = position;
                continue;
            }
            
            var question = new Question
            {
                Id = Guid.NewGuid(),
                Type = Enum.Parse<QuestionType>(questionDto.Type!),
                QuestionText = questionDto.QuestionText,
                ExplainText = questionDto.ExplainText,
                TextFormat = TextFormatHelper.Resolve(questionDto.TextFormat, TextFormat.PlainText),
                Score = questionDto.Score,
                Order = position,
                DataJson = CreateUpdateQuestionDto.Serializer.Serialize(questionDto)
            };
            question.ApplyMedia(questionDto, mediaMap);
            
            var templateQuestion = new TestTemplateQuestion
            {
                Id = Guid.NewGuid(), QuestionId = question.Id , TestTemplateId = rq.TestTemplateId
            };
            
            listQuestions.Add(question);
            
            listTemplateQuestions.Add(templateQuestion);   
        }
        
        _context.Questions.AddRange(listQuestions);
        
        _context.TestTemplateQuestions.AddRange(listTemplateQuestions);
        
        _context.TestTemplateQuestions.RemoveRange(deleteUpdateQuestion);
        
        await _context.SaveChangesAsync(cancellationToken);
        
        return rq.TestTemplateId;
    }
}
