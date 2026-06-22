using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;

namespace Backend.Veteriner.Application.Dashboard.ReadModels;

public interface IDashboardRecentPaymentsReadModelReader
{
    Task<IReadOnlyList<DashboardFinanceRecentPaymentDto>> GetRecentAsync(
        DashboardRecentPaymentsReadRequest request,
        CancellationToken cancellationToken = default);
}
