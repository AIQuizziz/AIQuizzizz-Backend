using System.Reflection;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;
using System.Linq.Dynamic.Core;
using CleanArchitectureBase.Application.Common.Security;

namespace CleanArchitectureBase.Application.Users;

public class UserForListDto
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public string? FullName { get; set; }
    public Guid? AvatarId { get; set; }
    public long Token { get; set; }
    public long Balance { get; set; }
    public bool IsBanned { get; set; }
    public string? Role { get; set; }
}

[Authorize(Roles = Domain.Constants.Roles.Administrator + "," + Domain.Constants.Roles.Moderator)]
public class SearchAllAccountQuery : IRequest<PaginatedList<UserForListDto>>
{
    public string? Keyword { get; set; }
    public string? FieldName { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 5;
    public bool? IsBanned { get; set; }
    /// <summary>
    /// User/Administrator/Moderator
    /// </summary>
    public string? Role { get; set; }
}

public class SearchUserDto
{
    public string? Keyword { get; set; }
    public string? FieldName { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 5;
    public bool? IsBanned { get; set; }
    public string? Role { get; set; }
}

public class SearchAllAccountQueryValidator : AbstractValidator<SearchAllAccountQuery>
{
    private static readonly string[] AllowedRoles = { "User", "Administrator", "Moderator" };

    public SearchAllAccountQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Số trang phải lớn hơn hoặc bằng 1");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Kích thước trang phải từ 1 đến 100");
        RuleFor(x => x.Role)
            .Must(role => string.IsNullOrEmpty(role) || AllowedRoles.Contains(role))
            .WithMessage("Role phải là một trong: User, Administrator, Moderator");
    }
}

public class GetAllAccountCommandHandler : IRequestHandler<SearchAllAccountQuery, PaginatedList<UserForListDto>>
{
    private readonly IIdentityService _identityService;

    public GetAllAccountCommandHandler(IApplicationDbContext context, IIdentityService identityService)
    {
        _identityService = identityService;
    }

    public async Task<PaginatedList<UserForListDto>> Handle(SearchAllAccountQuery rq, CancellationToken cancellationToken)
    {
        SearchUserDto dto = new SearchUserDto
        {
            Keyword = rq.Keyword,
            FieldName = rq.FieldName,
            PageNumber = rq.PageNumber,
            PageSize = rq.PageSize,
            IsBanned = rq.IsBanned,
            Role = rq.Role
        };
        
        var result = await _identityService.SearchUserWithRole(dto);
        return result;
    }
}
