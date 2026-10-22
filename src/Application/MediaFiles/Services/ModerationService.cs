using System.Text.Json;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Settings;
using CleanArchitectureBase.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanArchitectureBase.Application.MediaFiles.Services;

/// <summary>Kết quả kiểm duyệt ảnh đại diện (chạy ngay lúc upload)</summary>
public enum AvatarModerationOutcome
{
    Approved,
    Violated, // đã từ chối: xoá file + ghi 1 vi phạm cho chủ ảnh
    Failed    // không kiểm duyệt được (service lỗi): đã xoá file, không tính vi phạm
}

/// <summary>Đưa job kiểm duyệt vào hàng đợi (Hangfire)</summary>
public interface IModerationScheduler
{
    void EnqueueImageModeration(Guid mediaId);
}

/// <summary>
/// Kiểm duyệt ảnh sau khi upload + quản lý vi phạm (strike):
/// - Ảnh vi phạm -> xoá file, xoá (soft delete) mọi câu hỏi dùng ảnh, ghi 1 vi phạm cho chủ ảnh.
/// - Vi phạm hết hiệu lực sau StrikeExpiryDays (job hằng ngày gỡ dần).
/// - Đủ StrikesToBan vi phạm còn hiệu lực -> tự động ban, lưu lý do (hiện khi đăng nhập).
/// </summary>
public interface IModerationService
{
    Task ModerateImageAsync(Guid mediaId);
    /// <summary>
    /// Kiểm duyệt ảnh đại diện ngay (không qua hàng đợi, không thử lại). Vi phạm -> xoá file + ghi 1 vi phạm
    /// (có thể tự động ban). Lỗi -> xoá file, đánh dấu Failed. Chỉ Approved mới được gán làm avatar.
    /// </summary>
    Task<AvatarModerationOutcome> ModerateAvatarAsync(Guid mediaId);
    Task SweepPendingAsync();
    Task DailyReviewAsync();
}

public class ModerationService : IModerationService
{
    private const int SweepBatchSize = 50;

    private readonly IApplicationDbContext _context;
    private readonly IMediaAiClient _mediaAiClient;
    private readonly IIdentityService _identityService;
    private readonly IEmailService _emailService;
    private readonly ILogger<ModerationService> _logger;
    private readonly ModerationSettings _settings;

