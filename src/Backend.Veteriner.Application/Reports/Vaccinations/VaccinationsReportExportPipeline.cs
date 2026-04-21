using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Reports.Vaccinations;

internal static class VaccinationsReportExportPipeline
{
    public sealed record Loaded(IReadOnlyList<VaccinationReportItemDto> Items, DateTime FromUtc, DateTime ToUtc);

    public static async Task<Result<Loaded>> LoadAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? clinicId,
        Guid? clientId,
        Guid? petId,
        VaccinationStatus? status,
        string? search,
        CancellationToken ct)
    {
        var validated = await VaccinationsReportQueryValidation.ValidateAsync(
            tenantContext,
            clinicContext,
            clinics,
            clinicId,
            fromUtc,
            toUtc,
            ct);
        if (!validated.IsSuccess)
            return Result<Loaded>.Failure(validated.Error);

        var (tenantId, effectiveClinicId, validatedFrom, validatedTo) = validated.Value!;

        var restricted = await VaccinationsReportClientPetFilter.ResolveAsync(
            tenantId,
            clientId,
            petId,
            pets,
            ct);
        if (!restricted.IsSuccess)
            return Result<Loaded>.Failure(restricted.Error);
        if (restricted.Value!.SkipQueryEmpty)
            return Result<Loaded>.Success(new Loaded([], validatedFrom, validatedTo));

        var restrictedPetIds = restricted.Value.RestrictedPetIds;

        var searchCtx = await VaccinationsReportSearchHelper.ResolveAsync(
            tenantId,
            search,
            clients,
            pets,
            ct);

        var total = await vaccinations.CountAsync(
            new VaccinationsReportFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                petId,
                restrictedPetIds,
                status,
                validatedFrom,
                validatedTo,
                searchCtx.Pattern,
                searchCtx.PetIds),
            ct);

        if (total > VaccinationsReportConstants.MaxExportRows)
        {
            return Result<Loaded>.Failure(
                "Vaccinations.ReportExportTooManyRows",
                $"Dışa aktarma en fazla {VaccinationsReportConstants.MaxExportRows} satır destekler; filtreyi daraltın.");
        }

        var rows = await vaccinations.ListAsync(
            new VaccinationsReportFilteredOrderedForExportSpec(
                tenantId,
                effectiveClinicId,
                petId,
                restrictedPetIds,
                status,
                validatedFrom,
                validatedTo,
                searchCtx.Pattern,
                searchCtx.PetIds),
            ct);

        var items = await VaccinationsReportItemMapping.MapAsync(tenantId, rows, clients, pets, clinics, ct);

        return Result<Loaded>.Success(new Loaded(items, validatedFrom, validatedTo));
    }
}
