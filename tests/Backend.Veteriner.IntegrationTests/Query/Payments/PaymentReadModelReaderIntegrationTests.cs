using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Payments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Query.Payments;

[Collection("payment-projection")]
public sealed class PaymentReadModelReaderIntegrationTests
{
    private readonly PaymentProjectionWebApplicationFactory _factory;

    public PaymentReadModelReaderIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetList_Should_ReturnOnlyRowsForRequestedTenantAndClinic()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicA, paidAtUtc: new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc)),
            Row(tenantId, clinicA, paidAtUtc: new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc)),
            Row(tenantId, clinicB, paidAtUtc: new DateTime(2026, 6, 3, 10, 0, 0, DateTimeKind.Utc)),
            Row(Guid.NewGuid(), clinicA, paidAtUtc: new DateTime(2026, 6, 4, 10, 0, 0, DateTimeKind.Utc)));

        var result = await reader.GetListAsync(new PaymentsListReadRequest(tenantId, clinicA, 1, 20));

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(x => x.ClinicId == clinicA);
    }

    [Fact]
    public async Task GetList_Should_NotReturnCrossTenantRows()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantA, clinicId),
            Row(tenantB, clinicId));

        var result = await reader.GetListAsync(new PaymentsListReadRequest(tenantA, clinicId, 1, 20));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetList_Should_NotReturnCrossClinicRows()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicA),
            Row(tenantId, clinicB));

        var result = await reader.GetListAsync(new PaymentsListReadRequest(tenantId, clinicA, 1, 20));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.ClinicId == clinicA);
    }

    [Fact]
    public async Task GetList_Should_OrderByPaidAtUtcDescThenPaymentIdDesc_And_Paginate()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

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

        var page1 = await reader.GetListAsync(new PaymentsListReadRequest(tenantId, clinicId, 1, 2));
        page1.TotalCount.Should().Be(3);
        page1.Items.Should().HaveCount(2);
        page1.Items[0].Id.Should().Be(idNewer);
        page1.Items[1].Id.Should().Be(idOlder);

        var page2 = await reader.GetListAsync(new PaymentsListReadRequest(tenantId, clinicId, 2, 2));
        page2.TotalCount.Should().Be(3);
        page2.Items.Should().HaveCount(1);
        page2.Items[0].PaidAtUtc.Should().BeBefore(paidAt);
    }

    [Fact]
    public async Task GetList_Should_FilterByClientId()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientA),
            Row(tenantId, clinicId, clientId: clientB));

        var result = await reader.GetListAsync(
            new PaymentsListReadRequest(tenantId, clinicId, 1, 20, ClientId: clientA));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.ClientId == clientA);
    }

    [Fact]
    public async Task GetList_Should_FilterByPetId()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petA = Guid.NewGuid();
        var petB = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, petId: petA, petName: "Pamuk"),
            Row(tenantId, clinicId, petId: petB, petName: "Minnoş"));

        var result = await reader.GetListAsync(
            new PaymentsListReadRequest(tenantId, clinicId, 1, 20, PetId: petA));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.PetId == petA);
    }

    [Fact]
    public async Task GetList_Should_FilterByMethod()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, method: (int)PaymentMethod.Cash),
            Row(tenantId, clinicId, method: (int)PaymentMethod.Card));

        var result = await reader.GetListAsync(
            new PaymentsListReadRequest(tenantId, clinicId, 1, 20, Method: PaymentMethod.Card));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.Method == PaymentMethod.Card);
    }

    [Fact]
    public async Task GetList_Should_FilterByPaidAtUtcRange()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var from = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 15, 23, 59, 59, DateTimeKind.Utc);
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, paidAtUtc: new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc)),
            Row(tenantId, clinicId, paidAtUtc: new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc)),
            Row(tenantId, clinicId, paidAtUtc: new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc)));

        var result = await reader.GetListAsync(
            new PaymentsListReadRequest(tenantId, clinicId, 1, 20, PaidFromUtc: from, PaidToUtc: to));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.PaidAtUtc == new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("yılmaz", "Ayşe Yılmaz", null, null)]
    [InlineData("pamuk", "Mehmet Demir", "Pamuk", null)]
    [InlineData("nakit", "Ali Veli", "Boncuk", "Nakit ödeme")]
    public async Task GetList_Should_MatchSearchAcrossClientPetNotesAndCurrency(
        string term,
        string clientName,
        string? petName,
        string? notes)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientName: clientName, petName: petName, notes: notes, currency: "TRY"),
            Row(tenantId, clinicId, clientName: "Other Client", petName: "Other Pet", notes: "Other notes", currency: "USD"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize(term)!);
        var result = await reader.GetListAsync(
            new PaymentsListReadRequest(tenantId, clinicId, 1, 20, SearchContainsLikePattern: pattern));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.ClientName == clientName);
    }

    [Fact]
    public async Task GetList_Should_MatchSearchByCurrency()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, currency: "TRY"),
            Row(tenantId, clinicId, currency: "USD"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("try")!);
        var result = await reader.GetListAsync(
            new PaymentsListReadRequest(tenantId, clinicId, 1, 20, SearchContainsLikePattern: pattern));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.Currency == "TRY");
    }

    [Fact]
    public async Task GetList_Should_MatchSearchByClientIdLookup()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientMatch = Guid.NewGuid();
        var clientOther = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, clientId: clientMatch, clientName: "Hidden Name"),
            Row(tenantId, clinicId, clientId: clientOther, clientName: "Other"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("nomatchdirect")!);
        var result = await reader.GetListAsync(
            new PaymentsListReadRequest(
                tenantId,
                clinicId,
                1,
                20,
                SearchContainsLikePattern: pattern,
                SearchMatchClientIds: [clientMatch]));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.ClientId == clientMatch);
    }

    [Fact]
    public async Task GetList_Should_MatchSearchByPetIdLookup()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petMatch = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, petId: petMatch, petName: "Hidden Pet"),
            Row(tenantId, clinicId, petId: Guid.NewGuid(), petName: "Visible Pet"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("nomatchdirect")!);
        var result = await reader.GetListAsync(
            new PaymentsListReadRequest(
                tenantId,
                clinicId,
                1,
                20,
                SearchContainsLikePattern: pattern,
                SearchMatchPetIds: [petMatch]));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.PetId == petMatch);
    }

    [Fact]
    public async Task GetList_Should_NotLeakLookupMatchesAcrossClinic()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicA, clientId: clientId, clientName: "Ayşe Yılmaz"),
            Row(tenantId, clinicB, clientId: clientId, clientName: "Ayşe Yılmaz"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("nomatchdirect")!);
        var result = await reader.GetListAsync(
            new PaymentsListReadRequest(
                tenantId,
                clinicA,
                1,
                20,
                SearchContainsLikePattern: pattern,
                SearchMatchClientIds: [clientId]));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.ClinicId == clinicA);
    }

    [Fact]
    public async Task GetList_Should_ReturnEmpty_WhenSearchUnrelated()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb, Row(tenantId, clinicId));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("zzznomatch")!);
        var result = await reader.GetListAsync(
            new PaymentsListReadRequest(tenantId, clinicId, 1, 20, SearchContainsLikePattern: pattern));

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetList_Should_HandleNullablePetWithoutError()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentsListReadModelReader>();

        await ResetAsync(queryDb);

        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantId, clinicId, petId: null, petName: null, notes: "Genel ödeme"),
            Row(tenantId, clinicId, petId: Guid.NewGuid(), petName: "Pamuk"));

        var all = await reader.GetListAsync(new PaymentsListReadRequest(tenantId, clinicId, 1, 20));
        all.TotalCount.Should().Be(2);
        all.Items.Should().ContainSingle(x => x.PetId == null && x.PetName == string.Empty);

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("genel")!);
        var search = await reader.GetListAsync(
            new PaymentsListReadRequest(tenantId, clinicId, 1, 20, SearchContainsLikePattern: pattern));
        search.TotalCount.Should().Be(1);
        search.Items.Should().ContainSingle(x => x.PetId == null);
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
