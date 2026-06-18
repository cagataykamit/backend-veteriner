using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Appointments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Appointments;

[Collection("appointment-projection")]
public sealed class AppointmentCommandOutboxIntegrationTests : IClassFixture<AppointmentProjectionWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppointmentProjectionWebApplicationFactory _factory;

    public AppointmentCommandOutboxIntegrationTests(AppointmentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task CreateCommand_Should_PersistAppointmentAndCreatedOutboxMessage()
    {
        await ResetDatabasesAsync();

        var (clinicId, petId) = await SeedPetAsync();
        var client = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, "admin@example.com", "123456");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var scheduledAt = SlotAlignedUtcPlusDays(2);
        var response = await client.PostAsJsonAsync("/api/v1/appointments", new
        {
            ClinicId = clinicId,
            PetId = petId,
            ScheduledAtUtc = scheduledAt,
            AppointmentType = AppointmentType.Consultation,
            Notes = "integration create"
        });

        response.EnsureSuccessStatusCode();
        var appointmentId = await response.Content.ReadFromJsonAsync<Guid>();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var appointment = await commandDb.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        appointment.Notes.Should().Be("integration create");

        var outbox = await commandDb.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == AppointmentIntegrationEventTypes.Created)
            .SingleAsync();

        var payload = JsonSerializer.Deserialize<AppointmentCreatedIntegrationEvent>(outbox.Payload, JsonOptions);
        payload.Should().NotBeNull();
        payload!.Current.AppointmentId.Should().Be(appointmentId);
        payload.Current.ScheduledAtUtc.Should().BeCloseTo(scheduledAt, TimeSpan.FromSeconds(1));
        payload.Current.Notes.Should().Be("integration create");
    }

    [Fact]
    public async Task UpdateCommand_Should_PersistUpdatedOutboxMessage()
    {
        await ResetDatabasesAsync();

        var (clinicId, petId) = await SeedPetAsync();
        var appointmentId = await SeedScheduledAppointmentAsync(clinicId, petId);
        var client = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, "admin@example.com", "123456");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var newWhen = SlotAlignedUtcPlusDays(5);
        var response = await client.PutAsJsonAsync($"/api/v1/appointments/{appointmentId}", new
        {
            PetId = petId,
            ClinicId = clinicId,
            ScheduledAtUtc = newWhen,
            AppointmentType = AppointmentType.Vaccination,
            Status = AppointmentStatus.Scheduled,
            Notes = "integration update"
        });
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outbox = await commandDb.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Type == AppointmentIntegrationEventTypes.Updated);

        var payload = JsonSerializer.Deserialize<AppointmentUpdatedIntegrationEvent>(outbox.Payload, JsonOptions);
        payload!.Previous.ScheduledAtUtc.Should().NotBe(payload.Current.ScheduledAtUtc);
        payload.Current.ScheduledAtUtc.Should().BeCloseTo(newWhen, TimeSpan.FromSeconds(1));
        payload.Current.AppointmentType.Should().Be((int)AppointmentType.Vaccination);
        payload.Current.Notes.Should().Be("integration update");
    }

    [Fact]
    public async Task RescheduleCommand_Should_PersistRescheduledOutboxMessage()
    {
        await ResetDatabasesAsync();
        var testStartedAtUtc = DateTime.UtcNow;

        var (clinicId, petId) = await SeedPetAsync();
        var appointmentId = await SeedScheduledAppointmentAsync(clinicId, petId);

        DateTime originalScheduledAtUtc;
        await using (var readScope = _factory.Services.CreateAsyncScope())
        {
            var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
            originalScheduledAtUtc = await readDb.Appointments.AsNoTracking()
                .Where(a => a.Id == appointmentId)
                .Select(a => a.ScheduledAtUtc)
                .SingleAsync();
        }

        var requestedScheduledAtUtc = DistinctSlotAlignedUtc(originalScheduledAtUtc, startDaysOffset: 7);
        requestedScheduledAtUtc.Should().NotBe(originalScheduledAtUtc);

        var client = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, "admin@example.com", "123456");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync($"/api/v1/appointments/{appointmentId}/reschedule", new
        {
            ScheduledAtUtc = requestedScheduledAtUtc
        });
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxMessages = await commandDb.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == AppointmentIntegrationEventTypes.Rescheduled && m.CreatedAtUtc >= testStartedAtUtc)
            .OrderByDescending(m => m.CreatedAtUtc)
            .ToListAsync();

        AppointmentRescheduledIntegrationEvent? payload = null;
        foreach (var message in outboxMessages)
        {
            var candidate = JsonSerializer.Deserialize<AppointmentRescheduledIntegrationEvent>(message.Payload, JsonOptions);
            if (candidate?.Current.AppointmentId == appointmentId)
            {
                payload = candidate;
                break;
            }
        }

        payload.Should().NotBeNull();
        payload!.Previous.ScheduledAtUtc.Should().NotBe(payload.Current.ScheduledAtUtc);
        payload.Previous.ScheduledAtUtc.Should().BeCloseTo(originalScheduledAtUtc, TimeSpan.FromSeconds(1));
        payload.Current.ScheduledAtUtc.Should().BeCloseTo(requestedScheduledAtUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CancelCommand_Should_PersistCancelledOutboxMessage()
    {
        await ResetDatabasesAsync();

        var (clinicId, petId) = await SeedPetAsync();
        var appointmentId = await SeedScheduledAppointmentAsync(clinicId, petId);
        var client = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, "admin@example.com", "123456");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync($"/api/v1/appointments/{appointmentId}/cancel", new
        {
            Reason = "integration cancel"
        });
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outbox = await commandDb.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Type == AppointmentIntegrationEventTypes.Cancelled);

        var payload = JsonSerializer.Deserialize<AppointmentCancelledIntegrationEvent>(outbox.Payload, JsonOptions);
        payload!.Previous.Status.Should().Be((int)AppointmentStatus.Scheduled);
        payload.Current.Status.Should().Be((int)AppointmentStatus.Cancelled);
        payload.Current.Notes.Should().Contain("integration cancel");
    }

    [Fact]
    public async Task CompleteCommand_Should_PersistCompletedOutboxMessage()
    {
        await ResetDatabasesAsync();

        var (clinicId, petId) = await SeedPetAsync();
        var appointmentId = await SeedScheduledAppointmentAsync(clinicId, petId);
        var client = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, "admin@example.com", "123456");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsync($"/api/v1/appointments/{appointmentId}/complete", null);
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outbox = await commandDb.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Type == AppointmentIntegrationEventTypes.Completed);

        var payload = JsonSerializer.Deserialize<AppointmentCompletedIntegrationEvent>(outbox.Payload, JsonOptions);
        payload!.Current.Status.Should().Be((int)AppointmentStatus.Completed);
    }

    [Fact]
    public async Task SaveChangesRollback_Should_NotPersistAppointmentOrOutboxTogether()
    {
        await ResetDatabasesAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var buffer = scope.ServiceProvider.GetRequiredService<OutboxBuffer>();

        var (clinicId, petId) = await SeedPetAsync();
        var tenantId = await commandDb.Clinics.Where(c => c.Id == clinicId).Select(c => c.TenantId).SingleAsync();

        await using var transaction = await commandDb.Database.BeginTransactionAsync();
        var appointment = new Appointment(
            tenantId,
            clinicId,
            petId,
            SlotAlignedUtcPlusDays(2),
            30,
            AppointmentType.Consultation,
            AppointmentStatus.Scheduled,
            "rollback test");
        commandDb.Appointments.Add(appointment);

        var snapshot = AppointmentProjectionTestSupport.CreateSnapshot(
            appointment.Id,
            tenantId,
            clinicId,
            petId,
            Guid.NewGuid(),
            appointment.ScheduledAtUtc);
        var json = JsonSerializer.Serialize(
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, snapshot),
            JsonOptions);
        await buffer.EnqueueAsync(AppointmentIntegrationEventTypes.Created, json);

        var batch = buffer.Drain();
        foreach (var item in batch)
        {
            commandDb.OutboxMessages.Add(new OutboxMessage
            {
                Type = item.Type,
                Payload = item.Payload,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await transaction.RollbackAsync();
        commandDb.ChangeTracker.Clear();

        (await commandDb.Appointments.CountAsync()).Should().Be(0);
        var appointmentEventTypes = AppointmentIntegrationEventTypes.All;
        (await commandDb.OutboxMessages.CountAsync(m => appointmentEventTypes.Contains(m.Type))).Should().Be(0);
    }

    [Fact]
    public async Task CreateCommand_Should_ProjectToQueryDb_WhenProcessorRuns()
    {
        await ResetDatabasesAsync();

        var (clinicId, petId) = await SeedPetAsync();
        var client = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, "admin@example.com", "123456");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var scheduledAt = SlotAlignedUtcPlusDays(3);
        var response = await client.PostAsJsonAsync("/api/v1/appointments", new
        {
            ClinicId = clinicId,
            PetId = petId,
            ScheduledAtUtc = scheduledAt,
            AppointmentType = AppointmentType.Consultation
        });
        response.EnsureSuccessStatusCode();
        var appointmentId = await response.Content.ReadFromJsonAsync<Guid>();

        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();

        (await commandDb.OutboxMessages.CountAsync(m => m.Type == AppointmentIntegrationEventTypes.Created)).Should().Be(1);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1);

        (await queryDb.AppointmentReadModels.CountAsync(x => x.AppointmentId == appointmentId)).Should().Be(1);
        (await queryDb.ClinicPetActivityReadModels.CountAsync()).Should().BeGreaterThan(0);
        (await queryDb.ClinicDailyAppointmentStatsReadModels.CountAsync()).Should().BeGreaterThan(0);

        var outbox = await commandDb.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Type == AppointmentIntegrationEventTypes.Created);
        outbox.ProcessedAtUtc.Should().NotBeNull();
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
        var client = new Client(tenant.Id, $"OutboxClient-{Guid.NewGuid():N}"[..14], "905551110066");
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, $"OutboxPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();
        return (clinic.Id, pet.Id);
    }

    private async Task<Guid> SeedScheduledAppointmentAsync(Guid clinicId, Guid petId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = await db.Clinics.Where(c => c.Id == clinicId).Select(c => c.TenantId).SingleAsync();
        var appointment = new Appointment(
            tenantId,
            clinicId,
            petId,
            SlotAlignedUtcPlusDays(2),
            30,
            AppointmentType.Consultation,
            AppointmentStatus.Scheduled);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private static DateTime SlotAlignedUtcPlusDays(int days)
    {
        var date = DateTime.UtcNow.Date.AddDays(days);
        while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            date = date.AddDays(1);
        return date.AddHours(9);
    }

    private static DateTime DistinctSlotAlignedUtc(DateTime avoid, int startDaysOffset)
    {
        for (var days = startDaysOffset; days <= startDaysOffset + 30; days++)
        {
            var candidate = SlotAlignedUtcPlusDays(days);
            if (candidate != avoid)
                return candidate;
        }

        throw new InvalidOperationException("Distinct reschedule slot could not be resolved for integration test.");
    }
}
