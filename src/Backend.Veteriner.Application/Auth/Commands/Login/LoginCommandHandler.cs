using System.Security.Claims;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Auth.Contracts;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;

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
    private readonly IPermissionReader _permissionReader;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IUserTenantRepository _userTenants;
    private readonly SessionOptions _sessionOpt;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IUserReadRepository users,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        ITokenHashService tokenHash,
        IRefreshTokenRepository refreshRepo,
        IClientContext client,
        IJwtOptionsProvider jwtOpt,
        IPermissionReader permissionReader,
        IReadRepository<Tenant> tenants,
        IUserTenantRepository userTenants,
        IOptions<SessionOptions> sessionOpt,
        ILogger<LoginCommandHandler>? logger = null)
    {
        _users = users;
        _hasher = hasher;
        _jwt = jwt;
        _tokenHash = tokenHash;
        _refreshRepo = refreshRepo;
        _client = client;
        _jwtOpt = jwtOpt;
        _permissionReader = permissionReader;
        _tenants = tenants;
        _userTenants = userTenants;
        _sessionOpt = sessionOpt.Value;
        _logger = logger ?? NullLogger<LoginCommandHandler>.Instance;
    }

    public async Task<Result<LoginResultDto>> Handle(LoginCommand request, CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();
        var querySteps = 0;
        var slowestStep = string.Empty;
        long slowestMs = 0;

        void MarkStep(string name)
        {
            querySteps++;
            var elapsed = stepSw.ElapsedMilliseconds;
            if (elapsed > slowestMs)
            {
                slowestMs = elapsed;
                slowestStep = name;
            }

            stepSw.Restart();
        }

        var user = await _users.FirstOrDefaultAsync(new UserByEmailSpec(request.Email), ct);
        MarkStep("userByEmail");

        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.InvalidCredentials",
                "Kullanıcı veya şifre hatalı.");
        }

        if (_sessionOpt.SingleSessionPerUser)
        {
            await _refreshRepo.RevokeAllByUserAsync(user.Id, ct);
            MarkStep("revokeAllSessions");
        }

        var permissionCodes = await _permissionReader.GetPermissionsAsync(user.Id, principal: null, ct)
                              ?? Array.Empty<string>();
        MarkStep("permissionCodes");
        var extraClaims = permissionCodes.Select(code => new Claim("permission", code)).ToList();

        if (IsPlatformAdmin(user))
            extraClaims.Add(new Claim(VeterinerClaims.PlatformAdmin, bool.TrueString, ClaimValueTypes.Boolean));

        var memberships = await _userTenants.GetTenantIdsByUserIdAsync(user.Id, ct);
        MarkStep("tenantMemberships");
        var distinctTenantIds = memberships.Distinct().ToList();
        if (distinctTenantIds.Count == 0)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.TenantMembershipRequired",
                "Bu kullanıcı için kiracı üyeliği tanımlı değil.");
        }

        if (distinctTenantIds.Count > 1)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.UserMultipleTenantsForbidden",
                "Bu kullanıcı birden fazla kiracıya bağlı; yönetici tek kiracıya indirgemelidir.");
        }

        var onlyTenantId = distinctTenantIds[0];
        if (request.TenantId is { } provided && provided != Guid.Empty && provided != onlyTenantId)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.TenantMismatch",
                "Tek kiracılı kullanıcı için farklı TenantId belirtilemez.");
        }

        var tenantId = onlyTenantId;

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        MarkStep("tenantLookup");
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
        MarkStep("tenantMembershipExists");

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

        _logger.LogInformation(
            "Login succeeded. UserId={UserId} TenantId={TenantId} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
            user.Id,
            tenantId,
            querySteps,
            slowestStep,
            slowestMs,
            totalSw.ElapsedMilliseconds);

        var dto = new LoginResultDto(access, refreshRaw, accessExp, tenantId, 1);
        return Result<LoginResultDto>.Success(dto);
    }

    private static bool IsPlatformAdmin(User user)
        => user.Roles.Any(r => string.Equals(r.Name, "PlatformAdmin", StringComparison.OrdinalIgnoreCase));
}
