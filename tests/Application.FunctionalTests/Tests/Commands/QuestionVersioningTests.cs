using CleanArchitectureBase.Application.Classes;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.QuestionSets.Commands;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.QuestionSets.Queries;
using CleanArchitectureBase.Application.Tests;
using CleanArchitectureBase.Application.Tests.Dto;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;
using CleanArchitectureBase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static CleanArchitectureBase.Application.Command.UnitTests.Testing;

namespace CleanArchitectureBase.Application.Command.UnitTests.Tests.Commands;

/// <summary>
/// Câu hỏi trong bộ câu hỏi có Version / ContentHash; bài test chép câu từ bộ câu hỏi thành bản sao riêng
/// (SourceQuestionId + SourceVersion), có thể "cập nhật theo bản gốc".
/// </summary>
[TestFixture]
public class QuestionVersioningTests : BaseTestFixture
{
    private static CreateUpdateQuestionDto Question(string text, float score = 1) => new()
    {
        Type = "MultipleChoice",
        QuestionText = text,
        ExplainText = "Giải thích " + text,
        TextFormat = "Markdown",
        Score = score,
        MultipleChoices = new List<CreateMultipleChoiceDto>
        {
            new() { Text = "Đúng", IsAnswer = true },
            new() { Text = "Sai", IsAnswer = false }
        }
    };

    private static async Task<(Guid SetId, List<Question> Questions)> CreateQuestionSet(params string[] texts)
    {
        var setId = await SendAsync(new CreateQuestionSetCommand { Name = "Bộ nguồn", Questions = texts.Select(t => Question(t)).ToList() });
        var questions = (await QueryListAsync<Question>(q => q.Where(x => x.QuestionSetId == setId))).OrderBy(x => x.Order).ToList();
        return (setId, questions);
    }

    /// <summary>Câu trong bộ câu hỏi dạng gửi lên (giống trang sửa bộ / hộp chọn câu khi tạo đề của FE)</summary>
    private static async Task<List<CreateUpdateQuestionDto>> QuestionSetDtos(Guid setId) =>
        (await SendAsync(new GetQuestionSetQuestionsQuery { QuestionSetId = setId })).Select(q => new CreateUpdateQuestionDto
        {
            QuestionId = q.Id,
            Type = q.Type,
            QuestionText = q.QuestionText,
            ExplainText = q.ExplainText,
            TextFormat = q.TextFormat,
            Score = q.Score,
            MultipleChoices = q.QuestionData.MultipleChoice!.Select(c => new CreateMultipleChoiceDto { Text = c.Text, IsAnswer = c.IsAnswer ?? false }).ToList()
        }).ToList();

    private static List<CreateUpdateQuestionDto> AsEditDtos(TestDetailDto detail) => detail.Questions.Select(q => new CreateUpdateQuestionDto
    {
        QuestionId = q.Id,
        Type = q.Type,
        QuestionText = q.QuestionText,
        ExplainText = q.ExplainText,
        TextFormat = q.TextFormat,
        Score = q.Score,
        MultipleChoices = q.QuestionData.MultipleChoice!.Select(c => new CreateMultipleChoiceDto { Text = c.Text, IsAnswer = c.IsAnswer ?? false }).ToList()
    }).ToList();

    private static async Task<Guid> CreateTestFrom(Guid classId, List<CreateUpdateQuestionDto> questions) =>
        await SendAsync(new CreateTestCommand
        {
            Name = "Đề từ bộ câu hỏi",
            ClassId = classId,
            TimeLimit = 60,
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(2),
            GradeAttemptMethod = "LastAttempt",
            GradeQuestionMethod = "AllOrNothing",
            NumberOfShuffles = 1,
            MaxAttempt = 1,
            PassingScore = 50,
            Questions = questions
        });

    private static UpdateTestCommand UpdateCommand(Guid testId, List<CreateUpdateQuestionDto> questions) => new()
    {
        TestId = testId,
        Name = "Đề từ bộ câu hỏi",
        TimeLimit = 60,
        StartTime = DateTime.UtcNow.AddDays(1),
        EndTime = DateTime.UtcNow.AddDays(2),
        GradeAttemptMethod = "LastAttempt",
        GradeQuestionMethod = "AllOrNothing",
        NumberOfShuffles = 1,
        MaxAttempt = 1,
        PassingScore = 50,
        CreateUpdateQuestions = questions
    };

