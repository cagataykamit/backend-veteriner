using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Invites;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Mediator;

namespace Backend.Veteriner.Application.Public.Commands.AcceptInvite;

public sealed class AcceptTenantInviteCommandHandler
    : IRequestHandler<AcceptTenantInviteCommand, Result<TenantInviteAcceptResultDto>>
{
    private readonly ITokenHashService _tokenHash;
    private readonly IRepository<TenantInvite> _invites;
    private readonly ITenantScope _tenantScope;
    private readonly IUserReadRepository _usersRead;
    private readonly TenantInviteAcceptanceService _acceptance;
    private readonly TimeProvider _timeProvider;

    public AcceptTenantInviteCommandHandler(
        ITokenHashService tokenHash,
        IRepository<TenantInvite> invites,
        ITenantScope tenantScope,
        IUserReadRepository usersRead,
        TenantInviteAcceptanceService acceptance,
        TimeProvider timeProvider)
    {
        _tokenHash = tokenHash;
        _invites = invites;
        _tenantScope = tenantScope;
        _usersRead = usersRead;
        _acceptance = acceptance;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<TenantInviteAcceptResultDto>> Handle(AcceptTenantInviteCommand request, CancellationToken ct)
    {
        string hash;
        try
        {
            hash = _tokenHash.ComputeSha256(request.RawToken.Trim());
        }
        catch
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.TokenInvalid",
                "Davet token geçersiz.");
        }

        // Davet bağlantısı anonimdir: token araması kiracılar arasıdır, sonrası davetin kiracısının kapsamında (ADR-040).
        TenantInvite? invite;
        using (_tenantScope.AsSystem())
            invite = await _invites.FirstOrDefaultAsync(new TenantInviteByTokenHashSpec(hash), ct);
        if (invite is null)
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.NotFound",
                "Davet bulunamadı.");
        }

        using var tenantScope = _tenantScope.UseTenant(invite.TenantId);

        if (invite.Status != TenantInviteStatus.Pending)
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.NotPending",
                "Bu davet artık geçerli değil.");
        }

        if (_timeProvider.GetUtcNow().UtcDateTime >= invite.ExpiresAtUtc)
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.Expired",
                "Davet süresi dolmuş.");
        }

        var user = await _usersRead.FirstOrDefaultAsync(
            new UserByIdWithRolesSpec(request.CurrentUserId), ct);
        if (user is null)
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Users.NotFound",
                "Kullanıcı bulunamadı.");
        }

        return await _acceptance.AcceptAsync(invite, user, ct);
    }
}
