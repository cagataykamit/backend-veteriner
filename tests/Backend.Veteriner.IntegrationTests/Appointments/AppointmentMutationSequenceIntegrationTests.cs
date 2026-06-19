using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Appointments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Appointments;

[Collection("appointment-projection")]
public sealed class AppointmentMutationSequenceIntegrationTests : IClassFixture<AppointmentProjectionWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppointmentProjectionWebApplicationFactory _factory;

    public AppointmentMutationSequenceIntegrationTests(AppointmentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Create_Should_Persist_MutationSequence_One_And_OutboxMetadata()
    {
        await ResetDatabasesAsync();

        var (clinicId, petId) = await SeedPetAsync();
        var appointmentId = await CreateAppointmentViaApiAsync(clinicId, petId, SlotAlignedUtcPlusDays(2));

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var appointment = await commandDb.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        appointment.MutationSequence.Should().Be(1);

        var outbox = await commandDb.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Type == AppointmentIntegrationEventTypes.Created);

        outbox.AppointmentId.Should().Be(appointmentId);
        outbox.AppointmentSequence.Should().Be(1);

        var payload = JsonSerializer.Deserialize<AppointmentCreatedIntegrationEvent>(outbox.Payload, JsonOptions);
        payload!.AppointmentSequence.Should().Be(1);
        payload.AppointmentId.Should().Be(appointmentId);
    }

    [Fact]
    public async Task RescheduleThenCancel_Should_Produce_Strictly_Increasing_Sequences()
    {
        await ResetDatabasesAsync();

        var (clinicId, petId) = await SeedPetAsync();
        var appointmentId = await CreateAppointmentViaApiAsync(clinicId, petId, SlotAlignedUtcPlusDays(2));
        var client = await CreateAuthenticatedClientAsync();

        var rescheduleAt = SlotAlignedUtcPlusDays(4);
        (await client.PostAsJsonAsync($"/api/v1/appointments/{appointmentId}/reschedule", new { ScheduledAtUtc = rescheduleAt }))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/v1/appointments/{appointmentId}/cancel", new { Reason = "seq test" }))
            .EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var appointment = await commandDb.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        appointment.MutationSequence.Should().Be(3);

        var sequences = await commandDb.OutboxMessages.AsNoTracking()
            .Where(m => m.AppointmentId == appointmentId)
            .OrderBy(m => m.AppointmentSequence)
            .Select(m => m.AppointmentSequence)
            .ToListAsync();

        sequences.Should().Equal(1L, 2L, 3L);
    }

    [Fact]
    public async Task ConcurrentReschedule_WithStaleSequence_Should_LeaveSingleOutboxMessage()
    {
        await ResetDatabasesAsync();

        var (clinicId, petId) = await SeedPetAsync();
        var appointmentId = await CreateAppointmentViaApiAsync(clinicId, petId, SlotAlignedUtcPlusDays(2));

        var winnerTime = SlotAlignedUtcPlusDays(5);
        var loserTime = SlotAlignedUtcPlusDays(6);

        await using var scope1 = _factory.Services.CreateAsyncScope();
        await using var scope2 = _factory.Services.CreateAsyncScope();

        var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var snapshotFactory1 = scope1.ServiceProvider.GetRequiredService<IAppointmentProjectionSnapshotFactory>();
        var snapshotFactory2 = scope2.ServiceProvider.GetRequiredService<IAppointmentProjectionSnapshotFactory>();
        var eventOutbox1 = scope1.ServiceProvider.GetRequiredService<IAppointmentIntegrationEventOutbox>();
        var eventOutbox2 = scope2.ServiceProvider.GetRequiredService<IAppointmentIntegrationEventOutbox>();

        var appt1 = await db1.Appointments.SingleAsync(a => a.Id == appointmentId);
        var appt2 = await db2.Appointments.SingleAsync(a => a.Id == appointmentId);
        appt1.MutationSequence.Should().Be(1);
        appt2.MutationSequence.Should().Be(1);

        var previous1 = await snapshotFactory1.CreateAsync(appt1, CancellationToken.None);
        appt1.RescheduleTo(winnerTime).IsSuccess.Should().BeTrue();
        var current1 = snapshotFactory1.CreateScalarsFromPrevious(appt1, previous1);
        await eventOutbox1.EnqueueAsync(
            AppointmentIntegrationEventTypes.Rescheduled,
            new AppointmentRescheduledIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, appt1.MutationSequence, previous1, current1));
        await db1.SaveChangesAsync();

        var previous2 = await snapshotFactory2.CreateAsync(appt2, CancellationToken.None);
        appt2.RescheduleTo(loserTime).IsSuccess.Should().BeTrue();
        var current2 = snapshotFactory2.CreateScalarsFromPrevious(appt2, previous2);
        await eventOutbox2.EnqueueAsync(
            AppointmentIntegrationEventTypes.Rescheduled,
            new AppointmentRescheduledIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, appt2.MutationSequence, previous2, current2));

        var act = () => db2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var commandDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var appointment = await commandDb.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        appointment.MutationSequence.Should().Be(2);
        appointment.ScheduledAtUtc.Should().BeCloseTo(winnerTime, TimeSpan.FromSeconds(1));

        var outboxRows = await commandDb.OutboxMessages.AsNoTracking()
            .Where(m => m.AppointmentId == appointmentId && m.Type == AppointmentIntegrationEventTypes.Rescheduled)
            .ToListAsync();

        outboxRows.Should().ContainSingle();
        outboxRows[0].AppointmentSequence.Should().Be(2);
    }

    [Fact]
    public async Task HistoricalProcessedOutbox_WithNullAppointmentMetadata_Should_RemainReadable()
    {
        await ResetDatabasesAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var legacy = new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = """{"eventId":"11111111-1111-1111-1111-111111111111","occurredAtUtc":"2026-01-01T00:00:00Z","current":{}}""",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            ProcessedAtUtc = DateTime.UtcNow.AddDays(-1),
            AppointmentId = null,
            AppointmentSequence = null
        };
        commandDb.OutboxMessages.Add(legacy);
        await commandDb.SaveChangesAsync();

        var loaded = await commandDb.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == legacy.Id);
        loaded.AppointmentId.Should().BeNull();
        loaded.AppointmentSequence.Should().BeNull();
        loaded.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task NonAppointmentOutboxMessage_Should_Keep_Null_AppointmentMetadata()
    {
        await ResetDatabasesAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var buffer = scope.ServiceProvider.GetRequiredService<OutboxBuffer>();

        await buffer.EnqueueAsync("email", """{"to":"a@b.com"}""");
        await commandDb.SaveChangesAsync();

        var email = await commandDb.OutboxMessages.AsNoTracking().SingleAsync(m => m.Type == "email");
        email.AppointmentId.Should().BeNull();
        email.AppointmentSequence.Should().BeNull();
    }

    [Fact]
    public async Task Migration_Should_Add_MutationSequence_With_DefaultZero_Backfill()
    {
        await ResetDatabasesAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (clinicId, petId) = await SeedPetAsync();
        var tenantId = await commandDb.Clinics.Where(c => c.Id == clinicId).Select(c => c.TenantId).SingleAsync();

        var legacyAppointment = new Appointment(
            tenantId,
            clinicId,
            petId,
            SlotAlignedUtcPlusDays(10),
            30,
            AppointmentType.Consultation,
            AppointmentStatus.Scheduled);
        commandDb.Appointments.Add(legacyAppointment);
        await commandDb.SaveChangesAsync();

        var sequence = await commandDb.Appointments.AsNoTracking()
            .Where(a => a.Id == legacyAppointment.Id)
            .Select(a => a.MutationSequence)
            .SingleAsync();
        sequence.Should().Be(0);
    }

    [Fact]
    public async Task Duplicate_AppointmentId_And_Sequence_Should_Be_Rejected_By_Unique_Index()
    {
        await ResetDatabasesAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var appointmentId = Guid.NewGuid();
        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            AppointmentId = appointmentId,
            AppointmentSequence = 1
        });
        await commandDb.SaveChangesAsync();

        commandDb.OutboxMessages.Add(new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Updated,
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            AppointmentId = appointmentId,
            AppointmentSequence = 1
        });

        var act = () => commandDb.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private async Task<Guid> CreateAppointmentViaApiAsync(Guid clinicId, Guid petId, DateTime scheduledAt)
    {
        var client = await CreateAuthenticatedClientAsync();
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

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, "admin@example.com", "123456");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private async Task ResetDatabasesAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await AppointmentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await commandDb.Appointments.ExecuteDeleteAsync();
        await AppointmentProjectionTestSupport.ResetQuerySideAsync(queryDb);
    }

    private async Task<(Guid ClinicId, Guid PetId)> SeedPetAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var clinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);
        var client = new Client(tenant.Id, $"SeqClient-{Guid.NewGuid():N}"[..14], "905551110077");
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, $"SeqPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();
        return (clinic.Id, pet.Id);
    }

    private static DateTime SlotAlignedUtcPlusDays(int days)
    {
        var date = DateTime.UtcNow.Date.AddDays(days);
        while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            date = date.AddDays(1);
        return date.AddHours(9);
    }
}
