using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.Tests.Service;
using CleanArchitectureBase.Domain.Entities;
using FluentAssertions;
using NUnit.Framework;

namespace CleanArchitectureBase.Application.UnitTests.Tests;

public class TestVersionBuilderTests
{
    private static readonly Guid TestId = Guid.NewGuid();

    private static TestVersion Version(int no) => new() { Id = Guid.NewGuid(), TestId = TestId, No = no };

    [Test]
    public void Build_FirstVersionKeepsOriginalOrder_OthersContainSameQuestions()
    {
        var ids = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToList();
        // truyền lộn thứ tự: bản đầu tiên phải là bản có No nhỏ nhất
        var versions = new List<TestVersion> { Version(2), Version(0), Version(1) };

        var rows = TestVersionBuilder.Build(ids, versions, new Random(1));

        rows.Should().HaveCount(ids.Count * versions.Count);
        foreach (var version in versions)
        {
            var ordered = rows.Where(r => r.TestVersionId == version.Id).OrderBy(r => r.Order).ToList();
            ordered.Select(r => r.Order).Should().Equal(Enumerable.Range(0, ids.Count));
            ordered.Select(r => r.QuestionId).Should().BeEquivalentTo(ids);
            if (version.No == 0)
                ordered.Select(r => r.QuestionId).Should().Equal(ids);
        }

        var shuffled = rows.Where(r => r.TestVersionId == versions[0].Id).OrderBy(r => r.Order).Select(r => r.QuestionId);
        shuffled.Should().NotEqual(ids, "30 câu trộn ngẫu nhiên gần như không thể trùng thứ tự gốc");
    }

    [Test]
    public void Resize_AddsAfterHighestNo_RemovesHighestNoFirst()
    {
        var current = new List<TestVersion> { Version(0), Version(1), Version(3) };

        var grow = TestVersionBuilder.Resize(TestId, current, 5);
        grow.Removed.Should().BeEmpty();
        grow.Added.Select(v => v.No).Should().Equal(4, 5);
        grow.Kept.Should().HaveCount(5);
        grow.Added.Should().OnlyContain(v => v.TestId == TestId);

        var shrink = TestVersionBuilder.Resize(TestId, current, 1);
        shrink.Kept.Select(v => v.No).Should().Equal(0);
        shrink.Removed.Select(v => v.No).Should().BeEquivalentTo(new[] { 1, 3 });
        shrink.Added.Should().BeEmpty();
    }

    [Test]
    public void HasDistinctQuestionIds_RejectsDuplicates_IgnoresNewQuestions()
    {
        var id = Guid.NewGuid();
        TestQuestionRules.HasDistinctQuestionIds(new List<CreateUpdateQuestionDto>
        {
            new() { QuestionId = id }, new() { QuestionId = null }, new() { QuestionId = null }
        }).Should().BeTrue();
        TestQuestionRules.HasDistinctQuestionIds(new List<CreateUpdateQuestionDto>
        {
            new() { QuestionId = id }, new() { QuestionId = id }
        }).Should().BeFalse();
        TestQuestionRules.HasDistinctQuestionIds(null).Should().BeTrue();
    }
}
