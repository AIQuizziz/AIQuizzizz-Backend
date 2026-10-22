using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Tests.Service;

/// <summary>
/// Sinh câu hỏi cho các bản trộn (TestVersion) từ bản gốc của bài test (TestQuestions).
/// Bản trộn chỉ là thứ tự phát cho học sinh; danh sách câu, thứ tự gốc và điểm luôn đọc từ TestQuestions.
/// </summary>
public static class TestVersionBuilder
{
    /// <summary>
    /// Bản trộn đầu tiên (No nhỏ nhất) giữ đúng thứ tự gốc, các bản còn lại trộn ngẫu nhiên
    /// (giữ hành vi cũ: chọn 1 bản trộn thì học sinh làm theo đúng thứ tự giáo viên soạn).
    /// </summary>
    public static List<TestVersionQuestion> Build(IReadOnlyList<Guid> orderedQuestionIds, IEnumerable<TestVersion> versions, Random random)
    {
        return versions
            .OrderBy(v => v.No)
            .SelectMany((version, versionIndex) =>
            {
                var indexes = Enumerable.Range(0, orderedQuestionIds.Count).ToList();
                if (versionIndex > 0)
                    indexes = indexes.OrderBy(_ => random.Next()).ToList();

                return indexes.Select((questionIndex, order) => new TestVersionQuestion
                {
                    Id = Guid.NewGuid(),
                    TestVersionId = version.Id,
                    QuestionId = orderedQuestionIds[questionIndex],
                    Order = order
                });
            })
            .ToList();
    }

    /// <summary>Tạo thêm / bỏ bớt bản trộn cho đủ <paramref name="count"/> bản; bỏ các bản có No lớn nhất trước</summary>
    public static (List<TestVersion> Kept, List<TestVersion> Added, List<TestVersion> Removed) Resize(
        Guid testId, IReadOnlyCollection<TestVersion> current, int count)
    {
        var ordered = current.OrderBy(v => v.No).ToList();
        var kept = ordered.Take(count).ToList();
        var removed = ordered.Skip(count).ToList();

        var nextNo = ordered.Count == 0 ? 0 : ordered.Max(v => v.No) + 1;
        var added = Enumerable.Range(0, Math.Max(0, count - kept.Count))
            .Select(i => new TestVersion { Id = Guid.NewGuid(), TestId = testId, No = nextNo + i })
            .ToList();

        return (kept.Concat(added).ToList(), added, removed);
    }
}
