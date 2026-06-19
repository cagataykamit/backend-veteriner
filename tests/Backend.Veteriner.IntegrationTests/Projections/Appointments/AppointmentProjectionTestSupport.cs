using System.Text.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.IntegrationTests.Projections.Appointments;

internal static class AppointmentProjectionTestSupport
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public const string ConsumerName = "appointment-read-model-v1";

    public static async Task ResetQuerySideAsync(QueryDbContext queryDb, CancellationToken ct = default)
        => await IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb, ct);

    public static async Task ClearOutboxAsync(AppDbContext commandDb, CancellationToken ct = default)
    {
        await commandDb.OutboxMessages.ExecuteDeleteAsync(ct);
    }

    public static async Task ResetHealthBaselineAsync(
        AppDbContext commandDb,
        QueryDbContext queryDb,
        CancellationToken ct = default)
    {
        await ClearOutboxAsync(commandDb, ct);
        await ResetQuerySideAsync(queryDb, ct);
    }

    public static async Task<OutboxMessage> EnqueueIntegrationEventAsync(
        AppDbContext commandDb,
        string eventType,
        object integrationEvent,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), Json);
        var message = new OutboxMessage
        {
            Type = eventType,
            Payload = payload,
            CreatedAtUtc = DateTime.UtcNow,
            // CQRS-11D drain-first: tarihî / test harness enqueue metadata null kalır.
            AppointmentId = null,
            AppointmentSequence = null
        };

        commandDb.OutboxMessages.Add(message);
        await commandDb.SaveChangesAsync(ct);
        return message;
    }

    public static AppointmentProjectionSnapshot CreateSnapshot(
        Guid appointmentId,
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid clientId,
        DateTime scheduledAtUtc,
        int status = (int)AppointmentStatus.Scheduled,
        int durationMinutes = 30,
        string clinicName = "Test Klinik",
        string petName = "Boncuk",
        string clientName = "Test Musteri",
        string? petBreed = null,
        string? petBreedRefName = null,
        string? clientEmail = null,
        string? clientPhoneNormalized = "+905551112233")
        => new(
            appointmentId,
            tenantId,
            clinicId,
            clinicName,
            petId,
            petName,
            Guid.NewGuid(),
            "Kopek",
            clientId,
            clientName,
            "+905551112233",
            scheduledAtUtc,
            durationMinutes,
            0,
            status,
            "Test notu",
            petBreed,
            petBreedRefName,
            clientEmail,
            clientPhoneNormalized);

    public static AppointmentProjectionSnapshot WithClinic(
        AppointmentProjectionSnapshot source,
        Guid clinicId,
        string clinicName)
        => source with { ClinicId = clinicId, ClinicName = clinicName };

    public static AppointmentProjectionSnapshot WithPet(
        AppointmentProjectionSnapshot source,
        Guid petId,
        string petName)
        => source with { PetId = petId, PetName = petName };

    public static AppointmentProjectionSnapshot WithClient(
        AppointmentProjectionSnapshot source,
        Guid clientId,
        string clientName)
        => source with { ClientId = clientId, ClientName = clientName };

    public static AppointmentProjectionSnapshot WithSchedule(
        AppointmentProjectionSnapshot source,
        DateTime scheduledAtUtc,
        int status = (int)AppointmentStatus.Scheduled)
        => source with { ScheduledAtUtc = scheduledAtUtc, Status = status };

    public static async Task<(Guid TenantId, Guid ClinicId, Guid PetId, Guid ClientId)> SeedIdsAsync(
        AppDbContext commandDb,
        CancellationToken ct = default)
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        // Projection testleri read-model odaklı; command tarafında FK zorunluluğu yok (manuel outbox).
        _ = tenantId;
        _ = clinicId;
        _ = petId;
        _ = clientId;

        await Task.CompletedTask;
        return (tenantId, clinicId, petId, clientId);
    }

    public static DateOnly LocalDateForUtc(DateTime scheduledAtUtc)
        => OperationDayBounds.ToLocalDate(scheduledAtUtc);

    public static async Task MarkProcessedInQueryDbAsync(
        QueryDbContext queryDb,
        Guid eventId,
        CancellationToken ct = default)
    {
        queryDb.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = eventId,
            ConsumerName = ConsumerName,
            ProcessedAtUtc = DateTime.UtcNow
        });
        await queryDb.SaveChangesAsync(ct);
    }
}
