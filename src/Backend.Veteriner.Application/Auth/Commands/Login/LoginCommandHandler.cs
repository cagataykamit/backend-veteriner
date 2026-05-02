using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Auth.Contracts;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Threading;

namespace Backend.Veteriner.Application.Auth.Commands.Login;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResultDto>>
{
    /// <summary>Tanılama: ardışık login denemelerinde userLookup süresini karşılaştırmak için (1. genelde soğuk path).</summary>
    private static long _loginUserLookupAttemptCounter;

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
        var stepElapsedMs = new Dictionary<string, long>(StringComparer.Ordinal);

        void MarkStep(string name)
        {
            querySteps++;
            var elapsed = stepSw.ElapsedMilliseconds;
            stepElapsedMs[name] = elapsed;
            if (elapsed > slowestMs)
            {
                slowestMs = elapsed;
                slowestStep = name;
            }

            stepSw.Restart();
        }

        void LogStepBreakdown(string outcome)
        {
            if (!_logger.IsEnabled(LogLevel.Debug))
                return;

            var summary = string.Join(
                ", ",
                stepElapsedMs.Select(kv => $"{kv.Key}={kv.Value}ms"));
            _logger.LogDebug("Login perf ({Outcome}): {Steps}", outcome, summary);
        }

        var loginEmail = request.Email.Trim();
        MarkStep("normalizeEmail");

        var lookupAttempt = Interlocked.Increment(ref _loginUserLookupAttemptCounter);
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Login userLookup step: attempt #{Attempt} calling IUserReadRepository.GetForLoginByEmailAsync (stopwatch step starts after normalizeEmail)",
                lookupAttempt);
        }

        var loginUser = await _users.GetForLoginByEmailAsync(loginEmail, ct);
        MarkStep("userLookup");

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Login userLookup step: attempt #{Attempt} finished; MarkStep userLookup = {UserLookupMs}ms (compare attempt 1 vs 2 for cold pool / warmup)",
                lookupAttempt,
                stepElapsedMs.GetValueOrDefault("userLookup", -1));
        }

        if (loginUser is null)
        {
            LogStepBreakdown("invalid-credentials");
            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.InvalidCredentials",
                "Kullanıcı veya şifre hatalı.");
        }

        var passwordOk = _hasher.Verify(request.Password, loginUser.PasswordHash);
        MarkStep("passwordVerify");

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var wf = TryParseBcryptWorkFactor(loginUser.PasswordHash);
            if (wf.HasValue)
                _logger.LogDebug("Login BCrypt work factor (cost) from stored hash: {WorkFactor}", wf.Value);
        }

        if (!passwordOk)
        {
            LogStepBreakdown("invalid-credentials");
            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.InvalidCredentials",
                "Kullanıcı veya şifre hatalı.");
        }

        var roleNames = await _users.GetRoleNamesByUserIdAsync(loginUser.Id, ct);
        MarkStep("roleNamesLookup");

        if (_sessionOpt.SingleSessionPerUser)
        {
            await _refreshRepo.RevokeAllByUserAsync(loginUser.Id, ct);
            MarkStep("revokeAllSessions");
        }

        var permissionCodes = await _permissionReader.GetPermissionsAsync(loginUser.Id, principal: null, ct)
                              ?? Array.Empty<string>();
        MarkStep("permissionCodes");
        var extraClaims = new List<Claim>(permissionCodes.Count + 2);
        foreach (var code in permissionCodes)
            extraClaims.Add(new Claim("permission", code));
        MarkStep("permissionClaimsBuild");

        if (IsPlatformAdmin(roleNames))
            extraClaims.Add(new Claim(VeterinerClaims.PlatformAdmin, bool.TrueString, ClaimValueTypes.Boolean));

        var memberships = await _userTenants.GetTenantIdsByUserIdAsync(loginUser.Id, ct);
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

        if (!await _userTenants.ExistsAsync(loginUser.Id, tenantId, ct))
        {
            return Result<LoginResultDto>.Failure(
                "Auth.TenantNotMember",
                "Bu kiracıda üyeliğiniz yok.");
        }
        MarkStep("tenantMembershipExists");

        extraClaims.Add(new Claim(VeterinerClaims.TenantId, tenantId.ToString("D")));

        var (access, refreshRaw, accessExp) = _jwt.Create(
            loginUser.Id,
            loginUser.Email,
            roleNames,
            extraClaims);
        MarkStep("jwtCreate");

        var refreshHash = _tokenHash.ComputeSha256(refreshRaw);

        var rt = new Backend.Veteriner.Domain.Users.RefreshToken(
            loginUser.Id,
            refreshHash,
            DateTime.UtcNow.AddDays(_jwtOpt.RefreshTokenDays),
            _client.IpAddress,
            _client.UserAgent,
            tenantId,
            clinicId: null);

        await _refreshRepo.AddAsync(rt, ct);
        await _refreshRepo.SaveChangesAsync(ct);
        MarkStep("saveRefreshToken");

        _logger.LogInformation(
            "Login succeeded. UserId={UserId} TenantId={TenantId} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
            loginUser.Id,
            tenantId,
            querySteps,
            slowestStep,
            slowestMs,
            totalSw.ElapsedMilliseconds);

        LogStepBreakdown("success");

        var dto = new LoginResultDto(access, refreshRaw, accessExp, tenantId, 1);
        return Result<LoginResultDto>.Success(dto);
    }

    /// <summary>
    /// BCrypt hash formatı: $2a$10$... → segment index 2 maliyet (cost).
    /// </summary>
    private static int? TryParseBcryptWorkFactor(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return null;

        // Örn: $2a$10$... → Split('$') → "", "2a", "10", ...
        var parts = hash.Split('$');
        if (parts.Length < 4)
            return null;

        return int.TryParse(parts[2], out var cost) ? cost : null;
    }

    private static bool IsPlatformAdmin(IReadOnlyList<string> roleNames)
        => roleNames.Any(r => string.Equals(r, "PlatformAdmin", StringComparison.OrdinalIgnoreCase));
}
