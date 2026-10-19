using System.Text.Json;
using CleanArchitectureBase.Application.MediaFiles.Dtos;
using CleanArchitectureBase.Application.Questions.Dtos;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.QuestionSets.Dtos;

public class CreateMultipleChoiceDto
{
    public string? Text { get; set; }
    public bool IsAnswer { get; set; }
}

public class CreateMatchingPairDto
{
    public string? LeftItem { get; set; }
    public string? RightItem { get; set; }
}

public class CreateOrderingItemDto
{
    public string? Text { get; set; }
    public int CorrectOrder { get; set; }
}

public class CreateUpdateQuestionDto
{
    public Guid? QuestionId { get; set; }
    public string? Type { get; set; }
    public string? QuestionText { get; set; }
    public string? ExplainText { get; set; }
    /// <summary>"PlainText" | "Html" | "Markdown". Không gửi = giữ hành vi cũ (Html cho câu mới / câu cũ giữ nguyên mặc định của từng lệnh).</summary>
    public string? TextFormat { get; set; }
    public float Score { get; set; } = 10;
    public List<CreateMultipleChoiceDto>? MultipleChoices { get; set; }
    public List<CreateMatchingPairDto>? MatchingPairs { get; set; }
    public List<CreateOrderingItemDto>? OrderingItems { get; set; }
    public string? ShortAnswer { get; set; }
    /// <summary>Id media (ảnh/video) đã upload qua /api/Media/Upload</summary>
    public Guid? MediaId { get; set; }
    /// <summary>Chỉ dùng cho response (preview media khi copy/sửa), bị bỏ qua khi gửi lên</summary>
    public QuestionMediaDto? Media { get; set; }