    public ModerationService(IApplicationDbContext context, IMediaAiClient mediaAiClient, IIdentityService identityService,
        IEmailService emailService, ILogger<ModerationService> logger, IOptions<ModerationSettings> settings)
    {
        _context = context;
        _mediaAiClient = mediaAiClient;
        _identityService = identityService;
        _emailService = emailService;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task ModerateImageAsync(Guid mediaId)
    {
        var media = await _context.Media
            .FirstOrDefaultAsync(m => m.Id == mediaId && m.Type == MediaType.Image
                                                       && m.ModerationStatus == MediaModerationStatus.Pending);
        if (media == null)
            return; // đã xử lý hoặc không tồn tại

        ModerationResult result;
        try
        {
            result = await RunModerationAsync(media);
        }
        catch (Exception ex)
        {
            media.ModerationAttempts++;
            if (media.ModerationAttempts >= _settings.MaxAttempts)
                media.ModerationStatus = MediaModerationStatus.Failed;
            await _context.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning(ex, "Moderation failed for media {MediaId} (attempt {Attempt})", media.Id, media.ModerationAttempts);
            return;
        }

        media.ModerationAttempts++;
        media.ModeratedAt = DateTimeOffset.UtcNow;
        media.ModerationResultJson = JsonSerializer.Serialize(result);

        if (!result.Violated)
        {
            media.ModerationStatus = MediaModerationStatus.Approved;
            await _context.SaveChangesAsync(CancellationToken.None);
            return;
        }

        await HandleViolationAsync(media, result);
    }

    public async Task<AvatarModerationOutcome> ModerateAvatarAsync(Guid mediaId)
    {
        var media = await _context.Media
            .FirstOrDefaultAsync(m => m.Id == mediaId && m.Type == MediaType.Image
                                                       && m.ModerationStatus == MediaModerationStatus.Pending);
        if (media == null)
            return AvatarModerationOutcome.Failed;

        media.ModerationAttempts++;
        ModerationResult result;
        try
        {
            result = await RunModerationAsync(media);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Avatar moderation failed for media {MediaId}", media.Id);
            media.ModerationStatus = MediaModerationStatus.Failed;
            media.IsDeleted = true;
            await _context.SaveChangesAsync(CancellationToken.None);
            await DeleteObjectsSafeAsync(media);
            return AvatarModerationOutcome.Failed;
        }

        media.ModeratedAt = DateTimeOffset.UtcNow;
        media.ModerationResultJson = JsonSerializer.Serialize(result);

        if (!result.Violated)
        {
            media.ModerationStatus = MediaModerationStatus.Approved;
            await _context.SaveChangesAsync(CancellationToken.None);
            return AvatarModerationOutcome.Approved;
        }

        media.ModerationStatus = MediaModerationStatus.Violated;
        media.IsDeleted = true;
        var reasonText = DescribeReasons(result.Reasons);
        await RecordStrikeAsync(media, $"Ảnh đại diện vi phạm tiêu chuẩn cộng đồng ({reasonText})", 0);

        _logger.LogWarning("Avatar media {MediaId} violated ({Reasons}); owner {OwnerId}", media.Id, reasonText, media.OwnerId);

        await DeleteObjectsSafeAsync(media);
        await BanOrNotifyAsync(media.OwnerId,
            $"Ảnh đại diện bạn tải lên bị phát hiện vi phạm ({reasonText}) và đã bị từ chối.");
        return AvatarModerationOutcome.Violated;
    }

    public async Task SweepPendingAsync()
    {
        // bỏ qua ảnh vừa upload (job enqueue ngay sau upload sẽ xử lý) để tránh chạy trùng
        var threshold = DateTimeOffset.UtcNow.AddMinutes(-2);
        var ids = await _context.Media
            .Where(m => m.Type == MediaType.Image && m.ModerationStatus == MediaModerationStatus.Pending
                                                  && m.Created < threshold)
            .OrderBy(m => m.Created)
            .Select(m => m.Id)
            .Take(SweepBatchSize)
            .ToListAsync();

        foreach (var id in ids)
            await ModerateImageAsync(id);
    }

    public async Task DailyReviewAsync()
    {
        var now = DateTimeOffset.UtcNow;

        // 1. Gỡ các vi phạm đã quá hạn
        var expired = await _context.UserViolations
            .Where(v => !v.IsExpired && v.ExpiresAt <= now)
            .ToListAsync();
        foreach (var v in expired)
            v.IsExpired = true;
        await _context.SaveChangesAsync(CancellationToken.None);

        // 2. Đảm bảo user đủ số vi phạm còn hiệu lực đều đã bị ban
        var usersToBan = await _context.UserViolations
            .Where(v => !v.IsExpired && v.ExpiresAt > now)
            .GroupBy(v => v.UserId)
            .Where(g => g.Count() >= _settings.StrikesToBan)
            .Select(g => g.Key)
            .ToListAsync();

        foreach (var userId in usersToBan)
            await BanIfNeededAsync(userId);

        _logger.LogInformation("Violation daily review: {Expired} strikes expired, {Checked} users over threshold",
            expired.Count, usersToBan.Count);
    }

    private async Task<ModerationResult> RunModerationAsync(Media media)
    {
        if (!_settings.Enabled)
            return new ModerationResult { Violated = false };

        // Hook chỉ dùng khi test: tên file có tiền tố cấu hình -> coi như vi phạm
        if (!string.IsNullOrEmpty(_settings.TestViolationFilePrefix)
            && media.OriginalFileName != null
            && media.OriginalFileName.StartsWith(_settings.TestViolationFilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return new ModerationResult { Violated = true, Reasons = ["test"] };
        }

        return await _mediaAiClient.ModerateImageAsync(media.ObjectKey, CancellationToken.None);
    }

    private async Task HandleViolationAsync(Media media, ModerationResult result)
    {
        media.ModerationStatus = MediaModerationStatus.Violated;
        media.IsDeleted = true;

        // Xoá (soft delete) mọi câu hỏi dùng ảnh vi phạm, kể cả bản sao trong bài test
        var questions = await _context.Questions
            .Where(q => q.MediaId == media.Id && !q.IsDeleted)
            .ToListAsync();
        var questionIds = questions.Select(q => q.Id).ToList();
        foreach (var q in questions)
            q.IsDeleted = true;

        foreach (var group in questions.Where(q => q.QuestionSetId.HasValue).GroupBy(q => q.QuestionSetId!.Value))
        {
            var set = await _context.QuestionSets.FirstOrDefaultAsync(s => s.Id == group.Key);
            if (set != null)
                set.QuestionCount = Math.Max(0, set.QuestionCount - group.Count());
        }

        var versionLinks = await _context.TestVersionQuestions
            .Where(v => questionIds.Contains(v.QuestionId))
            .ToListAsync();
        _context.TestVersionQuestions.RemoveRange(versionLinks);

        // Bản gốc của bài test (TestQuestions) quyết định số câu / tổng điểm
        var testLinks = await _context.TestQuestions
            .Where(t => questionIds.Contains(t.QuestionId))
            .ToListAsync();
        _context.TestQuestions.RemoveRange(testLinks);
        foreach (var group in testLinks.GroupBy(t => t.TestId))
        {
            var test = await _context.Tests.FirstOrDefaultAsync(t => t.Id == group.Key);
            if (test != null)
                test.QuestionCount = Math.Max(0, test.QuestionCount - group.Count());
        }

        var reasonText = DescribeReasons(result.Reasons);
        await RecordStrikeAsync(media, $"Ảnh vi phạm tiêu chuẩn cộng đồng ({reasonText})", questions.Count);

        _logger.LogWarning("Media {MediaId} violated ({Reasons}); {Count} questions removed; owner {OwnerId}",
            media.Id, reasonText, questions.Count, media.OwnerId);

        await DeleteObjectsSafeAsync(media);
        await BanOrNotifyAsync(media.OwnerId,
            $"Một ảnh bạn tải lên bị phát hiện vi phạm ({reasonText}). {questions.Count} câu hỏi dùng ảnh này đã bị xóa.");
    }

    /// <summary>Ghi 1 vi phạm cho chủ ảnh và lưu mọi thay đổi đang chờ</summary>
    private async Task RecordStrikeAsync(Media media, string reason, int deletedQuestionCount)
    {
        var now = DateTimeOffset.UtcNow;
        _context.UserViolations.Add(new UserViolation
        {
            UserId = media.OwnerId,
            MediaId = media.Id,
            Reason = reason,
            DetailJson = media.ModerationResultJson,
            DeletedQuestionCount = deletedQuestionCount,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_settings.StrikeExpiryDays)
        });
        await _context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task DeleteObjectsSafeAsync(Media media)
    {
        try
        {
            await _mediaAiClient.DeleteAsync(new[] { media.ObjectKey, media.ThumbnailKey }.Where(k => k != null)!, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not delete objects of media {MediaId}", media.Id);
        }
    }

    /// <summary>Đủ số vi phạm -> ban (kèm email); chưa đủ -> email cảnh báo</summary>
    private async Task BanOrNotifyAsync(Guid userId, string whatHappened)
    {
        var activeCount = await CountActiveStrikesAsync(userId);
        var banned = await BanIfNeededAsync(userId);
        if (!banned)
            await NotifyStrikeAsync(userId, whatHappened, activeCount);
    }

    private Task<int> CountActiveStrikesAsync(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        return _context.UserViolations.CountAsync(v => v.UserId == userId && !v.IsExpired && v.ExpiresAt > now);
    }

    /// <returns>true nếu user vừa bị ban</returns>
    private async Task<bool> BanIfNeededAsync(Guid userId)
    {
        var activeCount = await CountActiveStrikesAsync(userId);
        if (activeCount < _settings.StrikesToBan)
            return false;

        var user = await _context.DomainUsers.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || user.IsBanned)
            return false;

        // Lưu mã ổn định để giao diện tự dịch (vi/en): AUTO_BAN_STRIKES:{số vi phạm}:{số ngày}
        var reason = $"AUTO_BAN_STRIKES:{activeCount}:{_settings.StrikeExpiryDays}";
        var readableReason = $"Tài khoản bị khóa tự động do có {activeCount} vi phạm tiêu chuẩn cộng đồng " +
                             $"(ảnh khỏa thân/khiêu dâm/bạo lực) trong vòng {_settings.StrikeExpiryDays} ngày. " +
                             $"/ Your account was automatically banned for {activeCount} community-guideline violations (nudity/sexual content/violence) within {_settings.StrikeExpiryDays} days.";
        user.IsBanned = true;
        user.BanReason = reason;
        user.BannedAt = DateTimeOffset.UtcNow;
        await _identityService.BanUser(userId, true, reason);
        await _context.SaveChangesAsync(CancellationToken.None);

        _logger.LogWarning("User {UserId} auto-banned: {Reason}", userId, reason);
        await SendEmailSafeAsync(user.Email, "[AIQuizzizz] Tài khoản của bạn đã bị khóa",
            $"<p>{readableReason}</p><p>Nếu bạn cho rằng đây là nhầm lẫn, vui lòng liên hệ quản trị viên.</p>");
        return true;
    }

    private async Task NotifyStrikeAsync(Guid userId, string whatHappened, int activeCount)
    {
        var user = await _context.DomainUsers.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return;

        await SendEmailSafeAsync(user.Email, "[AIQuizzizz] Cảnh báo vi phạm tiêu chuẩn cộng đồng",
            $"<p>{whatHappened}</p>" +
            $"<p>Số vi phạm còn hiệu lực: <b>{activeCount}/{_settings.StrikesToBan}</b>. " +
            $"Mỗi vi phạm tự hết hiệu lực sau {_settings.StrikeExpiryDays} ngày. " +
            $"Đạt {_settings.StrikesToBan} vi phạm tài khoản sẽ bị khóa tự động.</p>");
    }

    private async Task SendEmailSafeAsync(string to, string subject, string body)
    {
        try
        {
            await _emailService.SendEmailAsync(to, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send moderation email");
        }
    }

    private static string DescribeReasons(IEnumerable<string> reasons)
    {
        var mapped = reasons.Select(r => r switch
        {
            "nudity" => "khỏa thân",
            "nsfw" => "khiêu dâm",
            "violence" => "bạo lực",
            "test" => "kiểm thử",
            _ => r
        }).ToList();
        return mapped.Count == 0 ? "nội dung không phù hợp" : string.Join(", ", mapped);
    }
}
