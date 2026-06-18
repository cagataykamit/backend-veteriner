using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Queries.GetList;
using Backend.Veteriner.Application.Appointments.ReadModels;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Appointments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Projections.Appointments;

[Collection("appointment-projection")]
public sealed class AppointmentEventualConsistencyIntegrationTests
{
    private readonly AppointmentProjectionWebApplicationFactory _factory;

    public AppointmentEventualConsistencyIntegrationTests(AppointmentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Create_Should_BeImmediatelyVisibleInCommandDetail_BeforeProjection()
    {
        await ResetAsync();
        var (clinicId, petId) = await SeedPetAsync();
        var client = await CreateAuthenticatedClientAsync();
        var scheduledAt = SlotAlignedUtcPlusDays(2);

        var appointmentId = await CreateAppointmentViaApiAsync(client, clinicId, petId, scheduledAt);

        var detailResponse = await client.GetAsync($"/api/v1/appointments/{appointmentId}");
        detailResponse.EnsureSuccessStatusCode();
        var detail = (await detailResponse.Content.ReadFromJsonAsync<AppointmentDetailDto>())!;
        detail.Id.Should().Be(appointmentId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.AppointmentReadModels.CountAsync(x => x.AppointmentId == appointmentId)).Should().Be(0);

        var listQuery = new GetAppointmentsListQuery(
            new PageRequest { Page = 1, PageSize = 20 },
            clinicId);
        var listBeforeProjection = await InvokeListHandlerAsync(listQuery, appointmentsEnabled: true);
        listBeforeProjection.Value!.Items.Should().NotContain(i => i.Id == appointmentId);
    }

    [Fact]
    public async Task Create_Should_BecomeVisibleInQueryReadModels_AfterProjectorRuns()
    {
        await ResetAsync();
        var (clinicId, petId) = await SeedPetAsync();
        var client = await CreateAuthenticatedClientAsync();
        var scheduledAt = SlotAlignedUtcPlusDays(2);
        var appointmentId = await CreateAppointmentViaApiAsync(client, clinicId, petId, scheduledAt);

        await ProcessAllPendingAsync();

        await EventualConsistencyTestSupport.EventuallyAsync(
            async () =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
                return await queryDb.AppointmentReadModels.AnyAsync(x => x.AppointmentId == appointmentId);
            },
            timeout: TimeSpan.FromSeconds(10),
            pollInterval: TimeSpan.FromMilliseconds(200),
            because: $"Appointment {appointmentId} should appear in query read model after projection.");

        var listQuery = new GetAppointmentsListQuery(new PageRequest { Page = 1, PageSize = 20 }, clinicId);
        var list = await InvokeListHandlerAsync(listQuery, appointmentsEnabled: true);
        list.Value!.Items.Should().Contain(i => i.Id == appointmentId);
    }

    [Fact]
    public async Task Reschedule_Should_ShowNewTimeInCommandImmediately_AndInQueryAfterProjection()
    {
        await ResetAsync();
        var (clinicId, petId) = await SeedPetAsync();
        var client = await CreateAuthenticatedClientAsync();
        var original = SlotAlignedUtcPlusDays(2);
        var rescheduled = SlotAlignedUtcPlusDays(4);
        var appointmentId = await CreateAppointmentViaApiAsync(client, clinicId, petId, original);
        await ProcessAllPendingAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/appointments/{appointmentId}/reschedule", new
        {
            ScheduledAtUtc = rescheduled
        });
        response.EnsureSuccessStatusCode();

        var detailResponse = await client.GetAsync($"/api/v1/appointments/{appointmentId}");
        detailResponse.EnsureSuccessStatusCode();
        var detail = (await detailResponse.Content.ReadFromJsonAsync<AppointmentDetailDto>())!;
        detail.ScheduledAtUtc.Should().BeCloseTo(rescheduled, TimeSpan.FromSeconds(1));

        await ProcessAllPendingAsync();

