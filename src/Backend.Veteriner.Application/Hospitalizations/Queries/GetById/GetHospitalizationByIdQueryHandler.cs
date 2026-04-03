using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Queries.GetById;

public sealed class GetHospitalizationByIdQueryHandler
    : IRequestHandler<GetHospitalizationByIdQuery, Result<HospitalizationDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetHospitalizationByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _hospitalizations = hospitalizations;
        _pets = pets;
        _clients = clients;
    }

    public async Task<Result<HospitalizationDetailDto>> Handle(GetHospitalizationByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<HospitalizationDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var row = await _hospitalizations.FirstOrDefaultAsync(
            new HospitalizationByIdSpec(tenantId, request.Id), ct);
        if (row is null)
            return Result<HospitalizationDetailDto>.Failure("Hospitalizations.NotFound", "Yatış kaydı bulunamadı.");
        if (_clinicContext.ClinicId is { } clinicId && row.ClinicId != clinicId)
            return Result<HospitalizationDetailDto>.Failure("Hospitalizations.NotFound", "Yatış kaydı bulunamadı.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, row.PetId), ct);
        var petName = pet?.Name ?? string.Empty;
        var clientId = pet?.ClientId ?? Guid.Empty;

        var client = clientId != Guid.Empty
            ? await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, clientId), ct)
            : null;
        var clientName = client?.FullName ?? string.Empty;

        var isActive = row.DischargedAtUtc is null;

        var dto = new HospitalizationDetailDto(
            row.Id,
            row.TenantId,
            row.ClinicId,
            row.PetId,
            petName,
            clientId,
            clientName,
            row.ExaminationId,
            row.AdmittedAtUtc,
            row.PlannedDischargeAtUtc,
            row.DischargedAtUtc,
            row.Reason,
            row.Notes,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            isActive);

        return Result<HospitalizationDetailDto>.Success(dto);
    }
}
