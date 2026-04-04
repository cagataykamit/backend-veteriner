using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Queries.GetPaymentSummary;

public sealed class GetClientPaymentSummaryQueryHandler
    : IRequestHandler<GetClientPaymentSummaryQuery, Result<ClientPaymentSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;

    public GetClientPaymentSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Client> clients,
        IReadRepository<Payment> payments,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clients = clients;
        _payments = payments;
        _pets = pets;
        _clinics = clinics;
    }

    public async Task<Result<ClientPaymentSummaryDto>> Handle(GetClientPaymentSummaryQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClientPaymentSummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.Id), ct);
        if (client is null)
            return Result<ClientPaymentSummaryDto>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        var clinicId = _clinicContext.ClinicId;
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
}
