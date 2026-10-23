using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.QuestionSets.QuestionJson;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Command.UnitTests.QuestionSets.Queries;

using static Testing;

public class QuestionJsonTests : BaseTestFixture
{
    private const string ShortTextDataJson = "{\"Answer\":\"Thomas Edison\"}";

    private const string OrderingDataJson =
        "[{\"Id\":\"c89ece70-ace7-4773-8856-e54073dd21d0\",\"Text\":\"Sao Kim\",\"CorrectOrder\":1,\"ShuffleOrder\":0},{\"Id\":\"0992dd4c-3edb-4667-9ff4-2f95ee56257f\",\"Text\":\"Tr\\u00E1i \\u0110\\u1EA5t\",\"CorrectOrder\":2,\"ShuffleOrder\":1},{\"Id\":\"54648c68-4f9f-4fa4-9c99-70e841d7b7f1\",\"Text\":\"Sao Th\\u1EE7y\",\"CorrectOrder\":0,\"ShuffleOrder\":2}]";

    private const string MultipleChoiceDataJson =
        "[{\"Id\":\"6efb008f-fb47-4a91-a638-41cda932de41\",\"Text\":\"V\\u00E0ng\",\"IsAnswer\":false,\"ShuffleOrder\":0},{\"Id\":\"9f583bb3-2496-4b2c-837a-89dc3cd0313a\",\"Text\":\"Xanh d\\u01B0\\u01A1ng\",\"IsAnswer\":true,\"ShuffleOrder\":1}]";

    private static async Task<QuestionSet> CreateQuestionSetAsync(Guid ownerId)
    {
        var questionSet = new QuestionSet
        {
            Id = Guid.NewGuid(),
            Name = "Thiên văn",
            Description = "Hệ mặt trời",
            CreatedBy = ownerId,
            QuestionCount = 3,
            VisibilityMode = QuestionSetVisibilityMode.Private
        };
        await AddAsync(questionSet);
        await AddAsync(new QuestionSetUser { UserId = ownerId, QuestionSetId = questionSet.Id, ShareMode = QuestionSetUserShareMode.Owner });

        var tag = new Tag { Id = Guid.NewGuid(), Name = "vật lý" };
        await AddAsync(tag);
        await AddAsync(new QuestionSetTag { TagId = tag.Id, QuestionSetId = questionSet.Id });

        await AddAsync(new Question
        {
            Id = Guid.NewGuid(), QuestionSetId = questionSet.Id, Order = 2, QuestionText = "Ai phát minh bóng đèn?",
            Type = QuestionType.ShortText, Score = 1, DataJson = ShortTextDataJson, TextFormat = TextFormat.PlainText
        });
        await AddAsync(new Question
        {
            Id = Guid.NewGuid(), QuestionSetId = questionSet.Id, Order = 0, QuestionText = "Màu của bầu trời?",
            Type = QuestionType.MultipleChoice, Score = 5, DataJson = MultipleChoiceDataJson, TextFormat = TextFormat.Markdown,
            ExplainText = "Tán xạ Rayleigh"
        });
        await AddAsync(new Question
        {
            Id = Guid.NewGuid(), QuestionSetId = questionSet.Id, Order = 1, QuestionText = "Xếp theo khoảng cách tới Mặt Trời",
            Type = QuestionType.Ordering, Score = 2, DataJson = OrderingDataJson, TextFormat = TextFormat.PlainText
        });
        await AddAsync(new Question
        {
            Id = Guid.NewGuid(), QuestionSetId = questionSet.Id, Order = 3, QuestionText = "Đã xoá",
            Type = QuestionType.ShortText, Score = 1, DataJson = ShortTextDataJson, TextFormat = TextFormat.PlainText, IsDeleted = true
        });
        return questionSet;
    }

    //normal
    [Test]
    public async Task Export_ShouldReturnQuestionsInOrderWithSetInfo_WhenUserIsOwner()
    {
        var ownerId = await RunAsDefaultUserAsync();
        var questionSet = await CreateQuestionSetAsync(ownerId);

        var file = await SendAsync(new ExportQuestionSetJsonQuery { QuestionSetId = questionSet.Id });

        file.Format.Should().Be(QuestionJsonFormat.FormatName);
        file.QuestionSet!.Name.Should().Be("Thiên văn");
        file.QuestionSet.Description.Should().Be("Hệ mặt trời");
        file.QuestionSet.Tags.Should().Equal("vật lý");
        file.Questions.Select(q => q.Content).Should().Equal("Màu của bầu trời?", "Xếp theo khoảng cách tới Mặt Trời", "Ai phát minh bóng đèn?");

        var mc = file.Questions[0];
        mc.Type.Should().Be("MultipleChoice");
        mc.Points.Should().Be(5);
        mc.TextFormat.Should().Be("Markdown");
        mc.Explanation.Should().Be("Tán xạ Rayleigh");
        mc.Options!.Select(o => (o.Text, o.Correct)).Should().Equal(("Vàng", false), ("Xanh dương", true));
        file.Questions[1].Items.Should().Equal("Sao Thủy", "Sao Kim", "Trái Đất");
        file.Questions[2].Answer.Should().Be("Thomas Edison");
    }

