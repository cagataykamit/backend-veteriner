using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetById;

public sealed class GetVaccinationByIdQueryHandler
    : IRequestHandler<GetVaccinationByIdQuery, Result<VaccinationDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetVaccinationByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _vaccinations = vaccinations;
        _pets = pets;
        _clients = clients;
    }

    public async Task<Result<VaccinationDetailDto>> Handle(GetVaccinationByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<VaccinationDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var v = await _vaccinations.FirstOrDefaultAsync(
            new VaccinationByIdSpec(tenantId, request.Id), ct);
        if (v is null)
            return Result<VaccinationDetailDto>.Failure("Vaccinations.NotFound", "Aşı kaydı bulunamadı.");
        if (_clinicContext.ClinicId is { } clinicId && v.ClinicId != clinicId)
            return Result<VaccinationDetailDto>.Failure("Vaccinations.NotFound", "Asi kaydi bulunamadi.");

        var pets = await _pets.ListAsync(new PetsByTenantIdsSpec(tenantId, [v.PetId]), ct);
        var pet = pets.FirstOrDefault();
        var petName = pet?.Name ?? string.Empty;
        var clientId = pet?.ClientId ?? Guid.Empty;
        string clientName = string.Empty;
        if (pet is not null)
        {
            var clients = await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, [pet.ClientId]), ct);
            clientName = clients.FirstOrDefault()?.FullName ?? string.Empty;
        }

        var dto = new VaccinationDetailDto(
            v.Id,
            v.TenantId,
            v.PetId,
            petName,
            clientName,
            clientId,
            v.ClinicId,
            v.ExaminationId,
            v.VaccineName,
            v.AppliedAtUtc,
            v.DueAtUtc,
            v.Status,
            v.Notes,
            v.CreatedAtUtc,
            v.UpdatedAtUtc);
        return Result<VaccinationDetailDto>.Success(dto);
    }
}
