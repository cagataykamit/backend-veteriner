using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.Queries.GetOperationalAlerts;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Dashboard;

[Collection("dashboard-query-read")]
public sealed class DashboardTenantWideParityIntegrationTests : IClassFixture<DashboardQueryReadModelWebApplicationFactory>
{
    private readonly DashboardQueryReadModelWebApplicationFactory _factory;

    public DashboardTenantWideParityIntegrationTests(DashboardQueryReadModelWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Summary_TenantWide_TodayStatus_Should_MatchCommandPath_AfterRebuild()
    {
        await DashboardQueryParityTestSupport.ResetAppointmentProjectionAsync(_factory.Services);
        var scenario = await DashboardQueryParityTestSupport.SeedTwoClinicScenarioAsync(_factory.Services);
        var today = DashboardQueryParityTestSupport.TodayWithinOperationalWindow();

        foreach (var clinicId in new[] { scenario.ClinicAId, scenario.ClinicBId })
        {
            var petId = clinicId == scenario.ClinicAId ? scenario.PetAId : scenario.PetBId;
            await DashboardQueryParityTestSupport.SeedAppointmentAsync(
                _factory.Services, scenario.TenantId, clinicId, petId, today, AppointmentStatus.Scheduled);
            await DashboardQueryParityTestSupport.SeedAppointmentAsync(
                _factory.Services, scenario.TenantId, clinicId, petId, today, AppointmentStatus.Completed);
            await DashboardQueryParityTestSupport.SeedAppointmentAsync(
                _factory.Services, scenario.TenantId, clinicId, petId, today, AppointmentStatus.Cancelled);
        }

        await DashboardQueryParityTestSupport.RebuildAsync(_factory.Services);

        var (command, query) = await DashboardQueryParityTestSupport.CompareSummaryPathsAsync(
            _factory.Services, scenario.TenantId, clinicId: null);

        command.TodayAppointmentsCount.Should().Be(2);
        command.CompletedTodayCount.Should().Be(2);
        command.CancelledTodayCount.Should().Be(2);
        DashboardQueryParityTestSupport.AssertAppointmentDerivedParity(command, query);
    }

    [Fact]
    public async Task Summary_TenantWide_Upcoming_Should_MatchCommandPath_CountListSortLimitAndTieBreak()
    {
        await DashboardQueryParityTestSupport.ResetAppointmentProjectionAsync(_factory.Services);
        var scenario = await DashboardQueryParityTestSupport.SeedTwoClinicScenarioAsync(_factory.Services);

        var futureA = DashboardQueryParityTestSupport.HoursFromUtcNow(48);
        var futureB = DashboardQueryParityTestSupport.HoursFromUtcNow(72);
        var tieBreakTime = DashboardQueryParityTestSupport.HoursFromUtcNow(96);
        var pastScheduled = DashboardQueryParityTestSupport.DayOffsetFromUtcNow(-2, hoursFromDayStart: 10);
        var futureCancelled = DashboardQueryParityTestSupport.HoursFromUtcNow(50);
        var futureCompleted = DashboardQueryParityTestSupport.HoursFromUtcNow(52);

        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, scenario.TenantId, scenario.ClinicAId, scenario.PetAId, futureA, AppointmentStatus.Scheduled);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, scenario.TenantId, scenario.ClinicBId, scenario.PetBId, futureB, AppointmentStatus.Scheduled);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, scenario.TenantId, scenario.ClinicAId, scenario.PetAId, tieBreakTime, AppointmentStatus.Scheduled);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, scenario.TenantId, scenario.ClinicBId, scenario.PetBId, tieBreakTime, AppointmentStatus.Scheduled);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, scenario.TenantId, scenario.ClinicAId, scenario.PetAId, pastScheduled, AppointmentStatus.Scheduled);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, scenario.TenantId, scenario.ClinicBId, scenario.PetBId, futureCancelled, AppointmentStatus.Cancelled);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, scenario.TenantId, scenario.ClinicAId, scenario.PetAId, futureCompleted, AppointmentStatus.Completed);

        await DashboardQueryParityTestSupport.RebuildAsync(_factory.Services);

        var (command, query) = await DashboardQueryParityTestSupport.CompareSummaryPathsAsync(
            _factory.Services, scenario.TenantId, clinicId: null);

        command.UpcomingAppointmentsCount.Should().BeGreaterThanOrEqualTo(4);
        query.UpcomingAppointmentsCount.Should().Be(command.UpcomingAppointmentsCount);
        query.UpcomingAppointments.Should().BeEquivalentTo(command.UpcomingAppointments, options => options.WithStrictOrdering());
        query.UpcomingAppointments.Should().HaveCountLessThanOrEqualTo(20);
        query.UpcomingAppointments.Should().OnlyContain(a => a.Status == AppointmentStatus.Scheduled);

        var tieBreakIds = query.UpcomingAppointments
            .Where(a => a.ScheduledAtUtc == tieBreakTime)
            .Select(a => a.Id)
            .ToList();
        tieBreakIds.Should().HaveCount(2);
        tieBreakIds.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Summary_TenantWide_LastSevenDays_Should_MatchCommandPath_IncludingBoundaryAndZeroFill()
    {
        await DashboardQueryParityTestSupport.ResetAppointmentProjectionAsync(_factory.Services);
        var scenario = await DashboardQueryParityTestSupport.SeedTwoClinicScenarioAsync(_factory.Services);
        var buckets = OperationPeriodBounds.Last7DaysForUtcNow(DateTime.UtcNow);
        var statuses = new[]
        {
            AppointmentStatus.Scheduled,
            AppointmentStatus.Completed,
            AppointmentStatus.Cancelled,
            AppointmentStatus.Scheduled,
            AppointmentStatus.Completed,
            AppointmentStatus.Cancelled,
            AppointmentStatus.Scheduled
        };

        for (var i = 0; i < buckets.Count; i++)
        {
            if (i % 2 == 1)
                continue;

            var when = buckets[i].StartUtcInclusive.AddHours(10);
            await DashboardQueryParityTestSupport.SeedAppointmentAsync(
                _factory.Services, scenario.TenantId, scenario.ClinicAId, scenario.PetAId, when, statuses[i]);

            if (i % 4 == 0)
            {
                await DashboardQueryParityTestSupport.SeedAppointmentAsync(
                    _factory.Services,
                    scenario.TenantId,
                    scenario.ClinicBId,
                    scenario.PetBId,
                    buckets[i].StartUtcInclusive.AddMinutes(30),
                    AppointmentStatus.Completed);
            }
        }

        var boundaryWhen = buckets[3].StartUtcInclusive.AddMinutes(-1);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, scenario.TenantId, scenario.ClinicAId, scenario.PetAId, boundaryWhen, AppointmentStatus.Scheduled);

        await DashboardQueryParityTestSupport.RebuildAsync(_factory.Services);

        var (command, query) = await DashboardQueryParityTestSupport.CompareSummaryPathsAsync(
            _factory.Services, scenario.TenantId, clinicId: null);

        query.Last7DaysAppointments.Should().HaveCount(7);
        query.Last7DaysAppointments.Select(x => x.Date).Should().BeInAscendingOrder();
        query.Last7DaysAppointments.Should().BeEquivalentTo(command.Last7DaysAppointments, options => options.WithStrictOrdering());
        query.Last7DaysAppointments.Where(x => x.Count == 0).Should().NotBeEmpty();
        command.Last7DaysAppointments.Where(x => x.Count == 0).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Summary_TenantWide_WhenFlagTrue_MasterDataFields_Should_StillComeFromCommandDb()
    {
        await DashboardQueryParityTestSupport.ResetAppointmentProjectionAsync(_factory.Services);
        var scenario = await DashboardQueryParityTestSupport.SeedTwoClinicScenarioAsync(_factory.Services);
        var today = DashboardQueryParityTestSupport.TodayWithinOperationalWindow();

        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, scenario.TenantId, scenario.ClinicAId, scenario.PetAId, today, AppointmentStatus.Scheduled);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, scenario.TenantId, scenario.ClinicBId, scenario.PetBId, today, AppointmentStatus.Completed);

        await DashboardQueryParityTestSupport.RebuildAsync(_factory.Services);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expectedClients = await db.Clients.CountAsync(c => c.TenantId == scenario.TenantId);
        var expectedPets = await db.Pets.CountAsync(p => p.TenantId == scenario.TenantId);

        var (command, query) = await DashboardQueryParityTestSupport.CompareSummaryPathsAsync(
            _factory.Services, scenario.TenantId, clinicId: null);

        query.TotalClientsCount.Should().Be(expectedClients);
        query.TotalPetsCount.Should().Be(expectedPets);
        query.TotalClientsCount.Should().Be(command.TotalClientsCount);
        query.TotalPetsCount.Should().Be(command.TotalPetsCount);
        query.RecentClients.Should().BeEquivalentTo(command.RecentClients, options => options.WithStrictOrdering());
        query.RecentPets.Should().BeEquivalentTo(command.RecentPets, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Summary_TenantWide_WhenProjectionPartiallyMissing_Should_NotFallbackToCommandDb()
    {
        await DashboardQueryParityTestSupport.ResetAppointmentProjectionAsync(_factory.Services);
        var scenario = await DashboardQueryParityTestSupport.SeedTwoClinicScenarioAsync(_factory.Services);
        var today = DashboardQueryParityTestSupport.TodayWithinOperationalWindow();

        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, scenario.TenantId, scenario.ClinicAId, scenario.PetAId, today, AppointmentStatus.Scheduled);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, scenario.TenantId, scenario.ClinicBId, scenario.PetBId, today, AppointmentStatus.Scheduled);

        await DashboardQueryParityTestSupport.RebuildAsync(_factory.Services);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
            await queryDb.ClinicDailyAppointmentStatsReadModels
                .Where(x => x.TenantId == scenario.TenantId && x.ClinicId == scenario.ClinicBId)
                .ExecuteDeleteAsync();
        }

        var command = await DashboardQueryParityTestSupport.InvokeSummaryHandlerAsync(
            _factory.Services, dashboardEnabled: false, scenario.TenantId, clinicId: null);
        var query = await DashboardQueryParityTestSupport.InvokeSummaryHandlerAsync(
            _factory.Services, dashboardEnabled: true, scenario.TenantId, clinicId: null);

        command.IsSuccess.Should().BeTrue();
        query.IsSuccess.Should().BeTrue();
        command.Value!.TodayAppointmentsCount.Should().Be(2);
        query.Value!.TodayAppointmentsCount.Should().Be(1);
    }
}

