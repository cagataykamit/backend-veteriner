using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalPetVaccinations;

/// <summary>
/// Hayvanın aşı geçmişi (uygulanan + iptal) ve planlı aşıları (docs/PORTAL_API_CONTRACT.md §6.3). Gecikme ADR-028
/// <see cref="VaccinationDueRule"/>; vefat eden hayvanın planlı aşısı ne gecikmiş ne yaklaşan sayılır (<see cref="Pet.ReceivesCare"/>).
/// </summary>
public sealed record GetPortalPetVaccinationsQuery(Guid OwnerAccountId, Guid LinkId, Guid PetId)
    : IRequest<Result<PortalPetVaccinationsDto>>, IAuditableRequest
{
    public string AuditAction => "Portal.Read.Vaccinations";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId};PetId={PetId}";
}

public sealed class GetPortalPetVaccinationsQueryHandler
    : IRequestHandler<GetPortalPetVaccinationsQuery, Result<PortalPetVaccinationsDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly TimeProvider _timeProvider;

    public GetPortalPetVaccinationsQueryHandler(
        OwnerPortalAccess access,
        IReadRepository<Pet> pets,
        IReadRepository<Vaccination> vaccinations,
        TimeProvider timeProvider)
    {
        _access = access;
        _pets = pets;
        _vaccinations = vaccinations;
        _timeProvider = timeProvider;
    }

    public async Task<Result<PortalPetVaccinationsDto>> Handle(GetPortalPetVaccinationsQuery request, CancellationToken ct)
    {
        var scopeResult = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PortalPetVaccinationsDto>.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        var pet = await _pets.FirstOrDefaultAsync(new PortalPetOfClientSpec(scope.TenantId, scope.ClientId, request.PetId), ct);
        if (pet is null)
            return Result<PortalPetVaccinationsDto>.Failure(OwnerPortalErrors.PetNotFound);

        var rows = await _vaccinations.ListAsync(new PortalVaccinationsForPetSpec(scope.TenantId, pet.Id), ct);

        var history = rows
            .Where(v => v.Status != VaccinationStatus.Scheduled)
            .OrderByDescending(v => v.AppliedAtUtc ?? v.DueAtUtc ?? v.CreatedAtUtc)
            .ThenByDescending(v => v.Id)
            .Select(v => new PortalVaccinationHistoryItemDto(v.Id, v.VaccineName, v.Status, v.AppliedAtUtc, v.DueAtUtc))
            .ToArray();

        var overdueBeforeUtc = VaccinationDueRule.OverdueBeforeUtc(_timeProvider.GetUtcNow().UtcDateTime);
        var upcoming = pet.IsReceivingCare
            ? rows
                .Where(v => v.Status == VaccinationStatus.Scheduled)
                .OrderBy(v => v.DueAtUtc is null)
                .ThenBy(v => v.DueAtUtc)
                .ThenBy(v => v.Id)
                .Select(v => new PortalUpcomingVaccinationDto(
                    v.Id,
                    v.VaccineName,
                    v.DueAtUtc,
                    v.DueAtUtc is { } due && VaccinationDueRule.IsOverdueAt(due, overdueBeforeUtc)))
                .ToArray()
            : [];

        return Result<PortalPetVaccinationsDto>.Success(new PortalPetVaccinationsDto(history, upcoming));
    }
}
