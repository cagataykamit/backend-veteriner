using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Clients.Queries.GetPaymentSummary;

public sealed class GetClientPaymentSummaryQueryHandler
    : IRequestHandler<GetClientPaymentSummaryQuery, Result<ClientPaymentSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IClientPaymentSummaryReadModelReader _summaryReadModelReader;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;
    private readonly ILogger<GetClientPaymentSummaryQueryHandler> _logger;

    public GetClientPaymentSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Client> clients,
        IReadRepository<Payment> payments,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        IClientPaymentSummaryReadModelReader summaryReadModelReader,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions,
        ILogger<GetClientPaymentSummaryQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _clients = clients;
        _payments = payments;
        _pets = pets;
        _clinics = clinics;
        _summaryReadModelReader = summaryReadModelReader;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
        _logger = logger ?? NullLogger<GetClientPaymentSummaryQueryHandler>.Instance;
    }

    public async Task<Result<ClientPaymentSummaryDto>> Handle(GetClientPaymentSummaryQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClientPaymentSummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        // Client existence/access validation: mevcut Command DB pattern'i (ClientByIdSpec) her iki yolda da korunur.
        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.Id), ct);
        if (client is null)
            return Result<ClientPaymentSummaryDto>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        var clinicId = _clinicContext.ClinicId;

        // CQRS-15E: Query DB PaymentReadModels routing.
        // Flag açık + scope represent edilebiliyorsa (tek klinik veya tenant-wide Admin/Owner) Query DB'den okunur.
        // Multi-clinic (ClinicAdmin, aktif klinik yok) scope tek ClinicId/tenant-wide ile represent edilemez → Command DB fallback.
        // Query path seçildiğinde Command DB'ye fallback YAPILMAZ; Query DB boşsa count 0 / totals boş / recent boş döner.
        if (_queryReadModelsOptions.ClientPaymentSummaryReadEnabled)
        {
            var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
            if (scopeResult.IsSuccess
                && TryGetRepresentableQueryClinicScope(scopeResult.Value!, out var queryClinicId))
            {
                var readResult = await _summaryReadModelReader.GetSummaryAsync(
                    new ClientPaymentSummaryReadRequest(
                        tenantId,
                        request.Id,
                        queryClinicId,
                        ClientPaymentSummaryConstants.RecentPaymentsTake),
                    ct);

                var queryDistinctCurrencies = readResult.CurrencyTotals.Count;
                var queryTotalPaidAmount = queryDistinctCurrencies == 1
                    ? readResult.CurrencyTotals[0].TotalAmount
                    : 0m;

                _logger.LogInformation(
                    "Client payment summary generated from Query DB read model. TenantId={TenantId} ClientId={ClientId} ClinicScoped={ClinicScoped}",
                    tenantId,
                    request.Id,
                    queryClinicId.HasValue);

                return Result<ClientPaymentSummaryDto>.Success(new ClientPaymentSummaryDto(
                    request.Id,
                    client.FullName,
                    readResult.TotalPaymentsCount,
                    queryTotalPaidAmount,
                    readResult.CurrencyTotals,
                    readResult.LastPaymentAtUtc,
                    readResult.RecentPayments));
            }
        }

        var rows = await _payments.ListAsync(
            new PaymentsForClientSummaryRowsSpec(tenantId, clinicId, request.Id), ct);

        var count = rows.Count;
        var currencyTotals = rows
            .GroupBy(r => r.Currency, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ClientPaymentCurrencyTotalDto(g.Key, g.Sum(x => x.Amount)))
            .OrderBy(x => x.Currency, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var distinctCurrencies = currencyTotals.Count;
        var totalPaidAmount = distinctCurrencies == 1 ? currencyTotals[0].TotalAmount : 0m;

        DateTime? lastAt = count == 0 ? null : rows.Max(r => r.PaidAtUtc);

        var recentRows = rows
            .OrderByDescending(r => r.PaidAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(ClientPaymentSummaryConstants.RecentPaymentsTake)
            .ToList();

        var petIds = recentRows
            .Where(r => r.PetId.HasValue)
            .Select(r => r.PetId!.Value)
            .Distinct()
            .ToArray();
        var petNameById = new Dictionary<Guid, string>();
        if (petIds.Length > 0)
        {
            var pets = await _pets.ListAsync(new PetsByTenantIdsSpec(tenantId, petIds), ct);
            petNameById = pets.ToDictionary(p => p.Id, p => p.Name);
        }

        var clinicIds = recentRows.Select(r => r.ClinicId).Distinct().ToArray();
        var clinicRows = clinicIds.Length == 0
            ? []
            : await _clinics.ListAsync(new ClinicsByTenantIdsSpec(tenantId, clinicIds), ct);
        var clinicNameById = clinicRows.ToDictionary(c => c.Id, c => c.Name);

        var recentDtos = recentRows
            .Select(r => new ClientPaymentRecentItemDto(
                r.Id,
                r.PaidAtUtc,
                r.ClinicId,
                clinicNameById.GetValueOrDefault(r.ClinicId, string.Empty),
                r.PetId,
                r.PetId is { } pid ? petNameById.GetValueOrDefault(pid, string.Empty) : string.Empty,
                r.Amount,
                r.Currency,
                r.Method,
                r.Notes))
            .ToList();

        var dto = new ClientPaymentSummaryDto(
            request.Id,
            client.FullName,
            count,
            totalPaidAmount,
            currencyTotals,
            lastAt,
            recentDtos);

        return Result<ClientPaymentSummaryDto>.Success(dto);
    }

    /// <summary>
    /// Çözülen scope'un Query DB reader (tenant + opsiyonel tek clinic) ile represent edilip edilemeyeceğini belirler.
    /// </summary>
    /// <returns>
    /// <c>true</c> + <paramref name="queryClinicId"/> dolu: tek klinik kapsamı. <c>true</c> + <paramref name="queryClinicId"/> null:
    /// tenant-wide (Admin/Owner, aktif klinik yok) — clinic filtresi olmadan TenantId+ClientId okunur (mevcut Command DB davranışı).
    /// <c>false</c>: multi-clinic (ClinicAdmin, aktif klinik yok) — tek ClinicId/tenant-wide ile represent edilemez, Command DB fallback.
    /// </returns>
    private static bool TryGetRepresentableQueryClinicScope(ClinicReadScope scope, out Guid? queryClinicId)
    {
        if (scope.SingleClinicId is { } single)
        {
            queryClinicId = single;
            return true;
        }

        if (scope.AccessibleClinicIds is null)
        {
            queryClinicId = null;
            return true;
        }

        queryClinicId = null;
        return false;
    }
}
