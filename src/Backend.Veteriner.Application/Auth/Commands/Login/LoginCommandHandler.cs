using System.Security.Claims;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using MediatR;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Auth.Commands.Login;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResultDto>>
{
    private readonly IUserReadRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly ITokenHashService _tokenHash;
    private readonly IRefreshTokenRepository _refreshRepo;
    private readonly IClientContext _client;
    private readonly IJwtOptionsProvider _jwtOpt;
    private readonly IOperationClaimPermissionRepository _ocpRepo;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IUserTenantRepository _userTenants;
    private readonly SessionOptions _sessionOpt;

    public LoginCommandHandler(
        IUserReadRepository users,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        ITokenHashService tokenHash,
        IRefreshTokenRepository refreshRepo,
        IClientContext client,
        IJwtOptionsProvider jwtOpt,
        IOperationClaimPermissionRepository ocpRepo,
        IReadRepository<Tenant> tenants,
        IUserTenantRepository userTenants,
        IOptions<SessionOptions> sessionOpt)
    {
        _users = users;
        _hasher = hasher;
        _jwt = jwt;
        _tokenHash = tokenHash;
        _refreshRepo = refreshRepo;
        _client = client;
        _jwtOpt = jwtOpt;
        _ocpRepo = ocpRepo;
        _tenants = tenants;
        _userTenants = userTenants;
        _sessionOpt = sessionOpt.Value;
    }

    public async Task<Result<LoginResultDto>> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await _users.FirstOrDefaultAsync(new UserByEmailSpec(request.Email), ct);

        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.InvalidCredentials",
                "Kullanıcı veya şifre hatalı.");
        }

        if (_sessionOpt.SingleSessionPerUser)
            await _refreshRepo.RevokeAllByUserAsync(user.Id, ct);

        var permissionCodes = await _ocpRepo.GetPermissionCodesByUserIdAsync(user.Id, ct)
                              ?? Array.Empty<string>();
        var extraClaims = permissionCodes.Select(code => new Claim("permission", code)).ToList();

        if (IsPlatformAdmin(user))
            extraClaims.Add(new Claim(VeterinerClaims.PlatformAdmin, bool.TrueString, ClaimValueTypes.Boolean));

        var memberships = await _userTenants.GetTenantIdsByUserIdAsync(user.Id, ct);
        if (memberships.Count == 0)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.TenantMembershipRequired",
                "Bu kullanıcı için kiracı üyeliği tanımlı değil.");
        }

        var resolved = ResolveLoginTenant(request.TenantId, memberships);
        if (!resolved.IsSuccess)
            return Result<LoginResultDto>.Failure(resolved.Code!, resolved.Message!);

        var tenantId = resolved.TenantId!.Value;

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
        {
            return Result<LoginResultDto>.Failure(
                "Tenants.NotFound",
                "Kiracı bulunamadı.");
        }

        if (!tenant.IsActive)
        {
            return Result<LoginResultDto>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için oturum açılamaz.");
        }

        if (!await _userTenants.ExistsAsync(user.Id, tenantId, ct))
        {
            return Result<LoginResultDto>.Failure(
                "Auth.TenantNotMember",
                "Bu kiracıda üyeliğiniz yok.");
        }

        extraClaims.Add(new Claim(VeterinerClaims.TenantId, tenantId.ToString("D")));

        var (access, refreshRaw, accessExp) = _jwt.Create(user, extraClaims);

        var refreshHash = _tokenHash.ComputeSha256(refreshRaw);

        var rt = new Backend.Veteriner.Domain.Users.RefreshToken(
            user.Id,
            refreshHash,
            DateTime.UtcNow.AddDays(_jwtOpt.RefreshTokenDays),
            _client.IpAddress,
            _client.UserAgent,
            tenantId,
            clinicId: null);

        user.AddRefreshToken(rt);
        await _refreshRepo.AddAsync(rt, ct);
        await _refreshRepo.SaveChangesAsync(ct);

        var dto = new LoginResultDto(access, refreshRaw, accessExp);
        return Result<LoginResultDto>.Success(dto);
    }

    private static bool IsPlatformAdmin(User user)
        => user.Roles.Any(r => string.Equals(r.Name, "PlatformAdmin", StringComparison.OrdinalIgnoreCase));

    private static (bool IsSuccess, Guid? TenantId, string? Code, string? Message) ResolveLoginTenant(
        Guid? requested,
        IReadOnlyList<Guid> memberships)
    {
        if (memberships.Count == 1)
        {
            var only = memberships[0];
            if (requested is { } r && r != Guid.Empty && r != only)
            {
                return (false, null, "Auth.TenantMismatch",
                    "Tek kiracılı kullanıcı için farklı TenantId belirtilemez.");
            }

            return (true, only, null, null);
        }

        if (requested is null || requested == Guid.Empty)
        {
            return (false, null, "Auth.TenantRequired",
                "Birden fazla kiracıya üyesiniz; girişte TenantId zorunludur.");
        }

        var req = requested.Value;
        if (!memberships.Contains(req))
            return (false, null, "Auth.TenantNotMember", "Bu kiracıda üyeliğiniz yok.");

        return (true, req, null, null);
    }
}