    public class QuestionCreateDtoValidator : AbstractValidator<CreateUpdateQuestionDto>
    {
        public QuestionCreateDtoValidator()
        {
            RuleFor(x => x.Type)
                .NotEmpty().WithMessage($"Loại câu hỏi không được để trống")
                .Must(type => new[]
                {
                    "MultipleChoice", "Matching", "Ordering", "ShortText"
                }.Contains(type))
                .WithMessage($"Loại câu hỏi phải là 'MultipleChoice', 'Matching', 'Ordering','ShortText'");

            RuleFor(x => x)
                .Must(question => question.Type switch
                {
                    nameof(QuestionType.MultipleChoice) => question.MultipleChoices != null && question.MultipleChoices.Any(),
                    nameof(QuestionType.Matching) => question.MatchingPairs != null && question.MatchingPairs.Any(),
                    nameof(QuestionType.Ordering) => question.OrderingItems != null && question.OrderingItems.Any(),
                    nameof(QuestionType.ShortText) => !string.IsNullOrWhiteSpace(question.ShortAnswer),
                    _ => false
                })
                .WithMessage("Một hoặc nhiều câu hỏi có loại hoặc dữ liệu không hợp lệ");


            RuleFor(x => x.QuestionText)
                .NotEmpty().WithMessage($"Nội dung câu hỏi không được để trống")
                .MaximumLength(5000).WithMessage($"Nội dung câu hỏi không được vượt quá 500 ký tự"); //Tăng số ký tự cho phép cho câu hỏi

            RuleFor(x => x.ExplainText)
                .MaximumLength(5000).WithMessage($" Giải thích không được vượt quá 1000 ký tự");

            RuleFor(x => x.TextFormat)
                .Must(TextFormatHelper.IsValid)
                .WithMessage("Định dạng nội dung phải là 'PlainText', 'Html' hoặc 'Markdown'");

            RuleFor(x => x.Score)
                .GreaterThanOrEqualTo(0).WithMessage($"Điểm phải lớn hơn hoặc bằng 0")
                .LessThanOrEqualTo(999).WithMessage($"Điểm không được vượt quá 999");

            RuleFor(x => x.QuestionId)
                .Must(id => !id.HasValue || (id.Value != Guid.Empty && id.Value != default(Guid)))
                .WithMessage("QuestionId phải là một Guid hợp lệ hoặc null");

            // Validate cho MultipleChoice
            When(x => x.Type == "MultipleChoice", () =>
            {
                RuleFor(x => x.MultipleChoices)
                    .NotEmpty().WithMessage("Phải có ít nhất 2 lựa chọn cho câu hỏi trắc nghiệm")
                    .Must(options => options is { Count: >= 2 }).WithMessage("Phải có ít nhất 2 lựa chọn")
                    .Must(options => options is { Count: <= 10 }).WithMessage("Không được quá 10 lựa chọn")
                    .Must(options => options != null && options.Any(o => o.IsAnswer)).WithMessage($"Phải có ít nhất một lựa chọn đúng")
                    .Must(options => options == null || options
                        .Select(o => (o.Text ?? string.Empty).Trim())
                        .Where(text => text.Length > 0)
                        .GroupBy(text => text)
                        .All(g => g.Count() == 1))
                    .WithMessage("Các lựa chọn không được trùng nội dung");

                RuleForEach(x => x.MultipleChoices)
                    .ChildRules((options) =>
                    {
                        options.RuleFor(o => o.Text)
                            .NotEmpty().WithMessage($"Nội dung không được để trống")
                            .MaximumLength(1000).WithMessage($"Nội dung không được vượt quá 1000 ký tự");
                    });
            });

            // Validate cho Matching
            When(x => x.Type == "Matching", () =>
            {
                RuleFor(x => x.MatchingPairs)
                    .NotEmpty().WithMessage("Phải có ít nhất 2 cặp ghép đôi")
                    .Must(items => items != null && items.Count >= 2).WithMessage("Phải có ít nhất 2 cặp ghép đôi")
                    ;

                RuleForEach(x => x.MatchingPairs)
                    .ChildRules((items) =>
                    {
                        items.RuleFor(i => i.LeftItem)
                            .NotEmpty().WithMessage("Mục bên trái không được để trống")
                            .MaximumLength(1000).WithMessage("Mục bên trái không được vượt quá 1000 ký tự");
                        items.RuleFor(i => i.RightItem)
                            .NotEmpty().WithMessage("Mục bên phải không được để trống")
                            .MaximumLength(1000).WithMessage($"Mục bên phải không được vượt quá 1000 ký tự");
                    });
            });

            // Validate cho Ordering
            When(x => x.Type == "Ordering", () =>
            {
                RuleFor(x => x.OrderingItems)
                    .NotEmpty().WithMessage("Câu hỏi Phải có ít nhất 2 mục để sắp xếp")
                    .Must(items => items != null && items.Count >= 2).WithMessage("Phải có ít nhất 2 mục để sắp xếp")
                    .Must(items => items != null && items.Select(i => i.CorrectOrder).Distinct().Count() == items.Count)
                    .WithMessage("Các thứ tự đúng phải là duy nhất")
                    .Must(items => items != null && items.All(i => i.CorrectOrder >= 0 && i.CorrectOrder < items.Count))
                    .WithMessage("Thứ tự đúng phải nằm trong khoảng từ 0 đến n -1 (Tức là phần tử )");

                RuleForEach(x => x.OrderingItems)
                    .ChildRules(item =>
                    {
                        item.RuleFor(i => i.Text)
                            .NotEmpty().WithMessage($"Nội dung không được để trống")
                            .MaximumLength(1000).WithMessage($"Nội dung không được vượt quá 1000 ký tự");
                    });
            });

            When(x => x.Type == "ShortText", () =>
            {
                RuleFor(x => x.ShortAnswer)
                    .NotEmpty().WithMessage($"Đáp án không được để trống")
                    .MaximumLength(1000).WithMessage($"Đáp án không được vượt quá 500 ký tự");
            });
        }
    }

    public static class Serializer
    {
        /// <summary>
        /// Convert CreateUpdateQuestionDto to JSON
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        /// <exception cref="InvalidDataException"></exception>
        public static string Serialize(CreateUpdateQuestionDto dto)
        {
            return dto.Type switch
            {
                nameof(QuestionType.MultipleChoice) => QuestionTypeSerializer.SerializeMultipleChoice(dto.MultipleChoices!),
                nameof(QuestionType.Matching) => QuestionTypeSerializer.SerializeMatchingPairs(dto.MatchingPairs!),
                nameof(QuestionType.Ordering) => QuestionTypeSerializer.SerializeOrderingItems(dto.OrderingItems!),
                nameof(QuestionType.ShortText) => JsonSerializer.Serialize(new QTypeShortAnswer
                {
                    Answer = dto.ShortAnswer!
                }),
                _ => throw new InvalidDataException($"Invalid question type: {dto.Type}")
            };
        }
    }
    
