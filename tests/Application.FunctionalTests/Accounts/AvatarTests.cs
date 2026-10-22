using System.Text;
using CleanArchitectureBase.Application.Command.UnitTests.Fakes;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Users;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitectureBase.Application.Command.UnitTests.Accounts;

using static Testing;

public class AvatarTests : BaseTestFixture
{
    private static UploadAvatarCommand Upload(string fileName, string contentType = "image/png")
    {
        var bytes = Encoding.UTF8.GetBytes("fake image bytes");
        return new UploadAvatarCommand
        {
            File = new FileStreamData { Data = new MemoryStream(bytes), ContentType = contentType, FileName = fileName },
            Length = bytes.Length
        };
    }

    private static FakeMediaAiClient MediaAi() =>
        (FakeMediaAiClient)CreateScope().ServiceProvider.GetRequiredService<IMediaAiClient>();

    private static async Task<User> GetUserAsync(Guid userId) =>
        (await QueryListAsync<User>(q => q.IgnoreQueryFilters().Where(u => u.Id == userId))).Single();

    private static async Task<int> ActiveStrikesAsync(Guid userId) =>
        (await QueryListAsync<UserViolation>(q => q.Where(v => v.UserId == userId && !v.IsExpired))).Count;

    [Test]
    public async Task ShouldSetAvatarWhenImageIsClean()
    {
        var userId = await RunAsDefaultUserAsync();

        var result = await SendAsync(Upload("me.png"));

        result.AvatarId.Should().NotBeNull();
        (await GetUserAsync(userId)).AvatarMediaId.Should().Be(result.AvatarId);
        var media = await FindAsync<Media>(result.AvatarId!.Value);
        media!.ModerationStatus.Should().Be(MediaModerationStatus.Approved);
        media.IsDeleted.Should().BeFalse();
        media.OwnerId.Should().Be(userId);

        var profile = await SendAsync(new GetProfileQuery());
        profile.AvatarId.Should().Be(result.AvatarId);

        // ai cũng lấy được URL ảnh (thẻ <img> không gửi token)
        Logout();
        var url = await SendAsync(new GetAvatarUrlQuery { AvatarId = result.AvatarId.Value });
        url.Should().Contain($"img/{result.AvatarId}.jpg");
    }

    [Test]
    public async Task ShouldReplaceOldAvatarAndDeleteItsFile()
    {
        var userId = await RunAsDefaultUserAsync();
        var first = await SendAsync(Upload("one.png"));

        var second = await SendAsync(Upload("two.png"));

        (await GetUserAsync(userId)).AvatarMediaId.Should().Be(second.AvatarId);
        (await FindAsync<Media>(first.AvatarId!.Value))!.IsDeleted.Should().BeTrue();
        MediaAi().DeletedKeys.Should().Contain($"img/{first.AvatarId}.jpg");

        // ảnh cũ không còn là avatar -> không lấy được URL
        await FluentActions.Invoking(() => SendAsync(new GetAvatarUrlQuery { AvatarId = first.AvatarId.Value }))
            .Should().ThrowAsync<ErrorCodeException>()
            .Where(e => e.Errors.ContainsKey(ErrorCodes.AVATAR_NOT_FOUND));
    }

    [Test]
    public async Task ShouldRejectViolatedImageAndRecordStrike()
    {
        var userId = await RunAsDefaultUserAsync();
        var clean = await SendAsync(Upload("clean.png"));

        await FluentActions.Invoking(() => SendAsync(Upload("violate-nude.png")))
            .Should().ThrowAsync<ErrorCodeException>()
            .Where(e => e.Errors.ContainsKey(ErrorCodes.AVATAR_VIOLATED));

        // avatar giữ nguyên ảnh cũ, ảnh vi phạm bị xoá, tính 1 vi phạm
        (await GetUserAsync(userId)).AvatarMediaId.Should().Be(clean.AvatarId);
        var violated = await QueryListAsync<Media>(q => q.Where(m => m.OwnerId == userId && m.OriginalFileName == "violate-nude.png"));
        violated.Single().ModerationStatus.Should().Be(MediaModerationStatus.Violated);
        violated.Single().IsDeleted.Should().BeTrue();
        MediaAi().DeletedKeys.Should().Contain(violated.Single().ObjectKey);
        var strikes = await QueryListAsync<UserViolation>(q => q.Where(v => v.UserId == userId));
        strikes.Should().ContainSingle();
        strikes.Single().Reason.Should().StartWith("Ảnh đại diện vi phạm");
        strikes.Single().DeletedQuestionCount.Should().Be(0);
    }

