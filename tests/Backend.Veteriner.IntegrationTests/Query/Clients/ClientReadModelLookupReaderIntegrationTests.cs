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
public sealed class ClientReadModelLookupReaderIntegrationTests
{
    private readonly ClientProjectionWebApplicationFactory _factory;

    public ClientReadModelLookupReaderIntegrationTests(ClientProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task ResolveClientIdsByTextSearch_Should_ReturnEmpty_When_PatternNull()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        await SeedAsync(queryDb, Row(tenant, "Ayşe Yılmaz"));

        var result = await reader.ResolveClientIdsByTextSearchAsync(
            new ClientTextSearchLookupRequest(tenant, null));

        result.ClientIds.Should().BeEmpty();
    }

    [Theory]
    [InlineData("yılmaz")]
    [InlineData("ayse@")]
    [InlineData("905321")]
    public async Task ResolveClientIdsByTextSearch_Should_MatchAcrossNameEmailPhone(string term)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        var ayse = Row(tenant, "Ayşe Yılmaz", email: "ayse@example.com", phone: "905321234567");
        await SeedAsync(queryDb,
            ayse,
            Row(tenant, "Mehmet Demir", email: "mehmet@example.com", phone: "905339999999"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize(term)!);
        var result = await reader.ResolveClientIdsByTextSearchAsync(
            new ClientTextSearchLookupRequest(tenant, pattern));

        result.ClientIds.Should().ContainSingle().Which.Should().Be(ayse.ClientId);
    }

    [Fact]
    public async Task ResolveClientIdsByTextSearch_Should_ReturnOnlyRowsForRequestedTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var rowA = Row(tenantA, "Ayşe Yılmaz");
        await SeedAsync(queryDb,
            rowA,
            Row(tenantB, "Ayşe Yılmaz"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern("yılmaz");
        var result = await reader.ResolveClientIdsByTextSearchAsync(
            new ClientTextSearchLookupRequest(tenantA, pattern));

        result.ClientIds.Should().ContainSingle().Which.Should().Be(rowA.ClientId);
    }

    [Fact]
    public async Task ResolveClientIdsByTextSearch_Should_EscapeLikeWildcards()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenant, "100% Match"),
            Row(tenant, "Unrelated Name"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("100%")!);
        var result = await reader.ResolveClientIdsByTextSearchAsync(
            new ClientTextSearchLookupRequest(tenant, pattern));

        result.ClientIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetNamesByIds_Should_ReturnEmpty_When_NoIds()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IClientReadModelLookupReader>();

        var result = await reader.GetNamesByIdsAsync(
            new ClientNamesLookupRequest(Guid.NewGuid(), []));

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNamesByIds_Should_ReturnOnlyTenantScopedNames()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var rowA = Row(tenantA, "Ali Veli");
        var rowB = Row(tenantB, "Other Tenant");
        await SeedAsync(queryDb, rowA, rowB);

        var result = await reader.GetNamesByIdsAsync(
            new ClientNamesLookupRequest(tenantA, [rowA.ClientId, rowB.ClientId]));

        result.Items.Should().ContainSingle();
        result.Items[0].ClientId.Should().Be(rowA.ClientId);
        result.Items[0].FullName.Should().Be("Ali Veli");
    }

    [Fact]
    public async Task GetNamesByIds_Should_OrderByNormalizedNameThenId()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IClientReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        var cem = Row(tenant, "Cem Çelik");
        var ali = Row(tenant, "Ali Veli");
        var bora = Row(tenant, "Bora Kaya");
        await SeedAsync(queryDb, cem, ali, bora);

        var result = await reader.GetNamesByIdsAsync(
            new ClientNamesLookupRequest(tenant, [cem.ClientId, ali.ClientId, bora.ClientId]));

        result.Items.Select(x => x.FullName).Should().Equal("Ali Veli", "Bora Kaya", "Cem Çelik");
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