    /// <summary>
    /// Convert Question entity to CreateUpdateQuestionDto
    /// </summary>
    public static class Deserializer
    {
        public static CreateUpdateQuestionDto Deserialize(Question question)
        {
            var dto = new CreateUpdateQuestionDto
            {
                QuestionId = question.Id,
                Type = question.Type.ToString(),
                QuestionText = question.QuestionText,
                ExplainText = question.ExplainText,
                TextFormat = question.TextFormat.ToString(),
                Score = question.Score,
                MediaId = question.MediaId,
                Media = QuestionMediaDto.From(question.MediaId, question.MediaType)
            };

            if (string.IsNullOrEmpty(question.DataJson))
                return dto;

            switch (question.Type)
            {
                case QuestionType.MultipleChoice:
                    var multipleChoices = JsonSerializer.Deserialize<List<QTypeMultipleChoice>>(question.DataJson);
                    if (multipleChoices != null)
                    {
                        dto.MultipleChoices = QuestionOrderHelper
                            .AuthorOrder(multipleChoices, x => x.Position)
                            .Select(x => new CreateMultipleChoiceDto
                            {
                                Text = x.Text,
                                IsAnswer = x.IsAnswer
                            })
                            .ToList();
                    }
                    break;

                case QuestionType.Matching:
                    var matchingItems = JsonSerializer.Deserialize<List<QTypeMatching>>(question.DataJson);
                    if (matchingItems != null)
                    {
                        // Group items by AnswerId to reconstruct pairs
                        var leftItems = QuestionOrderHelper.AuthorOrder(
                            matchingItems.Where(x => !string.IsNullOrEmpty(x.AnswerId)), x => x.Position);
                        var rightItems = matchingItems.Where(x => string.IsNullOrEmpty(x.AnswerId)).ToList();

                        dto.MatchingPairs = new List<CreateMatchingPairDto>();

                        foreach (var leftItem in leftItems)
                        {
                            var rightItem = rightItems.FirstOrDefault(x => x.Id.ToString() == leftItem.AnswerId);
                            if (rightItem != null)
                            {
                                dto.MatchingPairs.Add(new CreateMatchingPairDto
                                {
                                    LeftItem = leftItem.Text,
                                    RightItem = rightItem.Text
                                });
                            }
                        }
                    }
                    break;

                case QuestionType.Ordering:
                    var orderingItems = JsonSerializer.Deserialize<List<QTypeOrderingItem>>(question.DataJson);
                    if (orderingItems != null)
                    {
                        dto.OrderingItems = QuestionOrderHelper
                            .AuthorOrder(orderingItems, x => x.Position)
                            .Select(x => new CreateOrderingItemDto
                            {
                                Text = x.Text,
                                CorrectOrder = x.CorrectOrder
                            })
                            .ToList();
                    }
                    break;

                case QuestionType.ShortText:
                    var shortAnswer = JsonSerializer.Deserialize<QTypeShortAnswer>(question.DataJson);
                    if (shortAnswer != null)
                    {
                        dto.ShortAnswer = shortAnswer.Answer;
                    }
                    break;
            }

            return dto;
        }
    }

