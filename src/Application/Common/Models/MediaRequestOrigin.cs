namespace CleanArchitectureBase.Application.Common.Models;

/// <summary>
/// Địa chỉ public (scheme://host[:port]) mà trình duyệt đang dùng cho request hiện tại, vd https://thanhkhac.id.vn
/// hoặc http://100.86.165.118:8090. Presigned URL của media phải ký đúng host này (SigV4 ký cả header Host).
/// Middleware gán theo Host của request (chỉ nhận địa chỉ có trong cấu hình); null = dùng PublicS3Endpoint.
/// </summary>
public static class MediaRequestOrigin
{
    private static readonly AsyncLocal<Uri?> CurrentOrigin = new();

    public static Uri? Current
    {
        get => CurrentOrigin.Value;
        set => CurrentOrigin.Value = value;
    }
}
