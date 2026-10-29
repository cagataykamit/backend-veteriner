using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.UpdatePortalReminderPreferences;

/// <summary>
/// Hasta sahibi bu bağdaki hatırlatmaları kapatır/açar (APPT-014, docs/PORTAL_API_CONTRACT.md §5.11). Kural
/// <see cref="Client.SetRemindersOptedOutByOwner"/> içindedir. Kiracı aboneliği salt okunur olsa da sahip tercihini değiştirebilir
/// (hatırlatmayı durdurmak sahibin hakkıdır).
/// </summary>
/// <param name="RemindersOptedOut">Zorunlu; <c>null</c> → 400.</param>
public sealed record UpdatePortalReminderPreferencesCommand(Guid OwnerAccountId, Guid LinkId, bool? RemindersOptedOut)
    : IRequest<Result<PortalReminderPreferencesDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.ReminderPreferences.Update";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

public sealed class UpdatePortalReminderPreferencesCommandHandler
    : IRequestHandler<UpdatePortalReminderPreferencesCommand, Result<PortalReminderPreferencesDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IRepository<Client> _clients;
    private readonly TimeProvider _timeProvider;

    public UpdatePortalReminderPreferencesCommandHandler(OwnerPortalAccess access, IRepository<Client> clients, TimeProvider timeProvider)
    {
        _access = access;
        _clients = clients;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PortalReminderPreferencesDto>> Handle(UpdatePortalReminderPreferencesCommand request, CancellationToken ct)
    {
        var scope = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scope.IsSuccess)
            return Result<PortalReminderPreferencesDto>.Failure(scope.Error);

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(scope.Value!.TenantId, scope.Value.ClientId), ct);
        if (client is null)
            return Result<PortalReminderPreferencesDto>.Failure("Portal.Links.NotFound", "Kayıt bulunamadı.");

        var applied = client.SetRemindersOptedOutByOwner(request.RemindersOptedOut!.Value, _timeProvider.GetUtcNow().UtcDateTime);
        if (!applied.IsSuccess)
            return Result<PortalReminderPreferencesDto>.Failure(applied.Error);

        try
        {
            await _clients.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Okuma ile yazma arasında klinik müşteri kaydını değiştirdi (Clients.RowVersion).
            return Result<PortalReminderPreferencesDto>.Failure(
                "Clients.ConcurrencyConflict",
                "Kayıt bu sırada klinik tarafından güncellendi; sayfayı yenileyip tekrar deneyin.");
        }

        return Result<PortalReminderPreferencesDto>.Success(new PortalReminderPreferencesDto(client.RemindersOptedOut, client.OwnerCanEnableReminders));
    }
}
