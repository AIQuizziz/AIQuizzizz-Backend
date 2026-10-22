using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.Common.Settings;
using CleanArchitectureBase.Application.MediaFiles.Dtos;
using CleanArchitectureBase.Application.MediaFiles.Services;
using CleanArchitectureBase.Application.Plans.Service;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;
using Microsoft.Extensions.Options;

namespace CleanArchitectureBase.Application.MediaFiles.Commands;

/// <summary>
/// Upload 1 ảnh/video để gắn vào câu hỏi. Ảnh -> JPEG (tối đa Full HD), video -> MP4 H.264 faststart.
/// Ảnh được upload bình thường, kiểm duyệt (NudeNet + ViT violence) chạy sau bằng job.
/// </summary>
[Authorize]
public class UploadMediaCommand : IRequest<UploadMediaResultDto>
{
    public required FileStreamData File { get; set; }
    public long Length { get; set; }
}

public class UploadMediaCommandHandler : IRequestHandler<UploadMediaCommand, UploadMediaResultDto>
{
    internal static readonly string[] ImageContentTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif", "image/bmp", "image/heic", "image/heif", "image/tiff"];

    private static readonly string[] VideoContentTypes =
        ["video/mp4", "video/quicktime", "video/webm", "video/x-matroska", "video/x-msvideo", "video/mpeg", "video/3gpp"];

    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly IPlanService _planService;
    private readonly IMediaAiClient _mediaAiClient;
    private readonly IModerationScheduler _moderationScheduler;
    private readonly MediaSettings _mediaSettings;

    public UploadMediaCommandHandler(IApplicationDbContext context, IUser user, IPlanService planService,
        IMediaAiClient mediaAiClient, IModerationScheduler moderationScheduler, IOptions<MediaSettings> mediaSettings)
    {
        _context = context;
        _user = user;
        _planService = planService;
        _mediaAiClient = mediaAiClient;
        _moderationScheduler = moderationScheduler;
        _mediaSettings = mediaSettings.Value;
    }

    public async Task<UploadMediaResultDto> Handle(UploadMediaCommand rq, CancellationToken cancellationToken)
    {
        var userId = _user.UserId!.Value;
        var contentType = (rq.File.ContentType ?? string.Empty).ToLowerInvariant();

        MediaType type;
        if (ImageContentTypes.Contains(contentType))
            type = MediaType.Image;
        else if (VideoContentTypes.Contains(contentType))
            type = MediaType.Video;
        else
            throw new ErrorCodeException(ErrorCodes.MEDIA_UNSUPPORTED_TYPE, $"Định dạng {contentType} không được hỗ trợ");

        // Quyền theo gói
        if (type == MediaType.Image && !await _planService.CanUploadImage(userId))
            throw new ErrorCodeException(ErrorCodes.PLAN_NOT_ALLOW_UPLOAD_IMAGE, "Gói hiện tại không cho phép upload ảnh");
        if (type == MediaType.Video && !await _planService.CanUploadVideo(userId))
            throw new ErrorCodeException(ErrorCodes.PLAN_NOT_ALLOW_UPLOAD_VIDEO, "Gói hiện tại không cho phép upload video");

        var maxMb = type == MediaType.Image ? _mediaSettings.MaxImageSizeMb : _mediaSettings.MaxVideoSizeMb;
        if (rq.Length <= 0)
            throw new ErrorCodeException(ErrorCodes.MEDIA_INVALID_FILE, "File rỗng");
        if (rq.Length > maxMb * 1024L * 1024L)
            throw new ErrorCodeException(ErrorCodes.MEDIA_FILE_TOO_LARGE, $"File vượt quá {maxMb}MB");

        var mediaId = Guid.NewGuid();
        MediaProcessResult processed;
        try
        {
            processed = await _mediaAiClient.ProcessAsync(rq.File.Data!, rq.File.FileName ?? "upload", contentType, mediaId,
                type == MediaType.Image ? "image" : "video", cancellationToken);
        }
        catch (MediaAiException ex)
        {
            var code = ex.ErrorCode switch
            {
                ErrorCodes.MEDIA_INVALID_FILE => ErrorCodes.MEDIA_INVALID_FILE,
                ErrorCodes.MEDIA_VIDEO_TOO_LONG => ErrorCodes.MEDIA_VIDEO_TOO_LONG,
                _ => ErrorCodes.MEDIA_PROCESS_FAILED
            };
            throw new ErrorCodeException(code, ex.Message);
        }

        var media = new Media
        {
            Id = mediaId,
            OwnerId = userId,
            Type = type,
            ObjectKey = processed.ObjectKey,
            ThumbnailKey = processed.ThumbnailKey,
            ContentType = processed.ContentType,
            Size = processed.Size,
            Width = processed.Width,
            Height = processed.Height,
            DurationSeconds = processed.DurationSeconds,
            OriginalFileName = rq.File.FileName,
            // Chỉ ảnh được kiểm duyệt tự động
            ModerationStatus = type == MediaType.Image ? MediaModerationStatus.Pending : MediaModerationStatus.NotRequired
        };

        _context.Media.Add(media);
        await _context.SaveChangesAsync(cancellationToken);

        if (type == MediaType.Image)
            _moderationScheduler.EnqueueImageModeration(media.Id);

        var dto = QuestionMediaDto.From(media.Id, media.Type)!;
        return new UploadMediaResultDto
        {
            Id = media.Id,
            Type = dto.Type,
            Url = dto.Url,
            ThumbnailUrl = dto.ThumbnailUrl,
            ExpiresAt = dto.ExpiresAt,
            Width = media.Width,
            Height = media.Height,
            DurationSeconds = media.DurationSeconds,
            Size = media.Size
        };
    }
}
