using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.QuestionSets;
using CleanArchitectureBase.Application.QuestionSets.Commands;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;
using static CleanArchitectureBase.Application.Command.UnitTests.Testing;

namespace CleanArchitectureBase.Application.Command.UnitTests.QuestionSets
{
    public class ResetQuestionSetHistoryCommandTests : BaseTestFixture
    {
        [Test]
        public async Task ShouldResetHistory_WhenValidRequest()
        {
            var userId = await RunAsDefaultUserAsync();
            var questionSet = new QuestionSet
            {
                Id = Guid.NewGuid(),
                Name = "Test Question Set"
            };
            await AddAsync(questionSet);

            var question = new Question
            {
                Id = Guid.NewGuid(),
                QuestionSetId = questionSet.Id,
                QuestionText = "Sample Question",
                Type = QuestionType.MultipleChoice,
                TextFormat = TextFormat.Markdown,
                DataJson = "{\"Answer\":\"\\u00DD\"}"
            };
            await AddAsync(question);

            var command = new ResetQuestionSetHistoryCommand
            {
                QuestionSetId = questionSet.Id
            };
            await AddAsync(new UserQuestionSetHistory
            {
                UserId = userId,
                QuestionId = question.Id,
                IsCorrect = true
            });

            await SendAsync(command);

            var histories = await QueryListAsync<UserQuestionSetHistory>(h => h.Where(x => x.UserId == userId));
            ;
            histories.Should().BeEmpty();
        }

        [Test]
        public async Task ShouldThrowError_WhenQuestionSetNotFound()
        {
            var userId = await RunAsDefaultUserAsync();
            var command = new ResetQuestionSetHistoryCommand
            {
                QuestionSetId = Guid.NewGuid()
            };
            var ex = await FluentActions.Invoking(() => SendAsync(command)).Should().ThrowAsync<ErrorCodeException>();
            ex.Which.Errors.Should().ContainKey(ErrorCodes.QUESTION_SET_NOT_FOUND);
        }

        [Test]
        public async Task ShouldThrowError_WhenUserCannotViewQuestionSet()
        {
            var userId = await RunAsDefaultUserAsync();
            var questionSet = new QuestionSet
            {
                Id = Guid.NewGuid(),
                Name = "Test Question Set",
                VisibilityMode = QuestionSetVisibilityMode.Private
            };
            await AddAsync(questionSet);
            var question = new Question
            {
                Id = Guid.NewGuid(),
                QuestionSetId = questionSet.Id,
                QuestionText = "Sample Question",
                Type = QuestionType.MultipleChoice,
                TextFormat = TextFormat.Markdown,
                DataJson = "{\"Answer\":\"\\u00DD\"}"
            };
            await AddAsync(question);

            var command = new ResetQuestionSetHistoryCommand
            {
                QuestionSetId = questionSet.Id
            };
            await AddAsync(new UserQuestionSetHistory
            {
                UserId = userId,
                QuestionId = question.Id,
                IsCorrect = true
            });

            var ex = await FluentActions.Invoking(() => SendAsync(command)).Should().ThrowAsync<ErrorCodeException>();
            ex.Which.Errors.Should().ContainKey(ErrorCodes.COMMON_FORBIDDEN);
        }
        
        
        [Test]
        public async Task ShouldThrowError_WhenUserNotLogin()
        {


            var command = new ResetQuestionSetHistoryCommand
            {
                QuestionSetId = Guid.NewGuid()
            };
            var ex = await FluentActions.Invoking(() => SendAsync(command)).Should().ThrowAsync<ErrorCodeException>();
            ex.Which.Errors.Should().ContainKey(ErrorCodes.COMMON_UNAUTHORIZED);
        }
    }
}
