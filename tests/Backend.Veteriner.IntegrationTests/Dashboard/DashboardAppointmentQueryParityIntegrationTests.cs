using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Dashboard;

[Collection("dashboard-query-read")]
public sealed class DashboardAppointmentQueryParityIntegrationTests : IClassFixture<DashboardQueryReadModelWebApplicationFactory>
{
    private readonly DashboardQueryReadModelWebApplicationFactory _factory;

    public DashboardAppointmentQueryParityIntegrationTests(DashboardQueryReadModelWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Summary_ClinicScoped_Should_MatchCommandPath_AfterRebuild()
    {
        await DashboardQueryParityTestSupport.ResetAppointmentProjectionAsync(_factory.Services);
        var (tenantId, clinicId, petId, _) = await DashboardQueryParityTestSupport.SeedSingleClinicScenarioAsync(_factory.Services);
        var today = DashboardQueryParityTestSupport.TodayWithinOperationalWindow();
        var future = SlotAlignedUtcPlusDays(2);

        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, tenantId, clinicId, petId, today, AppointmentStatus.Scheduled);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, tenantId, clinicId, petId, today, AppointmentStatus.Completed);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, tenantId, clinicId, petId, today, AppointmentStatus.Cancelled);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, tenantId, clinicId, petId, future, AppointmentStatus.Scheduled);

        await DashboardQueryParityTestSupport.RebuildAsync(_factory.Services);

        var (command, query) = await DashboardQueryParityTestSupport.CompareSummaryPathsAsync(
            _factory.Services, tenantId, clinicId);
        AssertFullParity(command, query);
    }

    [Fact]
    public async Task Summary_LastSevenDays_Should_MatchCommandPath()
    {
        await DashboardQueryParityTestSupport.ResetAppointmentProjectionAsync(_factory.Services);
        var (tenantId, clinicId, petId, _) = await DashboardQueryParityTestSupport.SeedSingleClinicScenarioAsync(_factory.Services);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, tenantId, clinicId, petId, SlotAlignedUtcPlusDays(-2), AppointmentStatus.Scheduled);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, tenantId, clinicId, petId, SlotAlignedUtcPlusDays(-1), AppointmentStatus.Completed);
        await DashboardQueryParityTestSupport.RebuildAsync(_factory.Services);

        var (command, query) = await DashboardQueryParityTestSupport.CompareSummaryPathsAsync(
            _factory.Services, tenantId, clinicId);

        query.Last7DaysAppointments.Should().BeEquivalentTo(
            command.Last7DaysAppointments,
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Summary_RecentPetsAndClients_Should_MatchCommandPath()
    {
        await DashboardQueryParityTestSupport.ResetAppointmentProjectionAsync(_factory.Services);
        var (tenantId, clinicId, petId, _) = await DashboardQueryParityTestSupport.SeedSingleClinicScenarioAsync(_factory.Services);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, tenantId, clinicId, petId, SlotAlignedUtcPlusDays(-3), AppointmentStatus.Completed);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, tenantId, clinicId, petId, SlotAlignedUtcPlusDays(-1), AppointmentStatus.Scheduled);
        await DashboardQueryParityTestSupport.RebuildAsync(_factory.Services);

        var (command, query) = await DashboardQueryParityTestSupport.CompareSummaryPathsAsync(
            _factory.Services, tenantId, clinicId);

        query.RecentPets.Should().BeEquivalentTo(command.RecentPets, options => options.WithStrictOrdering());
        query.RecentClients.Should().BeEquivalentTo(command.RecentClients, options => options.WithStrictOrdering());
        query.TotalPetsCount.Should().Be(command.TotalPetsCount);
        query.TotalClientsCount.Should().Be(command.TotalClientsCount);
    }

    [Fact]
    public async Task Summary_WhenProjectionMissing_Should_NotFallbackToCommandDb()
    {
        await DashboardQueryParityTestSupport.ResetAppointmentProjectionAsync(_factory.Services);
        var (tenantId, clinicId, petId, _) = await DashboardQueryParityTestSupport.SeedSingleClinicScenarioAsync(_factory.Services);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, tenantId, clinicId, petId, DashboardQueryParityTestSupport.TodayWithinOperationalWindow(), AppointmentStatus.Scheduled);
        await DashboardQueryParityTestSupport.RebuildAsync(_factory.Services);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
            await queryDb.AppointmentReadModels.ExecuteDeleteAsync();
            await queryDb.ClinicDailyAppointmentStatsReadModels.ExecuteDeleteAsync();
        }

        var query = await DashboardQueryParityTestSupport.InvokeSummaryHandlerAsync(
            _factory.Services, dashboardEnabled: true, tenantId, clinicId);
        var command = await DashboardQueryParityTestSupport.InvokeSummaryHandlerAsync(
            _factory.Services, dashboardEnabled: false, tenantId, clinicId);

        query.Value!.TodayAppointmentsCount.Should().Be(0);
        command.Value!.TodayAppointmentsCount.Should().BeGreaterThan(0);
    }

    private static void AssertFullParity(DashboardSummaryDto command, DashboardSummaryDto query)
    {
        DashboardQueryParityTestSupport.AssertAppointmentDerivedParity(command, query);
        query.TotalPetsCount.Should().Be(command.TotalPetsCount);
        query.TotalClientsCount.Should().Be(command.TotalClientsCount);
        query.RecentPets.Should().BeEquivalentTo(command.RecentPets, options => options.WithStrictOrdering());
        query.RecentClients.Should().BeEquivalentTo(command.RecentClients, options => options.WithStrictOrdering());
    }

    private static DateTime SlotAlignedUtcPlusDays(int days)
    {
        var date = DateTime.UtcNow.Date.AddDays(days);
        while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            date = date.AddDays(days > 0 ? 1 : -1);
        return date.AddHours(10);
    }
}

[CollectionDefinition("dashboard-query-read", DisableParallelization = true)]
public sealed class DashboardQueryReadCollection : ICollectionFixture<DashboardQueryReadModelWebApplicationFactory>;
