using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Application.Prescriptions.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Prescriptions.Queries.GetById;

public sealed class GetPrescriptionByIdQueryHandler
    : IRequestHandler<GetPrescriptionByIdQuery, Result<PrescriptionDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Prescription> _prescriptions;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetPrescriptionByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Prescription> prescriptions,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _prescriptions = prescriptions;
        _pets = pets;
        _clients = clients;
    }

    public async Task<Result<PrescriptionDetailDto>> Handle(GetPrescriptionByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PrescriptionDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var p = await _prescriptions.FirstOrDefaultAsync(
            new PrescriptionByIdSpec(tenantId, request.Id), ct);
        if (p is null)
            return Result<PrescriptionDetailDto>.Failure("Prescriptions.NotFound", "Reçete kaydı bulunamadı.");
        if (_clinicContext.ClinicId is { } clinicId && p.ClinicId != clinicId)
            return Result<PrescriptionDetailDto>.Failure("Prescriptions.NotFound", "Reçete kaydı bulunamadı.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, p.PetId), ct);
        var petName = pet?.Name ?? string.Empty;
        var clientId = pet?.ClientId ?? Guid.Empty;

        var client = clientId != Guid.Empty
            ? await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, clientId), ct)
            : null;
        var clientName = client?.FullName ?? string.Empty;

        var dto = new PrescriptionDetailDto(
            p.Id,
            p.TenantId,
            p.ClinicId,
            p.PetId,
            petName,
            clientId,
            clientName,
            p.ExaminationId,
            p.TreatmentId,
            p.PrescribedAtUtc,
            p.Title,
            p.Content,
            p.Notes,
            p.FollowUpDateUtc,
            p.CreatedAtUtc,
            p.UpdatedAtUtc);

        return Result<PrescriptionDetailDto>.Success(dto);
    }
}
