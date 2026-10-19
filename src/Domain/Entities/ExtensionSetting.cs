namespace CleanArchitectureBase.Domain.Entities;

public enum ExtensionTargetMode
{
    /// <summary>Luôn thêm vào một bộ câu hỏi cố định (TargetQuestionSetId)</summary>
    Fixed,
    /// <summary>Tự tạo bộ mới theo ngày / tuần / tháng (tính theo múi giờ người dùng)</summary>
    Daily,
    Weekly,
    Monthly
}

/// <summary>Cài đặt của extension trình duyệt (mỗi người dùng một dòng), đồng bộ giữa các thiết bị.</summary>
public class ExtensionSetting : BaseAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ExtensionTargetMode TargetMode { get; set; } = ExtensionTargetMode.Daily;
    /// <summary>Bộ câu hỏi đích khi TargetMode = Fixed</summary>
    public Guid? TargetQuestionSetId { get; set; }
    /// <summary>Tự động tra nghĩa/dịch (Free Dictionary + dịch sang ngôn ngữ đích). Tắt: thẻ được tạo với phần nghĩa để trống.</summary>
    public bool TranslateEnabled { get; set; } = true;
    public string SourceLang { get; set; } = "en";
    public string TargetLang { get; set; } = "vi";
    /// <summary>Lưu câu chứa từ được bôi đen (ngữ cảnh) vào thẻ</summary>
    public bool StoreContext { get; set; } = true;
    /// <summary>Múi giờ IANA dùng để chia bộ theo ngày/tuần/tháng, vd. Asia/Ho_Chi_Minh</summary>
    public string TimeZone { get; set; } = "UTC";
}
