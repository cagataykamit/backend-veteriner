using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Appointments.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.Appointments;

internal static class AppointmentsReportExportPipeline
{
    public sealed record Loaded(IReadOnlyList<AppointmentReportItemDto> Items, DateTime FromUtc, DateTime ToUtc);

    public static async Task<Result<Loaded>> LoadAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? clinicId,
        AppointmentStatus? status,
        Guid? clientId,
        Guid? petId,
        string? search,
        CancellationToken ct)
    {
        var validated = await AppointmentsReportQueryValidation.ValidateAsync(
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

        var restricted = await AppointmentsReportClientPetFilter.ResolveAsync(
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

        var searchCtx = await AppointmentsReportSearchHelper.ResolveAsync(
            tenantId,
            search,
            clients,
            pets,
            ct);

        var total = await appointments.CountAsync(
            new AppointmentsReportFilteredCountSpec(
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

        if (total > AppointmentsReportConstants.MaxExportRows)
        {
            return Result<Loaded>.Failure(
                "Appointments.ReportExportTooManyRows",
                $"Dışa aktarma en fazla {AppointmentsReportConstants.MaxExportRows} satır destekler; filtreyi daraltın.");
        }

        var rows = await appointments.ListAsync(
            new AppointmentsReportFilteredOrderedForExportSpec(
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

        var items = await AppointmentsReportItemMapping.MapAsync(tenantId, rows, clients, pets, clinics, ct);

        return Result<Loaded>.Success(new Loaded(items, validatedFrom, validatedTo));
    }
}
