using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetFinanceSummary;

public sealed class GetDashboardFinanceSummaryQueryHandler
    : IRequestHandler<GetDashboardFinanceSummaryQuery, Result<DashboardFinanceSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;

    public GetDashboardFinanceSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Payment> payments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _payments = payments;
        _clients = clients;
        _pets = pets;
    }

    public async Task<Result<DashboardFinanceSummaryDto>> Handle(
        GetDashboardFinanceSummaryQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<DashboardFinanceSummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var utcNow = DateTime.UtcNow;
        var (dayStart, dayEnd) = OperationDayBounds.ForUtcNow(utcNow);
        var (weekStart, weekEnd) = OperationPeriodBounds.WeekForUtcNow(utcNow);
        var (monthStart, monthEnd) = OperationPeriodBounds.MonthForUtcNow(utcNow);
        var clinicId = _clinicContext.ClinicId;

        var todayAmounts = await _payments.ListAsync(
            new PaymentsAmountInPaidAtWindowSpec(tenantId, clinicId, dayStart, dayEnd), ct);
        var weekAmounts = await _payments.ListAsync(
            new PaymentsAmountInPaidAtWindowSpec(tenantId, clinicId, weekStart, weekEnd), ct);
        var monthAmounts = await _payments.ListAsync(
            new PaymentsAmountInPaidAtWindowSpec(tenantId, clinicId, monthStart, monthEnd), ct);

        var todayTotalPaid = todayAmounts.Sum();
        var weekTotalPaid = weekAmounts.Sum();
        var monthTotalPaid = monthAmounts.Sum();
        var todayPaymentsCount = todayAmounts.Count;
        var weekPaymentsCount = weekAmounts.Count;
        var monthPaymentsCount = monthAmounts.Count;

        var recentRows = await _payments.ListAsync(
            new PaymentsForDashboardRecentSpec(tenantId, clinicId, DashboardFinanceSummaryConstants.RecentPaymentsTake),
            ct);

        var clientIds = recentRows.Select(r => r.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0
            ? []
            : await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, clientIds), ct);
        var clientNameById = clients.ToDictionary(c => c.Id, c => c.FullName);

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

        var recentDtos = recentRows
            .Select(r => new DashboardFinanceRecentPaymentDto(
                r.Id,
                r.PaidAtUtc,
                r.ClientId,
                clientNameById.GetValueOrDefault(r.ClientId, string.Empty),
                r.PetId,
                r.PetId is { } pid ? petNameById.GetValueOrDefault(pid, string.Empty) : string.Empty,
                r.Amount,
                r.Currency,
                r.Method))
            .ToList();

        var dto = new DashboardFinanceSummaryDto(
            todayTotalPaid,
            weekTotalPaid,
            monthTotalPaid,
            todayPaymentsCount,
            weekPaymentsCount,
            monthPaymentsCount,
            recentDtos);

        return Result<DashboardFinanceSummaryDto>.Success(dto);
    }
}
