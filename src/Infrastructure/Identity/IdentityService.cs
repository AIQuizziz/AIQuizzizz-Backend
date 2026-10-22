using System.IdentityModel.Tokens.Jwt;
using System.Linq.Dynamic.Core;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Common.Settings;
using CleanArchitectureBase.Application.Users;
using CleanArchitectureBase.Application.Users.Common;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;
using CleanArchitectureBase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

// ReSharper disable InconsistentNaming

namespace CleanArchitectureBase.Infrastructure.Identity;

public class IdentityService : IIdentityService
{
    private readonly UserManager<UserAccount> _userManager;
    private readonly IUserClaimsPrincipalFactory<UserAccount> _userClaimsPrincipalFactory;
    private readonly IAuthorizationService _authorizationService;
    private readonly SignInManager<UserAccount> _signInManager;
    private readonly JwtSettings _jwtSettings;
    private readonly ApplicationDbContext _dbContext;
    private readonly IGoogleAuthService _googleAuthService;
    private readonly IEmailService _emailService;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IMemoryCache? _memoryCache;

    private void InvalidateUserStatus(Guid userId) => _memoryCache?.Remove(UserStatusService.CacheKey(userId));

    private static class LockoutSettings
    {
        //Số lần gửi email tối đa
        public const int MaxEmailRequestAttempts = 5;

        //Lượng thời gian khóa sau khi người dùng gửi vượt quá lượt email cho phép
        public const int EmailRequestLockoutMinutes = 10;

        //Số lượt xác minh cho phép
        public const int MaxEmailVerificationAttempts = 5;

        //Thời gian hiệu lực cho mã xác minh email
        public const int EmailVerificationCodeExpiryMinutes = 10;

        //Số lần tối đa cho phép reset password
        public const int MaxPasswordResetAttempts = 5;

        //Thời gian hiệu lực cho mã reset password
        public const int PasswordResetCodeExpiryMinutes = 10;

        //Lượng thời gian khóa sau khi người dùng lock email 
        public const int PasswordResetLockoutMinutes = 60;
    }

    public IdentityService(
        UserManager<UserAccount> userManager,
        IUserClaimsPrincipalFactory<UserAccount> userClaimsPrincipalFactory,
        IAuthorizationService authorizationService,
        SignInManager<UserAccount> signInManager,
        IOptions<JwtSettings> jwtSettings,
        ApplicationDbContext dbContext,
        IGoogleAuthService googleAuthService,
        IEmailService emailService,
        RoleManager<ApplicationRole> roleManager,
        IMemoryCache? memoryCache = null)
    {
        _memoryCache = memoryCache;
        _userManager = userManager;
        _userClaimsPrincipalFactory = userClaimsPrincipalFactory;
        _authorizationService = authorizationService;
        _signInManager = signInManager;
        _dbContext = dbContext;
        _jwtSettings = jwtSettings.Value;
        _googleAuthService = googleAuthService;
        _emailService = emailService;
        _roleManager = roleManager;
    }