    private static async Task EditQuestionSet(Guid setId, List<CreateUpdateQuestionDto> questions) =>
        await SendAsync(new UpdateQuestionSetCommand { QuestionSetId = setId, Name = "Bộ nguồn", CreateUpdateQuestions = questions });

    [Test]
    public async Task QuestionSet_BumpsVersionOnlyWhenContentChanges_AndStartupBackfillMatches()
    {
        await RunAsUserWithPlanAsync();
        var (setId, questions) = await CreateQuestionSet("Câu 1", "Câu 2", "Câu 3");
        questions.Should().OnlyContain(q => q.Version == 1 && q.ContentHash != null && q.ContentHash.Length == 64);

        // lưu lại y nguyên: không tăng version
        await EditQuestionSet(setId, await QuestionSetDtos(setId));
        (await QueryListAsync<Question>(q => q.Where(x => x.QuestionSetId == setId))).Should().OnlyContain(q => q.Version == 1);

        // sửa câu 1 (nội dung), câu 2 chỉ đổi điểm, câu 3 đổi thứ tự lên đầu
        var dtos = await QuestionSetDtos(setId);
        dtos[0].QuestionText = "Câu 1 (sửa)";
        dtos[1].Score = 5;
        await EditQuestionSet(setId, new() { dtos[2], dtos[0], dtos[1] });

        var after = (await QueryListAsync<Question>(q => q.Where(x => x.QuestionSetId == setId))).ToDictionary(x => x.Id);
        after[questions[0].Id].Version.Should().Be(2);
        after[questions[1].Id].Version.Should().Be(1, "điểm không thuộc nội dung");
        after[questions[2].Id].Version.Should().Be(1, "đổi thứ tự không đổi nội dung");
        after[questions[0].Id].ContentHash.Should().NotBe(questions[0].ContentHash);

        // câu cũ chưa có hash (trước khi có version): backfill lúc khởi động cho ra đúng hash như khi lưu, không đổi Version
        using var scope = CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Questions.Where(x => x.QuestionSetId == setId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ContentHash, (string?)null));
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>().BackfillQuestionContentHashAsync();
        var refilled = (await QueryListAsync<Question>(q => q.Where(x => x.QuestionSetId == setId))).ToDictionary(x => x.Id);
        foreach (var question in after.Values)
        {
            refilled[question.Id].ContentHash.Should().Be(question.ContentHash);
            refilled[question.Id].Version.Should().Be(question.Version);
        }
    }

    [Test]
    public async Task CreateTest_CopiesQuestionsWithSourceAndVersion_SourceUntouched()
    {
        await RunAsUserWithPlanAsync();
        var (setId, sources) = await CreateQuestionSet("Câu 1", "Câu 2");
        var classId = await SendAsync(new CreateClassCommand { Name = "Lớp", Topic = "Test" });

        var picked = await QuestionSetDtos(setId);
        picked[1].Score = 4; // điểm trong đề
        var testId = await CreateTestFrom(classId, picked);

        var original = (await QueryListAsync<TestQuestion>(q => q.Where(x => x.TestId == testId))).OrderBy(x => x.Order).ToList();
        original.Select(x => x.Score).Should().Equal(1f, 4f);
        for (var i = 0; i < 2; i++)
        {
            var copy = (await FindAsync<Question>(original[i].QuestionId))!;
            copy.Id.Should().NotBe(sources[i].Id, "đề giữ bản sao riêng");
            copy.QuestionSetId.Should().BeNull();
            copy.SourceQuestionId.Should().Be(sources[i].Id);
            copy.SourceVersion.Should().Be(1);
            copy.ContentHash.Should().Be(sources[i].ContentHash, "chép nguyên nội dung");
        }

        // sửa câu trong đề không đụng tới bộ câu hỏi
        var edit = AsEditDtos(await SendAsync(new GetTestDetailQuery { TestId = testId }));
        edit[0].QuestionText = "Câu 1 (chỉ sửa trong đề)";
        await SendAsync(UpdateCommand(testId, edit));
        var source0 = (await FindAsync<Question>(sources[0].Id))!;
        source0.QuestionText.Should().Be("Câu 1");
        source0.Version.Should().Be(1);
    }

    [Test]
    public async Task SourceEditedAfterCopy_DetailShowsNewerVersion_SyncUpdatesCopyAndKeepsScore()
    {
        await RunAsUserWithPlanAsync();
        var (setId, sources) = await CreateQuestionSet("Câu 1", "Câu 2");
        var classId = await SendAsync(new CreateClassCommand { Name = "Lớp", Topic = "Test" });
        var picked = await QuestionSetDtos(setId);
        picked[0].Score = 6;
        var testId = await CreateTestFrom(classId, picked);

        // giáo viên sửa câu gốc sau khi đã ra đề
        var setDtos = await QuestionSetDtos(setId);
        setDtos[0].QuestionText = "Câu 1 bản mới";
        await EditQuestionSet(setId, setDtos);

        var detail = await SendAsync(new GetTestDetailQuery { TestId = testId });
        var first = detail.Questions[0];
        first.QuestionText.Should().Be("Câu 1", "đề không đổi theo bộ câu hỏi");
        first.SourceQuestionId.Should().Be(sources[0].Id);
        first.SourceVersion.Should().Be(1);
        first.LatestSourceVersion.Should().Be(2);
        first.SourceQuestionSetName.Should().Be("Bộ nguồn");
        detail.Questions[1].LatestSourceVersion.Should().Be(detail.Questions[1].SourceVersion);

        var synced = await SendAsync(new SyncTestQuestionWithSourceCommand { TestId = testId, QuestionId = first.Id });
        synced.Id.Should().Be(first.Id);
        synced.QuestionText.Should().Be("Câu 1 bản mới");
        synced.SourceVersion.Should().Be(2);
        synced.Score.Should().Be(6, "điểm trong đề giữ nguyên");

        var after = await SendAsync(new GetTestDetailQuery { TestId = testId });
        after.Questions[0].QuestionText.Should().Be("Câu 1 bản mới");
        after.Questions[0].SourceVersion.Should().Be(after.Questions[0].LatestSourceVersion);
        after.Questions[0].Score.Should().Be(6);
    }

    [Test]
    public async Task Sync_FailsWhenSourceDeleted()
    {
        await RunAsUserWithPlanAsync();
        var (setId, sources) = await CreateQuestionSet("Câu 1", "Câu 2");
        var classId = await SendAsync(new CreateClassCommand { Name = "Lớp", Topic = "Test" });
        var testId = await CreateTestFrom(classId, await QuestionSetDtos(setId));

        var setDtos = await QuestionSetDtos(setId);
        await SendAsync(new UpdateQuestionSetCommand
        {
            QuestionSetId = setId, Name = "Bộ nguồn", CreateUpdateQuestions = new() { setDtos[1] }, DeleteQuestionIds = new() { sources[0].Id }
        });

        var detail = await SendAsync(new GetTestDetailQuery { TestId = testId });
        detail.Questions[0].LatestSourceVersion.Should().BeNull("câu gốc đã bị xoá");

        var ex = await FluentActions.Invoking(() => SendAsync(new SyncTestQuestionWithSourceCommand { TestId = testId, QuestionId = detail.Questions[0].Id }))
            .Should().ThrowAsync<ErrorCodeException>();
        ex.Which.Errors.Should().ContainKey(ErrorCodes.SOURCE_QUESTION_NOT_FOUND);
    }

    [Test]
    public async Task CreateTest_FromPrivateQuestionSetOfOthers_IsRejected()
    {
        await RunAsUserWithPlanAsync(email: "owner-src@local");
        var (setId, _) = await CreateQuestionSet("Câu riêng tư");
        var picked = await QuestionSetDtos(setId);

        await RunAsUserWithPlanAsync(email: "teacher-src@local");
        var classId = await SendAsync(new CreateClassCommand { Name = "Lớp", Topic = "Test" });

        var ex = await FluentActions.Invoking(() => CreateTestFrom(classId, picked)).Should().ThrowAsync<ErrorCodeException>();
        ex.Which.Errors.Should().ContainKey(ErrorCodes.USER_NOT_ACCESS_TO_QUESTION_SET);
    }
}
