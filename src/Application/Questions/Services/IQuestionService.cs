using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Questions.Dtos;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Questions.Services;

public interface IQuestionService
{
    public Task<List<QuestionResponseDto>> GetQuestionsBySetIdForDetailAsync(Guid questionSetId, Guid? userId,
        CancellationToken cancellationToken = default);

    public Task<(List<QuestionResponseDto> Questions, int TotalQuestions, int CompletedQuestions)> GetQuestionsBySetIdForLearnAsync(Guid questionSetId,
        Guid userId, int questionCount,
        CancellationToken cancellationToken = default);

}

public class QuestionService : IQuestionService
{
    private readonly IApplicationDbContext _context;

    public QuestionService(IApplicationDbContext context)
    {
        _context = context;
    }


    public async Task<List<QuestionResponseDto>> GetQuestionsBySetIdForDetailAsync(Guid questionSetId, Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || userId == null)
        {
            var questions = await _context.Questions
                .Where(q =>
                    q.QuestionSetId == questionSetId
                    && q.IsDeleted == false)
                .OrderBy(q => q.Order).ThenBy(q => q.Created).ThenBy(q => q.Id)
                .ToListAsync(cancellationToken);

            // shuffle: false -> trả đáp án theo thứ tự tác giả
            return questions
                .Select(q => QuestionResponseDto.Mapper.FromEntity(q, isCorrect: null, shuffle: false, quizView: false))
                .ToList();
        }
        {
            var query = from q in _context.Questions
                where q.QuestionSetId == questionSetId && q.IsDeleted == false
                join h in _context.UserQuestionSetHistories
                    on new
                    {
                        QuestionId = q.Id,
                        UserId = userId.Value
                    }
                    equals new
                    {
                        h.QuestionId,
                        h.UserId
                    }
                    into gj
                from history in gj.DefaultIfEmpty() // LEFT JOIN
                select new
                {
                    Question = q,
                    IsCorrect = history != null ? history.IsCorrect : (bool?)null
                };

            var result = await query
                .OrderBy(x => x.Question.Order).ThenBy(x => x.Question.Created).ThenBy(x => x.Question.Id)
                .ToListAsync(cancellationToken);

            return result
                .Select(x => QuestionResponseDto.Mapper.FromEntity(x.Question, x.IsCorrect, shuffle: false, quizView: false))
                .ToList();
        }
    }

    public async Task<(List<QuestionResponseDto> Questions, int TotalQuestions, int CompletedQuestions)>
        GetQuestionsBySetIdForLearnAsync(Guid questionSetId, Guid userId, int questionCount,
            CancellationToken cancellationToken = default)
    {
        var query = _context.Questions
            .Where(q => q.QuestionSetId == questionSetId && !q.IsDeleted)
            .GroupJoin(_context.UserQuestionSetHistories,
                q => new
                {
                    QuestionId = q.Id,
                    UserId = userId
                },
                h => new
                {
                    h.QuestionId,
                    h.UserId
                },
                (q, gj) => new
                {
                    Question = q,
                    History = gj
                })
            .SelectMany(x => x.History.DefaultIfEmpty(),
                (q, history) => new
                {
                    q.Question,
                    History = history
                })
            .Where(x => x.History == null || x.History.IsCorrect == false)
            .OrderBy(x => x.History != null && x.History.IsCorrect == false ? 0 : 1)
            .ThenBy(x => x.Question.Order).ThenBy(x => x.Question.Created).ThenBy(x => x.Question.Id)
            .Select(x => new
            {
                Question = x.Question,
                IsCorrect = x.History != null ? x.History.IsCorrect : (bool?)null
            });

        var completedQuestionCount = await _context.UserQuestionSetHistories
            .Where(h => h.UserId == userId && h.IsCorrect == true)
            .Join(_context.Questions.Where(q => q.QuestionSetId == questionSetId && !q.IsDeleted),
                h => h.QuestionId,
                q => q.Id,
                (h, q) => q)
            .CountAsync(cancellationToken);

        var questionSet = (await _context.QuestionSets
            .Where(q => q.Id == questionSetId && !q.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken));

        var totalQuestionCount = questionSet?.QuestionCount ?? 0;

        // Thẻ nháp (chưa có nghĩa) không có đáp án để học: bỏ ra rồi mới lấy đủ số câu (bộ tối đa 500 câu nên đọc hết rồi lọc là chấp nhận được)
        var result = (await query.ToListAsync(cancellationToken))
            .Where(x => FlashcardQuizBuilder.IsPlayable(x.Question))
            .Take(questionCount)
            .ToList();
        // Thẻ ghi nhớ cần các thẻ khác trong bộ để dựng đáp án nhiễu
        List<Question>? flashcardPool = null;
        if (result.Any(x => x.Question.Type == QuestionType.LanguageFlashcard))
        {
            flashcardPool = await _context.Questions
                .Where(q => q.QuestionSetId == questionSetId && !q.IsDeleted && q.Type == QuestionType.LanguageFlashcard)
                .ToListAsync(cancellationToken);
        }

        var questionDtos = result
            .Select(x => QuestionResponseDto.Mapper.FromEntity(x.Question, x.IsCorrect, flashcardPool: flashcardPool))
            .ToList();

        return (questionDtos, totalQuestionCount, completedQuestionCount);
    }
}
