using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Application.Dashboard.ReadModels;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Payments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Query.Dashboard;

[Collection("payment-projection")]
public sealed class DashboardRecentPaymentsReadModelReaderIntegrationTests
{
    private readonly PaymentProjectionWebApplicationFactory _factory;

    public DashboardRecentPaymentsReadModelReaderIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetRecent_Should_ReturnOnlyRowsForRequestedTenantAndClinic()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IDashboardRecentPaymentsReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicA, paidAtUtc: new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc)),
            Row(tenantId, clinicA, paidAtUtc: new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc)),
            Row(tenantId, clinicB, paidAtUtc: new DateTime(2026, 6, 3, 10, 0, 0, DateTimeKind.Utc)),
            Row(Guid.NewGuid(), clinicA, paidAtUtc: new DateTime(2026, 6, 4, 10, 0, 0, DateTimeKind.Utc)));

        var result = await reader.GetRecentAsync(
            new DashboardRecentPaymentsReadRequest(tenantId, clinicA, DashboardFinanceSummaryConstants.RecentPaymentsTake));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(x => x.Id != Guid.Empty);
    }

    [Fact]
    public async Task GetRecent_Should_NotReturnCrossTenantRows()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IDashboardRecentPaymentsReadModelReader>();

        await ResetAsync(queryDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantA, clinicId),
            Row(tenantB, clinicId));

        var result = await reader.GetRecentAsync(
            new DashboardRecentPaymentsReadRequest(tenantA, clinicId, DashboardFinanceSummaryConstants.RecentPaymentsTake));

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetRecent_Should_NotReturnCrossClinicRows()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IDashboardRecentPaymentsReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicA),
            Row(tenantId, clinicB));

        var result = await reader.GetRecentAsync(
            new DashboardRecentPaymentsReadRequest(tenantId, clinicA, DashboardFinanceSummaryConstants.RecentPaymentsTake));

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetRecent_Should_OrderByPaidAtUtcDescThenPaymentIdDesc_AndRespectTake()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IDashboardRecentPaymentsReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var paidAt = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var idOlder = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var idNewer = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, paymentId: idOlder, paidAtUtc: paidAt),
            Row(tenantId, clinicId, paymentId: idNewer, paidAtUtc: paidAt),
            Row(tenantId, clinicId, paidAtUtc: paidAt.AddDays(-1)));

        var result = await reader.GetRecentAsync(
            new DashboardRecentPaymentsReadRequest(tenantId, clinicId, Take: 2));

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(idNewer);
        result[1].Id.Should().Be(idOlder);
    }

    [Fact]
    public async Task GetRecent_Should_MapDenormalizedClientAndPetNames()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IDashboardRecentPaymentsReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientId, clientName: "Ayşe Yılmaz", petId: petId, petName: "Pamuk"));

        var result = await reader.GetRecentAsync(
            new DashboardRecentPaymentsReadRequest(tenantId, clinicId, DashboardFinanceSummaryConstants.RecentPaymentsTake));

        var item = result.Should().ContainSingle().Subject;
        item.ClientId.Should().Be(clientId);
        item.ClientName.Should().Be("Ayşe Yılmaz");
        item.PetId.Should().Be(petId);
        item.PetName.Should().Be("Pamuk");
        item.Amount.Should().Be(100m);
        item.Currency.Should().Be("TRY");
        item.Method.Should().Be(PaymentMethod.Cash);
    }

    [Fact]
    public async Task GetRecent_Should_HandleNullablePetWithoutError()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IDashboardRecentPaymentsReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, petId: null, petName: null));

        var result = await reader.GetRecentAsync(
            new DashboardRecentPaymentsReadRequest(tenantId, clinicId, DashboardFinanceSummaryConstants.RecentPaymentsTake));

        result.Should().ContainSingle();
        result[0].PetId.Should().BeNull();
        result[0].PetName.Should().BeEmpty();
    }

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
        Guid? petId = null,
        string? petName = null,
        string? notes = null,
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
            ClinicName = "Vetinity Clinic",
            ClientId = clientId ?? Guid.NewGuid(),
            ClientName = clientName,
            ClientNameNormalized = clientName.Trim().ToLowerInvariant(),
            PetId = petId,
            PetName = petName,
            PetNameNormalized = string.IsNullOrWhiteSpace(petName) ? null : petName.Trim().ToLowerInvariant(),
            Amount = 100m,
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
