using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalReminderPreferences;

/// <summary>Bağın müşteri kaydındaki hatırlatma tercihi (docs/PORTAL_API_CONTRACT.md §5.11).</summary>
public sealed record GetPortalReminderPreferencesQuery(Guid OwnerAccountId, Guid LinkId)
    : IRequest<Result<PortalReminderPreferencesDto>>, IAuditableRequest
{
    public string AuditAction => "Portal.Read.ReminderPreferences";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

public sealed class GetPortalReminderPreferencesQueryHandler
    : IRequestHandler<GetPortalReminderPreferencesQuery, Result<PortalReminderPreferencesDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<Client> _clients;

    public GetPortalReminderPreferencesQueryHandler(OwnerPortalAccess access, IReadRepository<Client> clients)
    {
        _access = access;
        _clients = clients;
    }

    public async ValueTask<Result<PortalReminderPreferencesDto>> Handle(GetPortalReminderPreferencesQuery request, CancellationToken ct)
    {
        var scope = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scope.IsSuccess)
            return Result<PortalReminderPreferencesDto>.Failure(scope.Error);

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(scope.Value!.TenantId, scope.Value.ClientId), ct);
        return client is null
            ? Result<PortalReminderPreferencesDto>.Failure("Portal.Links.NotFound", "Kayıt bulunamadı.")
            : Result<PortalReminderPreferencesDto>.Success(new PortalReminderPreferencesDto(client.RemindersOptedOut, client.OwnerCanEnableReminders));
    }
}
