using CleanArchitectureBase.Application.Classes;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Questions.Dtos;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.Tests;
using CleanArchitectureBase.Application.Tests.Dto;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static CleanArchitectureBase.Application.Command.UnitTests.Testing;

namespace CleanArchitectureBase.Application.Command.UnitTests.Tests.Commands;

/// <summary>Bản gốc của bài test (TestQuestions): thứ tự, điểm riêng theo bài, sinh bản trộn, chấm điểm</summary>
[TestFixture]
public class TestQuestionsTests : BaseTestFixture
{
    private static CreateUpdateQuestionDto Question(string text, float score) => new()
    {
        Type = "MultipleChoice",
        QuestionText = text,
        Score = score,
        MultipleChoices = new List<CreateMultipleChoiceDto>
        {
            new() { Text = "Đúng", IsAnswer = true },
            new() { Text = "Sai", IsAnswer = false }
        }
    };

    private static CreateTestCommand CreateCommand(Guid classId, List<CreateUpdateQuestionDto> questions, int shuffles, DateTime start) => new()
    {
        Name = "Bài kiểm tra bản gốc",
        ClassId = classId,
        TimeLimit = 60,
        StartTime = start,
        EndTime = start.AddDays(1),
        GradeAttemptMethod = "LastAttempt",
        GradeQuestionMethod = "AllOrNothing",
        IsShowCorrectAnswerInReview = true,
        IsAllowReviewAfterSubmit = true,
        NumberOfShuffles = shuffles,
        MaxAttempt = 1,
        PassingScore = 50,
        Questions = questions
    };

    private static UpdateTestCommand UpdateCommand(Guid testId, List<CreateUpdateQuestionDto> questions, int shuffles, List<Guid>? deleteIds = null) => new()
    {
        TestId = testId,
        Name = "Bài kiểm tra bản gốc",
        TimeLimit = 60,
        StartTime = DateTime.UtcNow.AddDays(1),
        EndTime = DateTime.UtcNow.AddDays(2),
        GradeAttemptMethod = "LastAttempt",
        GradeQuestionMethod = "AllOrNothing",
        IsShowCorrectAnswerInReview = true,
        IsAllowReviewAfterSubmit = true,
        NumberOfShuffles = shuffles,
        MaxAttempt = 1,
        PassingScore = 50,
        CreateUpdateQuestions = questions,
        DeleteQuestionIds = deleteIds ?? new List<Guid>()
    };

    private static async Task<List<TestQuestion>> OriginalOf(Guid testId) =>
        (await QueryListAsync<TestQuestion>(q => q.Where(x => x.TestId == testId))).OrderBy(x => x.Order).ToList();

    private static async Task<List<TestVersionQuestion>> VersionRowsOf(Guid testId)
    {
        var versionIds = (await QueryListAsync<TestVersion>(q => q.Where(x => x.TestId == testId))).Select(v => v.Id).ToList();
        return await QueryListAsync<TestVersionQuestion>(q => q.Where(x => versionIds.Contains(x.TestVersionId)));
    }

    /// <summary>Câu hỏi đang có trong bài, dạng gửi lên khi sửa (giống trang sửa của FE)</summary>
    private static List<CreateUpdateQuestionDto> AsEditDtos(TestDetailDto detail) => detail.Questions.Select(q => new CreateUpdateQuestionDto
    {
        QuestionId = q.Id,
        Type = q.Type,
        QuestionText = q.QuestionText,
        TextFormat = q.TextFormat,
        Score = q.Score,
        MultipleChoices = q.QuestionData.MultipleChoice!.Select(c => new CreateMultipleChoiceDto { Text = c.Text, IsAnswer = c.IsAnswer ?? false }).ToList()
    }).ToList();

