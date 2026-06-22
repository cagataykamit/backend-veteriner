using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Payments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Query.Payments;

[Collection("payment-projection")]
public sealed class PaymentReadModelParityIntegrationTests
{
    private readonly PaymentProjectionWebApplicationFactory _factory;

    public PaymentReadModelParityIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task ClinicParity_Should_BeInSync_AfterBackfill()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc);
        await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", "Byron", 100m, paidAt);
        await SeedPaymentAsync(tenantId, clinicId, "Grace Hopper", null, 200m, paidAt.AddHours(1));

        await RunBackfillAsync(tenantId);

        var result = await GetParityAsync(tenantId, clinicId);

        result.CommandCount.Should().Be(2);
        result.QueryCount.Should().Be(2);
        result.CountInSync.Should().BeTrue();
        result.RowSampleParityInSync.Should().BeTrue();
        result.RecentOrderingInSync.Should().BeTrue();
        result.InSync.Should().BeTrue();
    }

    [Fact]
    public async Task ClinicParity_Should_BeOutOfSync_OnCountMismatch()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", null, 100m, DateTime.UtcNow);

        // Backfill yapılmadı → read-model boş.
        var result = await GetParityAsync(tenantId, clinicId);

        result.CommandCount.Should().Be(1);
        result.QueryCount.Should().Be(0);
        result.CountInSync.Should().BeFalse();
        result.InSync.Should().BeFalse();
    }

    [Fact]
    public async Task ClinicParity_Should_BeOutOfSync_OnRowFieldMismatch()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc);
        var paymentId = await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", null, 100m, paidAt);

        await RunBackfillAsync(tenantId);

        // Read-model satırını bozarak field mismatch üret.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
            var rm = await queryDb.PaymentReadModels.SingleAsync(x => x.PaymentId == paymentId);
            rm.Amount = 999m;
            await queryDb.SaveChangesAsync();
        }

        var result = await GetParityAsync(tenantId, clinicId);

        result.CountInSync.Should().BeTrue();
        result.RowSampleParityInSync.Should().BeFalse();
        result.RowSampleMismatchCount.Should().BeGreaterThanOrEqualTo(1);
        result.RowSampleMismatches.Should().Contain(m => m.PaymentId == paymentId && m.Field == "Amount");
        result.InSync.Should().BeFalse();
    }

    [Fact]
    public async Task ClinicParity_Should_BeOutOfSync_OnClinicNameMismatch()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc);
        var paymentId = await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", null, 100m, paidAt);

        await RunBackfillAsync(tenantId);

        // Read-model ClinicName'i bozarak field mismatch üret (Command DB truth = "Clinic A").
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
            var rm = await queryDb.PaymentReadModels.SingleAsync(x => x.PaymentId == paymentId);
            rm.ClinicName = "Drifted Clinic";
            await queryDb.SaveChangesAsync();
        }

        var result = await GetParityAsync(tenantId, clinicId);

        result.CountInSync.Should().BeTrue();
        result.RowSampleParityInSync.Should().BeFalse();
        result.RowSampleMismatches.Should().Contain(m => m.PaymentId == paymentId && m.Field == "ClinicName");
        result.InSync.Should().BeFalse();
    }

    [Fact]
    public async Task ClinicParity_Should_BeOutOfSync_OnRecentOrderingMismatch()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var older = new DateTime(2026, 6, 21, 9, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var p1 = await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", null, 100m, older);
        var p2 = await SeedPaymentAsync(tenantId, clinicId, "Grace Hopper", null, 200m, newer);

        await RunBackfillAsync(tenantId);

        // İki kaydın PaidAtUtc değerlerini read-model'de yer değiştir → sıralama bozulur, count korunur.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
            var rm1 = await queryDb.PaymentReadModels.SingleAsync(x => x.PaymentId == p1);
            var rm2 = await queryDb.PaymentReadModels.SingleAsync(x => x.PaymentId == p2);
            rm1.PaidAtUtc = newer;
            rm2.PaidAtUtc = older;
            await queryDb.SaveChangesAsync();
        }

        var result = await GetParityAsync(tenantId, clinicId);

        result.CountInSync.Should().BeTrue();
        result.RecentOrderingInSync.Should().BeFalse();
        result.InSync.Should().BeFalse();
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
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (tenantId, clinicId, _) = await PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
        return (tenantId, clinicId);
    }

    private async Task<Guid> SeedPaymentAsync(
        Guid tenantId,
        Guid clinicId,
        string clientName,
        string? petName,
        decimal amount,
        DateTime paidAtUtc)
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
            amount, "TRY", PaymentMethod.Cash, paidAtUtc, notes: null);
        commandDb.Payments.Add(payment);
        await commandDb.SaveChangesAsync();
        return payment.Id;
    }

    private async Task RunBackfillAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var backfill = scope.ServiceProvider.GetRequiredService<IPaymentReadModelBackfillService>();
        await backfill.BackfillAsync(tenantId, batchSize: 100, CancellationToken.None);
    }

    private async Task<PaymentReadModelParityResult> GetParityAsync(Guid tenantId, Guid clinicId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var parity = scope.ServiceProvider.GetRequiredService<IPaymentReadModelParityReader>();
        return await parity.GetClinicParityAsync(tenantId, clinicId, cancellationToken: CancellationToken.None);
    }
}
