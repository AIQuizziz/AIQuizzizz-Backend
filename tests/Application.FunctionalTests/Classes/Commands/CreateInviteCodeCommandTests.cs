using CleanArchitectureBase.Application.Classes;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Command.UnitTests.Classes.Commands;

using static Testing;

public class CreateInviteCodeCommandTests : BaseTestFixture
{
    [Test]
    public async Task ShouldRequireValidClassId()
    {
        var userId = await RunAsDefaultUserAsync();

        var command = new CreateInviteCodeCommand
        {
            ClassId = null,
            ExpiredTime = 1
        };

        var ex = await FluentActions.Invoking((() => SendAsync(command)))
            .Should().ThrowAsync<ErrorCodeException>();

        ex.Which.Errors.Should().ContainKey(ErrorCodes.COMMON_INVALID_MODEL);
    }

    [Test]
    public async Task ShouldRequireExpiredTimeGreaterThanZero()
    {
        var userId = await RunAsDefaultUserAsync();

        var classEntity = new Class { Name = "Test Class" };
        await AddAsync(classEntity);
        await AddAsync(new ClassUser { UserId = userId, ClassId = classEntity.Id, ShareMode = ClassShareMode.Owner });

        var command = new CreateInviteCodeCommand
        {
            ClassId = classEntity.Id,
            ExpiredTime = 0
        };

        var ex = await FluentActions.Invoking((() => SendAsync(command)))
            .Should().ThrowAsync<ErrorCodeException>();

        ex.Which.Errors.Should().ContainKey(ErrorCodes.COMMON_INVALID_MODEL);
    }

    [Test]
    public async Task ShouldThrowErrorWhenClassNotFound()
    {
        await RunAsDefaultUserAsync();

        var command = new CreateInviteCodeCommand
        {
            ClassId = Guid.NewGuid(),
            ExpiredTime = 1
        };

        var ex = await FluentActions.Invoking((() => SendAsync(command)))
            .Should().ThrowAsync<ErrorCodeException>();

        ex.Which.Errors.Should().ContainKey(ErrorCodes.CLASS_NOTFOUND);
    }

    [Test]
    public async Task ShouldThrowErrorWhenUserIsNotLecturerOrOwner()
    {
        var ownerId = await RunAsUserAsync("owner@local", "Owner1234!", []);
        var classEntity = new Class { Name = "Test Class", CreatedBy = ownerId };
        await AddAsync(classEntity);
        await AddAsync(new ClassUser { UserId = ownerId, ClassId = classEntity.Id, ShareMode = ClassShareMode.Owner });

        await RunAsDefaultUserAsync(); // Run as a non-owner/lecturer user

        var command = new CreateInviteCodeCommand
        {
            ClassId = classEntity.Id,
            ExpiredTime = 1
        };

        var ex = await FluentActions.Invoking((() => SendAsync(command)))
            .Should().ThrowAsync<ErrorCodeException>();

        ex.Which.Errors.Should().ContainKey(ErrorCodes.NOT_FOUND_TEACHER_OR_OWNER_IN_CLASS);
    }

