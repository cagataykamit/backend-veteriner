using Backend.Veteriner.Application.Clients;
using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Payments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Query.Clients;

[Collection("payment-projection")]
public sealed class ClientPaymentSummaryReadModelReaderIntegrationTests
{
    private readonly PaymentProjectionWebApplicationFactory _factory;

    public ClientPaymentSummaryReadModelReaderIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetSummary_Should_ReturnOnlyRowsForRequestedTenantAndClient()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientPaymentSummaryReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var otherClient = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientId, amount: 100m),
            Row(tenantId, clinicId, clientId: clientId, amount: 50m),
            Row(tenantId, clinicId, clientId: otherClient, amount: 999m),
            Row(Guid.NewGuid(), clinicId, clientId: clientId, amount: 999m));

        var result = await reader.GetSummaryAsync(Request(tenantId, clientId, null));

        result.TotalPaymentsCount.Should().Be(2);
        result.RecentPayments.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSummary_WithClinicFilter_Should_ReturnOnlyThatClinic()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientPaymentSummaryReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicA, clientId: clientId, amount: 100m),
            Row(tenantId, clinicA, clientId: clientId, amount: 100m),
            Row(tenantId, clinicB, clientId: clientId, amount: 100m));

        var scoped = await reader.GetSummaryAsync(Request(tenantId, clientId, clinicA));
        scoped.TotalPaymentsCount.Should().Be(2);

        var tenantWide = await reader.GetSummaryAsync(Request(tenantId, clientId, null));
        tenantWide.TotalPaymentsCount.Should().Be(3);
    }

    [Fact]
    public async Task GetSummary_Should_ComputeCurrencyTotalsAndLastPaymentDate()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientPaymentSummaryReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var lastPaid = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientId, amount: 100m, currency: "TRY",
                paidAtUtc: new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc)),
            Row(tenantId, clinicId, clientId: clientId, amount: 250m, currency: "TRY",
                paidAtUtc: lastPaid),
            Row(tenantId, clinicId, clientId: clientId, amount: 40m, currency: "EUR",
                paidAtUtc: new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc)));

        var result = await reader.GetSummaryAsync(Request(tenantId, clientId, null));

        result.TotalPaymentsCount.Should().Be(3);
        result.LastPaymentAtUtc.Should().Be(lastPaid);
        result.CurrencyTotals.Should().HaveCount(2);
        // OrdinalIgnoreCase ordering: EUR < TRY
        result.CurrencyTotals[0].Currency.Should().Be("EUR");
        result.CurrencyTotals[0].TotalAmount.Should().Be(40m);
        result.CurrencyTotals[1].Currency.Should().Be("TRY");
        result.CurrencyTotals[1].TotalAmount.Should().Be(350m);
    }

    [Fact]
    public async Task GetSummary_Should_OrderRecentByPaidAtUtcDescThenPaymentIdDesc_AndRespectTake()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientPaymentSummaryReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paidAt = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var idOlder = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var idNewer = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientId, paymentId: idOlder, paidAtUtc: paidAt),
            Row(tenantId, clinicId, clientId: clientId, paymentId: idNewer, paidAtUtc: paidAt),
            Row(tenantId, clinicId, clientId: clientId, paidAtUtc: paidAt.AddDays(-1)));

        var result = await reader.GetSummaryAsync(
            new ClientPaymentSummaryReadRequest(tenantId, clientId, null, RecentTake: 2));

        result.TotalPaymentsCount.Should().Be(3);
        result.RecentPayments.Should().HaveCount(2);
        result.RecentPayments[0].Id.Should().Be(idNewer);
        result.RecentPayments[1].Id.Should().Be(idOlder);
    }

    [Fact]
    public async Task GetSummary_Should_MapDenormalizedClinicNameAndPetName()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientPaymentSummaryReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientId, clinicName: "Ankara Vet", petId: petId, petName: "Pamuk"));

        var result = await reader.GetSummaryAsync(Request(tenantId, clientId, null));

        var item = result.RecentPayments.Should().ContainSingle().Subject;
        item.ClinicId.Should().Be(clinicId);
        item.ClinicName.Should().Be("Ankara Vet");
        item.PetId.Should().Be(petId);
        item.PetName.Should().Be("Pamuk");
        item.Currency.Should().Be("TRY");
        item.Method.Should().Be(PaymentMethod.Cash);
    }

    [Fact]
    public async Task GetSummary_WhenEmpty_Should_ReturnZeroCountEmptyTotalsNullLastEmptyRecent()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientPaymentSummaryReadModelReader>();

        await ResetAsync(queryDb);

        var result = await reader.GetSummaryAsync(Request(Guid.NewGuid(), Guid.NewGuid(), null));

        result.TotalPaymentsCount.Should().Be(0);
        result.CurrencyTotals.Should().BeEmpty();
        result.LastPaymentAtUtc.Should().BeNull();
        result.RecentPayments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSummary_Should_HandleNullablePetWithoutError()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientPaymentSummaryReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientId, petId: null, petName: null));

        var result = await reader.GetSummaryAsync(Request(tenantId, clientId, null));

        var item = result.RecentPayments.Should().ContainSingle().Subject;
        item.PetId.Should().BeNull();
        item.PetName.Should().BeEmpty();
    }

    private static ClientPaymentSummaryReadRequest Request(Guid tenantId, Guid clientId, Guid? clinicId)
        => new(tenantId, clientId, clinicId, ClientPaymentSummaryConstants.RecentPaymentsTake);

    private static async Task ResetAsync(QueryDbContext queryDb)
    {
        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await queryDb.PaymentReadModels.ExecuteDeleteAsync();
    }

    private static async Task SeedAsync(QueryDbContext queryDb, params PaymentReadModel[] rows)
    {
        queryDb.PaymentReadModels.AddRange(rows);
        await queryDb.SaveChangesAsync();
    }

    private static PaymentReadModel Row(
        Guid tenantId,
        Guid clinicId,
        Guid? paymentId = null,
        Guid? clientId = null,
        string clientName = "Ayşe Yılmaz",
        string clinicName = "Vetinity Clinic",
        Guid? petId = null,
        string? petName = null,
        string? notes = null,
        decimal amount = 100m,
        string currency = "TRY",
        int method = (int)PaymentMethod.Cash,
        DateTime? paidAtUtc = null)
    {
        var now = DateTime.UtcNow;
        var paid = paidAtUtc ?? new DateTime(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc);
        return new PaymentReadModel
        {
            PaymentId = paymentId ?? Guid.NewGuid(),
            TenantId = tenantId,
            ClinicId = clinicId,
            ClinicName = clinicName,
            ClientId = clientId ?? Guid.NewGuid(),
            ClientName = clientName,
            ClientNameNormalized = clientName.Trim().ToLowerInvariant(),
            PetId = petId,
            PetName = petName,
            PetNameNormalized = string.IsNullOrWhiteSpace(petName) ? null : petName.Trim().ToLowerInvariant(),
            Amount = amount,
            Currency = currency,
            Method = method,
            PaidAtUtc = paid,
            Notes = notes,
            NotesNormalized = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim().ToLowerInvariant(),
            LastEventId = Guid.NewGuid(),
            LastEventOccurredAtUtc = now,
            LastProjectedAtUtc = now
        };
    }
}
