using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Examinations.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.Examinations;

internal static class ExaminationsReportExportPipeline
{
    public sealed record Loaded(IReadOnlyList<ExaminationReportItemDto> Items, DateTime FromUtc, DateTime ToUtc);

    public static async Task<Result<Loaded>> LoadAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Examination> examinations,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? clinicId,
        Guid? clientId,
        Guid? petId,
        Guid? appointmentId,
        string? search,
        CancellationToken ct)
    {
        var validated = await ExaminationsReportQueryValidation.ValidateAsync(
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

        var restricted = await ExaminationsReportClientPetFilter.ResolveAsync(
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

        var searchCtx = await ExaminationsReportSearchHelper.ResolveAsync(
            tenantId,
            search,
            clients,
            pets,
            ct);

        var total = await examinations.CountAsync(
            new ExaminationsReportFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                petId,
                restrictedPetIds,
                appointmentId,
                validatedFrom,
                validatedTo,
                searchCtx.Pattern,
                searchCtx.PetIds),
            ct);

        if (total > ExaminationsReportConstants.MaxExportRows)
        {
            return Result<Loaded>.Failure(
                "Examinations.ReportExportTooManyRows",
                $"Dışa aktarma en fazla {ExaminationsReportConstants.MaxExportRows} satır destekler; filtreyi daraltın.");
        }

        var rows = await examinations.ListAsync(
            new ExaminationsReportFilteredOrderedForExportSpec(
                tenantId,
                effectiveClinicId,
                petId,
                restrictedPetIds,
                appointmentId,
                validatedFrom,
                validatedTo,
                searchCtx.Pattern,
                searchCtx.PetIds),
            ct);

        var items = await ExaminationsReportItemMapping.MapAsync(tenantId, rows, clients, pets, clinics, ct);

        return Result<Loaded>.Success(new Loaded(items, validatedFrom, validatedTo));
    }
}
