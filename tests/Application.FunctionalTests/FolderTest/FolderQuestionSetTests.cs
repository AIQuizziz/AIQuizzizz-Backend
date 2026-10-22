using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.FolderTest;
using CleanArchitectureBase.Application.QuestionSets.Commands;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.QuestionSets.Queries;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Command.UnitTests.FolderTest;

using static Testing;

/// <summary>Thư mục chứa bộ câu hỏi (thay cho thư mục chứa mẫu đề thi)</summary>
public class FolderQuestionSetTests : BaseTestFixture
{
    private static CreateUpdateQuestionDto Question(string text) => new()
    {
        Type = "MultipleChoice",
        QuestionText = text,
        Score = 1,
        MultipleChoices = new List<CreateMultipleChoiceDto>
        {
            new() { Text = "Đúng", IsAnswer = true },
            new() { Text = "Sai", IsAnswer = false }
        }
    };

    private static async Task<Guid> CreateQuestionSet(string name) =>
        await SendAsync(new CreateQuestionSetCommand { Name = name, Questions = new() { Question("Câu 1"), Question("Câu 2") } });

    private static async Task<Guid> CreateFolder() => await SendAsync(new CreateFolderCommand { FolderName = "Thư mục đề" });

    [Test]
    public async Task AddSearchRemove_QuestionSetInFolder()
    {
        await RunAsUserWithPlanAsync();
        var setId = await CreateQuestionSet("Bộ trong thư mục");
        var folderId = await CreateFolder();

        await SendAsync(new AddQuestionSetToFolderCommand { FolderId = folderId, QuestionSetId = setId });

        var page = await SendAsync(new SearchQuestionSetInFolderQuery { FolderId = folderId });
        page.QuestionSets.Items.Should().ContainSingle(x => x.QuestionSetId == setId && x.NumberOfQuestion == 2);

        var again = await FluentActions.Invoking(() => SendAsync(new AddQuestionSetToFolderCommand { FolderId = folderId, QuestionSetId = setId }))
            .Should().ThrowAsync<ErrorCodeException>();
        again.Which.Errors.Should().ContainKey(ErrorCodes.QUESTION_SET_ALREADY_EXISTS_IN_FOLDER);

        await SendAsync(new RemoveQuestionSetInFolderCommand { FolderId = folderId, QuestionSetId = setId });
        (await SendAsync(new SearchQuestionSetInFolderQuery { FolderId = folderId })).QuestionSets.Items.Should().BeEmpty();

        var missing = await FluentActions.Invoking(() => SendAsync(new RemoveQuestionSetInFolderCommand { FolderId = folderId, QuestionSetId = setId }))
            .Should().ThrowAsync<ErrorCodeException>();
        missing.Which.Errors.Should().ContainKey(ErrorCodes.QUESTION_SET_NOT_FOUND_IN_FOLDER);
    }

    [Test]
    public async Task Add_RequiresEditPermissionOnQuestionSet()
    {
        await RunAsUserWithPlanAsync(email: "owner-fqs@local");
        var setId = await CreateQuestionSet("Bộ của người khác");

        var otherId = await RunAsUserWithPlanAsync(email: "other-fqs@local");
        var folderId = await CreateFolder();

        var ex = await FluentActions.Invoking(() => SendAsync(new AddQuestionSetToFolderCommand { FolderId = folderId, QuestionSetId = setId }))
            .Should().ThrowAsync<ErrorCodeException>();
        ex.Which.Errors.Should().ContainKey(ErrorCodes.USER_NOT_ACCESS_TO_QUESTION_SET);
        otherId.Should().NotBeEmpty();
    }

    [Test]
    public async Task FolderMember_CanViewPrivateQuestionSetInFolder()
    {
        await RunAsUserWithPlanAsync(email: "owner-view@local");
        var setId = await CreateQuestionSet("Bộ riêng tư");
        var folderId = await CreateFolder();
        await SendAsync(new AddQuestionSetToFolderCommand { FolderId = folderId, QuestionSetId = setId });

        var memberId = await RunAsUserWithPlanAsync(email: "member-view@local");

        // chưa là thành viên thư mục: không xem được bộ riêng tư
        await FluentActions.Invoking(() => SendAsync(new GetQuestionSetQuestionsQuery { QuestionSetId = setId }))
            .Should().ThrowAsync<ErrorCodeException>();

        await AddAsync(new FolderUser { UserId = memberId, FolderId = folderId, ShareMode = FolderShareMode.ViewOnly });

        var questions = await SendAsync(new GetQuestionSetQuestionsQuery { QuestionSetId = setId });
        questions.Should().HaveCount(2);
        questions.Should().OnlyContain(q => q.Version == 1);
    }
}