    public async Task<string?> GetUserNameAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user?.UserName;
    }

    private async Task<UserAccount> CreateUserAccountAsync(string email, string? password = null, string? fullName = null, bool emailConfirmed = false)
    {
        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser != null)
        {
            if (existingUser.IsBanned)
                throw new ErrorCodeException(ErrorCodes.ACCOUNT_EMAIL_BANNED);
            throw new ErrorCodeException(ErrorCodes.IDENTITY_DUPLICATE_EMAIL);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = fullName ?? email
        };
        var userAccount = new UserAccount
        {
            Id = user.Id,
            UserName = Guid.NewGuid().ToString(),
            Email = email,
            User = user,
            EmailConfirmed = emailConfirmed
        };


        IdentityResult result = password != null
            ? await _userManager.CreateAsync(userAccount, password)
            : await _userManager.CreateAsync(userAccount);

        await _userManager.AddToRolesAsync(userAccount, new[]
        {
            Roles.User
        });

        if (!result.Succeeded)
            throw new ErrorCodeException(ErrorCodes.COMMON_SERVER_INTERNAL_ERROR);

        return userAccount;
    }

    public async Task<(Result Result, Guid UserId)> CreateUserAsync(string email, string password)
    {
        var userAccount = await CreateUserAccountAsync(email, password);
        return (Result.Success(), userAccount.Id);
    }

    public async Task<bool> IsInRoleAsync(Guid userId, string role)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user != null && await _userManager.IsInRoleAsync(user, role);
    }

    public async Task<bool> AuthorizeAsync(Guid userId, string policyName)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return false;

        var principal = await _userClaimsPrincipalFactory.CreateAsync(user);
        var result = await _authorizationService.AuthorizeAsync(principal, policyName);
        return result.Succeeded;
    }

    public async Task<Result> DeleteUserAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user != null ? await DeleteUserAsync(user) : Result.Success();
    }

    public async Task<Result> DeleteUserAsync(UserAccount userAccount)
    {
        userAccount.IsDeleted = true;
        var result = await _userManager.UpdateAsync(userAccount);

        return result.ToApplicationResult();
    }


    public async Task<TokenDto> TryLoginAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email);

        if (user == null)
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_INVALID_CREDENTIALS, $"User with email {email} not found");
        if (user.IsBanned)
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_BANNED, user.BanReason ?? "");
        if (user.EmailConfirmed == false)
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_EMAIL_NOT_VERIFIED);

        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        if (result.IsLockedOut)
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_LOCKED_OUT);
        if (!result.Succeeded)
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_INVALID_CREDENTIALS, "Incorrect password");

        return await GenerateJwtTokenAsync(user);
    }

    //TODO: Tên hàm đặt sai
    public async Task<List<Guid>> GetUsersInRoleAsync()
    {
        var admin = await _userManager.GetUsersInRoleAsync(Roles.Administrator);
        return admin.Select(u => u.Id).ToList();
    }

    public async Task<Guid> ChangeRoleAsync(Guid userId, string role, List<string>? protectedRoles = null)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_NOTFOUND, $"User with id {userId} not found");


        if (!await _roleManager.RoleExistsAsync(role))
            throw new ErrorCodeException(ErrorCodes.ROLE_NOTFOUND, $"Role '{role}' does not exist");

        var currentRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault();

        if (protectedRoles != null && protectedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            throw new ErrorCodeException(ErrorCodes.COMMON_FORBIDDEN, $"Cannot change to protected role '{role}'");

        if (currentRole != null && protectedRoles != null && protectedRoles.Contains(currentRole, StringComparer.OrdinalIgnoreCase))
            throw new ErrorCodeException(ErrorCodes.COMMON_FORBIDDEN, $"Cannot change from protected role '{currentRole}'");


        if (currentRole == role)
            return userId;

        if (currentRole != null)
            await _userManager.RemoveFromRoleAsync(user, currentRole);

        await _userManager.AddToRoleAsync(user, role);
        await _userManager.UpdateSecurityStampAsync(user);

        return userId;
    }

    private async Task<TokenDto> GenerateJwtTokenAsync(UserAccount userAccount)
    {
        // Đăng nhập thành công: bỏ cache trạng thái cũ để request kế tiếp đọc trạng thái mới nhất.
        InvalidateUserStatus(userAccount.Id);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userAccount.Id.ToString()),
        };

        Guard.Against.NullOrEmpty(_jwtSettings.SecretKey, "Secret key is null or empty");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);
        var refreshToken = await GenerateRefreshTokenAsync(userAccount);
        return new TokenDto
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = refreshToken.Token,
            ExpireMin = _jwtSettings.ExpiryMinutes
        };
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, Guid userId)
    {
        var storedRefreshToken = await _dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken && rt.UserAccountId == userId);

        if (storedRefreshToken != null || storedRefreshToken != null && storedRefreshToken.ExpireAt < DateTimeOffset.UtcNow)
        {
            _dbContext.Set<RefreshToken>().Remove(storedRefreshToken);
            await _dbContext.SaveChangesAsync();
        }
        else
        {
            throw new ErrorCodeException(ErrorCodes.REFRESHTOKEN_NOTFOUND);
        }
    }

    public async Task TrySetPasswordAsync(Guid userId, string password)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_NOTFOUND, $"User with id {userId} not found");

        // Nếu user đã có mật khẩu thì throw lỗi
        if (await _userManager.HasPasswordAsync(user))
            throw new ErrorCodeException(ErrorCodes.IDENTITY_USER_ALREADY_HAS_PASSWORD, "Người dùng đã có mật khẩu.");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, password);

        if (!result.Succeeded)
            throw new ErrorCodeException(ErrorCodes.COMMON_SERVER_INTERNAL_ERROR, "Failed to set password");
    }


    public async Task<TokenDto> RefreshTokenAsync(string accessToken, string refreshToken)
    {
        var storedRefreshToken = await _dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (storedRefreshToken == null || storedRefreshToken.ExpireAt < DateTime.UtcNow)
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_INVALID_CREDENTIALS, "Invalid or expired refresh token");

        var principal = GetPrincipalFromToken(accessToken, validateLifetime: false);
        var userIdClaim = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId) || userId != storedRefreshToken.UserAccountId)
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_INVALID_CREDENTIALS, "Invalid access token for refresh");

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null || user.IsDeleted || user.IsBanned
            // ||
            // await _userManager.IsLockedOutAsync(user)
           )
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_INVALID_CREDENTIALS, "User account is invalid or locked out or banned");

        _dbContext.Set<RefreshToken>().Remove(storedRefreshToken);
        await _dbContext.SaveChangesAsync();

        return await GenerateJwtTokenAsync(user);
    }

    private async Task<RefreshToken> GenerateRefreshTokenAsync(UserAccount userAccount)
    {
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid().ToString(),
            UserAccountId = userAccount.Id,
            Token = GenerateSecureToken(),
            ExpireAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays)
        };

        _dbContext.Set<RefreshToken>().Add(refreshToken);
        await _dbContext.SaveChangesAsync();

        return refreshToken;
    }


    private string GenerateSecureToken()
    {
        return Guid.NewGuid().ToString();
    }

    private ClaimsPrincipal? GetPrincipalFromToken(string token, bool validateLifetime = true)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        Guard.Against.NullOrEmpty(_jwtSettings.SecretKey, "Secret key is null or empty");
        var key = Encoding.UTF8.GetBytes(_jwtSettings.SecretKey);

        try
        {
            var principal = tokenHandler.ValidateToken(token,
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = validateLifetime,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = _jwtSettings.Issuer,
                    ValidAudience = _jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(key)
                }, out var validatedToken);

            return principal;
        }
        catch
        {
            return null;
        }
    }

    public async Task<TokenDto> TryGoogleLoginAsync(string authorizationCode, string redirectUri)
    {
        GoogleUserDto? googleUser;
        try
        {
            googleUser = await _googleAuthService.ExchangeCodeForUserInfoAsync(authorizationCode, redirectUri);
            if (googleUser == null) throw new Exception();
        }
        catch (Exception)
        {
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_INVALID_CREDENTIALS, "Invalid authorization code");
        }

        var existingUser = await _userManager.FindByEmailAsync(googleUser.Email);

        if (existingUser != null)
        {
            if (existingUser.IsDeleted)
                throw new ErrorCodeException(ErrorCodes.ACCOUNT_INVALID_CREDENTIALS, $"User with email {googleUser.Email} not found");

            if (existingUser.IsBanned)
                throw new ErrorCodeException(ErrorCodes.ACCOUNT_BANNED, existingUser.BanReason ?? "");

            if (!existingUser.EmailConfirmed)
            {
                existingUser.EmailConfirmed = true;
                await _userManager.UpdateAsync(existingUser);
            }

            return await GenerateJwtTokenAsync(existingUser);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = googleUser.Email,
            FullName = googleUser.Name,
        };

        var userAccount = new UserAccount
        {
            Id = user.Id,
            UserName = Guid.NewGuid().ToString(),
            Email = googleUser.Email,
            User = user,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(userAccount);

        await _userManager.AddToRolesAsync(userAccount, new[]
        {
            Roles.User
        });


        if (!result.Succeeded)
            throw new ErrorCodeException(ErrorCodes.COMMON_SERVER_INTERNAL_ERROR, $"Error at change password");

        var tokenDto = await GenerateJwtTokenAsync(userAccount);
        tokenDto.HasPassword = false;

        return tokenDto;
    }


    public async Task RequestEmailVerificationAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null) throw new ErrorCodeException(ErrorCodes.ACCOUNT_NOTFOUND, $"User with email {email} not found");

        // Lockout gửi email xác thực
        if (user.EmailVerificationRequestLockoutEnd.HasValue &&
            DateTimeOffset.UtcNow < user.EmailVerificationRequestLockoutEnd.Value)
        {
            throw new ErrorCodeException(ErrorCodes.EMAIL_VERIFICATION_REQUEST_TOO_MANY,
                $"Too much sent email request, please try after {LockoutSettings.EmailRequestLockoutMinutes} minutes.");
        }
        if (user.EmailVerificationRequestLockoutEnd.HasValue &&
            DateTimeOffset.UtcNow >= user.EmailVerificationRequestLockoutEnd.Value)
        {
            user.EmailVerificationRequestAttempts = 0;
            user.EmailVerificationRequestLockoutEnd = null;
        }

        // Tạo và lưu mã xác thực
        user.EmailVerificationCode = GenerateRandomCode();
        user.EmailVerificationCodeExpiryTime = DateTimeOffset.UtcNow.AddMinutes(LockoutSettings.EmailVerificationCodeExpiryMinutes);
        user.EmailVerificationRequestAttempts++;
        if (user.EmailVerificationRequestAttempts >= LockoutSettings.MaxEmailRequestAttempts)
        {
            user.EmailVerificationRequestLockoutEnd = DateTimeOffset.UtcNow.AddMinutes(LockoutSettings.EmailRequestLockoutMinutes);
        }
        await _userManager.UpdateAsync(user);

        var subject = "Xác thực email";
        var body =
            $@"<html><body><h2>Xác thực email</h2><p>Mã xác thực của bạn: <strong>{user.EmailVerificationCode}</strong></p><p>Mã xác thực sẽ hết hạn sau {LockoutSettings.EmailVerificationCodeExpiryMinutes} phút.</p><p>Nếu bạn không gửi yêu cầu xác thực, vui lòng bỏ qua email này.</p><br/><p>Trân trọng,<br/>AIQuizzizz</p></body></html>";
        await _emailService.SendEmailAsync(email, subject, body);
    }

    public async Task VerifyEmailAsync(EmailVerificationConfirmDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null) throw new ErrorCodeException(ErrorCodes.ACCOUNT_NOTFOUND, $"User with email {dto.Email} not found");

        // Check lock
        if (user.EmailVerificationLockoutEnd.HasValue &&
            DateTimeOffset.UtcNow < user.EmailVerificationLockoutEnd.Value)
        {
            throw new ErrorCodeException(ErrorCodes.EMAIL_VERIFICATION_CODE_FAILED_TOO_MANY,
                $"Failed too many. Try again after {LockoutSettings.EmailVerificationCodeExpiryMinutes} minutes.");
        }
        if (user.EmailVerificationLockoutEnd.HasValue &&
            DateTimeOffset.UtcNow >= user.EmailVerificationLockoutEnd.Value)
        {
            user.FailedEmailVerificationAttempts = 0;
        }
        // Kiểm tra mã và thời gian hết hạn
        if (user.EmailVerificationCode != dto.VerificationCode ||
            !user.EmailVerificationCodeExpiryTime.HasValue ||
            DateTimeOffset.UtcNow > user.EmailVerificationCodeExpiryTime.Value)
        {
            user.FailedEmailVerificationAttempts++;
            if (user.FailedEmailVerificationAttempts >= LockoutSettings.MaxEmailVerificationAttempts)
            {
                user.EmailVerificationLockoutEnd = DateTimeOffset.UtcNow.AddMinutes(LockoutSettings.EmailVerificationCodeExpiryMinutes);
            }
            await _userManager.UpdateAsync(user);
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_INVALID_VERIFICATION_CODE, "Invalid verification code");
        }
        // Xác minh thành công
        user.EmailVerificationCode = null;
        user.EmailVerificationCodeExpiryTime = null;
        user.FailedEmailVerificationAttempts = 0;
        user.EmailVerificationRequestAttempts = 0;
        user.EmailVerificationRequestLockoutEnd = null;
        user.EmailVerificationLockoutEnd = null;
        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);
    }

    public async Task RequestPasswordResetAsync(ForgotPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        // Không tiết lộ email có tồn tại hay không: trả về thành công im lặng (chỉ gửi mail khi tài khoản tồn tại)
        if (user == null || user.IsDeleted) return;

        // Lockout gửi email reset password
        if (user.PasswordResetRequestLockoutEnd.HasValue &&
            DateTimeOffset.UtcNow < user.PasswordResetRequestLockoutEnd.Value)
        {
            throw new ErrorCodeException(ErrorCodes.PASSWORD_RESET_REQUEST_TOO_MANY,
                $"Too many request. Please try again after {LockoutSettings.EmailRequestLockoutMinutes} minutes.");
        }
        if (user.PasswordResetRequestLockoutEnd.HasValue &&
            DateTimeOffset.UtcNow >= user.PasswordResetRequestLockoutEnd.Value)
        {
            user.PasswordResetRequestAttempts = 0;
            user.PasswordResetRequestLockoutEnd = null;
        }

        // Tạo mã reset ngẫu nhiên
        var resetCode = GenerateRandomCode();
        user.PasswordResetCode = resetCode;
        user.PasswordResetCodeExpiryTime = DateTimeOffset.UtcNow.AddMinutes(LockoutSettings.PasswordResetCodeExpiryMinutes);
        user.PasswordResetRequestAttempts++;
        if (user.PasswordResetRequestAttempts >= LockoutSettings.MaxEmailRequestAttempts)
        {
            user.PasswordResetRequestLockoutEnd = DateTimeOffset.UtcNow.AddMinutes(LockoutSettings.EmailRequestLockoutMinutes);
        }
        await _userManager.UpdateAsync(user);

        // Gửi email reset mật khẩu
        var subject = "Đặt lại mật khẩu";
        var body =
            $@"<html><body><h2>Đặt lại mật khẩu</h2><p>Mã đặt lại mật khẩu của bạn: <strong>{resetCode}</strong></p><p>Mã này sẽ hết hạn sau {LockoutSettings.PasswordResetCodeExpiryMinutes} phút.</p><p>Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này.</p><br/><p>Trân trọng,<br/>AIQuizzizz</p></body></html>";
        await _emailService.SendEmailAsync(dto.Email, subject, body);
    }

    public async Task ResetPasswordAsync(ResetPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        // Không tiết lộ email có tồn tại hay không
        if (user == null) throw new ErrorCodeException(ErrorCodes.ACCOUNT_INVALID_RESET_CODE, "Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");

        // Lockout
        if (user.PasswordResetLockoutEnd.HasValue &&
            DateTimeOffset.UtcNow < user.PasswordResetLockoutEnd.Value)
        {
            throw new ErrorCodeException(ErrorCodes.PASSWORD_RESET_CODE_FAILED_TOO_MANY,
                $"Failed too many. Try again after {LockoutSettings.PasswordResetLockoutMinutes} minutes.");
        }
        if (user.PasswordResetLockoutEnd.HasValue &&
            DateTimeOffset.UtcNow >= user.PasswordResetLockoutEnd.Value)
        {
            user.FailedPasswordResetAttempts = 0;
        }
        // Kiểm tra mã reset và thời gian hết hạn
        if (user.PasswordResetCode != dto.ResetCode ||
            !user.PasswordResetCodeExpiryTime.HasValue ||
            DateTimeOffset.UtcNow > user.PasswordResetCodeExpiryTime.Value)
        {
            user.FailedPasswordResetAttempts++;
            if (user.FailedPasswordResetAttempts >= LockoutSettings.MaxPasswordResetAttempts)
            {
                user.PasswordResetLockoutEnd = DateTimeOffset.UtcNow.AddMinutes(LockoutSettings.PasswordResetLockoutMinutes);
            }
            await _userManager.UpdateAsync(user);
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_INVALID_RESET_CODE, "Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
        }
        // Đặt lại mật khẩu
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);
        if (!result.Succeeded)
            throw new ErrorCodeException(ErrorCodes.COMMON_SERVER_INTERNAL_ERROR, "Đổi mật khẩu thất bại");
        user.PasswordResetCode = null;
        user.PasswordResetCodeExpiryTime = null;
        user.FailedPasswordResetAttempts = 0;
        user.PasswordResetLockoutEnd = null;
        await _userManager.UpdateAsync(user);
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_NOTFOUND, $"User with id {userId} not found");

        if (currentPassword == newPassword)
        {
            if (!await _userManager.CheckPasswordAsync(user, currentPassword))
                throw new ErrorCodeException(ErrorCodes.ACCOUNT_WRONG_PASSWORD);
            throw new ErrorCodeException(ErrorCodes.ACCOUNT_NEW_PASSWORD_SAME_AS_CURRENT);
        }

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == ErrorCodes.IDENTITY_PASSWORD_MISMATCH || e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
                throw new ErrorCodeException(ErrorCodes.ACCOUNT_WRONG_PASSWORD);
            throw new ErrorCodeException(ErrorCodes.COMMON_SERVER_INTERNAL_ERROR, $"Error at change password");
        }
    }

    public async Task BanUser(Guid userId, bool isBanned, string? reason = null)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) throw new ErrorCodeException(ErrorCodes.ACCOUNT_NOTFOUND, $"User with id  {userId} not found");

        user.IsBanned = isBanned;
        user.BanReason = isBanned ? reason : null;

        await _userManager.UpdateAsync(user);
        InvalidateUserStatus(userId);

        if (isBanned)
        {
            // Thu hồi toàn bộ refresh token của user bị khoá
            var tokens = await _dbContext.Set<RefreshToken>().Where(rt => rt.UserAccountId == userId).ToListAsync();
            if (tokens.Count > 0)
            {
                _dbContext.Set<RefreshToken>().RemoveRange(tokens);
                await _dbContext.SaveChangesAsync();
            }
        }
    }

    //TODO: Thêm navigation để truy vấn ngắn hơn
    public async Task<bool> IsInAnyRoleAsync(Guid userId, params string[] roles)
    {
        var isInRole = await _dbContext.UserRoles
            .Join(_dbContext.Roles,
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new
                {
                    ur.UserId,
                    RoleName = r.Name
                })
            .Where(x => x.UserId == userId && roles.Contains(x.RoleName))
            .AnyAsync();
        return isInRole;
    }
    public async Task<IList<string>> GetUserRolesAsync(Guid userId)
    {
        var identityUser = await _userManager.FindByIdAsync(userId.ToString());
        if (identityUser == null)
            return new List<string>();

        return await _userManager.GetRolesAsync(identityUser);
    }
    
    // SELECT a."Id", a."Email", d."FullName", d."IsBanned", d."TokenCount" AS "Token", COALESCE(a1."Name", 'User') AS "Role", d."Balance"
    // FROM "AspNetUsers" AS a
    // LEFT JOIN "DomainUsers" AS d ON a."Id" = d."Id"
    // LEFT JOIN "AspNetUserRoles" AS a0 ON a."Id" = a0."UserId"
    // LEFT JOIN "AspNetRoles" AS a1 ON a0."RoleId" = a1."Id"
    // WHERE NOT (a."IsDeleted") AND (d."Id" IS NULL OR d."IsDeleted" = FALSE)
    // ORDER BY a1."Name"
    // LIMIT @__p_1 OFFSET @__p_0

    
    public async Task<PaginatedList<UserForListDto>> SearchUserWithRole(SearchUserDto dto)
    {
        var query = from user in _dbContext.Users.IgnoreQueryFilters()
            join domainUser in _dbContext.DomainUsers.IgnoreQueryFilters() on user.Id equals domainUser.Id into domainGroup
            from du in domainGroup.DefaultIfEmpty()
            
            join userRole in _dbContext.UserRoles on user.Id equals userRole.UserId into userRoleGroup
            from ur in userRoleGroup.DefaultIfEmpty()
            
            join role in _dbContext.Roles on ur.RoleId equals role.Id into roleGroup
            from r in roleGroup.DefaultIfEmpty()
            where user.IsDeleted == false 
            select new
            {
                user,
                domain = du,
                RoleName = r.Name
            } ;
            
        query = query.OrderBy(x => x.RoleName);

        // Lọc theo từ khóa
        if (!string.IsNullOrEmpty(dto.Keyword) && !string.IsNullOrEmpty(dto.FieldName))
        {
            var property = typeof(User).GetProperty(dto.FieldName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.PropertyType != typeof(string))
                throw new ErrorCodeException(ErrorCodes.FIELD_NAME_NOT_FOUND, "Trường tìm kiếm không hợp lệ hoặc không phải kiểu string");

            string stm = $"domain.{dto.FieldName}.ToLower().Contains(@0)";
            query = query.Where(stm, dto.Keyword.ToLower());
        }

        if (dto.IsBanned.HasValue)
        {
            query = query.Where(x => x.domain.IsBanned == dto.IsBanned.Value);
        }

        if (!string.IsNullOrWhiteSpace(dto.Role))
        {
            query = query.Where(x => x.RoleName == dto.Role);
        }

        var projected = query.Select(x => new UserForListDto
        {
            Id = x.user.Id,
            Email = x.user.Email!,
            FullName = x.domain.FullName,
            AvatarId = x.domain.AvatarMediaId,
            IsBanned = x.domain.IsBanned,
            Token = x.domain.TokenCount,
            Role = x.RoleName ?? "User",
            Balance = x.domain.Balance
        });
        return await PaginatedList<UserForListDto>.CreateAsync(projected.AsNoTracking(), dto.PageNumber, dto.PageSize);
    }


    private string GenerateRandomCode()
    {
        var random = new Random();
        return random.Next(100000, 999999).ToString();
    }
}