    [Test]
    public async Task Create_WritesOriginalWithScoresAndOrder_AndBuildsVersionsFromIt()
    {
        await RunAsUserWithPlanAsync();
        var classId = await SendAsync(new CreateClassCommand { Name = "Lớp", Topic = "Test" });
        var questions = Enumerable.Range(1, 5).Select(i => Question($"Câu {i}", i)).ToList();

        var testId = await SendAsync(CreateCommand(classId, questions, 3, DateTime.UtcNow.AddDays(1)));

        var original = await OriginalOf(testId);
        original.Select(x => x.Score).Should().Equal(1f, 2f, 3f, 4f, 5f);
        original.Select(x => x.Order).Should().Equal(0, 1, 2, 3, 4);

        var versions = await QueryListAsync<TestVersion>(q => q.Where(x => x.TestId == testId));
        versions.Should().HaveCount(3);
        var rows = await VersionRowsOf(testId);
        foreach (var version in versions)
            rows.Where(r => r.TestVersionId == version.Id).Select(r => r.QuestionId)
                .Should().BeEquivalentTo(original.Select(x => x.QuestionId));
        var first = versions.OrderBy(v => v.No).First();
        rows.Where(r => r.TestVersionId == first.Id).OrderBy(r => r.Order).Select(r => r.QuestionId)
            .Should().Equal(original.Select(x => x.QuestionId));

        var detail = await SendAsync(new GetTestDetailQuery { TestId = testId });
        detail.Questions.Select(q => q.Score).Should().Equal(1f, 2f, 3f, 4f, 5f);
        detail.TotalScore.Should().Be(15);
        detail.NumberOfShuffles.Should().Be(3);
    }

    [Test]
    public async Task Update_ScoreOnly_KeepsQuestion_UpdatesOriginal()
    {
        await RunAsUserWithPlanAsync();
        var classId = await SendAsync(new CreateClassCommand { Name = "Lớp", Topic = "Test" });
        var testId = await SendAsync(CreateCommand(classId, new() { Question("Câu 1", 1), Question("Câu 2", 2) }, 1, DateTime.UtcNow.AddDays(1)));
        var before = await OriginalOf(testId);

        var dtos = AsEditDtos(await SendAsync(new GetTestDetailQuery { TestId = testId }));
        dtos[0].Score = 7; // chỉ đổi điểm
        await SendAsync(UpdateCommand(testId, dtos, 1));

        var after = await OriginalOf(testId);
        after.Select(x => x.QuestionId).Should().Equal(before.Select(x => x.QuestionId), "đổi điểm không tạo câu mới");
        after.Select(x => x.Score).Should().Equal(7f, 2f);
        (await SendAsync(new GetTestDetailQuery { TestId = testId })).TotalScore.Should().Be(9);
    }

    [Test]
    public async Task Update_ContentChange_Reorder_Delete_AndResizeVersions()
    {
        await RunAsUserWithPlanAsync();
        var classId = await SendAsync(new CreateClassCommand { Name = "Lớp", Topic = "Test" });
        var testId = await SendAsync(CreateCommand(classId, new() { Question("Câu 1", 1), Question("Câu 2", 2), Question("Câu 3", 3) }, 1, DateTime.UtcNow.AddDays(1)));
        var before = await OriginalOf(testId);

        var dtos = AsEditDtos(await SendAsync(new GetTestDetailQuery { TestId = testId }));
        dtos[1].QuestionText = "Câu 2 (đã sửa)";             // sửa nội dung -> câu mới
        var deleted = dtos[2].QuestionId!.Value;              // xoá câu 3
        var request = new List<CreateUpdateQuestionDto> { dtos[1], dtos[0], Question("Câu mới", 4) }; // đảo thứ tự + thêm câu
        await SendAsync(UpdateCommand(testId, request, 3, new() { deleted }));

        var after = await OriginalOf(testId);
        after.Should().HaveCount(3);
        after.Select(x => x.Order).Should().Equal(0, 1, 2);
        after[0].QuestionId.Should().NotBe(before[1].QuestionId, "nội dung đổi nên là câu mới");
        after[1].QuestionId.Should().Be(before[0].QuestionId, "câu không đổi được giữ nguyên");
        after.Select(x => x.QuestionId).Should().NotContain(deleted);
        after.Select(x => x.Score).Should().Equal(2f, 1f, 4f);

        var test = await FindAsync<Test>(testId);
        test!.QuestionCount.Should().Be(3);
        var versions = await QueryListAsync<TestVersion>(q => q.Where(x => x.TestId == testId));
        versions.Should().HaveCount(3);
        var rows = await VersionRowsOf(testId);
        rows.Should().HaveCount(9);
        foreach (var version in versions)
            rows.Where(r => r.TestVersionId == version.Id).Select(r => r.QuestionId)
                .Should().BeEquivalentTo(after.Select(x => x.QuestionId), "mọi bản trộn sinh lại từ bản gốc mới");
        var first = versions.OrderBy(v => v.No).First();
        rows.Where(r => r.TestVersionId == first.Id).OrderBy(r => r.Order).Select(r => r.QuestionId)
            .Should().Equal(after.Select(x => x.QuestionId));

        // giảm số bản trộn
        await SendAsync(UpdateCommand(testId, AsEditDtos(await SendAsync(new GetTestDetailQuery { TestId = testId })), 1));
        (await QueryListAsync<TestVersion>(q => q.Where(x => x.TestId == testId))).Should().HaveCount(1);
        (await VersionRowsOf(testId)).Should().HaveCount(3);
    }