[Collection("dashboard-query-broken")]
public sealed class DashboardQueryFailureParityIntegrationTests : IClassFixture<DashboardQueryReadModelBrokenQueryWebApplicationFactory>
{
    private readonly DashboardQueryReadModelBrokenQueryWebApplicationFactory _factory;

    public DashboardQueryFailureParityIntegrationTests(DashboardQueryReadModelBrokenQueryWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Summary_WhenQueryDbUnreachableAndFlagTrue_Should_FailWithoutCommandFallback()
    {
        var (tenantId, _, _, _) = await DashboardQueryParityTestSupport.SeedSingleClinicScenarioAsync(_factory.Services);

        var act = () => DashboardQueryParityTestSupport.InvokeSummaryHandlerAsync(
            _factory.Services, dashboardEnabled: true, tenantId, clinicId: null);

        var exception = await act.Should().ThrowAsync<Exception>();
        exception.Which.Should().NotBeNull();
    }

    [Fact]
    public async Task Summary_WhenQueryDbUnreachableAndFlagFalse_Should_SucceedViaCommandDb()
    {
        await DashboardQueryParityTestSupport.ResetCommandAppointmentsAsync(_factory.Services);
        var (tenantId, clinicId, petId, _) = await DashboardQueryParityTestSupport.SeedSingleClinicScenarioAsync(_factory.Services);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services,
            tenantId,
            clinicId,
            petId,
            DashboardQueryParityTestSupport.TodayWithinOperationalWindow(),
            AppointmentStatus.Scheduled);

        var result = await DashboardQueryParityTestSupport.InvokeSummaryHandlerAsync(
            _factory.Services, dashboardEnabled: false, tenantId, clinicId: null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TodayAppointmentsCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task OperationalAlerts_WhenDashboardFlagTrueAndQueryDbUnreachable_Should_KeepCommandDbBehavior()
    {
        await DashboardQueryParityTestSupport.ResetCommandAppointmentsAsync(_factory.Services);
        var (tenantId, clinicId, petId, _) = await DashboardQueryParityTestSupport.SeedSingleClinicScenarioAsync(_factory.Services);
        var today = DashboardQueryParityTestSupport.TodayWithinOperationalWindow();

        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services, tenantId, clinicId, petId, today, AppointmentStatus.Cancelled);
        await DashboardQueryParityTestSupport.SeedAppointmentAsync(
            _factory.Services,
            tenantId,
            clinicId,
            petId,
            DashboardQueryParityTestSupport.HoursFromUtcNow(-2),
            AppointmentStatus.Scheduled);

        var alerts = await DashboardOperationalAlertsTestInvoker.InvokeOperationalAlertsAsync(
            _factory, tenantId, clinicId: null);
        alerts.IsSuccess.Should().BeTrue();
        alerts.Value!.TodayCancelledAppointmentsCount.Should().BeGreaterThan(0);
        alerts.Value.OverdueScheduledAppointmentsCount.Should().BeGreaterThan(0);

        var summaryAct = () => DashboardQueryParityTestSupport.InvokeSummaryHandlerAsync(
            _factory.Services, dashboardEnabled: true, tenantId, clinicId: null);
        await summaryAct.Should().ThrowAsync<Exception>();
    }
}

[CollectionDefinition("dashboard-query-broken", DisableParallelization = true)]
public sealed class DashboardQueryBrokenCollection : ICollectionFixture<DashboardQueryReadModelBrokenQueryWebApplicationFactory>;

file static class DashboardOperationalAlertsTestInvoker
{
    internal static async Task<Backend.Veteriner.Domain.Shared.Result<DashboardOperationalAlertsDto>> InvokeOperationalAlertsAsync(
        DashboardQueryReadModelBrokenQueryWebApplicationFactory factory,
        Guid tenantId,
        Guid? clinicId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var handler = new GetDashboardOperationalAlertsQueryHandler(
            new FixedTenantContext(tenantId),
            clinicId is { } id ? new FixedClinicContext(id) : new NullClinicContext(),
            sp.GetRequiredService<IReadRepository<Appointment>>(),
            sp.GetRequiredService<IReadRepository<Backend.Veteriner.Domain.Vaccinations.Vaccination>>(),
            sp.GetRequiredService<IDashboardTodayAppointmentStatusCountsReader>());

        return await handler.Handle(new GetDashboardOperationalAlertsQuery(), CancellationToken.None);
    }
}
