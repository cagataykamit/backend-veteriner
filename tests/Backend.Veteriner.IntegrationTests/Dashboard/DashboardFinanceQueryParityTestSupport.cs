using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.Queries.GetFinanceSummary;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Application.Dashboard.ReadModels;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Dashboard;

internal static class DashboardFinanceQueryParityTestSupport
{
    internal static DateTime TodayWithinOperationalWindow()
    {
        var (dayStart, dayEnd) = OperationDayBounds.ForUtcNow(DateTime.UtcNow);
        var when = dayStart.AddHours(2);
        return when < dayEnd ? when : dayStart.AddMinutes(30);
    }

    internal static async Task ResetAsync(IServiceProvider rootServices)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await commandDb.Payments.ExecuteDeleteAsync();
        await queryDb.PaymentDailyContributionReadModels.ExecuteDeleteAsync();
        await queryDb.ClinicDailyPaymentStatsReadModels.ExecuteDeleteAsync();
    }

    internal static async Task<(DashboardFinanceSummaryDto Command, DashboardFinanceSummaryDto Query)> CompareFinancePathsAsync(
        IServiceProvider rootServices,
        Guid tenantId,
        Guid? clinicId)
    {
        var commandResult = await InvokeFinanceHandlerAsync(rootServices, dashboardFinanceReadEnabled: false, tenantId, clinicId);
        var queryResult = await InvokeFinanceHandlerAsync(rootServices, dashboardFinanceReadEnabled: true, tenantId, clinicId);

        commandResult.IsSuccess.Should().BeTrue();
        queryResult.IsSuccess.Should().BeTrue();

        return (commandResult.Value!, queryResult.Value!);
    }

    internal static async Task<Backend.Veteriner.Domain.Shared.Result<DashboardFinanceSummaryDto>> InvokeFinanceHandlerAsync(
        IServiceProvider rootServices,
        bool dashboardFinanceReadEnabled,
        Guid tenantId,
        Guid? clinicId)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var handler = new GetDashboardFinanceSummaryQueryHandler(
            new FixedTenantContext(tenantId),
            clinicId is { } id ? new FixedClinicContext(id) : new NullClinicContext(),
            sp.GetRequiredService<IReadRepository<Payment>>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IDashboardFinancePaymentAggregatesReader>(),
            sp.GetRequiredService<IDashboardFinanceReadModelReader>(),
            Options.Create(new QueryReadModelsOptions { DashboardFinanceReadEnabled = dashboardFinanceReadEnabled }));

        return await handler.Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);
    }
}
