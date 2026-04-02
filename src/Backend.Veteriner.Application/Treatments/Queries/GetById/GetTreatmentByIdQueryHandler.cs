using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Treatments.Contracts.Dtos;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Treatments;
using MediatR;

namespace Backend.Veteriner.Application.Treatments.Queries.GetById;

public sealed class GetTreatmentByIdQueryHandler
    : IRequestHandler<GetTreatmentByIdQuery, Result<TreatmentDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Treatment> _treatments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetTreatmentByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Treatment> treatments,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _treatments = treatments;
        _pets = pets;
        _clients = clients;
    }

    public async Task<Result<TreatmentDetailDto>> Handle(GetTreatmentByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<TreatmentDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var t = await _treatments.FirstOrDefaultAsync(
            new TreatmentByIdSpec(tenantId, request.Id), ct);
        if (t is null)
            return Result<TreatmentDetailDto>.Failure("Treatments.NotFound", "Tedavi kaydı bulunamadı.");
        if (_clinicContext.ClinicId is { } clinicId && t.ClinicId != clinicId)
            return Result<TreatmentDetailDto>.Failure("Treatments.NotFound", "Tedavi kaydı bulunamadı.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, t.PetId), ct);
        var petName = pet?.Name ?? string.Empty;
        var clientId = pet?.ClientId ?? Guid.Empty;

        var client = clientId != Guid.Empty
            ? await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, clientId), ct)
            : null;
        var clientName = client?.FullName ?? string.Empty;

        var dto = new TreatmentDetailDto(
            t.Id,
            t.TenantId,
            t.ClinicId,
            t.PetId,
            petName,
            clientId,
            clientName,
            t.ExaminationId,
            t.TreatmentDateUtc,
            t.Title,
            t.Description,
            t.Notes,
            t.FollowUpDateUtc,
            t.CreatedAtUtc,
            t.UpdatedAtUtc);

        return Result<TreatmentDetailDto>.Success(dto);
    }
}