    [Test]
    public async Task ShouldCreateInviteCodeSuccessfully()
    {
        var userId = await RunAsDefaultUserAsync();
        var classEntity = new Class { Name = "Test Class", CreatedBy = userId };
        await AddAsync(classEntity);
        await AddAsync(new ClassUser { UserId = userId, ClassId = classEntity.Id, ShareMode = ClassShareMode.Owner });

        var command = new CreateInviteCodeCommand
        {
            ClassId = classEntity.Id,
            ExpiredTime = 7 // 7 days
        };

        var result = await SendAsync(command);

        result.Should().NotBeNull();
        result.Code.Should().NotBeEmpty();

        var classInvitation = (await QueryListAsync<ClassInvitation>(x => x.Where(ci => ci.ClassId == classEntity.Id))).FirstOrDefault();
        classInvitation.Should().NotBeNull();
        classInvitation!.Code.Should().Be(result.Code);
        classInvitation.TimeEnd.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(5));
    }
    
    [Test]
    public async Task ShouldCreateNonExpiringInviteCode_WhenExpiredTimeIsNull()
    {
        var ownerId = await RunAsDefaultUserAsync();
        var classEntity = new Class { Name = "Test Class", CreatedBy = ownerId };
        await AddAsync(classEntity);
        await AddAsync(new ClassUser { UserId = ownerId, ClassId = classEntity.Id, ShareMode = ClassShareMode.Owner });

        var result = await SendAsync(new CreateInviteCodeCommand { ClassId = classEntity.Id, ExpiredTime = null });

        var classInvitation = (await QueryListAsync<ClassInvitation>(x => x.Where(ci => ci.ClassId == classEntity.Id))).Single();
        classInvitation.Code.Should().Be(result.Code);
        classInvitation.TimeEnd.Should().Be(ClassInvitation.NoExpiry);
        (await SendAsync(new GetInviteStudentCodeQuery { ClassId = classEntity.Id })).Should().Be(result.Code);

        var studentId = await RunAsUserAsync("student-noexpiry@local", "Student1234!", []);
        await SendAsync(new JoinClassByCodeCommand { Code = result.Code });
        (await QueryListAsync<ClassUser>(x => x.Where(cu => cu.ClassId == classEntity.Id && cu.UserId == studentId))).Should().ContainSingle();
    }

    [Test]
    public async Task ShouldRejectExpiredTimeOverTenYears()
    {
        var userId = await RunAsDefaultUserAsync();
        var classEntity = new Class { Name = "Test Class", CreatedBy = userId };
        await AddAsync(classEntity);
        await AddAsync(new ClassUser { UserId = userId, ClassId = classEntity.Id, ShareMode = ClassShareMode.Owner });

        var ex = await FluentActions.Invoking(() => SendAsync(new CreateInviteCodeCommand { ClassId = classEntity.Id, ExpiredTime = 3651 }))
            .Should().ThrowAsync<ErrorCodeException>();

        ex.Which.Errors.Should().ContainKey(ErrorCodes.COMMON_INVALID_MODEL);
    }

    [Test]
    public async Task ShouldCreateInviteCodeSuccessfullyWithTeacherRole()
    {
        var userId = await RunAsDefaultUserAsync();
        var classEntity = new Class { Name = "Test Class", CreatedBy = userId };
        await AddAsync(classEntity);
        await AddAsync(new ClassUser { UserId = userId, ClassId = classEntity.Id, ShareMode = ClassShareMode.Teacher });

        var command = new CreateInviteCodeCommand
        {
            ClassId = classEntity.Id,
            ExpiredTime = 7 // 7 days
        };

        var result = await SendAsync(command);

        result.Should().NotBeNull();
        result.Code.Should().NotBeEmpty();

        var classInvitation = (await QueryListAsync<ClassInvitation>(x => x.Where(ci => ci.ClassId == classEntity.Id))).FirstOrDefault();
        classInvitation.Should().NotBeNull();
        classInvitation!.Code.Should().Be(result.Code);
        classInvitation.TimeEnd.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ShouldReplaceExistingInviteCode()
    {
        var userId = await RunAsDefaultUserAsync();
        var classEntity = new Class { Name = "Test Class", CreatedBy = userId };
        await AddAsync(classEntity);
        await AddAsync(new ClassUser { UserId = userId, ClassId = classEntity.Id, ShareMode = ClassShareMode.Owner });

        var existingInviteCode = new ClassInvitation
        {
            ClassId = classEntity.Id,
            Code = "OLDCODE123",
            TimeStart = DateTime.UtcNow.AddDays(-1),
            TimeEnd = DateTime.UtcNow.AddDays(1),
            IsDeleted = false
        };
        await AddAsync(existingInviteCode);

        var command = new CreateInviteCodeCommand
        {
            ClassId = classEntity.Id,
            ExpiredTime = 7
        };

        var result = await SendAsync(command);

        result.Should().NotBeNull();
        result.Code.Should().NotBeEmpty();
        result.Code.Should().NotBe(existingInviteCode.Code);

        var oldInviteCode = (await QueryListAsync<ClassInvitation>(x => x.Where(ci => ci.Id == existingInviteCode.Id))).FirstOrDefault();
        oldInviteCode.Should().BeNull(); // Should be removed

        var newInviteCode = (await QueryListAsync<ClassInvitation>(x => x.Where(ci => ci.ClassId == classEntity.Id && ci.Code == result.Code))).FirstOrDefault();
        newInviteCode.Should().NotBeNull();
    }

    [Test]
    public async Task ShouldThrowErrorCodeExceptionWhenNotLoggedIn()
    {
        var command = new CreateInviteCodeCommand
        {
            ClassId = Guid.NewGuid(),
            ExpiredTime = 1
        };

        var ex = await FluentActions.Invoking((() => SendAsync(command)))
            .Should().ThrowAsync<ErrorCodeException>();

        ex.Which.Errors.Should().ContainKey(ErrorCodes.COMMON_UNAUTHORIZED);
    }
}
