using System.Collections.Concurrent;
using CleanArchitectureBase.Application.Common.Interfaces;

namespace CleanArchitectureBase.Application.Command.UnitTests.Fakes;

/// <summary>
/// media-ai giả cho functional test (không cần MinIO / model AI). Kết quả kiểm duyệt theo tên file gốc:
/// "violate*" -> vi phạm (nudity), "nsfw*" -> vi phạm (nsfw, tranh vẽ 18+), "error*" -> service lỗi, còn lại -> hợp lệ.
/// </summary>
public class FakeMediaAiClient : IMediaAiClient
{
    private readonly ConcurrentDictionary<string, string> _fileNameByKey = new();

    public ConcurrentBag<string> DeletedKeys { get; } = new();

    public Task<MediaProcessResult> ProcessAsync(Stream file, string fileName, string contentType, Guid mediaId, string kind,
        CancellationToken cancellationToken)
    {
        var key = kind == "image" ? $"img/{mediaId}.jpg" : $"vid/{mediaId}.mp4";
        _fileNameByKey[key] = fileName;
        return Task.FromResult(new MediaProcessResult
        {
            ObjectKey = key,
            ContentType = kind == "image" ? "image/jpeg" : "video/mp4",
            Size = 1234,
            Width = 512,
            Height = 512
        });
    }

    public Task<ModerationResult> ModerateImageAsync(string objectKey, CancellationToken cancellationToken)
    {
        var fileName = _fileNameByKey.GetValueOrDefault(objectKey, string.Empty);
        if (fileName.StartsWith("error", StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException("media-ai unavailable");
        if (fileName.StartsWith("nsfw", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new ModerationResult
            {
                Violated = true,
                Reasons = ["nsfw"],
                Nsfw = new ModerationNsfw { Label = "nsfw", Score = 0.99 }
            });
        var violated = fileName.StartsWith("violate", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(new ModerationResult
        {
            Violated = violated,
            Reasons = violated ? ["nudity"] : []
        });
    }

    public Task DeleteAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        foreach (var key in keys)
            DeletedKeys.Add(key);
        return Task.CompletedTask;
    }
}

/// <summary>Ký URL giả (không cần khoá S3)</summary>
public class FakeMediaUrlSigner : IMediaUrlSigner
{
    public string GetUrl(string objectKey, TimeSpan lifetime) => $"https://media.test/{objectKey}?ttl={(int)lifetime.TotalSeconds}";
}
