using CleanArchitectureBase.Application.QuestionSets.Dtos;

namespace CleanArchitectureBase.Application.Tests.Service;

public static class TestQuestionRules
{
    /// <summary>Không chọn trùng một câu hỏi có sẵn (QuestionId) trong cùng một bài test (TestQuestions unique TestId + QuestionId)</summary>
    public static bool HasDistinctQuestionIds(List<CreateUpdateQuestionDto>? questions)
    {
        if (questions == null) return true;
        var ids = questions.Where(q => q.QuestionId.HasValue).Select(q => q.QuestionId!.Value).ToList();
        return ids.Count == ids.Distinct().Count();
    }
}
