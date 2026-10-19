using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Extension;

public class WritableQuestionSetDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
}

/// <summary>Các bộ câu hỏi người dùng được phép thêm câu (chủ sở hữu hoặc được chia sẻ quyền sửa) - dùng cho ô chọn bộ đích của extension.</summary>
[Authorize]
public class GetWritableQuestionSetsQuery : IRequest<List<WritableQuestionSetDto>>
{
    public string? Search { get; set; }
}

public class GetWritableQuestionSetsQueryHandler : IRequestHandler<GetWritableQuestionSetsQuery, List<WritableQuestionSetDto>>
{
    private const int MaxResults = 200;

    private readonly IApplicationDbContext _context;
    private readonly IUser _user;

    public GetWritableQuestionSetsQueryHandler(IApplicationDbContext context, IUser user)
    {
        _context = context;
        _user = user;
    }

    public async Task<List<WritableQuestionSetDto>> Handle(GetWritableQuestionSetsQuery request, CancellationToken cancellationToken)
    {
        var userId = _user.UserId!.Value;

        var query = _context.QuestionSetUsers
            .Where(x => x.UserId == userId
                        && (x.ShareMode == QuestionSetUserShareMode.Owner || x.ShareMode == QuestionSetUserShareMode.Editable)
                        && !x.QuestionSet!.IsDeleted)
            .Select(x => x.QuestionSet!);

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var lowered = search.ToLower();
            query = query.Where(q => q.Name.ToLower().Contains(lowered)); // EF tự thoát ký tự đặc biệt của LIKE
        }

        return await query
            .OrderByDescending(q => q.LastModified)
            .Take(MaxResults)
            .Select(q => new WritableQuestionSetDto { Id = q.Id, Name = q.Name, QuestionCount = q.QuestionCount })
            .ToListAsync(cancellationToken);
    }
}