        await EventualConsistencyTestSupport.EventuallyAsync(
            async () =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
                var row = await queryDb.AppointmentReadModels.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.AppointmentId == appointmentId);
                return row is not null && row.ScheduledAtUtc == rescheduled;
            },
            timeout: TimeSpan.FromSeconds(10),
            pollInterval: TimeSpan.FromMilliseconds(200),
            because: "Rescheduled time should propagate to query read model.");
    }

    [Fact]
    public async Task Cancel_Should_UpdateQueryStatusAfterProjection()
    {
        await ResetAsync();
        var (clinicId, petId) = await SeedPetAsync();
        var client = await CreateAuthenticatedClientAsync();
        var appointmentId = await CreateAppointmentViaApiAsync(client, clinicId, petId, SlotAlignedUtcPlusDays(2));
        await ProcessAllPendingAsync();

        var cancelResponse = await client.PostAsync($"/api/v1/appointments/{appointmentId}/cancel", null);
        cancelResponse.EnsureSuccessStatusCode();

        var cancelDetailResponse = await client.GetAsync($"/api/v1/appointments/{appointmentId}");
        cancelDetailResponse.EnsureSuccessStatusCode();
        (await cancelDetailResponse.Content.ReadFromJsonAsync<AppointmentDetailDto>())!.Status
            .Should().Be(AppointmentStatus.Cancelled);
        await ProcessAllPendingAsync();

        await EventualConsistencyTestSupport.EventuallyAsync(
            async () =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
                var row = await queryDb.AppointmentReadModels.AsNoTracking()
                    .SingleAsync(x => x.AppointmentId == appointmentId);
                return row.Status == (int)AppointmentStatus.Cancelled;
            },
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200),
            "Cancelled status should appear in query read model.");
    }

    [Fact]
    public async Task Complete_Should_UpdateQueryStatusAfterProjection()
    {
        await ResetAsync();
        var (clinicId, petId) = await SeedPetAsync();
        var client = await CreateAuthenticatedClientAsync();
        var appointmentId = await CreateAppointmentViaApiAsync(client, clinicId, petId, SlotAlignedUtcPlusDays(2));
        await ProcessAllPendingAsync();

        var completeResponse = await client.PostAsync($"/api/v1/appointments/{appointmentId}/complete", null);
        completeResponse.EnsureSuccessStatusCode();
        await ProcessAllPendingAsync();

        await EventualConsistencyTestSupport.EventuallyAsync(
            async () =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
                var row = await queryDb.AppointmentReadModels.AsNoTracking()
                    .SingleAsync(x => x.AppointmentId == appointmentId);
                return row.Status == (int)AppointmentStatus.Completed;
            },
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200),
            "Completed status should appear in query read model.");
    }

    [Fact]
    public async Task ProjectorDisabled_Should_LeaveOutboxPendingWithoutAutomaticProcessing()
    {
        await ResetAsync();
        var (clinicId, petId) = await SeedPetAsync();
        var client = await CreateAuthenticatedClientAsync();
        var appointmentId = await CreateAppointmentViaApiAsync(client, clinicId, petId, SlotAlignedUtcPlusDays(2));

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var statusReader = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionStatusReader>();

        (await commandDb.OutboxMessages.CountAsync(m => m.ProcessedAtUtc == null)).Should().BeGreaterThan(0);
        (await queryDb.AppointmentReadModels.CountAsync(x => x.AppointmentId == appointmentId)).Should().Be(0);

        var status = await statusReader.GetStatusAsync(CancellationToken.None);
        status.ProjectionEnabled.Should().BeFalse();
        status.PendingCount.Should().BeGreaterThan(0);

        var evaluation = AppointmentProjectionHealthEvaluator.Evaluate(
            status,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppointmentProjectionHealthOptions>>().Value,
            new QueryReadModelsOptions { AppointmentsEnabled = true, DashboardAppointmentsEnabled = false });
        evaluation.Level.Should().Be(AppointmentProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public async Task QueryDbUnavailable_Should_NotMarkOutboxProcessed_AndNotFallbackOnRead()
    {
        await ResetAsync();
        var (clinicId, petId) = await SeedPetAsync();
        var client = await CreateAuthenticatedClientAsync();
        var appointmentId = await CreateAppointmentViaApiAsync(client, clinicId, petId, SlotAlignedUtcPlusDays(2));

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outbox = await commandDb.OutboxMessages
            .Where(m => m.Type == AppointmentIntegrationEventTypes.Created)
            .SingleAsync();

        var brokenQueryOptions = new DbContextOptionsBuilder<QueryDbContext>()
            .UseSqlServer(DashboardQueryReadModelBrokenQueryWebApplicationFactory.InvalidQueryConnection)
            .Options;

        await using var brokenQueryDb = new QueryDbContext(brokenQueryOptions);
        var processor = new AppointmentProjectionProcessor(
            commandDb,
            brokenQueryDb,
            Options.Create(new AppointmentProjectionOptions { ConsumerName = AppointmentProjectionTestSupport.ConsumerName }),
            Options.Create(new OutboxOptions { MaxRetryCount = 8, BaseDelaySeconds = 5 }),
            TimeProvider.System,
            NullLogger<AppointmentProjectionProcessor>.Instance);

        var act = () => processor.ProcessBatchAsync(CancellationToken.None);
        await act.Should().ThrowAsync<Exception>();

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().BeNull();
        (outbox.RetryCount > 0 || outbox.NextAttemptAtUtc != null || !string.IsNullOrEmpty(outbox.LastError))
            .Should().BeTrue("failure should be recorded on outbox without marking processed");

        var listQuery = new GetAppointmentsListQuery(new PageRequest { Page = 1, PageSize = 20 }, clinicId);
        var list = await InvokeListHandlerAsync(listQuery, appointmentsEnabled: true);
        list.Value!.Items.Should().NotContain(i => i.Id == appointmentId);
    }

    [Fact]
    public async Task QueryDbRecovery_Should_ProcessPreviouslyFailedOutbox()
    {
        await ResetAsync();
        var (clinicId, petId) = await SeedPetAsync();
        var client = await CreateAuthenticatedClientAsync();
        var appointmentId = await CreateAppointmentViaApiAsync(client, clinicId, petId, SlotAlignedUtcPlusDays(2));

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        var outbox = await commandDb.OutboxMessages.SingleAsync(m => m.Type == AppointmentIntegrationEventTypes.Created);
        outbox.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1);
        outbox.RetryCount = 2;
        await commandDb.SaveChangesAsync();

        (await processor.ProcessBatchAsync(CancellationToken.None)).Should().Be(1);
        (await queryDb.AppointmentReadModels.CountAsync(x => x.AppointmentId == appointmentId)).Should().Be(1);
        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RestartIdempotency_Should_NotDoubleApply_WhenProcessedEventExistsButOutboxOpen()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var appointmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc));

        queryDb.AppointmentReadModels.Add(new AppointmentReadModel
        {
            AppointmentId = appointmentId,
            TenantId = snapshot.TenantId,
            ClinicId = snapshot.ClinicId,
            ClinicName = snapshot.ClinicName,
            PetId = snapshot.PetId,
            PetName = snapshot.PetName,
            SpeciesId = snapshot.SpeciesId,
            SpeciesName = snapshot.SpeciesName,
            ClientId = snapshot.ClientId,
            ClientName = snapshot.ClientName,
            ClientPhone = snapshot.ClientPhone,
            ScheduledAtUtc = snapshot.ScheduledAtUtc,
            ScheduledEndUtc = snapshot.ScheduledAtUtc.AddMinutes(snapshot.DurationMinutes),
            DurationMinutes = snapshot.DurationMinutes,
            AppointmentType = snapshot.AppointmentType,
            Status = snapshot.Status,
            Notes = snapshot.Notes,
            LastEventId = eventId,
            LastProjectedAtUtc = DateTime.UtcNow
        });
        await queryDb.SaveChangesAsync();
        await AppointmentProjectionTestSupport.MarkProcessedInQueryDbAsync(queryDb, eventId);

        await AppointmentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(eventId, DateTime.UtcNow, snapshot));

        (await processor.ProcessBatchAsync(CancellationToken.None)).Should().Be(1);
        (await queryDb.AppointmentReadModels.CountAsync(x => x.AppointmentId == appointmentId)).Should().Be(1);

        var outbox = await commandDb.OutboxMessages.SingleAsync();
        outbox.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task FeatureFlagRollback_Should_ReturnCommandResults_WhenQueryProjectionIncomplete()
    {
        await ResetAsync();
        var (clinicId, petId) = await SeedPetAsync();
        await SeedAppointmentDirectAsync(clinicId, petId, SlotAlignedUtcPlusDays(2));

        var listQuery = new GetAppointmentsListQuery(new PageRequest { Page = 1, PageSize = 20 }, clinicId);
        var commandList = await InvokeListHandlerAsync(listQuery, appointmentsEnabled: false);
        var queryList = await InvokeListHandlerAsync(listQuery, appointmentsEnabled: true);

        commandList.Value!.TotalItems.Should().BeGreaterThan(0);
        queryList.Value!.TotalItems.Should().Be(0);

        var rollbackList = await InvokeListHandlerAsync(listQuery, appointmentsEnabled: false);
        rollbackList.Value!.TotalItems.Should().Be(commandList.Value.TotalItems);
    }

    [Fact]
    public async Task RebuildThenLiveEvent_Should_IncludeLegacyAndNewRecords()
    {
        await ResetAsync();
        var (clinicId, petId) = await SeedPetAsync();
        var legacyId = await SeedAppointmentDirectAsync(clinicId, petId, SlotAlignedUtcPlusDays(1));

        await using var scope = _factory.Services.CreateAsyncScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionRebuildService>();
        (await rebuild.RebuildAsync(500, CancellationToken.None)).CommandAppointmentCount.Should().BeGreaterThan(0);

        var client = await CreateAuthenticatedClientAsync();
        var newId = await CreateAppointmentViaApiAsync(client, clinicId, petId, SlotAlignedUtcPlusDays(3));
        await ProcessAllPendingAsync();

        await EventualConsistencyTestSupport.EventuallyAsync(
            async () =>
            {
                await using var inner = _factory.Services.CreateAsyncScope();
                var queryDb = inner.ServiceProvider.GetRequiredService<QueryDbContext>();
                var count = await queryDb.AppointmentReadModels.CountAsync(x =>
                    x.AppointmentId == legacyId || x.AppointmentId == newId);
                return count == 2;
            },
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(250),
            "Rebuild legacy and live projected events should both exist in query DB.");
    }

    private async Task ResetAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await commandDb.Appointments.ExecuteDeleteAsync();
        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
    }

    private async Task ProcessAllPendingAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();
        for (var i = 0; i < 10; i++)
        {
            var processed = await processor.ProcessBatchAsync(CancellationToken.None);
            if (processed == 0)
                break;
        }
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, "admin@example.com", "123456");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private async Task<Guid> CreateAppointmentViaApiAsync(HttpClient client, Guid clinicId, Guid petId, DateTime scheduledAt)
    {
        var response = await client.PostAsJsonAsync("/api/v1/appointments", new
        {
            ClinicId = clinicId,
            PetId = petId,
            ScheduledAtUtc = scheduledAt,
            AppointmentType = AppointmentType.Consultation
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Guid>())!;
    }

    private async Task<(Guid ClinicId, Guid PetId)> SeedPetAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var clinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);
        var client = new Client(tenant.Id, $"ECClient-{Guid.NewGuid():N}"[..12], "905551110077");
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, $"ECPet-{Guid.NewGuid():N}"[..10], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();
        return (clinic.Id, pet.Id);
    }

    private async Task<Guid> SeedAppointmentDirectAsync(Guid clinicId, Guid petId, DateTime when)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = await db.Clinics.Where(c => c.Id == clinicId).Select(c => c.TenantId).SingleAsync();
        var appointment = new Appointment(tenantId, clinicId, petId, when, 30, AppointmentType.Consultation, AppointmentStatus.Scheduled);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<Backend.Veteriner.Domain.Shared.Result<PagedResult<AppointmentListItemDto>>> InvokeListHandlerAsync(
        GetAppointmentsListQuery query,
        bool appointmentsEnabled)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var tenantId = await db.Tenants.Where(t => t.Name == DataSeeder.DefaultTenantName).Select(t => t.Id).SingleAsync();
        var clinicId = await db.Clinics.Where(c => c.TenantId == tenantId && c.Name == DataSeeder.DefaultSeedClinicName)
            .Select(c => c.Id).SingleAsync();

        var handler = new GetAppointmentsListQueryHandler(
            new FixedTenantContext(tenantId),
            new FixedClinicContext(clinicId),
            sp.GetRequiredService<IReadRepository<Appointment>>(),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IReadRepository<Clinic>>(),
            sp.GetRequiredService<IAppointmentReadModelReader>(),
            Options.Create(new QueryReadModelsOptions { AppointmentsEnabled = appointmentsEnabled }),
            NullLogger<GetAppointmentsListQueryHandler>.Instance);

        return await handler.Handle(query, CancellationToken.None);
    }

    private static DateTime SlotAlignedUtcPlusDays(int days)
    {
        var date = DateTime.UtcNow.Date.AddDays(days);
        while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            date = date.AddDays(1);
        return date.AddHours(9);
    }
}

file sealed class FixedTenantContext(Guid tenantId) : ITenantContext
{
    public Guid? TenantId { get; } = tenantId;
}

file sealed class FixedClinicContext(Guid clinicId) : IClinicContext
{
    public Guid? ClinicId { get; } = clinicId;
}
