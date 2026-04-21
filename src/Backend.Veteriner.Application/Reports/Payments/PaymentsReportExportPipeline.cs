using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.Payments;

/// <summary>CSV ve XLSX export için ortak doğrulama + satır yükleme.</summary>
internal static class PaymentsReportExportPipeline
{
    public sealed record Loaded(IReadOnlyList<PaymentReportItemDto> Items, DateTime FromUtc, DateTime ToUtc);

    public static async Task<Result<Loaded>> LoadAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Payment> payments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? clinicId,
        PaymentMethod? method,
        Guid? clientId,
        Guid? petId,
        string? search,
        CancellationToken ct)
    {
        var validated = await PaymentsReportQueryValidation.ValidateAsync(
            tenantContext,
            clinicContext,
            clinics,
            clinicId,
            fromUtc,
            toUtc,
            ct);
        if (!validated.IsSuccess)
            return Result<Loaded>.Failure(validated.Error);

        var (tenantId, effectiveClinicId, validatedFrom, validatedTo) = validated.Value;

        var (searchPattern, searchClientIds, searchPetIds) =
            await PaymentsReportSearchResolution.ResolveSearchAsync(tenantId, search, clients, pets, ct);

        var total = await payments.CountAsync(
            new PaymentsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                clientId,
                petId,
                method,
                validatedFrom,
                validatedTo,
                searchPattern,
                searchClientIds,
                searchPetIds),
            ct);

        if (total > PaymentsReportConstants.MaxExportRows)
        {
            return Result<Loaded>.Failure(
                "Payments.ReportExportTooManyRows",
                $"Dışa aktarma en fazla {PaymentsReportConstants.MaxExportRows} satır destekler; filtreyi daraltın.");
        }

        var rows = await payments.ListAsync(
            new PaymentsFilteredOrderedForReportSpec(
                tenantId,
                effectiveClinicId,
                clientId,
                petId,
                method,
                validatedFrom,
                validatedTo,
                searchPattern,
                searchClientIds,
                searchPetIds),
            ct);

        var items = await PaymentsReportItemMapping.MapAsync(tenantId, rows, clients, pets, clinics, ct);

        return Result<Loaded>.Success(new Loaded(items, validatedFrom, validatedTo));
    }
}
