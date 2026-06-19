using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Clients;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Query.Clients;

[Collection("client-projection")]
public sealed class ClientReadModelReaderIntegrationTests
{
    private readonly ClientProjectionWebApplicationFactory _factory;

    public ClientReadModelReaderIntegrationTests(ClientProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetList_Should_ReturnOnlyRowsForRequestedTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientReadModelReader>();

        await ResetAsync(queryDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantA, "Ayşe Yılmaz"),
            Row(tenantA, "Mehmet Demir"),
            Row(tenantB, "Other Tenant"));

        var result = await reader.GetListAsync(new ClientListReadRequest(tenantA, 1, 20, null));

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(x => x.TenantId == tenantA);
    }

    [Fact]
    public async Task GetList_Should_ReturnEmpty_When_NoRowsForTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientReadModelReader>();

        await ResetAsync(queryDb);
        await SeedAsync(queryDb, Row(Guid.NewGuid(), "Someone Else"));

        var result = await reader.GetListAsync(new ClientListReadRequest(Guid.NewGuid(), 1, 20, null));

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetList_Should_OrderByNormalizedNameThenId_And_Paginate()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientReadModelReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenant, "Cem Çelik"),
            Row(tenant, "Ali Veli"),
            Row(tenant, "Bora Kaya"));

        var page1 = await reader.GetListAsync(new ClientListReadRequest(tenant, 1, 2, null));
        page1.TotalCount.Should().Be(3);
        page1.Items.Should().HaveCount(2);
        page1.Items[0].FullName.Should().Be("Ali Veli");
        page1.Items[1].FullName.Should().Be("Bora Kaya");

        var page2 = await reader.GetListAsync(new ClientListReadRequest(tenant, 2, 2, null));
        page2.TotalCount.Should().Be(3);
        page2.Items.Should().HaveCount(1);
        page2.Items[0].FullName.Should().Be("Cem Çelik");
    }

    [Theory]
    [InlineData("yılmaz")]   // full name fragment
    [InlineData("ayse@")]    // email fragment
    [InlineData("905321")]   // phone / normalized phone fragment
    public async Task GetList_Should_MatchSearchAcrossNameEmailPhone(string term)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientReadModelReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenant, "Ayşe Yılmaz", email: "ayse@example.com", phone: "905321234567"),
            Row(tenant, "Mehmet Demir", email: "mehmet@example.com", phone: "905339999999"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize(term)!);
        var result = await reader.GetListAsync(new ClientListReadRequest(tenant, 1, 20, pattern));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.FullName == "Ayşe Yılmaz");
    }

    private static async Task ResetAsync(QueryDbContext queryDb)
    {
        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await queryDb.ClientReadModels.ExecuteDeleteAsync();
    }

    private static async Task SeedAsync(QueryDbContext queryDb, params ClientReadModel[] rows)
    {
        queryDb.ClientReadModels.AddRange(rows);
        await queryDb.SaveChangesAsync();
    }

    private static ClientReadModel Row(
        Guid tenantId,
        string fullName,
        string? email = null,
        string? phone = null)
    {
        var now = DateTime.UtcNow;
        return new ClientReadModel
        {
            ClientId = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = fullName,
            FullNameNormalized = fullName.Trim().ToLowerInvariant(),
            Email = email,
            Phone = phone,
            PhoneNormalized = phone,
            CreatedAtUtc = now,
            LastEventId = Guid.NewGuid(),
            LastProjectedAtUtc = now,
            LastEventOccurredAtUtc = now
        };
    }
}