    //normal
    [Test]
    public async Task Export_ThenImport_ShouldGiveSameQuestions()
    {
        var ownerId = await RunAsDefaultUserAsync();
        var questionSet = await CreateQuestionSetAsync(ownerId);
        var file = await SendAsync(new ExportQuestionSetJsonQuery { QuestionSetId = questionSet.Id });
        var json = System.Text.Json.JsonSerializer.Serialize(file, QuestionJsonFormat.SerializerOptions);

        var result = await SendAsync(new ImportQuestionJsonCommand { Json = json });

        result.Total.Should().Be(3);
        result.InvalidQuestions.Should().BeEmpty();
        result.QuestionSet!.Name.Should().Be("Thiên văn");
        result.ValidQuestions.Select(q => q.QuestionText).Should().Equal(file.Questions.Select(q => q.Content));
        result.ValidQuestions[1].OrderingItems!.OrderBy(o => o.CorrectOrder).Select(o => o.Text)
            .Should().Equal("Sao Thủy", "Sao Kim", "Trái Đất");
    }

    //abnormal
    [Test]
    public async Task Export_ShouldThrowForbidden_WhenUserHasNoAccess()
    {
        var ownerId = await RunAsUserAsync("owner@local", "Owner1234!", []);
        var questionSet = await CreateQuestionSetAsync(ownerId);
        await RunAsUserWithPlanAsync();

        var ex = await FluentActions.Invoking(() => SendAsync(new ExportQuestionSetJsonQuery { QuestionSetId = questionSet.Id }))
            .Should().ThrowAsync<ErrorCodeException>();
        ex.Which.Errors.Should().ContainKey(ErrorCodes.COMMON_FORBIDDEN);
    }

    //abnormal
    [Test]
    public async Task Export_ShouldRequirePlan_WhenEditorIsNotOwner()
    {
        var ownerId = await RunAsUserAsync("owner@local", "Owner1234!", []);
        var questionSet = await CreateQuestionSetAsync(ownerId);
        var editorId = await RunAsUserWithPlanAsync(canCopyOrImportQuestionSet: false);
        await AddAsync(new QuestionSetUser { UserId = editorId, QuestionSetId = questionSet.Id, ShareMode = QuestionSetUserShareMode.Editable });

        var ex = await FluentActions.Invoking(() => SendAsync(new ExportQuestionSetJsonQuery { QuestionSetId = questionSet.Id }))
            .Should().ThrowAsync<ErrorCodeException>();
        ex.Which.Errors.Should().ContainKey(ErrorCodes.PLAN_REQUIRE_PLAN);
    }

    //normal
    [Test]
    public async Task Export_ShouldSucceed_WhenEditorHasCopyPlan()
    {
        var ownerId = await RunAsUserAsync("owner@local", "Owner1234!", []);
        var questionSet = await CreateQuestionSetAsync(ownerId);
        var editorId = await RunAsUserWithPlanAsync();
        await AddAsync(new QuestionSetUser { UserId = editorId, QuestionSetId = questionSet.Id, ShareMode = QuestionSetUserShareMode.Editable });

        var file = await SendAsync(new ExportQuestionSetJsonQuery { QuestionSetId = questionSet.Id });

        file.Questions.Should().HaveCount(3);
    }

    //abnormal
    [Test]
    public async Task Export_ShouldThrowNotFound_WhenQuestionSetMissing()
    {
        await RunAsDefaultUserAsync();
        var ex = await FluentActions.Invoking(() => SendAsync(new ExportQuestionSetJsonQuery { QuestionSetId = Guid.NewGuid() }))
            .Should().ThrowAsync<ErrorCodeException>();
        ex.Which.Errors.Should().ContainKey(ErrorCodes.QUESTION_SET_NOT_FOUND);
    }

    //normal
    [Test]
    public async Task Import_ShouldSplitValidAndInvalidQuestions()
    {
        await RunAsDefaultUserAsync();
        var result = await SendAsync(new ImportQuestionJsonCommand
        {
            Json = """
                   ```json
                   { "questionSet": { "name": "Địa lý" },
                     "questions": [
                       { "type": "MultipleChoice", "content": "Thủ đô Việt Nam?", "options": ["Huế", "*Hà Nội"] },
                       { "type": "MultipleChoice", "content": "Thiếu đáp án đúng", "options": ["A", "B"] } ] }
                   ```
                   """
        });

        result.Total.Should().Be(2);
        result.QuestionSet!.Name.Should().Be("Địa lý");
        result.ValidQuestions.Should().ContainSingle().Which.Index.Should().Be(1);
        result.InvalidQuestions.Should().ContainSingle().Which.Errors.Should().NotBeEmpty();
    }

    //abnormal
    [Test]
    public async Task Import_ShouldThrow_WhenJsonInvalid()
    {
        await RunAsDefaultUserAsync();
        var ex = await FluentActions.Invoking(() => SendAsync(new ImportQuestionJsonCommand { Json = "{ \"questions\": [" }))
            .Should().ThrowAsync<ErrorCodeException>();
        ex.Which.Errors.Should().ContainKey(ErrorCodes.QUESTION_JSON_INVALID);
    }

    //abnormal
    [Test]
    public async Task Import_ShouldRequireLogin()
    {
        Logout();
        var ex = await FluentActions.Invoking(() => SendAsync(new ImportQuestionJsonCommand { Json = "[]" }))
            .Should().ThrowAsync<ErrorCodeException>();
        ex.Which.Errors.Should().ContainKey(ErrorCodes.COMMON_UNAUTHORIZED);
    }
}
