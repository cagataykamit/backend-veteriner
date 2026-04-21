using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.Vaccinations;

internal static class VaccinationsReportClientPetFilter
{
    public sealed record Resolution(bool SkipQueryEmpty, Guid[]? RestrictedPetIds);

    public static async Task<Result<Resolution>> ResolveAsync(
        Guid tenantId,
        Guid? clientId,
        Guid? petId,
        IReadRepository<Pet> pets,
        CancellationToken ct)
    {
        if (!clientId.HasValue)
            return Result<Resolution>.Success(new Resolution(false, null));

        var owned = await pets.ListAsync(new PetsByTenantForClientIdsSpec(tenantId, [clientId.Value]), ct);
        var ids = owned.Select(p => p.Id).ToArray();
        if (ids.Length == 0)
            return Result<Resolution>.Success(new Resolution(true, null));

        if (petId.HasValue)
        {
            var pet = await pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, petId.Value), ct);
            if (pet is null)
                return Result<Resolution>.Failure("Pets.NotFound", "Hayvan bulunamadı veya kiracıya ait değil.");
            if (pet.ClientId != clientId.Value)
                return Result<Resolution>.Failure(
                    "Vaccinations.Validation",
                    "Seçilen hayvan bu müşteriye ait değil.");
        }

        return Result<Resolution>.Success(new Resolution(false, ids));
    }
}
