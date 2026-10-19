using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.TestTemplates.Service;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.TestTemplates;

[Authorize]
public class CreateTestTemplateCommand : IRequest<Guid>
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<CreateUpdateQuestionDto> Questions { get; set; } = new ();
}

public class CreateTestTemplateCommandValidator : AbstractValidator<CreateTestTemplateCommand>
{
    public CreateTestTemplateCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên bộ câu hỏi không được để trống")
            .MaximumLength(200).WithMessage("Tên bộ câu hỏi không được vượt quá 200 ký tự");
        
        RuleFor(x => x.Questions)
            .Must(q => q != null && q.Count <= 100)
            .WithMessage("Test template không được vượt quá 100 câu");
        
        RuleFor(x => x.Questions)
            .NotEmpty().WithMessage("Bộ câu hỏi phải chứa ít nhất một câu hỏi")
            .Must(questions => questions.All(IsValidQuestionType))
            .WithMessage("Một hoặc nhiều câu hỏi có loại hoặc dữ liệu không hợp lệ");
        
        RuleForEach(x => x.Questions)
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

public class CreateTestTemplateCommandHandler : IRequestHandler<CreateTestTemplateCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly ITestTemplateService _testTemplateService;

    public CreateTestTemplateCommandHandler(
        IApplicationDbContext context,
        IUser user,
        ITestTemplateService testTemplateService)
    {
        _context = context;
        _user = user;
        _testTemplateService = testTemplateService;
    }
    
    /// <summary>
    /// The function creates a new test template with associated questions and assigns the creator as the owner, returning the test template ID
    /// </summary>
    /// <param name="rq">Request contains Name and Questions information</param>
    /// <param name="cancellationToken">Token to cancel the task</param>
    public async Task<Guid> Handle(CreateTestTemplateCommand rq, CancellationToken cancellationToken)
    {
        await _testTemplateService.TryCheckQuestionAccessForTestTemplate(rq.Questions, cancellationToken);
        
        var testTemplate = new TestTemplate { Id = Guid.NewGuid(), Name = rq.Name, IsDeleted = false, Description = rq.Description};

        var testTemplateUser = new TestTemplateUser
        {
            UserId = _user.UserId!.Value,
            TestTemplateId = testTemplate.Id,
            ShareMode = TestTemplateUserShareMode.Owner
        };
        
        var listTemplateQuestions = new List<TestTemplateQuestion>();
        
        var listQuestions = new List<Question>();

        var mediaMap = await _context.ResolveQuestionMediaAsync(rq.Questions, _user.UserId!.Value, cancellationToken);

        var questionOrder = 0;
        foreach (var questionDto in rq.Questions)
        {
            var question = new Question
            {
                Id = Guid.NewGuid(),
                Type = Enum.Parse<QuestionType>(questionDto.Type!),
                QuestionText = questionDto.QuestionText,
                ExplainText = questionDto.ExplainText,
                TextFormat = TextFormatHelper.Resolve(questionDto.TextFormat, TextFormat.PlainText),
                Score = questionDto.Score,
                Order = questionOrder++
            };
            
            var templateQuestion = new TestTemplateQuestion
            {
                Id = Guid.NewGuid(), QuestionId = question.Id , TestTemplateId = testTemplate.Id
            };
            
            question.DataJson = CreateUpdateQuestionDto.Serializer.Serialize(questionDto);
            question.ApplyMedia(questionDto, mediaMap);

            listQuestions.Add(question);
            
            listTemplateQuestions.Add(templateQuestion);   
        }
        
        _context.TestTemplates.Add(testTemplate);
        
        _context.TestTemplateUsers.Add(testTemplateUser);
        
        _context.Questions.AddRange(listQuestions);
        
        _context.TestTemplateQuestions.AddRange(listTemplateQuestions);
        
        await _context.SaveChangesAsync(cancellationToken);
        
        return testTemplate.Id;
    }
}
