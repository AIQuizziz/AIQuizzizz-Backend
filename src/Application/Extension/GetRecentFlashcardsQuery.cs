using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Extension;

public class RecentFlashcardDto
{
    public Guid QuestionId { get; set; }
    public Guid QuestionSetId { get; set; }
    public string QuestionSetName { get; set; } = string.Empty;
    public string Front { get; set; } = string.Empty;
    /// <summary>Nghĩa ngắn (bản dịch nếu có, không thì định nghĩa); null nếu thẻ chưa có nghĩa</summary>
    public string? Preview { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Các thẻ flashcard người dùng thêm gần đây nhất (mới nhất trước) - nguồn dữ liệu của tab "Vừa thêm" trong extension.
/// Chỉ gồm thẻ do chính người dùng tạo, chưa bị xoá, trong bộ chưa bị xoá và người dùng còn quyền sửa (để nút Xóa dùng được).
/// </summary>
[Authorize]
public class GetRecentFlashcardsQuery : IRequest<List<RecentFlashcardDto>>
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;

    public int? Limit { get; set; }
}

public class GetRecentFlashcardsQueryValidator : AbstractValidator<GetRecentFlashcardsQuery>
{
    public GetRecentFlashcardsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, GetRecentFlashcardsQuery.MaxLimit).When(x => x.Limit.HasValue)
            .WithMessage($"Limit phải từ 1 đến {GetRecentFlashcardsQuery.MaxLimit}");
    }
}

public class GetRecentFlashcardsQueryHandler : IRequestHandler<GetRecentFlashcardsQuery, List<RecentFlashcardDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;

    public GetRecentFlashcardsQueryHandler(IApplicationDbContext context, IUser user)
    {
        _context = context;
        _user = user;
    }

    public async Task<List<RecentFlashcardDto>> Handle(GetRecentFlashcardsQuery request, CancellationToken cancellationToken)
    {
        var userId = _user.UserId!.Value;
        var limit = request.Limit ?? GetRecentFlashcardsQuery.DefaultLimit;

        var rows = await (
                from q in _context.Questions
                join qs in _context.QuestionSets on q.QuestionSetId equals qs.Id
                join access in _context.QuestionSetUsers on qs.Id equals access.QuestionSetId
                where q.Type == QuestionType.LanguageFlashcard
                      && !q.IsDeleted
                      && !qs.IsDeleted
                      && q.CreatedBy == userId
                      && access.UserId == userId
                      && (access.ShareMode == QuestionSetUserShareMode.Owner || access.ShareMode == QuestionSetUserShareMode.Editable)
                orderby q.Created descending, q.Id
                select new { QuestionId = q.Id, SetId = qs.Id, SetName = qs.Name, q.QuestionText, q.DataJson, q.Created })
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(r =>
        {
            var card = FlashcardQuizBuilder.Parse(r.DataJson);
            return new RecentFlashcardDto
            {
                QuestionId = r.QuestionId,
                QuestionSetId = r.SetId,
                QuestionSetName = r.SetName,
                Front = r.QuestionText ?? string.Empty,
                Preview = card == null ? null : FlashcardQuizBuilder.AnswerText(card),
                CreatedAt = r.Created
            };
        }).ToList();
    }
}