    [Test]
    public async Task ShouldBanAfterEnoughViolations()
    {
        var userId = await RunAsDefaultUserAsync();

        for (var i = 0; i < 3; i++)
        {
            await FluentActions.Invoking(() => SendAsync(Upload($"violate-{i}.png")))
                .Should().ThrowAsync<ErrorCodeException>()
                .Where(e => e.Errors.ContainsKey(ErrorCodes.AVATAR_VIOLATED));
        }

        (await ActiveStrikesAsync(userId)).Should().Be(3);
        var user = await GetUserAsync(userId);
        user.IsBanned.Should().BeTrue();
        user.BanReason.Should().StartWith("AUTO_BAN_STRIKES:3:");
    }

    [Test]
    public async Task ShouldNotCountStrikeWhenModerationFails()
    {
        var userId = await RunAsDefaultUserAsync();

        await FluentActions.Invoking(() => SendAsync(Upload("error.png")))
            .Should().ThrowAsync<ErrorCodeException>()
            .Where(e => e.Errors.ContainsKey(ErrorCodes.AVATAR_MODERATION_FAILED));

        (await GetUserAsync(userId)).AvatarMediaId.Should().BeNull();
        (await ActiveStrikesAsync(userId)).Should().Be(0);
        var media = (await QueryListAsync<Media>(q => q.Where(m => m.OwnerId == userId))).Single();
        media.ModerationStatus.Should().Be(MediaModerationStatus.Failed);
        media.IsDeleted.Should().BeTrue();
        MediaAi().DeletedKeys.Should().Contain(media.ObjectKey);
    }

    [Test]
    public async Task ShouldRejectNonImageFile()
    {
        await RunAsDefaultUserAsync();

        await FluentActions.Invoking(() => SendAsync(Upload("clip.mp4", "video/mp4")))
            .Should().ThrowAsync<ErrorCodeException>()
            .Where(e => e.Errors.ContainsKey(ErrorCodes.MEDIA_UNSUPPORTED_TYPE));
    }

    [Test]
    public async Task ShouldDeleteAvatar()
    {
        var userId = await RunAsDefaultUserAsync();
        var uploaded = await SendAsync(Upload("me.png"));

        var result = await SendAsync(new DeleteAvatarCommand());

        result.AvatarId.Should().BeNull();
        (await GetUserAsync(userId)).AvatarMediaId.Should().BeNull();
        (await FindAsync<Media>(uploaded.AvatarId!.Value))!.IsDeleted.Should().BeTrue();
        MediaAi().DeletedKeys.Should().Contain($"img/{uploaded.AvatarId}.jpg");
    }

    [Test]
    public async Task ShouldRequireLoginToUpload()
    {
        Logout();

        await FluentActions.Invoking(() => SendAsync(Upload("me.png")))
            .Should().ThrowAsync<ErrorCodeException>()
            .Where(e => e.Errors.ContainsKey(ErrorCodes.COMMON_UNAUTHORIZED));
    }

    [Test]
    public async Task ShouldNotServeAvatarOfBannedUser()
    {
        var userId = await RunAsDefaultUserAsync();
        var uploaded = await SendAsync(Upload("me.png"));
        var user = await GetUserAsync(userId);
        user.IsBanned = true;
        await UpdateAsync(user);

        await FluentActions.Invoking(() => SendAsync(new GetAvatarUrlQuery { AvatarId = uploaded.AvatarId!.Value }))
            .Should().ThrowAsync<ErrorCodeException>()
            .Where(e => e.Errors.ContainsKey(ErrorCodes.AVATAR_NOT_FOUND));
    }
}