    public static class Compare
    {
        public static bool CompareQuestion(Question question, CreateUpdateQuestionDto questionDto)
        {
            var questionResponseDto = QuestionResponseDto.Mapper.FromEntity(question, true, shuffle: false);

            if (!question.QuestionText!.Trim().ToLower().Equals(questionDto.QuestionText!.Trim().ToLower()))
                return false;

            if (question.Type.ToString() != questionDto.Type)
                return false;

            // Cùng nội dung nhưng khác định dạng (Markdown/Html/PlainText) là câu hỏi khác.
            // Client cũ không gửi TextFormat -> không so sánh (giữ hành vi cũ, tránh tạo bản sao thừa).
            if (!string.IsNullOrWhiteSpace(questionDto.TextFormat)
                && question.TextFormat != TextFormatHelper.Resolve(questionDto.TextFormat, question.TextFormat))
                return false;

            if (question.MediaId != questionDto.MediaId)
                return false;

            return questionDto.Type switch
            {
                nameof(QuestionType.MultipleChoice) =>
                    CompareMultipleChoiceQuestion(questionResponseDto.QuestionData.MultipleChoice, questionDto.MultipleChoices!),
                nameof(QuestionType.Matching) =>
                    CompareMatchingQuestion(questionResponseDto.QuestionData.Matching, questionDto.MatchingPairs!),
                nameof(QuestionType.Ordering) =>
                    CompareOrderingQuestion(questionResponseDto.QuestionData.Ordering, questionDto.OrderingItems!),
                nameof(QuestionType.ShortText) =>
                    string.Equals(questionResponseDto.QuestionData.ShortText?.Trim(), questionDto.ShortAnswer?.Trim(),
                        StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        public static bool CompareMultipleChoiceQuestion(List<MultipleChoiceItemDto>? choiceItems,
            List<CreateMultipleChoiceDto> choiceItemsDto)
        {
            if (choiceItems == null || choiceItems.Count != choiceItemsDto.Count)
            {
                return false;
            }

            var sortedItems = choiceItems.OrderBy(x => x.Text.Trim().ToLower()).ToList();
            var sortedDtos = choiceItemsDto.OrderBy(x => x.Text!.Trim().ToLower()).ToList();

            for (int i = 0; i < sortedItems.Count; i++)
            {
                if (sortedItems[i].Text != sortedDtos[i].Text ||
                    sortedItems[i].IsAnswer != sortedDtos[i].IsAnswer)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool CompareMatchingQuestion(MatchingDataDto? matchingItems,
            List<CreateMatchingPairDto> matchingItemsDto)
        {
            if (matchingItems == null || matchingItems.Matches!.Count != matchingItemsDto.Count)
                return false;

            var leftItems = matchingItems.LeftItems.ToDictionary(x => x.Id, x => x.Text);
            var rightItems = matchingItems.RightItems.ToDictionary(x => x.Id, x => x.Text);

            var matchingDto = matchingItems.Matches
                .Select(q => (
                    leftItems.TryGetValue(q.LeftId, out var left) ? left.Trim().ToLower() : "",
                    rightItems.TryGetValue(q.RightId, out var right) ? right.Trim().ToLower() : ""
                ))
                .Select(p => string.Compare(p.Item1, p.Item2, StringComparison.OrdinalIgnoreCase) <= 0
                    ? p
                    : (p.Item2, p.Item1))
                .OrderBy(x => x.Item1).ThenBy(x => x.Item2)
                .ToList();

            var inputPairs = matchingItemsDto.Select(dto =>
                    string.Compare(dto.LeftItem, dto.RightItem, StringComparison.OrdinalIgnoreCase) <= 0
                        ? (dto.LeftItem?.Trim().ToLower() ?? "", dto.RightItem?.Trim().ToLower() ?? "")
                        : (dto.RightItem?.Trim().ToLower() ?? "", dto.LeftItem?.Trim().ToLower() ?? ""))
                .ToList();

            return matchingDto.OrderBy(x => x.Item1).ThenBy(x => x.Item2)
                .SequenceEqual(inputPairs.OrderBy(x => x.Item1).ThenBy(x => x.Item2));
        }

        public static bool CompareOrderingQuestion(List<OrderingItemDto>? orderingItems,
            List<CreateOrderingItemDto> orderingItemsDto)
        {
            if (orderingItems == null || orderingItems.Count != orderingItemsDto.Count)
                return false;

            var orderItems = orderingItems
                .OrderBy(x => x.CorrectOrder)
                .Select(x => x.Text.Trim().ToLower())
                .ToList();

            var orderItemDto = orderingItemsDto
                .OrderBy(x => x.CorrectOrder)
                .Select(x => x.Text!.Trim().ToLower())
                .ToList();

            var check = orderItems.SequenceEqual(orderItemDto);
            return check;
        }


    }
}
