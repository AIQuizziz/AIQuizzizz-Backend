using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Users;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureBase.Web.Endpoints;

public class AccountEndpoints : EndpointGroupBase
{
    // ảnh đại diện: giới hạn cứng của request (kiểm tra chi tiết theo MaxImageSizeMb nằm trong command)
    private const long MaxAvatarRequestBytes = 20L * 1024 * 1024;

    public override void Map(WebApplication app)
    {
        var group = app.MapGroup(this);

        group.MapGet(GetProfile, "Profile");

        group.MapPost(UploadAvatar, "Avatar")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxAvatarRequestBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxAvatarRequestBytes });
        group.MapDelete(DeleteAvatar, "Avatar");
        group.MapGet(GetAvatar, "Avatar/{avatarId:guid}");
    }

    public async Task<Ok<ApiResponse<UserProfileDto>>> GetProfile(ISender sender, HttpContext httpContext)
    {
        var result = await sender.Send(new GetProfileQuery());
        return result.ToOk();
    }

    /// <summary>
    /// Đổi ảnh đại diện (multipart field "file"). Ảnh được kiểm duyệt ngay: vi phạm -> lỗi AVATAR_VIOLATED và tính 1 vi phạm
    /// </summary>
    public async Task<Ok<ApiResponse<AvatarDto>>> UploadAvatar([FromForm] IFormFile file, ISender sender)
    {
        await using var stream = file.OpenReadStream();
        var result = await sender.Send(new UploadAvatarCommand
        {
            File = new FileStreamData
            {
                Data = stream,
                ContentType = file.ContentType,
                FileName = file.FileName
            },
            Length = file.Length
        });
        return result.ToOk();
    }

    /// <summary>Xoá ảnh đại diện (về ảnh mặc định)</summary>
    public async Task<Ok<ApiResponse<AvatarDto>>> DeleteAvatar(ISender sender)
    {
        var result = await sender.Send(new DeleteAvatarCommand());
        return result.ToOk();
    }

    /// <summary>
    /// Ảnh đại diện để dùng trực tiếp trong &lt;img src&gt;: chuyển hướng (302) tới presigned URL ngắn hạn.
    /// Không cần đăng nhập. Trình duyệt được giữ lần chuyển hướng 5 phút (URL ký còn hạn lâu hơn thế).
    /// </summary>
    public async Task<IResult> GetAvatar(Guid avatarId, ISender sender, HttpContext httpContext)
    {
        var url = await sender.Send(new GetAvatarUrlQuery { AvatarId = avatarId });
        httpContext.Response.Headers.CacheControl = "private, max-age=300";
        return Results.Redirect(url);
    }
}
