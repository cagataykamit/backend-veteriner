using Backend.Veteriner.Application.Common;
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
/// CQRS-15J + 15N: <see cref="IPaymentsReportExportReadModelReader"/> Query DB (PaymentReadModels) filtre/scope/ordering/search davranışı.
/// </summary>
[Collection("payment-projection")]
public sealed class PaymentsReportExportReadModelReaderIntegrationTests
{
    private static readonly DateTime From = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc);

    private readonly PaymentProjectionWebApplicationFactory _factory;

    public PaymentsReportExportReadModelReaderIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetExport_Should_IsolateByTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, amount: 100m),
            Row(tenantId, clinicId, amount: 50m),
            Row(Guid.NewGuid(), clinicId, amount: 999m));

        var result = await reader.GetExportAsync(Request(tenantId, clinicId: null));

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetExport_WithClinicFilter_Should_IsolateByClinic()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicA, amount: 100m),
            Row(tenantId, clinicA, amount: 100m),
            Row(tenantId, clinicB, amount: 100m));

        var scoped = await reader.GetExportAsync(Request(tenantId, clinicId: clinicA));
        scoped.TotalCount.Should().Be(2);
        scoped.Items.Should().HaveCount(2);

        var tenantWide = await reader.GetExportAsync(Request(tenantId, clinicId: null));
        tenantWide.TotalCount.Should().Be(3);
        tenantWide.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetExport_Should_FilterByDateRange()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, amount: 10m, paidAtUtc: From.AddDays(-1)),
            Row(tenantId, clinicId, amount: 20m, paidAtUtc: From),
            Row(tenantId, clinicId, amount: 30m, paidAtUtc: To),
            Row(tenantId, clinicId, amount: 40m, paidAtUtc: To.AddDays(1)));

        var result = await reader.GetExportAsync(Request(tenantId, clinicId));

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetExport_Should_FilterByClient()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientId, amount: 100m),
            Row(tenantId, clinicId, clientId: Guid.NewGuid(), amount: 999m));

        var result = await reader.GetExportAsync(Request(tenantId, clinicId, clientId: clientId));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetExport_Should_FilterByPet()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, petId: petId, amount: 100m),
            Row(tenantId, clinicId, petId: Guid.NewGuid(), amount: 999m));

        var result = await reader.GetExportAsync(Request(tenantId, clinicId, petId: petId));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetExport_Should_FilterByMethod()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, method: (int)PaymentMethod.Cash, amount: 100m),
            Row(tenantId, clinicId, method: (int)PaymentMethod.Transfer, amount: 999m));

        var result = await reader.GetExportAsync(Request(tenantId, clinicId, method: PaymentMethod.Cash));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetExport_Should_OrderByPaidAtUtcDescThenPaymentIdDesc()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
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

        var result = await reader.GetExportAsync(Request(tenantId, clinicId));

        result.Items.Should().HaveCount(3);
        result.Items[0].PaymentId.Should().Be(idNewer);
        result.Items[1].PaymentId.Should().Be(idOlder);
    }

    [Fact]
    public async Task GetExport_Should_MapDenormalizedFields()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientId, clientName: "Ali Veli",
                clinicName: "Ankara Vet", petId: petId, petName: "Pamuk", notes: "n1",
                amount: 75m, currency: "TRY", method: (int)PaymentMethod.Cash));

        var result = await reader.GetExportAsync(Request(tenantId, clinicId));

        var item = result.Items.Should().ContainSingle().Subject;
        item.ClinicName.Should().Be("Ankara Vet");
        item.ClientName.Should().Be("Ali Veli");
        item.PetName.Should().Be("Pamuk");
    }

    [Fact]
    public async Task GetExport_WhenEmpty_Should_ReturnZeroItems()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var result = await reader.GetExportAsync(Request(Guid.NewGuid(), Guid.NewGuid()));

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("yılmaz", "Ayşe Yılmaz", null, null)]
    [InlineData("pamuk", "Mehmet Demir", "Pamuk", null)]
    [InlineData("nakit", "Ali Veli", "Boncuk", "Nakit ödeme")]
    public async Task GetExport_Should_MatchSearchAcrossClientPetNotesAndCurrency(
        string term,
        string clientName,
        string? petName,
        string? notes)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientName: clientName, petName: petName, notes: notes, amount: 100m),
            Row(tenantId, clinicId, clientName: "Other Client", petName: "Other Pet", notes: "Other notes", amount: 999m));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize(term)!);
        var result = await reader.GetExportAsync(Request(tenantId, clinicId, searchPattern: pattern));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.ClientName == clientName);
    }

    [Fact]
    public async Task GetExport_Should_MatchSearchByCurrency()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, currency: "TRY", amount: 50m),
            Row(tenantId, clinicId, currency: "USD", amount: 999m));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("try")!);
        var result = await reader.GetExportAsync(Request(tenantId, clinicId, searchPattern: pattern));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.Currency == "TRY");
    }

    [Fact]
    public async Task GetExport_Should_MatchSearchByClientIdLookup()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientMatch = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientMatch, clientName: "Hidden Name", amount: 80m),
            Row(tenantId, clinicId, clientId: Guid.NewGuid(), clientName: "Other", amount: 999m));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("nomatchdirect")!);
        var result = await reader.GetExportAsync(
            Request(tenantId, clinicId, searchPattern: pattern, searchClientIds: [clientMatch]));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.ClientId == clientMatch);
    }

    [Fact]
    public async Task GetExport_Should_MatchSearchByPetIdLookup()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petMatch = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, petId: petMatch, petName: "Hidden Pet", amount: 60m),
            Row(tenantId, clinicId, petId: Guid.NewGuid(), petName: "Visible Pet", amount: 999m));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("nomatchdirect")!);
        var result = await reader.GetExportAsync(
            Request(tenantId, clinicId, searchPattern: pattern, searchPetIds: [petMatch]));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.PetId == petMatch);
    }

    [Fact]
    public async Task GetExport_Should_NotLeakLookupMatchesAcrossClinic()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicA, clientId: clientId, clientName: "Ayşe Yılmaz", amount: 100m),
            Row(tenantId, clinicB, clientId: clientId, clientName: "Ayşe Yılmaz", amount: 200m));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("nomatchdirect")!);
        var result = await reader.GetExportAsync(
            Request(tenantId, clinicA, searchPattern: pattern, searchClientIds: [clientId]));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.ClinicId == clinicA);
    }

    [Fact]
    public async Task GetExport_TenantWideSearch_Should_SeeAllClinicsInTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicA, clientName: "Ayşe Yılmaz", amount: 100m),
            Row(tenantId, clinicB, clientName: "Ayşe Yılmaz", amount: 50m),
            Row(Guid.NewGuid(), clinicA, clientName: "Ayşe Yılmaz", amount: 999m));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("yılmaz")!);
        var result = await reader.GetExportAsync(Request(tenantId, clinicId: null, searchPattern: pattern));

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetExport_Should_ReturnEmpty_WhenSearchUnrelated()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsReportExportReadModelReader>();
        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb, Row(tenantId, clinicId));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("zzznomatch")!);
        var result = await reader.GetExportAsync(Request(tenantId, clinicId, searchPattern: pattern));

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    private static PaymentsReportExportReadRequest Request(
        Guid tenantId,
        Guid? clinicId,
        Guid? clientId = null,
        Guid? petId = null,
        PaymentMethod? method = null,
        string? searchPattern = null,
        IReadOnlyList<Guid>? searchClientIds = null,
        IReadOnlyList<Guid>? searchPetIds = null)
        => new(
            tenantId,
            clinicId,
            clientId,
            petId,
            method,
            From,
            To,
            searchPattern,
            searchClientIds,
            searchPetIds);

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
