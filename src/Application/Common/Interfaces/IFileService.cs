using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Tests;

namespace CleanArchitectureBase.Application.Common.Interfaces;

public interface IFileService
{
    Task<ImportedQuestionDto> GetQuestionFromFile(FileStreamData fileData);
}
