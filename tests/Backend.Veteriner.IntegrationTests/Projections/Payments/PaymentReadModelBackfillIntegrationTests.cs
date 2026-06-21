using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Payments;

[Collection("payment-projection")]
public sealed class PaymentReadModelBackfillIntegrationTests
{
    private readonly PaymentProjectionWebApplicationFactory _factory;

    public PaymentReadModelBackfillIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Backfill_WithEmptyQueryDb_Should_InsertReadModels_AndBeInSync()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

        await SeedPaymentAsync(tenantId, clinicId, 100m, paidAt);
        await SeedPaymentAsync(tenantId, clinicId, 150m, paidAt);

        var result = await RunBackfillAsync(tenantId);

        result.Success.Should().BeTrue();
        result.CommandPaymentCount.Should().Be(2);
        result.QueryReadModelCount.Should().Be(2);
        result.InsertedCount.Should().Be(2);
        result.UpdatedCount.Should().Be(0);
        result.ParityInSync.Should().BeTrue();
    }

    [Fact]
    public async Task Backfill_Should_MapAllFields_FromCommandTruth_WithNormalizedNames()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 20, 9, 30, 0, DateTimeKind.Utc);

        var paymentId = await SeedCustomPaymentAsync(
            tenantId, clinicId,
            clientName: "  Ada Lovelace  ",
            petName: "  Lord Byron  ",
            amount: 275.50m,
            currency: "TRY",
            method: PaymentMethod.Card,
            paidAtUtc: paidAt,
            notes: "  Yıllık aşı  ");

        await RunBackfillAsync(tenantId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var rm = await PaymentProjectionTestSupport.FindReadModelAsync(queryDb, paymentId);

        rm.Should().NotBeNull();
        rm!.TenantId.Should().Be(tenantId);
        rm.ClinicId.Should().Be(clinicId);
        rm.ClientName.Should().Be("Ada Lovelace");
        rm.ClientNameNormalized.Should().Be("ada lovelace");
        rm.PetName.Should().Be("Lord Byron");
        rm.PetNameNormalized.Should().Be("lord byron");
        rm.Amount.Should().Be(275.50m);
        rm.Currency.Should().Be("TRY");
        rm.Method.Should().Be((int)PaymentMethod.Card);
        rm.PaidAtUtc.Should().Be(paidAt);
        rm.Notes.Should().Be("Yıllık aşı");
        rm.NotesNormalized.Should().Be("yıllık aşı");
        rm.LastEventId.Should().Be(PaymentReadModelBackfillService.BackfillEventId);
        rm.LastEventOccurredAtUtc.Should().Be(PaymentReadModelBackfillPlanner.BackfillBaselineOccurredAtUtc);
    }

    [Fact]
    public async Task Backfill_RunTwice_Should_BeIdempotent_WithoutDuplicates()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);
        await SeedPaymentAsync(tenantId, clinicId, 200m, paidAt);

        var first = await RunBackfillAsync(tenantId);
        var second = await RunBackfillAsync(tenantId);

        first.InsertedCount.Should().Be(1);
        second.InsertedCount.Should().Be(0);
        second.UpdatedCount.Should().Be(1);
        second.ParityInSync.Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.PaymentReadModels.CountAsync(x => x.TenantId == tenantId)).Should().Be(1);
    }

    [Fact]
    public async Task Backfill_Should_UpdateExistingRow_WithCommandTruth()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 22, 8, 0, 0, DateTimeKind.Utc);
        var paymentId = await SeedCustomPaymentAsync(
            tenantId, clinicId, "Grace Hopper", petName: null, amount: 500m,
            currency: "TRY", method: PaymentMethod.Cash, paidAtUtc: paidAt, notes: null);

        // Pre-existing stale read-model row (backfill sentinel ordering → Update beklenir).
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var queryDb = seedScope.ServiceProvider.GetRequiredService<QueryDbContext>();
            queryDb.PaymentReadModels.Add(new PaymentReadModel
            {
                PaymentId = paymentId,
                TenantId = tenantId,
                ClinicId = clinicId,
                ClientId = Guid.NewGuid(),
                ClientName = "STALE",
                ClientNameNormalized = "stale",
                Amount = 1m,
                Currency = "USD",
                Method = (int)PaymentMethod.Card,
                PaidAtUtc = paidAt.AddDays(-5),
                LastEventId = PaymentReadModelBackfillService.BackfillEventId,
                LastEventOccurredAtUtc = PaymentReadModelBackfillPlanner.BackfillBaselineOccurredAtUtc,
                LastProjectedAtUtc = DateTime.UtcNow
            });
            await queryDb.SaveChangesAsync();
        }

        var result = await RunBackfillAsync(tenantId);
        result.UpdatedCount.Should().BeGreaterThanOrEqualTo(1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var verifyDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var rm = await PaymentProjectionTestSupport.FindReadModelAsync(verifyDb, paymentId);
        rm!.ClientName.Should().Be("Grace Hopper");
        rm.Amount.Should().Be(500m);
        rm.Currency.Should().Be("TRY");
    }

    [Fact]
    public async Task Backfill_NullablePet_Should_Work()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 23, 14, 0, 0, DateTimeKind.Utc);
        var paymentId = await SeedCustomPaymentAsync(
            tenantId, clinicId, "Alan Turing", petName: null, amount: 90m,
            currency: "TRY", method: PaymentMethod.Cash, paidAtUtc: paidAt, notes: null);

        var result = await RunBackfillAsync(tenantId);
        result.ParityInSync.Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var rm = await PaymentProjectionTestSupport.FindReadModelAsync(queryDb, paymentId);
        rm!.PetId.Should().BeNull();
        rm.PetName.Should().BeNull();
        rm.PetNameNormalized.Should().BeNull();
        rm.ClientName.Should().Be("Alan Turing");
    }

    [Fact]
    public async Task Backfill_TenantScoped_Should_IsolateTenantsAndClinics()
    {
        await ResetAsync();
        var (tenantA, clinicA1, clinicA2) = await SeedTenantWithTwoClinicsAsync();
        var (tenantB, clinicB, _) = await SeedTenantWithTwoClinicsAsync();
        var paidAt = new DateTime(2026, 6, 18, 8, 0, 0, DateTimeKind.Utc);

        await SeedPaymentAsync(tenantA, clinicA1, 100m, paidAt);
        await SeedPaymentAsync(tenantA, clinicA2, 120m, paidAt);
        await SeedPaymentAsync(tenantB, clinicB, 300m, paidAt);

        var result = await RunBackfillAsync(tenantA);

        result.ScopeTenantId.Should().Be(tenantA);
        result.CommandPaymentCount.Should().Be(2);
        result.QueryReadModelCount.Should().Be(2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.PaymentReadModels.CountAsync(x => x.TenantId == tenantA)).Should().Be(2);
        (await queryDb.PaymentReadModels.CountAsync(x => x.TenantId == tenantB)).Should().Be(0);
    }

    [Fact]
    public async Task Backfill_Should_SkipStale_WhenNewerProjectionRowExists()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 24, 11, 0, 0, DateTimeKind.Utc);
        var paymentId = await SeedCustomPaymentAsync(
            tenantId, clinicId, "Edsger Dijkstra", petName: null, amount: 100m,
            currency: "TRY", method: PaymentMethod.Cash, paidAtUtc: paidAt, notes: null);

        // Daha yeni gerçek projection event'i (UtcNow) ile yazılmış satır — backfill ezmemeli.
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var queryDb = seedScope.ServiceProvider.GetRequiredService<QueryDbContext>();
            queryDb.PaymentReadModels.Add(new PaymentReadModel
            {
                PaymentId = paymentId,
                TenantId = tenantId,
                ClinicId = clinicId,
                ClientId = Guid.NewGuid(),
                ClientName = "NEWER",
                ClientNameNormalized = "newer",
                Amount = 999m,
                Currency = "TRY",
                Method = (int)PaymentMethod.Cash,
                PaidAtUtc = paidAt,
                LastEventId = Guid.NewGuid(),
                LastEventOccurredAtUtc = DateTime.UtcNow,
                LastProjectedAtUtc = DateTime.UtcNow
            });
            await queryDb.SaveChangesAsync();
        }

        var result = await RunBackfillAsync(tenantId);
        result.SkippedStaleCount.Should().BeGreaterThanOrEqualTo(1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var verifyDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var rm = await PaymentProjectionTestSupport.FindReadModelAsync(verifyDb, paymentId);
        rm!.Amount.Should().Be(999m, "backfill daha yeni event satırını ezmemeli");
        rm.ClientName.Should().Be("NEWER");
    }

    private async Task ResetAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await commandDb.Payments.ExecuteDeleteAsync();
    }

    private async Task<(Guid TenantId, Guid ClinicId)> SeedTenantAsync()
    {
        var (tenantId, clinicId, _) = await SeedTenantWithTwoClinicsAsync();
        return (tenantId, clinicId);
    }

    private async Task<(Guid TenantId, Guid ClinicA, Guid ClinicB)> SeedTenantWithTwoClinicsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
    }

    private async Task SeedPaymentAsync(Guid tenantId, Guid clinicId, decimal amount, DateTime paidAtUtc)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await PaymentFinanceTestSupport.SeedPaymentAsync(commandDb, tenantId, clinicId, amount, paidAtUtc);
    }

    private async Task<Guid> SeedCustomPaymentAsync(
        Guid tenantId,
        Guid clinicId,
        string clientName,
        string? petName,
        decimal amount,
        string currency,
        PaymentMethod method,
        DateTime paidAtUtc,
        string? notes)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var client = new Client(tenantId, clientName);
        commandDb.Clients.Add(client);
        await commandDb.SaveChangesAsync();

        Guid? petId = null;
        if (petName is not null)
        {
            var speciesId = await commandDb.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(tenantId, client.Id, petName, speciesId);
            commandDb.Pets.Add(pet);
            await commandDb.SaveChangesAsync();
            petId = pet.Id;
        }

        var payment = new Payment(
            tenantId, clinicId, client.Id, petId,
            appointmentId: null, examinationId: null,
            amount, currency, method, paidAtUtc, notes);
        commandDb.Payments.Add(payment);
        await commandDb.SaveChangesAsync();
        return payment.Id;
    }

    private async Task<PaymentReadModelBackfillResult> RunBackfillAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var backfill = scope.ServiceProvider.GetRequiredService<IPaymentReadModelBackfillService>();
        return await backfill.BackfillAsync(tenantId, batchSize: 100, CancellationToken.None);
    }
}
