namespace Backend.Veteriner.Application.Dashboard.ReadModels;

public interface IDashboardFinanceReadModelReader
{
    Task<DashboardFinanceReadResult> GetAsync(
        DashboardFinanceReadRequest request,
        CancellationToken cancellationToken = default);
}
