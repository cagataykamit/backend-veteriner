using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetClientPortalAccess;

/// <summary>Klinik paneli: müşterinin portal bağları, son davetleri ve davet edilebilirliği (docs/PORTAL_API_CONTRACT.md §4.1).</summary>
public sealed record GetClientPortalAccessQuery(Guid ClientId) : IRequest<Result<ClientPortalAccessDto>>;

public sealed class GetClientPortalAccessQueryHandler : IRequestHandler<GetClientPortalAccessQuery, Result<ClientPortalAccessDto>>
{
    /// <summary>Müşteri kartında gösterilen en yeni davet sayısı.</summary>
    public const int RecentInviteCount = 20;

    private readonly OwnerPortalStaffAccess _staffAccess;
    private readonly IReadRepository<OwnerAccountClientLink> _links;
    private readonly IReadRepository<OwnerPortalInvite> _invites;
    private readonly IReadRepository<OwnerAccount> _accounts;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly TimeProvider _timeProvider;

    public GetClientPortalAccessQueryHandler(
        OwnerPortalStaffAccess staffAccess,
        IReadRepository<OwnerAccountClientLink> links,
        IReadRepository<OwnerPortalInvite> invites,
        IReadRepository<OwnerAccount> accounts,
        IReadRepository<Clinic> clinics,
        TimeProvider timeProvider)
    {
        _staffAccess = staffAccess;
        _links = links;
        _invites = invites;
        _accounts = accounts;
        _clinics = clinics;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClientPortalAccessDto>> Handle(GetClientPortalAccessQuery request, CancellationToken ct)
    {
        var scopeResult = await _staffAccess.ResolveClientAsync(request.ClientId, ct);
        if (!scopeResult.IsSuccess)
            return Result<ClientPortalAccessDto>.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        var links = await _links.ListAsync(new OwnerAccountClientLinksByClientSpec(scope.TenantId, scope.Client.Id), ct);
        var invites = await _invites.ListAsync(
            new RecentOwnerPortalInvitesByClientSpec(scope.TenantId, scope.Client.Id, RecentInviteCount), ct);

        var clinicIds = links.Select(l => l.ClinicId).Distinct().ToArray();
        var clinicNames = clinicIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _clinics.ListAsync(new ClinicsByTenantIdsSpec(scope.TenantId, clinicIds), ct)).ToDictionary(c => c.Id, c => c.Name);

        var accountIds = links.Select(l => l.OwnerAccountId).Distinct().ToArray();
        var accountEmails = accountIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : (await _accounts.ListAsync(new OwnerAccountsByIdsSpec(accountIds), ct)).ToDictionary(a => a.Id, a => a.Email);

        var eligibility = OwnerPortalInvitePolicy.CanInvite(scope.Client);
        var blockedCode = !eligibility.IsSuccess
            ? eligibility.Error.Code
            : links.Any(l => l.IsActive) ? "OwnerPortal.AlreadyLinked" : null;

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        return Result<ClientPortalAccessDto>.Success(new ClientPortalAccessDto(
            scope.Client.Id,
            blockedCode is null,
            blockedCode,
            links.Select(l => new ClientPortalLinkDto(
                    l.Id,
                    l.ClinicId,
                    clinicNames.GetValueOrDefault(l.ClinicId),
                    l.Status,
                    accountEmails.GetValueOrDefault(l.OwnerAccountId),
                    l.CreatedAtUtc,
                    l.ClosedAtUtc,
                    l.CloseReason))
                .ToArray(),
            invites.Select(i => ClientPortalInviteDto.From(i, utcNow)).ToArray()));
    }
}
