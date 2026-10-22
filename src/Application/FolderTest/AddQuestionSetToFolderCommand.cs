using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.FolderTest.Service;
using CleanArchitectureBase.Application.QuestionSets.Services;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.FolderTest;

[Authorize]
public class AddQuestionSetToFolderCommand : IRequest<Guid>
{
    public Guid FolderId { get; set; }
    public Guid QuestionSetId { get; set; }
}

public class AddQuestionSetToFolderCommandValidator : AbstractValidator<AddQuestionSetToFolderCommand>
{
    public AddQuestionSetToFolderCommandValidator()
    {
        RuleFor(x => x.FolderId)
            .NotEmpty().WithMessage("FolderId không được trống");

        RuleFor(x => x.QuestionSetId)
            .NotEmpty().WithMessage("QuestionSetId không được trống");
    }
}

public class AddQuestionSetToFolderCommandHandler : IRequestHandler<AddQuestionSetToFolderCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly IFolderTestService _folderTestService;
    private readonly IQuestionSetService _questionSetService;
    private readonly IUser _user;

    public AddQuestionSetToFolderCommandHandler(
        IApplicationDbContext context,
        IFolderTestService folderTestService,
        IQuestionSetService questionSetService,
        IUser user)
    {
        _context = context;
        _folderTestService = folderTestService;
        _questionSetService = questionSetService;
        _user = user;
    }

    public async Task<Guid> Handle(AddQuestionSetToFolderCommand rq, CancellationToken cancellationToken)
    {
        var folder = await _context.Folders
            .Where(x => x.Id == rq.FolderId && x.IsDeleted == false)
            .FirstOrDefaultAsync(cancellationToken);
        if (folder == null)
            throw new ErrorCodeException(ErrorCodes.FOLDER_NOT_FOUND, "Không tìm thấy folder");

        var questionSet = await _questionSetService.GetActiveQuestionSet(rq.QuestionSetId, cancellationToken);
        if (questionSet == null)
            throw new ErrorCodeException(ErrorCodes.QUESTION_SET_NOT_FOUND);

        if (!await _folderTestService.IsOwnerOrEditor(rq.FolderId, cancellationToken))
            throw new ErrorCodeException(ErrorCodes.USER_NOT_HAVE_PERMISSION_IN_FOLDER,
                "User không có quyền trong folder");

        // Thêm bộ vào thư mục = chia sẻ lại cho thành viên thư mục -> chỉ Owner/Editable của bộ câu hỏi
        if (!await _questionSetService.CanUserEditQuestionSet(_user.UserId!.Value, questionSet.Id))
            throw new ErrorCodeException(ErrorCodes.USER_NOT_ACCESS_TO_QUESTION_SET, "User không có quyền dùng bộ câu hỏi này");

        var exists = await _context.FolderQuestionSets
            .AnyAsync(x => x.FolderId == rq.FolderId && x.QuestionSetId == rq.QuestionSetId, cancellationToken);
        if (exists)
            throw new ErrorCodeException(ErrorCodes.QUESTION_SET_ALREADY_EXISTS_IN_FOLDER, "Bộ câu hỏi đã có trong folder");

        _context.FolderQuestionSets.Add(new FolderQuestionSet { FolderId = rq.FolderId, QuestionSetId = rq.QuestionSetId });

        await _context.SaveChangesAsync(cancellationToken);

        return questionSet.Id;
    }
}
