using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.QuestionSets.Services;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Extension;

[Authorize]
public class GetExtensionSettingsQuery : IRequest<ExtensionSettingsDto>
{
}

public class GetExtensionSettingsQueryHandler : IRequestHandler<GetExtensionSettingsQuery, ExtensionSettingsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;

    public GetExtensionSettingsQueryHandler(IApplicationDbContext context, IUser user)
    {
        _context = context;
        _user = user;
    }

    public async Task<ExtensionSettingsDto> Handle(GetExtensionSettingsQuery request, CancellationToken cancellationToken)
    {
        var userId = _user.UserId!.Value;
        var setting = await _context.ExtensionSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
                      ?? ExtensionSettingsDto.Defaults(userId);

        string? targetName = null;
        if (setting.TargetQuestionSetId.HasValue)
        {
            targetName = await _context.QuestionSets
                .Where(q => q.Id == setting.TargetQuestionSetId && !q.IsDeleted)
                .Select(q => q.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return ExtensionSettingsDto.From(setting, targetName);
    }
}

[Authorize]
public class UpdateExtensionSettingsCommand : IRequest<ExtensionSettingsDto>
{
    public string? TargetMode { get; set; }
    public Guid? TargetQuestionSetId { get; set; }
    public bool TranslateEnabled { get; set; }
    public string? SourceLang { get; set; }
    public string? TargetLang { get; set; }
    public bool StoreContext { get; set; } = true;
    public string? TimeZone { get; set; }
}

public class UpdateExtensionSettingsCommandValidator : AbstractValidator<UpdateExtensionSettingsCommand>
{
    public UpdateExtensionSettingsCommandValidator()
    {
        RuleFor(x => x.TargetMode)
            .Must(m => Enum.TryParse<ExtensionTargetMode>(m, ignoreCase: false, out _))
            .WithMessage("Chế độ phải là Fixed, Daily, Weekly hoặc Monthly");

        RuleFor(x => x.TargetQuestionSetId)
            .NotNull().When(x => x.TargetMode == nameof(ExtensionTargetMode.Fixed))
            .WithMessage("Phải chọn bộ câu hỏi khi dùng chế độ Fixed");

        RuleFor(x => x.SourceLang).Must(ExtensionValidation.IsLang).WithMessage("Mã ngôn ngữ nguồn không hợp lệ (vd. en)");
        RuleFor(x => x.TargetLang).Must(ExtensionValidation.IsLang).When(x => x.TranslateEnabled).WithMessage("Mã ngôn ngữ đích không hợp lệ (vd. vi)");
        RuleFor(x => x.TimeZone).Must(ExtensionValidation.IsTimeZoneId).WithMessage("Múi giờ không hợp lệ (vd. Asia/Ho_Chi_Minh)");
    }
}

public class UpdateExtensionSettingsCommandHandler : IRequestHandler<UpdateExtensionSettingsCommand, ExtensionSettingsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly IQuestionSetService _questionSetService;

    public UpdateExtensionSettingsCommandHandler(IApplicationDbContext context, IUser user, IQuestionSetService questionSetService)
    {
        _context = context;
        _user = user;
        _questionSetService = questionSetService;
    }

    public async Task<ExtensionSettingsDto> Handle(UpdateExtensionSettingsCommand request, CancellationToken cancellationToken)
    {
        var userId = _user.UserId!.Value;
        var mode = Enum.Parse<ExtensionTargetMode>(request.TargetMode!);

        string? targetName = null;
        Guid? targetId = null;
        if (mode == ExtensionTargetMode.Fixed)
        {
            targetId = request.TargetQuestionSetId!.Value;
            var set = await _context.QuestionSets.Where(q => q.Id == targetId && !q.IsDeleted).FirstOrDefaultAsync(cancellationToken);
            if (set == null) throw new ErrorCodeException(ErrorCodes.QUESTION_SET_NOT_FOUND);
            if (!await _questionSetService.CanUserEditQuestionSet(userId, set.Id)) throw new ErrorCodeException(ErrorCodes.COMMON_FORBIDDEN);
            targetName = set.Name;
        }

        var setting = await _context.ExtensionSettings.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (setting == null)
        {
            setting = ExtensionSettingsDto.Defaults(userId);
            _context.ExtensionSettings.Add(setting);
        }

        setting.TargetMode = mode;
        setting.TargetQuestionSetId = targetId;
        setting.TranslateEnabled = request.TranslateEnabled;
        setting.SourceLang = request.SourceLang!;
        // Tắt dịch vẫn giữ ngôn ngữ đích cũ để bật lại không phải chọn lại
        if (ExtensionValidation.IsLang(request.TargetLang)) setting.TargetLang = request.TargetLang!;
        setting.StoreContext = request.StoreContext;
        setting.TimeZone = request.TimeZone!;

        await _context.SaveChangesAsync(cancellationToken);
        return ExtensionSettingsDto.From(setting, targetName);
    }
}
