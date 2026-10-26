using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Clients.Queries.GetById;

public sealed class GetClientByIdQueryHandler : IRequestHandler<GetClientByIdQuery, Result<ClientDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Client> _clients;
    private readonly IClinicVeterinarianReader _userNames;

    public GetClientByIdQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Client> clients,
        IClinicVeterinarianReader userNames)
    {
        _tenantContext = tenantContext;
        _clients = clients;
        _userNames = userNames;
    }

    public async ValueTask<Result<ClientDetailDto>> Handle(GetClientByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClientDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.Id), ct);
        if (client is null)
            return Result<ClientDetailDto>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        string? kvkkRecordedByName = null;
        if (client.KvkkConsentRecordedByUserId is { } recordedBy)
            (await _userNames.GetNamesAsync([recordedBy], ct)).TryGetValue(recordedBy, out kvkkRecordedByName);

        var dto = new ClientDetailDto(
            client.Id,
            client.TenantId,
            client.CreatedAtUtc,
            client.UpdatedAtUtc,
            client.FullName,
            client.Email,
            client.Phone,
            client.Address,
            client.PreferredContactChannel,
            client.RemindersOptedOut,
            client.KvkkConsentStatus,
            client.KvkkConsentRecordedOn,
            client.KvkkConsentRecordedByUserId,
            kvkkRecordedByName,
            client.KvkkConsentTemplateVersion,
            client.KvkkConsentTemplateHash,
            ExaminationRowVersion.Encode(client.RowVersion),
            ClientInvoicingSummaryDto.From(client));
        return Result<ClientDetailDto>.Success(dto);
    }
}