    [Test]
    public async Task Create_RejectsDuplicateQuestionIds()
    {
        await RunAsUserWithPlanAsync();
        var classId = await SendAsync(new CreateClassCommand { Name = "Lớp", Topic = "Test" });
        var id = Guid.NewGuid();
        var q1 = Question("Câu 1", 1); q1.QuestionId = id;
        var q2 = Question("Câu 2", 1); q2.QuestionId = id;

        await FluentActions.Invoking(() => SendAsync(CreateCommand(classId, new() { q1, q2 }, 1, DateTime.UtcNow.AddDays(1))))
            .Should().ThrowAsync<ErrorCodeException>();
    }

    [Test]
    public async Task Submit_GradesWithScoreFromOriginal_NotQuestionScore()
    {
        await RunAsUserWithPlanAsync();
        var classId = await SendAsync(new CreateClassCommand { Name = "Lớp", Topic = "Test" });
        var testId = await SendAsync(CreateCommand(classId, new() { Question("Câu 1", 1), Question("Câu 2", 1) }, 2, DateTime.UtcNow.AddSeconds(3)));

        // điểm trong bài khác Question.Score: chỉ bản gốc giữ điểm 7 và 3
        var original = await OriginalOf(testId);
        original[0].Score = 7;
        original[1].Score = 3;
        await UpdateAsync(original[0]);
        await UpdateAsync(original[1]);

        var studentId = await RunAsUserWithPlanAsync(email: "student-tq@local");
        await AddAsync(new ClassUser { UserId = studentId, ClassId = classId, ShareMode = ClassShareMode.Student });
        await Task.Delay(3500);

        var attempt = await SendAsync(new StartAttemptTestCommand { TestId = testId });
        attempt.Questions.Select(q => q.Score).Should().BeEquivalentTo(new[] { 7f, 3f }, "đề phát cho học sinh hiển thị điểm của bài");

        // trả lời đúng câu 7 điểm, sai câu 3 điểm
        var answers = attempt.Questions.Select(q =>
        {
            var pickCorrect = q.Score == 7;
            var choice = q.QuestionData.MultipleChoice!.First(c => c.Text == (pickCorrect ? "Đúng" : "Sai"));
            return new UserAnswerDto
            {
                QuestionId = q.Id,
                UserAnswerData = new UserAnswerDataDto { Type = q.Type, MultipleChoice = new List<Guid> { choice.Id } }
            };
        }).ToList();

        var result = await SendAsync(new SubmitTestAttemptCommand { AttemptId = attempt.AttemptId, UserAnswers = answers, IsSubmit = true });
        result.Score.Should().Be(7);

        var graded = await QueryListAsync<AttemptQuestion>(q => q.Where(x => x.AttemptId == attempt.AttemptId));
        graded.Select(x => x.Score).Should().BeEquivalentTo(new[] { 7f, 0f });
        (await QueryListAsync<TestGrade>(q => q.Where(x => x.TestId == testId && x.UserId == studentId))).Single().Score.Should().Be(7);
    }
}
