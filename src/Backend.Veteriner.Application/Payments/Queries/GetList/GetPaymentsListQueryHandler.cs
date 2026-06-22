using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Payments.Queries.GetList;

public sealed class GetPaymentsListQueryHandler
    : IRequestHandler<GetPaymentsListQuery, Result<PagedResult<PaymentListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IClientReadModelLookupReader _clientLookupReader;
    private readonly IPetReadModelLookupReader _petLookupReader;
    private readonly IPaymentsListReadModelReader _paymentsListReadModelReader;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;
    private readonly ILogger<GetPaymentsListQueryHandler> _logger;

    public GetPaymentsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Payment> payments,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IClientReadModelLookupReader clientLookupReader,
        IPetReadModelLookupReader petLookupReader,
        IPaymentsListReadModelReader paymentsListReadModelReader,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions,
        ILogger<GetPaymentsListQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _payments = payments;
        _pets = pets;
        _clients = clients;
        _clientLookupReader = clientLookupReader;
        _petLookupReader = petLookupReader;
        _paymentsListReadModelReader = paymentsListReadModelReader;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
        _logger = logger ?? NullLogger<GetPaymentsListQueryHandler>.Instance;
    }

    public async Task<Result<PagedResult<PaymentListItemDto>>> Handle(
        GetPaymentsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<PaymentListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.Paging.Page);
        var pageSize = Math.Clamp(request.Paging.PageSize, 1, 200);
        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();
        var querySteps = 0;
        var slowestStep = string.Empty;
        long slowestMs = 0;

        void MarkStep(string name)
        {
            querySteps++;
            var elapsed = stepSw.ElapsedMilliseconds;
            if (elapsed > slowestMs)
            {
                slowestMs = elapsed;
                slowestStep = name;
            }

            stepSw.Restart();
        }

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<PaymentListItemDto>>.Failure(
                "Payments.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var requestedClinicId = request.ClinicId ?? _clinicContext.ClinicId;

        // Güvenlik: açık bir klinik kapsamı (request.ClinicId veya aktif clinic context) yoksa
        // tüm kiracı ödeme kayıtlarını DÖNDÜRME. Tenant-wide kullanıcılar dahil, kapsamsız list/okuma engellenir.
        if (requestedClinicId is null)
        {
            return Result<PagedResult<PaymentListItemDto>>.Failure(
                "Payments.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi. Ödemeler klinik kapsamı olmadan listelenemez.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PagedResult<PaymentListItemDto>>.Failure(scopeResult.Error);
        MarkStep("scopeResolve");

        var effectiveClinicId = scopeResult.Value!.SingleClinicId;
        var accessibleClinicIds = scopeResult.Value!.AccessibleClinicIds;

        var normalizedSearch = ListQueryTextSearch.Normalize(request.Search);

        // CQRS-14E + 15L: Query DB read-model routing.
        // PaymentsListReadEnabled + klinik kapsamı tek kliniğe (SingleClinicId) çözülmüş → Query DB (search boş veya dolu).
        // Multi-clinic (SingleClinicId null + AccessibleClinicIds dolu) → Command DB fallback.
        // Query path search: Query DB lookup reader'lar + PaymentReadModels filtre; Command DB search resolution kullanılmaz.
        // Query DB yolu seçildiğinde Command DB'ye fallback YAPILMAZ; Query DB boşsa boş paged result döner.
        if (_queryReadModelsOptions.PaymentsListReadEnabled
            && effectiveClinicId is { } queryClinicId)
        {
            string? querySearchPattern = null;
            Guid[] querySearchClientIds = [];
            Guid[] querySearchPetIds = [];
            if (normalizedSearch is not null)
            {
                querySearchPattern = ListQueryTextSearch.BuildContainsLikePattern(normalizedSearch);
                (querySearchClientIds, querySearchPetIds) = await PaymentsListQuerySearchResolution.ResolveSearchIdsAsync(
                    tenantId,
                    querySearchPattern,
                    _clientLookupReader,
                    _petLookupReader,
                    ct);
                MarkStep("searchLookup");
            }

            var readRequest = new PaymentsListReadRequest(
                tenantId,
                queryClinicId,
                page,
                pageSize,
                request.ClientId,
                request.PetId,
                request.Method,
                request.PaidFromUtc,
                request.PaidToUtc,
                querySearchPattern,
                querySearchClientIds,
                querySearchPetIds);

            var readResult = await _paymentsListReadModelReader.GetListAsync(readRequest, ct);
            MarkStep("paymentsListReadModel");

            _logger.LogInformation(
                "Payments list generated from Query DB read model. TenantId={TenantId} ClinicId={ClinicId} Page={Page} PageSize={PageSize} SearchProvided={SearchProvided} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
                tenantId,
                queryClinicId,
                page,
                pageSize,
                normalizedSearch is not null,
                querySteps,
                slowestStep,
                slowestMs,
                totalSw.ElapsedMilliseconds);

            return Result<PagedResult<PaymentListItemDto>>.Success(
                PagedResult<PaymentListItemDto>.Create(readResult.Items, readResult.TotalCount, page, pageSize));
        }

        string? searchPattern = null;
        Guid[] searchClientIds = [];
        Guid[] searchPetIds = [];
        if (normalizedSearch is not null)
        {
            searchPattern = ListQueryTextSearch.BuildContainsLikePattern(normalizedSearch);
            (searchClientIds, searchPetIds) = await PaymentsListSearchResolution.ResolveSearchIdsAsync(
                tenantId,
                searchPattern,
                _queryReadModelsOptions.PaymentsSearchLookupEnabled,
                _clientLookupReader,
                _petLookupReader,
                _clients,
                _pets,
                ct);
            MarkStep("searchLookup");
        }

        var total = await _payments.CountAsync(
            new PaymentsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.ClientId,
                request.PetId,
                request.Method,
                request.PaidFromUtc,
                request.PaidToUtc,
                searchPattern,
                searchClientIds,
                searchPetIds,
                accessibleClinicIds),
            ct);
        MarkStep("paymentsCount");

        var rows = await _payments.ListAsync(
            new PaymentsListFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.ClientId,
                request.PetId,
                request.Method,
                request.PaidFromUtc,
                request.PaidToUtc,
                page,
                pageSize,
                searchPattern,
                searchClientIds,
                searchPetIds,
                accessibleClinicIds),
            ct);
        MarkStep("paymentsPage");

        var clientIds = rows.Select(x => x.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0
            ? []
            : await _clients.ListAsync(new ClientsByTenantIdsNameSpec(tenantId, clientIds), ct);
        if (clientIds.Length > 0)
            MarkStep("clientsLookup");

        var clientNameById = clients.ToDictionary(x => x.Id, x => x.FullName);

        var petIds = rows.Where(x => x.PetId.HasValue).Select(x => x.PetId!.Value).Distinct().ToArray();
        var pets = petIds.Length == 0
            ? []
            : await _pets.ListAsync(new PetsByTenantIdsNameClientSpec(tenantId, petIds), ct);
        if (petIds.Length > 0)
            MarkStep("petsLookup");

        var petNameById = pets.ToDictionary(x => x.Id, x => x.Name);

        var items = rows
            .Select(p =>
            {
                var clientName = clientNameById.TryGetValue(p.ClientId, out var cn) ? cn : string.Empty;
                var petName = p.PetId is { } pid && petNameById.TryGetValue(pid, out var pn) ? pn : string.Empty;

                return new PaymentListItemDto(
                    p.Id,
                    p.ClinicId,
                    p.ClientId,
                    clientName,
                    p.PetId,
                    petName,
                    p.Amount,
                    p.Currency,
                    p.Method,
                    p.PaidAtUtc);
            })
            .ToList();

        _logger.LogInformation(
            "Payments list generated. TenantId={TenantId} ClinicId={ClinicId} Page={Page} PageSize={PageSize} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
            tenantId,
            effectiveClinicId,
            page,
            pageSize,
            querySteps,
            slowestStep,
            slowestMs,
            totalSw.ElapsedMilliseconds);

        return Result<PagedResult<PaymentListItemDto>>.Success(
            PagedResult<PaymentListItemDto>.Create(items, total, page, pageSize));
    }
}
