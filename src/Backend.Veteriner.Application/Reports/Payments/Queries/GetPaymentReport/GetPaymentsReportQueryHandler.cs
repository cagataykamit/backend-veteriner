using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Payments.ReadModels;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Reports.Payments.Queries.GetPaymentReport;

public sealed class GetPaymentsReportQueryHandler
    : IRequestHandler<GetPaymentsReportQuery, Result<PaymentReportResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IClientReadModelLookupReader _clientLookupReader;
    private readonly IPetReadModelLookupReader _petLookupReader;
    private readonly IPaymentsReportReadModelReader _reportReadModelReader;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;
    private readonly ILogger<GetPaymentsReportQueryHandler> _logger;

    public GetPaymentsReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Payment> payments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        IClientReadModelLookupReader clientLookupReader,
        IPetReadModelLookupReader petLookupReader,
        IPaymentsReportReadModelReader reportReadModelReader,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions,
        ILogger<GetPaymentsReportQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _payments = payments;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
        _clientLookupReader = clientLookupReader;
        _petLookupReader = petLookupReader;
        _reportReadModelReader = reportReadModelReader;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
        _logger = logger ?? NullLogger<GetPaymentsReportQueryHandler>.Instance;
    }

    public async Task<Result<PaymentReportResultDto>> Handle(
        GetPaymentsReportQuery request,
        CancellationToken ct)
    {
        var validated = await PaymentsReportQueryValidation.ValidateAsync(
            _tenantContext,
            _clinicContext,
            _clinicScopeResolver,
            request.ClinicId,
            request.FromUtc,
            request.ToUtc,
            ct);
        if (!validated.IsSuccess)
            return Result<PaymentReportResultDto>.Failure(validated.Error);

        var (tenantId, effectiveClinicId, accessibleClinicIds, fromUtc, toUtc) = validated.Value;

        // CQRS-15G: payment report JSON Query DB routing (PaymentReadModels).
        // Flag açık + search boş + scope represent edilebiliyorsa (tek klinik veya tenant-wide Admin/Owner) Query DB'den okunur.
        // Search dolu (search parity 15I'ye bırakıldı) ya da multi-clinic (ClinicAdmin, aktif klinik yok) scope → Command DB fallback.
        // Scope resolve hatası validation aşamasında zaten failure döndürür (Command path da aynı scope'a bağlıdır) — Query DB'ye gidilmez.
        // Query path seçildiğinde Command DB'ye fallback YAPILMAZ; search resolution ÇALIŞTIRILMAZ (search boş olduğu garanti).
        // Export CSV/XLSX ayrı handler'larda; export routing için <see cref="QueryReadModelsOptions.PaymentsReportExportReadEnabled"/> (15J).
        if (_queryReadModelsOptions.PaymentsReportReadEnabled
            && ListQueryTextSearch.Normalize(request.Search) is null
            && TryGetRepresentableQueryClinicScope(effectiveClinicId, accessibleClinicIds, out var queryClinicId))
        {
            var queryPage = Math.Max(1, request.Page);
            var queryPageSize = Math.Clamp(request.PageSize, 1, PaymentsReportConstants.MaxPageSize);

            var readResult = await _reportReadModelReader.GetReportAsync(
                new PaymentsReportReadRequest(
                    tenantId,
                    queryClinicId,
                    request.ClientId,
                    request.PetId,
                    request.Method,
                    fromUtc,
                    toUtc,
                    queryPage,
                    queryPageSize),
                ct);

            _logger.LogInformation(
                "Payments report generated from Query DB read model. TenantId={TenantId} ClinicScoped={ClinicScoped} Total={Total}",
                tenantId,
                queryClinicId.HasValue,
                readResult.TotalCount);

            return Result<PaymentReportResultDto>.Success(
                new PaymentReportResultDto(readResult.TotalCount, readResult.TotalAmount, readResult.Items));
        }

        var (searchPattern, searchClientIds, searchPetIds) =
            await PaymentsReportSearchResolution.ResolveSearchAsync(
                tenantId,
                request.Search,
                _queryReadModelsOptions.PaymentsSearchLookupEnabled,
                _clientLookupReader,
                _petLookupReader,
                _clients,
                _pets,
                ct);

        var total = await _payments.CountAsync(
            new PaymentsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.ClientId,
                request.PetId,
                request.Method,
                fromUtc,
                toUtc,
                searchPattern,
                searchClientIds,
                searchPetIds,
                accessibleClinicIds),
            ct);

        var amountRows = await _payments.ListAsync(
            new PaymentsFilteredAmountsSpec(
                tenantId,
                effectiveClinicId,
                request.ClientId,
                request.PetId,
                request.Method,
                fromUtc,
                toUtc,
                searchPattern,
                searchClientIds,
                searchPetIds,
                accessibleClinicIds),
            ct);

        var totalAmount = amountRows.Sum();

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, PaymentsReportConstants.MaxPageSize);

        var rows = await _payments.ListAsync(
            new PaymentsFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.ClientId,
                request.PetId,
                request.Method,
                fromUtc,
                toUtc,
                page,
                pageSize,
                searchPattern,
                searchClientIds,
                searchPetIds,
                accessibleClinicIds),
            ct);

        var items = await PaymentsReportItemMapping.MapAsync(tenantId, rows, _clients, _pets, _clinics, ct);

        return Result<PaymentReportResultDto>.Success(
            new PaymentReportResultDto(total, totalAmount, items));
    }

    /// <summary>
    /// Validation tarafından çözülen scope'un (tek clinic veya tenant-wide) Query DB reader ile represent edilip
    /// edilemeyeceğini belirler. 15E (<c>GetClientPaymentSummaryQueryHandler</c>) ile aynı kural.
    /// </summary>
    /// <returns>
    /// <c>true</c> + <paramref name="queryClinicId"/> dolu: tek klinik kapsamı (o klinik filtreli).
    /// <c>true</c> + <paramref name="queryClinicId"/> null: tenant-wide (Admin/Owner, aktif klinik yok) — clinic filtresi yok.
    /// <c>false</c>: multi-clinic (ClinicAdmin, aktif klinik yok; <paramref name="accessibleClinicIds"/> dolu) — Command DB fallback.
    /// </returns>
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
