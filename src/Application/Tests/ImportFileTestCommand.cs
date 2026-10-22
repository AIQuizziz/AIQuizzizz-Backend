using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Domain.Constants;

namespace CleanArchitectureBase.Application.Tests;

public class ImportedQuestionDto
{
    public List<CreateUpdateQuestionDto>? validQuestions { get; set; }
    public List<CreateUpdateQuestionDto>? invalidQuestions { get; set; }
}

[Authorize]
public class ImportFileTestCommand : IRequest<ImportedQuestionDto>
{
    public FileStreamData? FileData { get; set; }
}

public class ImportFileTestCommandValidator : AbstractValidator<ImportFileTestCommand>
{
    public ImportFileTestCommandValidator()
    {
        RuleFor(x => x.FileData)
            .NotNull().WithMessage("FileData không được trống")
            .DependentRules(() =>
            {
                RuleFor(x => x.FileData!.Data)
                    .NotNull().WithMessage("Dữ liệu stream không được trống")
                    .Must(stream => stream!.Length > 0)
                    .WithMessage("Stream không được rỗng");
            });
    }
}

public class ImportFileTestCommandHandler : IRequestHandler<ImportFileTestCommand, ImportedQuestionDto>
{
    private readonly IFileService _file;

    public ImportFileTestCommandHandler(IFileService file)
    {
        _file = file;
    }
    
    /// <summary>
    /// The function imports questions from an Excel file for a test and returns the imported questions
    /// </summary>
    /// <param name="rq">Request contains FileData information</param>
    /// <param name="cancellationToken">Token to cancel the task</param>
    public Task<ImportedQuestionDto> Handle(ImportFileTestCommand rq, CancellationToken cancellationToken)
    {
        var file = rq.FileData;
        
        if (file == null || file.Data!.Length == 0)
        {
            throw new ErrorCodeException(ErrorCodes.FILE_NOT_FOUND, "Không tìm thấy file hoăc rỗng");
        }
        
        var fileFormat = Path.GetExtension(file.FileName);

        if (fileFormat != ".xlsx" && fileFormat != ".xls")
        {
            throw new ErrorCodeException(ErrorCodes.ERROR_FORMAT_FILE, "File không đúng định dạng");
        }

        var importQuestions = _file.GetQuestionFromFile(file);
        
        return importQuestions;
    }
}
