using System.Security.Claims;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
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
        _sessionOpt = sessionOpt.Value;
    }

    public async Task<Result<LoginResultDto>> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await _users.FirstOrDefaultAsync(new UserByEmailSpec(request.Email), ct)
                   ?? null;

        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.InvalidCredentials",
                "Kullanıcı veya şifre hatalı.");
        }

        // ✅ Opsiyonel politika: kullanıcı başına tek aktif refresh token
        if (_sessionOpt.SingleSessionPerUser)
            await _refreshRepo.RevokeAllByUserAsync(user.Id, ct);

        var permissionCodes = await _ocpRepo.GetPermissionCodesByUserIdAsync(user.Id, ct)
                              ?? Array.Empty<string>();
        var extraClaims = permissionCodes.Select(code => new Claim("permission", code)).ToList();

        if (request.TenantId is { } loginTenantId)
        {
            if (loginTenantId == Guid.Empty)
            {
                return Result<LoginResultDto>.Failure(
                    "Validation.TenantId",
                    "TenantId geçersiz.");
            }

            var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(loginTenantId), ct);
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

            extraClaims.Add(new Claim(VeterinerClaims.TenantId, loginTenantId.ToString("D")));
        }

        var (access, refreshRaw, accessExp) = _jwt.Create(user, extraClaims);

        var refreshHash = _tokenHash.ComputeSha256(refreshRaw);

        var rt = new Backend.Veteriner.Domain.Users.RefreshToken(
            user.Id,
            refreshHash,
            DateTime.UtcNow.AddDays(_jwtOpt.RefreshTokenDays),
            _client.IpAddress,
            _client.UserAgent
        );

        user.AddRefreshToken(rt);
        await _refreshRepo.AddAsync(rt, ct);
        await _refreshRepo.SaveChangesAsync(ct);

        var dto = new LoginResultDto(access, refreshRaw, accessExp);
        return Result<LoginResultDto>.Success(dto);
    }
}
