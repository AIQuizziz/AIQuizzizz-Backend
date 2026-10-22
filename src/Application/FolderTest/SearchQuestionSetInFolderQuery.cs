using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Domain.Constants;

namespace CleanArchitectureBase.Application.FolderTest;

public class QuestionSetInFolderDto
{
    public Guid QuestionSetId { get; set; }
    public string? FolderName { get; set; }
    public string? Name { get; set; }
    public string? VisibilityMode { get; set; }
    public int NumberOfQuestion { get; set; }
    public DateTime? DateCreated { get; set; }
    public string? CreateBy { get; set; }
}

public class SearchQuestionSetInFolderDto
{
    public string? FolderName { get; set; }
    public PaginatedList<QuestionSetInFolderDto> QuestionSets { get; set; } = null!;
}

[Authorize]
public class SearchQuestionSetInFolderQuery : IRequest<SearchQuestionSetInFolderDto>
{
    /// <summary>
    /// Id of the folder want to retrieve question sets
    /// </summary>
    public Guid? FolderId { get; set; }
    public string? QuestionSetName { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 5;
}

public class SearchQuestionSetInFolderQueryValidator : AbstractValidator<SearchQuestionSetInFolderQuery>
{
    public SearchQuestionSetInFolderQueryValidator()
    {
        RuleFor(x => x.FolderId)
            .NotEmpty().WithMessage("FolderId không được null");
    }
}

public class SearchQuestionSetInFolderQueryHandler : IRequestHandler<SearchQuestionSetInFolderQuery, SearchQuestionSetInFolderDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;

    public SearchQuestionSetInFolderQueryHandler(IApplicationDbContext context, IUser user)
    {
        _context = context;
        _user = user;
    }

    /// <summary>
    /// The function searches question sets in a folder by name, returning a paginated list
    /// </summary>
    /// <param name="rq">Request contains FolderId, QuestionSetName, PageNumber, and PageSize information</param>
    /// <param name="cancellationToken">Token to cancel the task</param>
    public async Task<SearchQuestionSetInFolderDto> Handle(SearchQuestionSetInFolderQuery rq, CancellationToken cancellationToken)
    {
        var accessUser = await _context.FolderUsers
            .Include(x => x.Folder)
            .Where(x => x.FolderId == rq.FolderId && x.UserId == _user.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (accessUser == null)
            throw new ErrorCodeException(ErrorCodes.USER_NOT_HAVE_PERMISSION_IN_FOLDER, "User không có quyền trong folder");

        accessUser.IsViewed = true;

        var questionSets = _context.FolderQuestionSets
            .Where(f => f.FolderId == rq.FolderId && f.Folder != null && f.Folder.IsDeleted == false && f.QuestionSet!.IsDeleted == false
                        && (string.IsNullOrEmpty(rq.QuestionSetName) || f.QuestionSet!.Name.ToLower().Contains(rq.QuestionSetName.ToLower())))
            .OrderByDescending(f => f.Created)
            .Select(f => new QuestionSetInFolderDto
            {
                QuestionSetId = f.QuestionSet!.Id,
                Name = f.QuestionSet.Name,
                FolderName = f.Folder!.Name,
                VisibilityMode = f.QuestionSet.VisibilityMode.ToString(),
                NumberOfQuestion = f.QuestionSet.QuestionCount,
                DateCreated = f.QuestionSet.Created.UtcDateTime,
                CreateBy = f.QuestionSet.CreatedByUser!.FullName,
            });

        var page = await PaginatedList<QuestionSetInFolderDto>.CreateAsync(
            questionSets,
            rq.PageNumber,
            rq.PageSize
        );

        await _context.SaveChangesAsync(cancellationToken);

        return new SearchQuestionSetInFolderDto
        {
            FolderName = accessUser.Folder!.Name,
            QuestionSets = page
        };
    }
}
