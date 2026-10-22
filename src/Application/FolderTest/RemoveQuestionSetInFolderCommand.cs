using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.FolderTest.Service;
using CleanArchitectureBase.Domain.Constants;

namespace CleanArchitectureBase.Application.FolderTest;

[Authorize]
public class RemoveQuestionSetInFolderCommand : IRequest<Guid>
{
    public Guid FolderId { get; set; }
    public Guid QuestionSetId { get; set; }
}

public class RemoveQuestionSetInFolderCommandValidator : AbstractValidator<RemoveQuestionSetInFolderCommand>
{
    public RemoveQuestionSetInFolderCommandValidator()
    {
        RuleFor(x => x.FolderId)
            .NotEmpty().WithMessage("FolderId không được trống");

        RuleFor(x => x.QuestionSetId)
            .NotEmpty().WithMessage("QuestionSetId không được trống");
    }
}

public class RemoveQuestionSetInFolderCommandHandler : IRequestHandler<RemoveQuestionSetInFolderCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly IFolderTestService _folderTestService;

    public RemoveQuestionSetInFolderCommandHandler(
        IApplicationDbContext context,
        IFolderTestService folderTestService)
    {
        _context = context;
        _folderTestService = folderTestService;
    }

    public async Task<Guid> Handle(RemoveQuestionSetInFolderCommand rq, CancellationToken cancellationToken)
    {
        var folder = await _context.Folders
            .Where(x => x.Id == rq.FolderId && x.IsDeleted == false)
            .FirstOrDefaultAsync(cancellationToken);
        if (folder == null)
            throw new ErrorCodeException(ErrorCodes.FOLDER_NOT_FOUND, "Không tìm thấy folder");

        if (!await _folderTestService.IsOwnerOrEditor(rq.FolderId, cancellationToken))
            throw new ErrorCodeException(ErrorCodes.USER_NOT_HAVE_PERMISSION_IN_FOLDER,
                "User không có quyền trong folder");

        var link = await _context.FolderQuestionSets
            .Where(x => x.FolderId == rq.FolderId && x.QuestionSetId == rq.QuestionSetId)
            .FirstOrDefaultAsync(cancellationToken);
        if (link == null)
            throw new ErrorCodeException(ErrorCodes.QUESTION_SET_NOT_FOUND_IN_FOLDER, "Bộ câu hỏi không có trong folder");

        _context.FolderQuestionSets.Remove(link);

        await _context.SaveChangesAsync(cancellationToken);

        return rq.QuestionSetId;
    }
}
