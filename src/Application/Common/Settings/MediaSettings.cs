namespace CleanArchitectureBase.Application.Common.Settings;

public class MediaSettings
{
    /// <summary>URL nội bộ của service media-ai (vd: http://media-ai:8000)</summary>
    public string MediaAiBaseUrl { get; set; } = "http://media-ai:8000";
    public string? MediaAiApiKey { get; set; }

    // ---- Presigned URL (bucket private) ----
    /// <summary>Endpoint public mà trình duyệt truy cập MinIO qua nginx (giữ nguyên Host), vd: http://100.86.165.118:8090</summary>
    public string PublicS3Endpoint { get; set; } = "http://localhost:8090";
    /// <summary>
    /// Các địa chỉ public khác cũng dùng được (cách nhau bởi dấu phẩy), vd https://thanhkhac.id.vn. URL media được ký
    /// theo địa chỉ khớp Host của request; không khớp -> PublicS3Endpoint.
    /// </summary>
    public string? PublicOrigins { get; set; }
    public string S3Bucket { get; set; } = "aiquizz-media";
    public string S3Region { get; set; } = "us-east-1";
    public string? S3AccessKey { get; set; }
    public string? S3SecretKey { get; set; }
    /// <summary>Thời hạn URL ảnh (phút)</summary>
    public int ImageUrlTtlMinutes { get; set; } = 15;
    /// <summary>Thời hạn URL video (phút) - dài hơn để không đứt khi đang xem (Range request)</summary>
    public int VideoUrlTtlMinutes { get; set; } = 120;
    /// <summary>Làm tròn thời điểm ký theo cửa sổ (phút) để URL ổn định -> cache được</summary>
    public int PresignWindowMinutes { get; set; } = 5;

    public int MaxImageSizeMb { get; set; } = 15;
    public int MaxVideoSizeMb { get; set; } = 200;
    public int MaxVideoSeconds { get; set; } = 600;
}

public class ModerationSettings
{
    /// <summary>Tắt để bỏ qua kiểm duyệt (ảnh sẽ được Approved ngay)</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Số vi phạm còn hiệu lực để tự động ban</summary>
    public int StrikesToBan { get; set; } = 3;
    /// <summary>Vi phạm hết hiệu lực sau N ngày (mặc định ~3 tháng)</summary>
    public int StrikeExpiryDays { get; set; } = 90;
    /// <summary>Số lần thử kiểm duyệt trước khi đánh dấu Failed</summary>
    public int MaxAttempts { get; set; } = 5;
    /// <summary>Cron job quét ảnh chờ kiểm duyệt (mặc định mỗi 5 phút)</summary>
    public string SweepCron { get; set; } = "*/5 * * * *";
    /// <summary>Cron job rà soát vi phạm hằng ngày (mặc định 02:00 UTC)</summary>
    public string DailyReviewCron { get; set; } = "0 2 * * *";
    /// <summary>
    /// CHỈ DÙNG KHI TEST: file có tên bắt đầu bằng tiền tố này luôn bị coi là vi phạm (để test luồng strike/ban
    /// mà không cần nội dung xấu thật). Để trống = tắt.
    /// </summary>
    public string? TestViolationFilePrefix { get; set; }
}
