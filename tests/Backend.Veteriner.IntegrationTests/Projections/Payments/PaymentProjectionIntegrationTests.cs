using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Payments;

[CollectionDefinition("payment-projection", DisableParallelization = true)]
public sealed class PaymentProjectionCollection : ICollectionFixture<PaymentProjectionWebApplicationFactory>;

[Collection("payment-projection")]
public sealed class PaymentProjectionIntegrationTests
{
    private readonly PaymentProjectionWebApplicationFactory _factory;

    public PaymentProjectionIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task CreatedEvent_Should_CreateContribution_And_UpdateDailyStats()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc);
        var paidAtUtc = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var localDate = OperationDayBounds.ToLocalDate(paidAtUtc);
        var snapshot = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 250m, paidAtUtc: paidAtUtc);

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(eventId, occurredAtUtc, snapshot));

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1);

        var contribution = await queryDb.PaymentDailyContributionReadModels.SingleAsync(x => x.PaymentId == paymentId);
        contribution.TenantId.Should().Be(tenantId);
        contribution.ClinicId.Should().Be(clinicId);
        contribution.LocalDate.Should().Be(localDate);
        contribution.Amount.Should().Be(250m);
        contribution.LastEventId.Should().Be(eventId);
        contribution.LastEventOccurredAtUtc.Should().Be(occurredAtUtc);

        var stats = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicId, localDate, "TRY");
        stats.Should().NotBeNull();
        stats!.PaidTotalAmount.Should().Be(250m);
        stats.PaidCount.Should().Be(1);

        (await queryDb.ProcessedProjectionEvents.CountAsync(
            x => x.EventId == eventId && x.ConsumerName == PaymentProjectionTestSupport.ConsumerName))
            .Should().Be(1);
    }

    [Fact]
    public async Task UpdatedEvent_AmountChange_Should_RecomputeDailyTotal()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var paidAtUtc = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var localDate = OperationDayBounds.ToLocalDate(paidAtUtc);

        var created = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 100m, paidAtUtc: paidAtUtc);
        var updated = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 150m, paidAtUtc: paidAtUtc);

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(
                Guid.NewGuid(), new DateTime(2026, 6, 19, 9, 0, 0, DateTimeKind.Utc), created));
        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Updated,
            new PaymentUpdatedIntegrationEvent(
                Guid.NewGuid(), new DateTime(2026, 6, 19, 11, 0, 0, DateTimeKind.Utc), updated));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var stats = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicId, localDate, "TRY");
        stats!.PaidTotalAmount.Should().Be(150m, "increment değil recompute; 100+150 olmamalı");
        stats.PaidCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdatedEvent_DateChange_Should_MoveBetweenDailyBuckets()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var oldPaidAtUtc = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var newPaidAtUtc = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
        var oldLocalDate = OperationDayBounds.ToLocalDate(oldPaidAtUtc);
        var newLocalDate = OperationDayBounds.ToLocalDate(newPaidAtUtc);

        var created = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 200m, paidAtUtc: oldPaidAtUtc);
        var updated = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 200m, paidAtUtc: newPaidAtUtc);

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(
                Guid.NewGuid(), new DateTime(2026, 6, 19, 9, 0, 0, DateTimeKind.Utc), created));
        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Updated,
            new PaymentUpdatedIntegrationEvent(
                Guid.NewGuid(), new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc), updated));

        await processor.ProcessBatchAsync(CancellationToken.None);

        (await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicId, oldLocalDate, "TRY")).Should().BeNull("eski gün recompute sonrası boş kalmalı");
        var newStats = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicId, newLocalDate, "TRY");
        newStats!.PaidTotalAmount.Should().Be(200m);
        newStats.PaidCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdatedEvent_ClinicChange_Should_RecomputeBothClinicBuckets()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var paidAtUtc = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var localDate = OperationDayBounds.ToLocalDate(paidAtUtc);

        var created = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicA, amount: 300m, paidAtUtc: paidAtUtc);
        var updated = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicB, amount: 300m, paidAtUtc: paidAtUtc);

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(
                Guid.NewGuid(), new DateTime(2026, 6, 19, 9, 0, 0, DateTimeKind.Utc), created));
        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Updated,
            new PaymentUpdatedIntegrationEvent(
                Guid.NewGuid(), new DateTime(2026, 6, 19, 11, 0, 0, DateTimeKind.Utc), updated));

        await processor.ProcessBatchAsync(CancellationToken.None);

        (await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicA, localDate, "TRY")).Should().BeNull();
        var clinicBStats = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicB, localDate, "TRY");
        clinicBStats!.PaidTotalAmount.Should().Be(300m);
        clinicBStats.PaidCount.Should().Be(1);
    }

    [Fact]
    public async Task DuplicateEvent_Should_BeIdempotent()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc);
        var snapshot = PaymentProjectionTestSupport.CreateSnapshot(paymentId, tenantId, clinicId, amount: 100m);
        var integrationEvent = new PaymentCreatedIntegrationEvent(eventId, occurredAtUtc, snapshot);

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, PaymentIntegrationEventTypes.Created, integrationEvent);
        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, PaymentIntegrationEventTypes.Created, integrationEvent);

        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.PaymentDailyContributionReadModels.CountAsync(x => x.PaymentId == paymentId)).Should().Be(1);
        (await queryDb.ProcessedProjectionEvents.CountAsync(x => x.EventId == eventId)).Should().Be(1);

        var paidAtUtc = snapshot.PaidAtUtc;
        var localDate = OperationDayBounds.ToLocalDate(paidAtUtc);
        var stats = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicId, localDate, "TRY");
        stats!.PaidTotalAmount.Should().Be(100m);
        stats.PaidCount.Should().Be(1);
    }

    [Fact]
    public async Task StaleEvent_Should_NotOverwriteNewerContributionAndDailyStats()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var paidAtUtc = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var localDate = OperationDayBounds.ToLocalDate(paidAtUtc);
        var newerAt = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var olderAt = new DateTime(2026, 6, 19, 8, 0, 0, DateTimeKind.Utc);

        var newerSnap = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 500m, paidAtUtc: paidAtUtc);
        var olderSnap = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 50m, paidAtUtc: paidAtUtc);

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Updated,
            new PaymentUpdatedIntegrationEvent(Guid.NewGuid(), newerAt, newerSnap));
        var staleOutbox = await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Updated,
            new PaymentUpdatedIntegrationEvent(Guid.NewGuid(), olderAt, olderSnap));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var contribution = await queryDb.PaymentDailyContributionReadModels.SingleAsync(x => x.PaymentId == paymentId);
        contribution.Amount.Should().Be(500m);
        contribution.LastEventOccurredAtUtc.Should().Be(newerAt);

        var stats = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicId, localDate, "TRY");
        stats!.PaidTotalAmount.Should().Be(500m);

        await commandDb.Entry(staleOutbox).ReloadAsync();
        staleOutbox.ProcessedAtUtc.Should().NotBeNull();
        staleOutbox.DeadLetterAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task UnknownPaymentEventType_Should_NotBeClaimed_And_RemainUntouched()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var unknown = await PaymentProjectionTestSupport.EnqueueRawAsync(
            commandDb, "payment.unknown.v1", """{"paymentId":"x"}""");

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        await commandDb.Entry(unknown).ReloadAsync();
        unknown.ProcessedAtUtc.Should().BeNull();
        unknown.DeadLetterAtUtc.Should().BeNull();
        unknown.RetryCount.Should().Be(0);
        (await queryDb.PaymentDailyContributionReadModels.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InvalidJson_Should_Retry_And_NotMarkOutboxProcessed()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var outbox = await PaymentProjectionTestSupport.EnqueueRawAsync(
            commandDb, PaymentIntegrationEventTypes.Created, "{ invalid json");

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        (await queryDb.PaymentDailyContributionReadModels.CountAsync()).Should().Be(0);
        (await queryDb.ProcessedProjectionEvents.CountAsync()).Should().Be(0);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().BeNull();
        outbox.RetryCount.Should().Be(1);
        outbox.LastError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TenantIsolation_Should_KeepDailyStatsSeparate()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var paidAtUtc = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var localDate = OperationDayBounds.ToLocalDate(paidAtUtc);

        var snapA = PaymentProjectionTestSupport.CreateSnapshot(
            Guid.NewGuid(), tenantA, clinicId, amount: 100m, paidAtUtc: paidAtUtc);
        var snapB = PaymentProjectionTestSupport.CreateSnapshot(
            Guid.NewGuid(), tenantB, clinicId, amount: 200m, paidAtUtc: paidAtUtc);

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, snapA));
        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, snapB));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var statsA = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantA, clinicId, localDate, "TRY");
        var statsB = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantB, clinicId, localDate, "TRY");

        statsA!.PaidTotalAmount.Should().Be(100m);
        statsB!.PaidTotalAmount.Should().Be(200m);
    }

    [Fact]
    public async Task CreatedEvent_Should_UpsertPaymentReadModel_WithMappedFields()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var examinationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc);
        var paidAtUtc = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

        var snapshot = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId,
            amount: 250m, currency: "TRY", paidAtUtc: paidAtUtc,
            clientId: clientId, petId: petId,
            clientName: "Ada Lovelace", petName: "Rex", notes: "Aşı bedeli",
            method: 2, appointmentId: appointmentId, examinationId: examinationId);

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(eventId, occurredAtUtc, snapshot));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var row = await PaymentProjectionTestSupport.FindReadModelAsync(queryDb, paymentId);
        row.Should().NotBeNull();
        row!.TenantId.Should().Be(tenantId);
        row.ClinicId.Should().Be(clinicId);
        row.ClientId.Should().Be(clientId);
        row.ClientName.Should().Be("Ada Lovelace");
        row.ClientNameNormalized.Should().Be("ada lovelace");
        row.PetId.Should().Be(petId);
        row.PetName.Should().Be("Rex");
        row.PetNameNormalized.Should().Be("rex");
        row.Amount.Should().Be(250m);
        row.Currency.Should().Be("TRY");
        row.Method.Should().Be(2);
        row.PaidAtUtc.Should().Be(paidAtUtc);
        row.Notes.Should().Be("Aşı bedeli");
        row.NotesNormalized.Should().Be("aşı bedeli");
        row.AppointmentId.Should().Be(appointmentId);
        row.ExaminationId.Should().Be(examinationId);
        row.LastEventId.Should().Be(eventId);
        row.LastEventOccurredAtUtc.Should().Be(occurredAtUtc);
    }

    [Fact]
    public async Task UpdatedEvent_Should_OverwritePaymentReadModel()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paidAtUtc = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

        var created = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 100m, paidAtUtc: paidAtUtc,
            clientId: clientId, clientName: "Ada Lovelace", petName: "Rex", notes: "ilk");
        var updated = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 175m, paidAtUtc: paidAtUtc,
            clientId: clientId, clientName: "Grace Hopper", petName: null, notes: "guncel", method: 3);

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(
                Guid.NewGuid(), new DateTime(2026, 6, 19, 9, 0, 0, DateTimeKind.Utc), created));
        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Updated,
            new PaymentUpdatedIntegrationEvent(
                Guid.NewGuid(), new DateTime(2026, 6, 19, 11, 0, 0, DateTimeKind.Utc), updated));

        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.PaymentReadModels.CountAsync(x => x.PaymentId == paymentId)).Should().Be(1);
        var row = await PaymentProjectionTestSupport.FindReadModelAsync(queryDb, paymentId);
        row!.ClientName.Should().Be("Grace Hopper");
        row.ClientNameNormalized.Should().Be("grace hopper");
        row.PetName.Should().BeNull();
        row.PetNameNormalized.Should().BeNull();
        row.Amount.Should().Be(175m);
        row.Method.Should().Be(3);
        row.Notes.Should().Be("guncel");
    }

    [Fact]
    public async Task DuplicateEvent_Should_NotDuplicatePaymentReadModel()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc);
        var snapshot = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 100m, clientName: "Ada Lovelace");
        var integrationEvent = new PaymentCreatedIntegrationEvent(eventId, occurredAtUtc, snapshot);

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, PaymentIntegrationEventTypes.Created, integrationEvent);
        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, PaymentIntegrationEventTypes.Created, integrationEvent);

        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.PaymentReadModels.CountAsync(x => x.PaymentId == paymentId)).Should().Be(1);
    }

    [Fact]
    public async Task NullablePet_Should_ProjectNullPetFields()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var snapshot = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, Guid.NewGuid(), Guid.NewGuid(),
            clientName: "Ada Lovelace", petId: null, petName: null);

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, snapshot));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var row = await PaymentProjectionTestSupport.FindReadModelAsync(queryDb, paymentId);
        row.Should().NotBeNull();
        row!.PetId.Should().BeNull();
        row.PetName.Should().BeNull();
        row.PetNameNormalized.Should().BeNull();
        row.ClientName.Should().Be("Ada Lovelace");
    }

    [Fact]
    public async Task LegacyPayload_MissingEnrichedFields_Should_NotThrow_And_FallbackEmptyClientName()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paidAtUtc = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

        // 14C öncesi payload: enrichment alanları yok.
        var legacy = new
        {
            eventId = Guid.NewGuid(),
            occurredAtUtc = new DateTime(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc),
            current = new
            {
                paymentId,
                tenantId,
                clinicId,
                clientId,
                petId = (Guid?)null,
                appointmentId = (Guid?)null,
                examinationId = (Guid?)null,
                amount = 100m,
                currency = "TRY",
                method = 1,
                paidAtUtc,
                schemaVersion = 1
            }
        };
        var payload = System.Text.Json.JsonSerializer.Serialize(legacy, PaymentProjectionTestSupport.Json);

        await PaymentProjectionTestSupport.EnqueueRawAsync(
            commandDb, PaymentIntegrationEventTypes.Created, payload);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1);

        var row = await PaymentProjectionTestSupport.FindReadModelAsync(queryDb, paymentId);
        row.Should().NotBeNull();
        row!.ClientName.Should().BeEmpty();
        row.ClientNameNormalized.Should().BeEmpty();
        row.PetName.Should().BeNull();
        row.PetNameNormalized.Should().BeNull();
        row.Notes.Should().BeNull();
        row.Amount.Should().Be(100m);

        // Finance projection eski payload ile regresyona girmemeli.
        var stats = await PaymentProjectionTestSupport.FindDailyStatsAsync(
            queryDb, tenantId, clinicId, OperationDayBounds.ToLocalDate(paidAtUtc), "TRY");
        stats!.PaidTotalAmount.Should().Be(100m);
    }

    [Fact]
    public async Task StaleEvent_Should_NotOverwritePaymentReadModel()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paidAtUtc = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var newerAt = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var olderAt = new DateTime(2026, 6, 19, 8, 0, 0, DateTimeKind.Utc);

        var newerSnap = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 500m, paidAtUtc: paidAtUtc,
            clientId: clientId, clientName: "Grace Hopper");
        var olderSnap = PaymentProjectionTestSupport.CreateSnapshot(
            paymentId, tenantId, clinicId, amount: 50m, paidAtUtc: paidAtUtc,
            clientId: clientId, clientName: "Stale Name");

        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Updated,
            new PaymentUpdatedIntegrationEvent(Guid.NewGuid(), newerAt, newerSnap));
        await PaymentProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PaymentIntegrationEventTypes.Updated,
            new PaymentUpdatedIntegrationEvent(Guid.NewGuid(), olderAt, olderSnap));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var row = await PaymentProjectionTestSupport.FindReadModelAsync(queryDb, paymentId);
        row!.ClientName.Should().Be("Grace Hopper", "stale event read-model'i ezmemeli");
        row.Amount.Should().Be(500m);
        row.LastEventOccurredAtUtc.Should().Be(newerAt);
    }

    [Fact]
    public async Task NonPaymentOutboxMessage_Should_NotBeConsumedByPaymentProcessor()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();

        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);

        var email = await PaymentProjectionTestSupport.EnqueueRawAsync(
            commandDb,
            OutboxMessageTypes.Email,
            """{"To":"test@example.com","Subject":"x","Body":"y"}""");

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        await commandDb.Entry(email).ReloadAsync();
        email.ProcessedAtUtc.Should().BeNull();
    }
}
