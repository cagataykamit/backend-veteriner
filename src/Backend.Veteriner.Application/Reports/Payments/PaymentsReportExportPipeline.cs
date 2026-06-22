using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Payments.ReadModels;
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
        IClinicReadScopeResolver scopeResolver,
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
        bool paymentsSearchLookupEnabled,
        bool paymentsReportExportReadEnabled,
        IClientReadModelLookupReader clientLookupReader,
        IPetReadModelLookupReader petLookupReader,
        IPaymentsReportExportReadModelReader exportReadModelReader,
        CancellationToken ct)
    {
        var validated = await PaymentsReportQueryValidation.ValidateAsync(
            tenantContext,
            clinicContext,
            scopeResolver,
            clinicId,
            fromUtc,
            toUtc,
            ct);
        if (!validated.IsSuccess)
            return Result<Loaded>.Failure(validated.Error);

        var (tenantId, effectiveClinicId, accessibleClinicIds, validatedFrom, validatedTo) = validated.Value;

        // CQRS-15J + 15N: payment export Query DB routing (PaymentReadModels).
        // Flag açık + scope represent edilebiliyorsa (tek klinik veya tenant-wide) Query DB'den okunur (search boş veya dolu).
        // Multi-clinic scope → Command DB fallback.
        // Query path seçildiğinde Command DB'ye fallback YAPILMAZ; Command PaymentsReportSearchResolution ÇALIŞTIRILMAZ.
        // Query path search: Query DB lookup reader'lar + PaymentReadModels filtre; PaymentsSearchLookupEnabled etkilemez.
        if (paymentsReportExportReadEnabled
            && TryGetRepresentableQueryClinicScope(effectiveClinicId, accessibleClinicIds, out var queryClinicId))
        {
            string? querySearchPattern = null;
            Guid[] querySearchClientIds = [];
            Guid[] querySearchPetIds = [];
            var normalizedSearch = ListQueryTextSearch.Normalize(search);
            if (normalizedSearch is not null)
            {
                querySearchPattern = ListQueryTextSearch.BuildContainsLikePattern(normalizedSearch);
                (querySearchClientIds, querySearchPetIds) = await PaymentsListQuerySearchResolution.ResolveSearchIdsAsync(
                    tenantId,
                    querySearchPattern,
                    clientLookupReader,
                    petLookupReader,
                    ct);
            }

            var readResult = await exportReadModelReader.GetExportAsync(
                new PaymentsReportExportReadRequest(
                    tenantId,
                    queryClinicId,
                    clientId,
                    petId,
                    method,
                    validatedFrom,
                    validatedTo,
                    querySearchPattern,
                    querySearchClientIds,
                    querySearchPetIds),
                ct);

            if (readResult.TotalCount > PaymentsReportConstants.MaxExportRows)
            {
                return Result<Loaded>.Failure(
                    "Payments.ReportExportTooManyRows",
                    $"Dışa aktarma en fazla {PaymentsReportConstants.MaxExportRows} satır destekler; filtreyi daraltın.");
            }

            return Result<Loaded>.Success(
                new Loaded(readResult.Items, validatedFrom, validatedTo));
        }

        var (searchPattern, searchClientIds, searchPetIds) =
            await PaymentsReportSearchResolution.ResolveSearchAsync(
                tenantId,
                search,
                paymentsSearchLookupEnabled,
                clientLookupReader,
                petLookupReader,
                clients,
                pets,
                ct);

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
                searchPetIds,
                accessibleClinicIds),
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
                searchPetIds,
                accessibleClinicIds),
            ct);

        var items = await PaymentsReportItemMapping.MapAsync(tenantId, rows, clients, pets, clinics, ct);

        return Result<Loaded>.Success(new Loaded(items, validatedFrom, validatedTo));
    }

    /// <summary>
    /// Validation tarafından çözülen scope'un (tek clinic veya tenant-wide) Query DB reader ile represent edilip
    /// edilemeyeceğini belirler. 15G/15E ile aynı kural.
    /// </summary>
    private static bool TryGetRepresentableQueryClinicScope(
        Guid? effectiveClinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        out Guid? queryClinicId)
    {
        if (effectiveClinicId is { } single)
        {
            queryClinicId = single;
            return true;
        }

        if (accessibleClinicIds is null)
        {
            queryClinicId = null;
            return true;
        }

        queryClinicId = null;
        return false;
    }
}
