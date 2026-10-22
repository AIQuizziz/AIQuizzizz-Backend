using CleanArchitectureBase.Application.MediaFiles;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Tests.Service;

/// <summary>
/// Câu hỏi trong bài test luôn là bản sao riêng của bài (QuestionSetId = null), không dùng chung với bộ câu hỏi:
/// sửa bộ câu hỏi không làm đổi đề đã giao, sửa đề không làm đổi bộ câu hỏi.
/// Bản sao ghi lại câu gốc (SourceQuestionId) và phiên bản câu gốc lúc chép (SourceVersion).
/// </summary>
public static class TestQuestionFactory
{
    /// <summary>Tạo câu hỏi mới cho bài test từ nội dung client gửi lên; <paramref name="source"/> là câu gốc nếu chọn từ bộ câu hỏi</summary>
    public static Question Create(CreateUpdateQuestionDto dto, IReadOnlyDictionary<Guid, MediaType> mediaMap, Question? source)
    {
        var question = new Question
        {
            Id = Guid.NewGuid(),
            Type = Enum.Parse<QuestionType>(dto.Type!),
        };
        Apply(question, dto, mediaMap);

        if (source != null)
        {
            question.SourceQuestionId = source.Id;
            question.SourceVersion = source.Version;
        }
        return question;
    }

    /// <summary>Ghi nội dung client gửi lên vào câu hỏi của bài test (sửa tại chỗ)</summary>
    public static void Apply(Question question, CreateUpdateQuestionDto dto, IReadOnlyDictionary<Guid, MediaType> mediaMap)
    {
        question.Type = Enum.Parse<QuestionType>(dto.Type!);
        question.QuestionText = dto.QuestionText;
        question.ExplainText = dto.ExplainText;
        question.TextFormat = TextFormatHelper.Resolve(dto.TextFormat, TextFormat.PlainText);
        question.Score = dto.Score;
        question.DataJson = CreateUpdateQuestionDto.Serializer.Serialize(dto);
        question.ApplyMedia(dto, mediaMap);
    }

    /// <summary>Hash nội dung của dto (để so với câu đang có mà không phải tạo câu hỏi)</summary>
    public static string HashOf(CreateUpdateQuestionDto dto, IReadOnlyDictionary<Guid, MediaType> mediaMap)
    {
        var probe = new Question { Type = Enum.Parse<QuestionType>(dto.Type!) };
        Apply(probe, dto, mediaMap);
        return QuestionContent.Hash(probe);
    }

    /// <summary>"Cập nhật theo bản gốc": chép nội dung hiện tại của câu gốc vào bản sao trong bài test</summary>
    public static void CopyContentFrom(Question target, Question source)
    {
        target.Type = source.Type;
        target.QuestionText = source.QuestionText;
        target.ExplainText = source.ExplainText;
        target.TextFormat = source.TextFormat;
        target.DataJson = source.DataJson;
        target.MediaId = source.MediaId;
        target.MediaType = source.MediaType;
        target.SourceQuestionId = source.Id;
        target.SourceVersion = source.Version;
    }
}
