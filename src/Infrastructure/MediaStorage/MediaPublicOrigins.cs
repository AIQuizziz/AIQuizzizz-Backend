using CleanArchitectureBase.Application.Common.Settings;
using Microsoft.Extensions.Options;

namespace CleanArchitectureBase.Infrastructure.MediaStorage;

/// <summary>
/// Danh sách địa chỉ public được phép ký presigned URL: PublicS3Endpoint + PublicOrigins (cách nhau bởi dấu phẩy).
/// Chọn theo tên host của request (bỏ cổng: nginx chuyển Host không kèm cổng), không lấy scheme / cổng từ request
/// nên Host giả mạo chỉ chọn được trong danh sách đã cấu hình.
/// </summary>
public class MediaPublicOrigins
{
    private readonly List<Uri> _origins;

    public MediaPublicOrigins(IOptions<MediaSettings> settings)
    {
        var raw = new[] { settings.Value.PublicS3Endpoint }
            .Concat((settings.Value.PublicOrigins ?? string.Empty).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries));
        _origins = raw
            .Select(o => Uri.TryCreate(o.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) ? uri : null)
            .Where(u => u != null && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            .Select(u => u!)
            .ToList();
    }

    public IReadOnlyList<Uri> All => _origins;

    /// <summary>requestHost: tên host của request (có hoặc không kèm cổng). Không khớp -> null (dùng mặc định)</summary>
    public Uri? Resolve(string? requestHost)
    {
        if (string.IsNullOrWhiteSpace(requestHost))
            return null;
        var host = requestHost.Trim();
        var colon = host.LastIndexOf(':');
        if (colon > 0 && !host.EndsWith(']')) // bỏ cổng (không đụng IPv6 dạng [::1])
            host = host[..colon];
        return _origins.FirstOrDefault(o => string.Equals(o.Host, host, StringComparison.OrdinalIgnoreCase));
    }
}
