using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.Common.Settings;
using CleanArchitectureBase.Application.MediaFiles.Commands;
using CleanArchitectureBase.Application.MediaFiles.Dtos;
using CleanArchitectureBase.Application.MediaFiles.Services;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanArchitectureBase.Application.Users;

public class AvatarDto
{
    /// <summary>Id ảnh đại diện; client tải ảnh qua GET /api/Account/Avatar/{AvatarId}</summary>
    public Guid? AvatarId { get; set; }
}

/// <summary>
/// Đổi ảnh đại diện. Client nên cắt vuông + thu nhỏ trước khi gửi; server chuẩn hoá thành JPEG (media-ai).
/// Ảnh được kiểm duyệt NGAY (khoả thân / khiêu dâm / bạo lực) trước khi gán: vi phạm -> từ chối + tính 1 vi phạm
/// (đủ số vi phạm sẽ tự động khoá tài khoản như ảnh câu hỏi). Không phụ thuộc gói.
/// </summary>
[Authorize]
public class UploadAvatarCommand : IRequest<AvatarDto>
{
    public required FileStreamData File { get; set; }
    public long Length { get; set; }
}

public class UploadAvatarCommandHandler : IRequestHandler<UploadAvatarCommand, AvatarDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly IMediaAiClient _mediaAiClient;
    private readonly IModerationService _moderationService;
    private readonly MediaSettings _mediaSettings;
    private readonly ILogger<UploadAvatarCommandHandler> _logger;

    public UploadAvatarCommandHandler(IApplicationDbContext context, IUser user, IMediaAiClient mediaAiClient,
        IModerationService moderationService, IOptions<MediaSettings> mediaSettings, ILogger<UploadAvatarCommandHandler> logger)
    {
        _context = context;
        _user = user;
        _mediaAiClient = mediaAiClient;
        _moderationService = moderationService;
        _mediaSettings = mediaSettings.Value;
        _logger = logger;
    }

    public async Task<AvatarDto> Handle(UploadAvatarCommand rq, CancellationToken cancellationToken)
    {
        var userId = _user.UserId!.Value;
        var contentType = (rq.File.ContentType ?? string.Empty).ToLowerInvariant();
        if (!UploadMediaCommandHandler.ImageContentTypes.Contains(contentType))
            throw new ErrorCodeException(ErrorCodes.MEDIA_UNSUPPORTED_TYPE, $"Định dạng {contentType} không được hỗ trợ");
        if (rq.Length <= 0)
            throw new ErrorCodeException(ErrorCodes.MEDIA_INVALID_FILE, "File rỗng");
        if (rq.Length > _mediaSettings.MaxImageSizeMb * 1024L * 1024L)
            throw new ErrorCodeException(ErrorCodes.MEDIA_FILE_TOO_LARGE, $"File vượt quá {_mediaSettings.MaxImageSizeMb}MB");

        var mediaId = Guid.NewGuid();
        MediaProcessResult processed;
        try
        {
            processed = await _mediaAiClient.ProcessAsync(rq.File.Data!, rq.File.FileName ?? "avatar", contentType, mediaId,
                "image", cancellationToken);
        }
        catch (MediaAiException ex)
        {
            var code = ex.ErrorCode == ErrorCodes.MEDIA_INVALID_FILE ? ErrorCodes.MEDIA_INVALID_FILE : ErrorCodes.MEDIA_PROCESS_FAILED;
            throw new ErrorCodeException(code, ex.Message);
        }

        _context.Media.Add(new Media
        {
            Id = mediaId,
            OwnerId = userId,
            Type = MediaType.Image,
            ObjectKey = processed.ObjectKey,
            ThumbnailKey = processed.ThumbnailKey,
            ContentType = processed.ContentType,
            Size = processed.Size,
            Width = processed.Width,
            Height = processed.Height,
            OriginalFileName = rq.File.FileName,
            ModerationStatus = MediaModerationStatus.Pending
        });
        await _context.SaveChangesAsync(cancellationToken);

        var outcome = await _moderationService.ModerateAvatarAsync(mediaId);
        if (outcome == AvatarModerationOutcome.Violated)
            throw new ErrorCodeException(ErrorCodes.AVATAR_VIOLATED,
                "Ảnh đại diện vi phạm tiêu chuẩn cộng đồng (khoả thân / khiêu dâm / bạo lực) nên đã bị từ chối và tính 1 vi phạm");
        if (outcome != AvatarModerationOutcome.Approved)
            throw new ErrorCodeException(ErrorCodes.AVATAR_MODERATION_FAILED, "Chưa kiểm duyệt được ảnh, vui lòng thử lại sau");

        var user = await _context.DomainUsers.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new ErrorCodeException(ErrorCodes.COMMON_NOT_FOUND, "User not found");
        var oldAvatarId = user.AvatarMediaId;
        user.AvatarMediaId = mediaId;
        await _context.SaveChangesAsync(cancellationToken);

        if (oldAvatarId != null)
            await AvatarCleanup.RemoveAsync(_context, _mediaAiClient, _logger, oldAvatarId.Value);

        return new AvatarDto { AvatarId = mediaId };
    }
}

