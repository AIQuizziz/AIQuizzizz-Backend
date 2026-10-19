using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.Plans.Service;
using CleanArchitectureBase.Application.Questions.Dtos;
using CleanArchitectureBase.Application.Questions.Services;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Application.QuestionSets.Services;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.QuestionSets.Queries;

[Authorize]
public class GetTestFromQuestionSetQuery : IRequest<List<QuestionResponseDto>>
{
    public Guid? QuestionSetId { get; set; }
    public int NumberOfQuestion { get; set; }
    public List<string> QuestionTypes { get; set; } = new();
}

public class GetTestFromQuestionSetQueryValidator : AbstractValidator<GetTestFromQuestionSetQuery>
{
    public GetTestFromQuestionSetQueryValidator()
    {
        RuleFor(x => x.QuestionSetId)
            .NotEmpty().WithMessage("QuestionId không được trống");
        RuleFor(x => x.NumberOfQuestion)
            .GreaterThan(0).WithMessage("NumberOfQuestion phải > 0")
            .LessThan(100).WithMessage("NumberOfQuestion giới hạn là 100");
        RuleFor(x => x.QuestionTypes)
            .NotEmpty().WithMessage("QuestionTypes không được bỏ trống")
            .Must(x => x.Count > 0).WithMessage("Số lượng type lớn hơn 0")
            .ForEach(type =>
                type.Must(x => new[] {"MultipleChoice", "Matching", "Ordering", "ShortText", "LanguageFlashcard"}.Contains(x))
                    .WithMessage($"Loại câu hỏi phải là MultipleChoice, Matching, Ordering, ShortText, LanguageFlashcard"));
    }
}

public class GetTestFromQuestionSetQueryHandler : IRequestHandler<GetTestFromQuestionSetQuery, List<QuestionResponseDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IQuestionSetService _questionSetService;
    private readonly IUser _user;
    private readonly IPlanService _planService;
    
    public GetTestFromQuestionSetQueryHandler(
        IApplicationDbContext context,
        IQuestionSetService questionSetService,
        IUser user,
        IPlanService planService)
    {
        _context = context;
        _questionSetService = questionSetService;
        _user = user;
        _planService = planService;
    }
    
    public async Task<List<QuestionResponseDto>> Handle(GetTestFromQuestionSetQuery rq, CancellationToken cancellationToken)
    {
        var questionSet = await _context.QuestionSets
            .Include(q => q.Questions)
            .Where(qs => qs.Id.Equals(rq.QuestionSetId))
            .FirstOrDefaultAsync(cancellationToken);
        if (questionSet == null)
            throw new ErrorCodeException(ErrorCodes.QUESTION_SET_NOT_FOUND);

        var checkPlan = await _planService.CanLearn(_user.UserId!.Value);
        if (!checkPlan)
            throw new ErrorCodeException(ErrorCodes.PLAN_REQUIRE_PLAN);
        
        var canView = await _questionSetService.CanUserViewQuestionSet(_user.UserId, questionSet);
        if (!canView)
            throw new ErrorCodeException(ErrorCodes.USER_NOT_ACCESS_TO_QUESTION_SET);

       
        // Chỉ lấy câu hỏi chưa xoá thuộc các loại được chọn; không bao giờ ném lỗi khi thiếu câu hỏi
        var selectedTypes = rq.QuestionTypes.Distinct().ToList();

        var typeToQuestions = selectedTypes.ToDictionary(
            type => type,
            type => questionSet.Questions
                .Where(q => !q.IsDeleted && q.Type.ToString() == type && FlashcardQuizBuilder.IsPlayable(q))
                .ToList());

        var random = new Random();
        var questions = new List<Question>();

        // Mỗi loại được chọn có ít nhất 1 câu (nếu có và còn chỗ)
        foreach (var type in selectedTypes.OrderBy(_ => random.Next()))
        {
            if (questions.Count >= rq.NumberOfQuestion) break;

            var pool = typeToQuestions[type];
            if (pool.Count == 0) continue;

            var question = pool[random.Next(pool.Count)];
            questions.Add(question);
            pool.Remove(question);
        }

        // Bổ sung ngẫu nhiên cho đủ số lượng, dừng khi hết câu hỏi (clamp theo số câu có sẵn)
        while (questions.Count < rq.NumberOfQuestion)
        {
            var remainingTypes = typeToQuestions.Where(x => x.Value.Count > 0).Select(x => x.Key).ToList();
            if (remainingTypes.Count == 0) break;

            var pool = typeToQuestions[remainingTypes[random.Next(remainingTypes.Count)]];
            var question = pool[random.Next(pool.Count)];
            questions.Add(question);
            pool.Remove(question);
        }

        var flashcardPool = questionSet.Questions
            .Where(q => !q.IsDeleted && q.Type == QuestionType.LanguageFlashcard)
            .ToList();

        return questions.Select(x =>
            QuestionResponseDto.Mapper.FromEntity(x, true, flashcardPool: flashcardPool)).ToList();
    }
}
