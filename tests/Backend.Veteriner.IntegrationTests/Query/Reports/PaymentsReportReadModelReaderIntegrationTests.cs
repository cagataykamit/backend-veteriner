using Backend.Veteriner.Application.Reports.Payments.ReadModels;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Payments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Query.Reports;

/// <summary>
/// CQRS-15G: <see cref="IPaymentsReportReadModelReader"/> Query DB (PaymentReadModels) filtre/scope/aggregate/ordering davranışı.
/// </summary>
[Collection("payment-projection")]
public sealed class PaymentsReportReadModelReaderIntegrationTests
{
    private static readonly DateTime From = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc);

    private readonly PaymentProjectionWebApplicationFactory _factory;

    public PaymentsReportReadModelReaderIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetReport_Should_IsolateByTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, amount: 100m),
            Row(tenantId, clinicId, amount: 50m),
            Row(Guid.NewGuid(), clinicId, amount: 999m));

        var result = await reader.GetReportAsync(Request(tenantId, clinicId: null));

        result.TotalCount.Should().Be(2);
        result.TotalAmount.Should().Be(150m);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetReport_WithClinicFilter_Should_IsolateByClinic()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicA, amount: 100m),
            Row(tenantId, clinicA, amount: 100m),
            Row(tenantId, clinicB, amount: 100m));

        var scoped = await reader.GetReportAsync(Request(tenantId, clinicId: clinicA));
        scoped.TotalCount.Should().Be(2);
        scoped.TotalAmount.Should().Be(200m);

        var tenantWide = await reader.GetReportAsync(Request(tenantId, clinicId: null));
        tenantWide.TotalCount.Should().Be(3);
        tenantWide.TotalAmount.Should().Be(300m);
    }

    [Fact]
    public async Task GetReport_Should_FilterByDateRange()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, amount: 10m, paidAtUtc: From.AddDays(-1)),  // before range
            Row(tenantId, clinicId, amount: 20m, paidAtUtc: From),               // inclusive lower
            Row(tenantId, clinicId, amount: 30m, paidAtUtc: To),                 // inclusive upper
            Row(tenantId, clinicId, amount: 40m, paidAtUtc: To.AddDays(1)));     // after range

        var result = await reader.GetReportAsync(Request(tenantId, clinicId));

        result.TotalCount.Should().Be(2);
        result.TotalAmount.Should().Be(50m);
    }

    [Fact]
    public async Task GetReport_Should_FilterByClient()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientId, amount: 100m),
            Row(tenantId, clinicId, clientId: Guid.NewGuid(), amount: 999m));

        var result = await reader.GetReportAsync(Request(tenantId, clinicId, clientId: clientId));

        result.TotalCount.Should().Be(1);
        result.TotalAmount.Should().Be(100m);
    }

    [Fact]
    public async Task GetReport_Should_FilterByPet()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, petId: petId, amount: 100m),
            Row(tenantId, clinicId, petId: Guid.NewGuid(), amount: 999m),
            Row(tenantId, clinicId, petId: null, amount: 999m));

        var result = await reader.GetReportAsync(Request(tenantId, clinicId, petId: petId));

        result.TotalCount.Should().Be(1);
        result.TotalAmount.Should().Be(100m);
    }

    [Fact]
    public async Task GetReport_Should_FilterByMethod()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, method: (int)PaymentMethod.Cash, amount: 100m),
            Row(tenantId, clinicId, method: (int)PaymentMethod.Transfer, amount: 999m));

        var result = await reader.GetReportAsync(Request(tenantId, clinicId, method: PaymentMethod.Cash));

        result.TotalCount.Should().Be(1);
        result.TotalAmount.Should().Be(100m);
    }

    [Fact]
    public async Task GetReport_Should_SumAllCurrenciesIntoTotalAmount()
    {
        // Mevcut Command DB davranışı: TotalAmount tüm satırların tutar toplamıdır (currency'den bağımsız).
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, amount: 100m, currency: "TRY"),
            Row(tenantId, clinicId, amount: 40m, currency: "EUR"));

        var result = await reader.GetReportAsync(Request(tenantId, clinicId));

        result.TotalCount.Should().Be(2);
        result.TotalAmount.Should().Be(140m);
    }

    [Fact]
    public async Task GetReport_Should_OrderByPaidAtUtcDescThenPaymentIdDesc()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var paidAt = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
        var idOlder = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var idNewer = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, paymentId: idOlder, paidAtUtc: paidAt),
            Row(tenantId, clinicId, paymentId: idNewer, paidAtUtc: paidAt),
            Row(tenantId, clinicId, paidAtUtc: paidAt.AddDays(-1)));

        var result = await reader.GetReportAsync(Request(tenantId, clinicId));

        result.Items.Should().HaveCount(3);
        result.Items[0].PaymentId.Should().Be(idNewer);
        result.Items[1].PaymentId.Should().Be(idOlder);
    }

    [Fact]
    public async Task GetReport_Should_RespectPaging_AndKeepTotalCountUnpaged()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            await SeedAsync(queryDb, Row(tenantId, clinicId, amount: 10m, paidAtUtc: From.AddDays(i)));

        var page1 = await reader.GetReportAsync(Request(tenantId, clinicId, page: 1, pageSize: 2));
        page1.TotalCount.Should().Be(5);
        page1.TotalAmount.Should().Be(50m);
        page1.Items.Should().HaveCount(2);

        var page3 = await reader.GetReportAsync(Request(tenantId, clinicId, page: 3, pageSize: 2));
        page3.TotalCount.Should().Be(5);
        page3.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetReport_Should_MapDenormalizedFields()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientId, clientName: "Ali Veli",
                clinicName: "Ankara Vet", petId: petId, petName: "Pamuk", notes: "n1",
                amount: 75m, currency: "TRY", method: (int)PaymentMethod.Cash));

        var result = await reader.GetReportAsync(Request(tenantId, clinicId));

        var item = result.Items.Should().ContainSingle().Subject;
        item.ClinicId.Should().Be(clinicId);
        item.ClinicName.Should().Be("Ankara Vet");
        item.ClientId.Should().Be(clientId);
        item.ClientName.Should().Be("Ali Veli");
        item.PetId.Should().Be(petId);
        item.PetName.Should().Be("Pamuk");
        item.Amount.Should().Be(75m);
        item.Currency.Should().Be("TRY");
        item.Method.Should().Be(PaymentMethod.Cash);
        item.Notes.Should().Be("n1");
    }

    [Fact]
    public async Task GetReport_WhenEmpty_Should_ReturnZeroReport()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportReadModelReader>();
        await ResetAsync(queryDb);

        var result = await reader.GetReportAsync(Request(Guid.NewGuid(), Guid.NewGuid()));

        result.TotalCount.Should().Be(0);
        result.TotalAmount.Should().Be(0m);
        result.Items.Should().BeEmpty();
    }

    private static PaymentsReportReadRequest Request(
        Guid tenantId,
        Guid? clinicId,
        Guid? clientId = null,
        Guid? petId = null,
        PaymentMethod? method = null,
        int page = 1,
        int pageSize = 50)
        => new(tenantId, clinicId, clientId, petId, method, From, To, page, pageSize);

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
        var paid = paidAtUtc ?? new DateTime(2026, 4, 19, 10, 0, 0, DateTimeKind.Utc);
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
