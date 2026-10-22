using System.Security.Cryptography;
using System.Text;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Common.Settings;
using Microsoft.Extensions.Options;

namespace CleanArchitectureBase.Infrastructure.MediaStorage;

/// <summary>
/// Tạo presigned GET URL (AWS Signature V4, query-string) cho MinIO. Chỉ tính toán cục bộ, không gọi mạng.
/// URL trỏ tới endpoint public (nginx -> MinIO, giữ nguyên Host) nên chữ ký phải dùng đúng host public.
/// Thời điểm ký được làm tròn theo cửa sổ để URL ổn định trong một khoảng -> trình duyệt cache được.
/// </summary>
public class S3PresignedUrlSigner : IMediaUrlSigner
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private readonly MediaSettings _settings;
    private readonly Uri _endpoint;
    private readonly TimeProvider _timeProvider;

    public S3PresignedUrlSigner(IOptions<MediaSettings> settings, TimeProvider timeProvider)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _endpoint = new Uri(_settings.PublicS3Endpoint.TrimEnd('/') + "/");
    }

    public string GetUrl(string objectKey, TimeSpan lifetime)
    {
        if (string.IsNullOrEmpty(_settings.S3AccessKey) || string.IsNullOrEmpty(_settings.S3SecretKey))
            throw new InvalidOperationException("Media S3 credentials are not configured.");

        var window = TimeSpan.FromMinutes(Math.Max(1, _settings.PresignWindowMinutes));
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var signedAt = new DateTime(now.Ticks - now.Ticks % window.Ticks, DateTimeKind.Utc);
        // cộng thêm 1 cửa sổ để thời gian còn hiệu lực luôn >= lifetime
        var expires = (int)Math.Min((lifetime + window).TotalSeconds, 604800);

        var amzDate = signedAt.ToString("yyyyMMdd'T'HHmmss'Z'");
        var dateStamp = signedAt.ToString("yyyyMMdd");
        var region = _settings.S3Region;
        var scope = $"{dateStamp}/{region}/s3/aws4_request";

        // ký theo địa chỉ trình duyệt đang dùng (IP / tên miền), mặc định PublicS3Endpoint
        var endpoint = MediaRequestOrigin.Current ?? _endpoint;
        var basePath = endpoint.AbsolutePath.TrimEnd('/');
        var canonicalUri = $"{basePath}/{_settings.S3Bucket}/{EncodePath(objectKey)}";
        var host = endpoint.IsDefaultPort ? endpoint.Host : $"{endpoint.Host}:{endpoint.Port}";

        var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Amz-Algorithm"] = Algorithm,
            ["X-Amz-Credential"] = $"{_settings.S3AccessKey}/{scope}",
            ["X-Amz-Date"] = amzDate,
            ["X-Amz-Expires"] = expires.ToString(),
            ["X-Amz-SignedHeaders"] = "host",
        };
        var canonicalQuery = string.Join("&", query.Select(kv => $"{Encode(kv.Key)}={Encode(kv.Value)}"));

        var canonicalRequest = string.Join("\n",
            "GET", canonicalUri, canonicalQuery, $"host:{host}\n", "host", "UNSIGNED-PAYLOAD");

        var stringToSign = string.Join("\n",
            Algorithm, amzDate, scope, Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var signingKey = Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4" + _settings.S3SecretKey), dateStamp), region), "s3"),
            "aws4_request");
        var signature = Hex(Hmac(signingKey, stringToSign));

        return $"{endpoint.Scheme}://{host}{canonicalUri}?{canonicalQuery}&X-Amz-Signature={signature}";
    }

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    // RFC 3986: chỉ giữ A-Z a-z 0-9 - _ . ~
    private static string Encode(string value) => Uri.EscapeDataString(value);

    private static string EncodePath(string key) => string.Join("/", key.Split('/').Select(Encode));
}
