using CleanArchitectureBase.Domain.Constants;

namespace CleanArchitectureBase.Domain.Entities;

//TODO: đổi tiền về kiểu dữ liệu khác nếu muốn sử dụng quốc tế
public class User
{
    public required Guid Id { get; set; } = Guid.NewGuid();
    public string? FullName { get; set; }
    public required string Email { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsBanned { get; set; }
    public string? BanReason { get; set; }
    public DateTimeOffset? BannedAt { get; set; }
    public long TokenCount { get; set; }
    public long Balance { get; set; }
    public string? PaymentCode { get; set; } = Payment.PaymentCodePrefix + Guid.NewGuid().ToString("N")[..30];
    public bool IsPaymentLocked { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Ảnh đại diện hiện tại (Media loại Image, đã qua kiểm duyệt). Null = dùng ảnh mặc định</summary>
    public Guid? AvatarMediaId { get; set; }
    // Navigation properties
    public Media? AvatarMedia { get; set; }
    public List<UserSubscription> UserSubscriptions { get; set; } = new();
    public List<QuestionSetUser> QuestionSetUsers { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
    public List<UserQuestionSetHistory> UserQuestionSetHistories { get; set; } = new();
    public List<FolderUser> FolderUsers { get; set; } = new();
    public List<ClassUser> ClassUsers { get; set; } = new();
    public List<ClassInvitationUser> ClassInvitationUsers { get; set; } = new();
    public List<TestGrade> TestGrades { get; set; } = new();
    public List<Attempt> Attempts { get; set; } = new();
    public List<UserQuestionSetAccessHistory> AccessHistories { get; set; } = new();
    public List<UserViolation> Violations { get; set; } = new();

}


