using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Domain.Constants;

namespace CleanArchitectureBase.Application.Users;

public class UserProfileDto
{
    public Guid? Id { get; set; }
    public string? Email { get; set; }
    public string? FullName { get; set; }
    /// <summary>Ảnh đại diện: GET /api/Account/Avatar/{AvatarId}; null = ảnh mặc định</summary>
    public Guid? AvatarId { get; set; }
    public long TokenCount { get; set; }
    public long Balance { get; set; }
    public List<string> Roles { get; set; } = new();
}

[Authorize]
public class GetProfileQuery : IRequest<UserProfileDto>
{

}

public class GetProfileQueryHandler : IRequestHandler<GetProfileQuery, UserProfileDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly IIdentityService _identityService;


    public GetProfileQueryHandler(IApplicationDbContext context, IUser user, IIdentityService identityService)
    {
        _context = context;
        _user = user;
        _identityService = identityService;
    }

    public async Task<UserProfileDto> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        var user = await _context.DomainUsers.Where(x => x.Id == _user.UserId).FirstOrDefaultAsync();
        if (user == null)
            throw new ErrorCodeException(ErrorCodes.COMMON_NOT_FOUND, $"User with id {_user.UserId} not found");

        var roles = await _identityService.GetUserRolesAsync(user.Id);


        var result = new UserProfileDto
        {
            Email = user.Email,
            FullName = user.FullName,
            AvatarId = user.AvatarMediaId,
            Id = user.Id,
            TokenCount = user.TokenCount,
            Balance = user.Balance,
            Roles = roles.ToList()
        };
        return result;
    }
}