/// <summary>Xoá ảnh đại diện, quay về ảnh mặc định</summary>
[Authorize]
public class DeleteAvatarCommand : IRequest<AvatarDto>
{
}

public class DeleteAvatarCommandHandler : IRequestHandler<DeleteAvatarCommand, AvatarDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly IMediaAiClient _mediaAiClient;
    private readonly ILogger<DeleteAvatarCommandHandler> _logger;

    public DeleteAvatarCommandHandler(IApplicationDbContext context, IUser user, IMediaAiClient mediaAiClient,
        ILogger<DeleteAvatarCommandHandler> logger)
    {
        _context = context;
        _user = user;
        _mediaAiClient = mediaAiClient;
        _logger = logger;
    }

    public async Task<AvatarDto> Handle(DeleteAvatarCommand rq, CancellationToken cancellationToken)
    {
        var user = await _context.DomainUsers.FirstOrDefaultAsync(u => u.Id == _user.UserId, cancellationToken)
                   ?? throw new ErrorCodeException(ErrorCodes.COMMON_NOT_FOUND, "User not found");
        var oldAvatarId = user.AvatarMediaId;
        if (oldAvatarId == null)
            return new AvatarDto();

        user.AvatarMediaId = null;
        await _context.SaveChangesAsync(cancellationToken);
        await AvatarCleanup.RemoveAsync(_context, _mediaAiClient, _logger, oldAvatarId.Value);
        return new AvatarDto();
    }
}

/// <summary>
/// URL tải ảnh đại diện (presigned, ngắn hạn). Không cần đăng nhập vì thẻ &lt;img&gt; không gửi được token:
/// chỉ trả ảnh đang là avatar của một tài khoản còn hoạt động; id là GUID ngẫu nhiên và đổi mỗi lần đổi ảnh.
/// </summary>
public class GetAvatarUrlQuery : IRequest<string>
{
    public required Guid AvatarId { get; set; }
}

public class GetAvatarUrlQueryHandler : IRequestHandler<GetAvatarUrlQuery, string>
{
    private readonly IApplicationDbContext _context;

    public GetAvatarUrlQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string> Handle(GetAvatarUrlQuery rq, CancellationToken cancellationToken)
    {
        // query filter của DomainUsers đã loại tài khoản bị xoá / bị khoá
        var isAvatar = await _context.DomainUsers.AnyAsync(u => u.AvatarMediaId == rq.AvatarId, cancellationToken);
        var url = isAvatar ? MediaKeys.SignedUrl(MediaKeys.MainKey(rq.AvatarId, MediaType.Image), MediaKeys.ImageTtl) : null;
        return url ?? throw new ErrorCodeException(ErrorCodes.AVATAR_NOT_FOUND, "Không tìm thấy ảnh đại diện");
    }
}

internal static class AvatarCleanup
{
    /// <summary>Đánh dấu xoá media của avatar cũ và xoá file trên MinIO (lỗi xoá file chỉ ghi log)</summary>
    public static async Task RemoveAsync(IApplicationDbContext context, IMediaAiClient mediaAiClient, ILogger logger, Guid mediaId)
    {
        var media = await context.Media.FirstOrDefaultAsync(m => m.Id == mediaId);
        if (media == null || media.IsDeleted)
            return;

        media.IsDeleted = true;
        await context.SaveChangesAsync(CancellationToken.None);
        try
        {
            await mediaAiClient.DeleteAsync(new[] { media.ObjectKey, media.ThumbnailKey }.Where(k => k != null)!, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not delete objects of old avatar {MediaId}", mediaId);
        }
    }
}
