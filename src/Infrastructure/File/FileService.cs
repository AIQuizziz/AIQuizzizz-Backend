using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.TestTemplates;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;
using ExcelDataReader;

namespace CleanArchitectureBase.Infrastructure.File;

public class FileService : IFileService
{
    public async Task<ImportedQuestionDto> GetQuestionFromFile(FileStreamData file)
    {
        try
        {
            return await ParseQuestionsFromFile(file);
        }
        catch (ErrorCodeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // File hỏng / không phải Excel hợp lệ (ZIP lỗi, thiếu sheet, dữ liệu rỗng...) -> 400 thay vì 500
            throw new ErrorCodeException(ErrorCodes.INVALID_FILE_FORMAT,
                $"File Excel không đọc được hoặc đã bị hỏng ({ex.GetType().Name})");
        }
    }

    private static async Task<ImportedQuestionDto> ParseQuestionsFromFile(FileStreamData file)
    {
        var validQuestions = new List<CreateUpdateQuestionDto>();
        var invalidQuestions = new List<CreateUpdateQuestionDto>();
        var validator = new CreateUpdateQuestionDto.QuestionCreateDtoValidator();
        
        using (var stream = new MemoryStream())
        { 
            await file.Data!.CopyToAsync(stream);
            
            stream.Position = 0;
            
            // Đăng ký encoding provider để hỗ trợ 1252
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                reader.Read();
                
                var headers = new List<string>();
                
                for (int col = 0; col < reader.FieldCount; col++)
                {
                    var headerValue = reader.GetValue(col)?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(headerValue))
                    {
                        headers.Add(headerValue);
                    }
                }
                
                if (!headers.Contains("Question Type") || !headers.Contains("Question Text") || !headers.Contains("Score"))
                {
                    throw new ErrorCodeException(ErrorCodes.INVALID_FILE_FORMAT, "File Excel không đúng định dạng: Thiếu cột bắt buộc");
                }

                while (reader.Read())
                {
                    var question = new CreateUpdateQuestionDto
                    {
                        MultipleChoices = new List<CreateMultipleChoiceDto>(),
                        MatchingPairs = new List<CreateMatchingPairDto>(),
                        OrderingItems = new List<CreateOrderingItemDto>(),
                        Score = 0
                    };
                    
                    question.Type = NormalizeQuestionType(reader.GetValue(headers.IndexOf("Question Type"))?.ToString());
                    question.QuestionText = reader.GetValue(headers.IndexOf("Question Text"))?.ToString();

                    // Cột tuỳ chọn "Text Format" (PlainText | Html | Markdown). Không có cột / ô trống = hành vi cũ.
                    var textFormatIndex = headers.IndexOf("Text Format");
                    if (textFormatIndex >= 0)
                        question.TextFormat = TextFormatHelper.Normalize(reader.GetValue(textFormatIndex)?.ToString());

                    try
                    {
                        question.Score = Convert.ToSingle(reader.GetValue(headers.IndexOf("Score")));
                    }
                    catch (FormatException)
                    {
                        question.Score = -1; 
                    }
                    
                    var options = new List<string>();
                    for (int i = 1; i <= 10; i++)
                    {
                        var optionIndex = headers.IndexOf($"Option {i}");

                        if (optionIndex >= 0)
                        {
                            var optValue = reader.GetValue(optionIndex)?.ToString()?.Trim();
                            
                            if (!string.IsNullOrEmpty(optValue))
                                options.Add(optValue);
                            else
                                options.Add("");
                        }
                    }

                    switch (question.Type)
                        {
                            case nameof(QuestionType.MultipleChoice):
                                foreach (var opt in options)
                                {
                                    bool isAnswer = opt.StartsWith("*");
                                    string text = isAnswer ? opt.Substring(1).Trim() : opt.Trim();
                                    if (text.Length > 0)
                                    {
                                        question.MultipleChoices.Add(new CreateMultipleChoiceDto { Text = text, IsAnswer = isAnswer });
                                    }
                                }
                                break;
                            
                            case nameof(QuestionType.Matching):
                                for (int i = 0; i < options.Count; i += 2)
                                {
                                    if (i + 1 < options.Count && options[i + 1].Length > 0 && options[i].Length > 0)
                                    {
                                        question.MatchingPairs.Add(new CreateMatchingPairDto
                                        {
                                            LeftItem = options[i],
                                            RightItem = options[i + 1]
                                        });
                                    }
                                }
                                break;
                            
                            case nameof(QuestionType.Ordering):
                                int order = 0;
                                
                                for (int i = 0; i < options.Count; i++)
                                {
                                    if (options[i].Length > 0)
                                    {
                                        question.OrderingItems.Add(new CreateOrderingItemDto { Text = options[i], CorrectOrder = order });

                                        order++;
                                    }
                                }
                                break;
                            
                            case nameof(QuestionType.ShortText):
                                for (int i = 0; i < options.Count; i++)
                                {
                                    if (options[i].Length > 0)
                                    {
                                        question.ShortAnswer = options[i];
                                    }
                                }
                                break;
                            
                            default:
                                invalidQuestions.Add(question);
                                continue;
                        }
                    
                    var validationResult = validator.Validate(question);
                    
                    if (validationResult.IsValid)
                    {
                        validQuestions.Add(question);
                    }
                    else
                    {
                        invalidQuestions.Add(question);
                    }
                }
            }
        }
        
        return new ImportedQuestionDto
        {
            validQuestions = validQuestions,
            invalidQuestions = invalidQuestions
        };
    }
    
    private static string? NormalizeQuestionType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;

        type = type.Trim();
        if (!Enum.TryParse<QuestionType>(type, ignoreCase: true, out _))
        {
            return null;
        }

        return type;
    }
}
