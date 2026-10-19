using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.QuestionSets.Commands;

[Authorize]
public class CreateQuestionSetCommand : IRequest<Guid>
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<CreateUpdateQuestionDto> Questions { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

public class CreateQuestionSetCommandValidator : AbstractValidator<CreateQuestionSetCommand>
{
    public CreateQuestionSetCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên bộ câu hỏi không được để trống")
            .MaximumLength(200).WithMessage("Tên bộ câu hỏi không được vượt quá 200 ký tự");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Mô tả không được vượt quá 500 ký tự");

        RuleFor(x => x.Questions)
            .NotEmpty().WithMessage("Bộ câu hỏi phải chứa ít nhất một câu hỏi");

        RuleFor(x => x.Questions)
            .Must(q => q != null && q.Count <= 500)
            .WithMessage("Bộ câu hỏi không được vượt quá 500 câu");

        RuleForEach(x => x.Questions)
            .SetValidator((command, question) => new CreateUpdateQuestionDto.QuestionCreateDtoValidator());
        
        RuleFor(x => x.Tags)
            .Must(tags => tags.Count <= 10)
            .WithMessage("Không được nhập quá 10 tag.");
        
        RuleForEach(x => x.Tags)
            .NotEmpty().WithMessage("Tag không được để trống.")
            .MaximumLength(50).WithMessage("Mỗi tag không được vượt quá 50 ký tự.");
    }
}

public class CreateQuestionSetCommandHandler : IRequestHandler<CreateQuestionSetCommand, Guid>
{

    private readonly IApplicationDbContext _dbContext;
    private readonly IUser _user;
    public CreateQuestionSetCommandHandler(IApplicationDbContext dbContext, IUser user)
    {
        _dbContext = dbContext;
        _user = user;
    }

    public async Task<Guid> Handle(CreateQuestionSetCommand request, CancellationToken cancellationToken)
    {
        //Khởi tạo questionSet
        var questionSet = new QuestionSet
        {
            Id = Guid.NewGuid(),
            Name = request.Name!,
            Description = request.Description!,
            VisibilityMode = QuestionSetVisibilityMode.Private, //Để mặc định là private
            QuestionCount = request.Questions.Count,
            Questions = new List<Question>()
        };

        var mediaMap = await _dbContext.ResolveQuestionMediaAsync(request.Questions, _user.UserId!.Value, cancellationToken);

        //Xử lý thêm các câu hỏi để đưa vào questionset
        var questionOrder = 0;
        foreach (var questionDto in request.Questions)
        {
            //Khởi tạo question
            var question = new Question
            {
                Id = Guid.NewGuid(),
                QuestionSetId = questionSet.Id,
                ExplainText = questionDto.ExplainText,
                Type = Enum.Parse<QuestionType>(questionDto.Type!),
                QuestionText = questionDto.QuestionText,
                TextFormat = TextFormatHelper.Resolve(questionDto.TextFormat, TextFormat.Html),
                Score = questionDto.Score,
                Order = questionOrder++
            };

            // Chuyển các nội dung câu hỏi về JSON
            question.DataJson = CreateUpdateQuestionDto.Serializer.Serialize(questionDto);
            question.ApplyMedia(questionDto, mediaMap);

            questionSet.Questions.Add(question);
        }
        
        QuestionSetUser  questionSetUser = new QuestionSetUser
        {
            UserId = _user.UserId!.Value,
            QuestionSetId = questionSet.Id,
            ShareMode = QuestionSetUserShareMode.Owner
        };
        
        var questionSetTags = new List<QuestionSetTag>();
        var normalizedTagNames = request.Tags
            .Select(t => t.Trim().ToLower())
            .Distinct()
            .ToList();
            
        var existingTags = await _dbContext.Tags
            .Where(t => normalizedTagNames.Contains(t.Name.ToLower()))
            .ToListAsync(cancellationToken);
            
        foreach (var tagName in normalizedTagNames)
        {
            // Kiểm tra có tồn tại chưa
            var existingTag = existingTags.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));

            Tag tag;
            if (existingTag != null)
            {
                tag = existingTag;
                tag.QuestionSetCount++;
            }
            else
            {
                tag = new Tag
                {
                    Id = Guid.NewGuid(),
                    Name = tagName,
                    QuestionSetCount = 1
                };
                _dbContext.Tags.Add(tag);
            }

            questionSetTags.Add(new QuestionSetTag
            {
                QuestionSetId = questionSet.Id,
                TagId = tag.Id
            });
        }
        _dbContext.QuestionSetTags.AddRange(questionSetTags);
        _dbContext.QuestionSets.Add(questionSet);
        _dbContext.QuestionSetUsers.Add(questionSetUser);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return questionSet.Id;
    }
}
