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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetFinanceSummary;

public sealed class GetDashboardFinanceSummaryQueryHandler
    : IRequestHandler<GetDashboardFinanceSummaryQuery, Result<DashboardFinanceSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly ILogger<GetDashboardFinanceSummaryQueryHandler> _logger;

    public GetDashboardFinanceSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Payment> payments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        ILogger<GetDashboardFinanceSummaryQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _payments = payments;
        _clients = clients;
        _pets = pets;
        _logger = logger ?? NullLogger<GetDashboardFinanceSummaryQueryHandler>.Instance;
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

        var monthPayments = await _payments.ListAsync(
            new PaymentsPaidAtAmountInWindowSpec(tenantId, clinicId, monthStart, monthEnd), ct);
        MarkStep("monthPaymentsScanSource");

        decimal todayTotalPaid = 0m;
        decimal weekTotalPaid = 0m;
        decimal monthTotalPaid = 0m;
        var todayPaymentsCount = 0;
        var weekPaymentsCount = 0;
        var monthPaymentsCount = 0;

        foreach (var payment in monthPayments)
        {
            monthTotalPaid += payment.Amount;
            monthPaymentsCount++;

            if (payment.PaidAtUtc >= weekStart && payment.PaidAtUtc < weekEnd)
            {
                weekTotalPaid += payment.Amount;
                weekPaymentsCount++;
            }

            if (payment.PaidAtUtc >= dayStart && payment.PaidAtUtc < dayEnd)
            {
                todayTotalPaid += payment.Amount;
                todayPaymentsCount++;
            }
        }

        var recentRows = await _payments.ListAsync(
            new PaymentsForDashboardRecentSpec(tenantId, clinicId, DashboardFinanceSummaryConstants.RecentPaymentsTake),
            ct);
        MarkStep("recentPayments");

        var clientIds = recentRows.Select(r => r.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0
            ? []
            : await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, clientIds), ct);
        MarkStep("recentClientsLookup");
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
            MarkStep("recentPetsLookup");
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

        _logger.LogInformation(
            "Dashboard finance summary generated. TenantId={TenantId} ClinicId={ClinicId} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} MonthPaymentsScanned={MonthPaymentsScanned} RecentPayments={RecentPayments} TotalElapsedMs={TotalElapsedMs}",
            tenantId,
            clinicId,
            querySteps,
            slowestStep,
            slowestMs,
            monthPaymentsCount,
            recentDtos.Count,
            totalSw.ElapsedMilliseconds);

        return Result<DashboardFinanceSummaryDto>.Success(dto);
    }
}
