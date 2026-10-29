using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Vaccinations.Specs;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Tenants;
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
        IClinicReadScopeResolver scopeResolver,
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
        ITrialLimitPolicy trialLimitPolicy,
        RegisterExportFormat format,
        CancellationToken ct)
    {
        var validated = await VaccinationsReportQueryValidation.ValidateAsync(
            tenantContext,
            clinicContext,
            scopeResolver,
            clinicId,
            fromUtc,
            toUtc,
            ct);
        if (!validated.IsSuccess)
            return Result<Loaded>.Failure(validated.Error);

        var (tenantId, effectiveClinicId, accessibleClinicIds, validatedFrom, validatedTo) = validated.Value!;

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
                searchCtx.PetIds,
                accessibleClinicIds),
            ct);

        // Satır tavanı kiracıya ve biçime göre (TRIAL-004, REPORT-007): deneme 1.000, ücretli ReportsSharedLimits.MaxExportRows, PDF en çok MaxPdfExportRows.
        var rowLimit = (await trialLimitPolicy.GetExportRowLimitAsync(tenantId, ct)).ForFormat(format);
        var withinLimit = rowLimit.Ensure(total, "Vaccinations.ReportExportTooManyRows");
        if (!withinLimit.IsSuccess)
            return Result<Loaded>.Failure(withinLimit.Error);

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
                searchCtx.PetIds,
                accessibleClinicIds),
            ct);

        var items = await VaccinationsReportItemMapping.MapAsync(tenantId, rows, clients, pets, clinics, ct);

        return Result<Loaded>.Success(new Loaded(items, validatedFrom, validatedTo));
    }
}
