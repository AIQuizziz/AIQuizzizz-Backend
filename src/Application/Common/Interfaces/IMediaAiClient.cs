namespace CleanArchitectureBase.Application.Common.Interfaces;

/// <summary>
/// Client gọi service media-ai (chuẩn hoá media + lưu MinIO + kiểm duyệt ảnh)
/// </summary>
public interface IMediaAiClient
{
    Task<MediaProcessResult> ProcessAsync(Stream file, string fileName, string contentType, Guid mediaId, string kind,
        CancellationToken cancellationToken);

    Task<ModerationResult> ModerateImageAsync(string objectKey, CancellationToken cancellationToken);

    Task DeleteAsync(IEnumerable<string> keys, CancellationToken cancellationToken);
}

public class MediaProcessResult
{
    public string ObjectKey { get; set; } = null!;
    public string? ThumbnailKey { get; set; }
    public string ContentType { get; set; } = null!;
    public long Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double? DurationSeconds { get; set; }
}

public class ModerationResult
{
    public bool Violated { get; set; }
    public List<string> Reasons { get; set; } = new();
    public ModerationNudity? Nudity { get; set; }
    public ModerationViolence? Violence { get; set; }
    /// <summary>Phân loại cả ảnh (media-ai >= 1.1: Falconsai), bắt ảnh 18+ dạng tranh vẽ / anime mà NudeNet chấm thấp</summary>
    public ModerationNsfw? Nsfw { get; set; }
}

public class ModerationNsfw
{
    public string? Label { get; set; }
    public double Score { get; set; }
}

public class ModerationNudity
{
    public double MaxScore { get; set; }
    public List<ModerationLabel> Labels { get; set; } = new();
}

public class ModerationViolence
{
    public string? Label { get; set; }
    public double Score { get; set; }
}

public class ModerationLabel
{
    public string Label { get; set; } = null!;
    public double Score { get; set; }
}

/// <summary>Lỗi do media-ai trả về (422/404/500) kèm mã lỗi</summary>
public class MediaAiException : Exception
{
    public string ErrorCode { get; }
    public int StatusCode { get; }

    public MediaAiException(string errorCode, int statusCode, string message) : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}
