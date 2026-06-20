using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Pets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Query.Pets;

[Collection("pet-projection")]
public sealed class PetReadModelLookupReaderIntegrationTests
{
    private readonly PetProjectionWebApplicationFactory _factory;

    public PetReadModelLookupReaderIntegrationTests(PetProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task ResolvePetIdsByTextSearch_Should_ReturnEmpty_When_PatternNull()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        await SeedAsync(queryDb, Row(tenant, "Pamuk"));

        var result = await reader.ResolvePetIdsByTextSearchAsync(
            new PetTextSearchLookupRequest(tenant, null));

        result.PetIds.Should().BeEmpty();
    }

    [Theory]
    [InlineData("pamuk", "Pamuk")]
    [InlineData("tekir", "Boncuk")]
    [InlineData("golden", "Max")]
    [InlineData("yılmaz", "Pamuk")]
    public async Task ResolvePetIdsByTextSearch_Should_MatchAcrossPetAndClientFields(
        string term,
        string expectedPetName)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        var pamuk = Row(tenant, "Pamuk", clientFullName: "Ayşe Yılmaz", speciesName: "Kedi", speciesId: speciesId);
        var boncuk = Row(tenant, "Boncuk", clientFullName: "Mehmet Demir", speciesName: "Kedi", speciesId: speciesId, breed: "Tekir");
        var max = Row(tenant, "Max", clientFullName: "Ali Veli", speciesName: "Köpek", speciesId: Guid.NewGuid(), breedRefName: "Golden Retriever");
        await SeedAsync(queryDb, pamuk, boncuk, max);

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize(term)!);
        var result = await reader.ResolvePetIdsByTextSearchAsync(
            new PetTextSearchLookupRequest(tenant, pattern));

        result.PetIds.Should().ContainSingle();
        var expected = new[] { pamuk, boncuk, max }.Single(p => p.Name == expectedPetName);
        result.PetIds[0].Should().Be(expected.PetId);
    }

    [Fact]
    public async Task ResolvePetIdsByPetTextFields_Should_NotMatchClientName()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenant, "Pamuk", clientFullName: "Ayşe Yılmaz"),
            Row(tenant, "Boncuk", clientFullName: "Mehmet Demir"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern("yılmaz");
        var aggregate = await reader.ResolvePetIdsByTextSearchAsync(
            new PetTextSearchLookupRequest(tenant, pattern));
        var petFieldsOnly = await reader.ResolvePetIdsByPetTextFieldsAsync(
            new PetTextFieldsSearchLookupRequest(tenant, pattern));

        aggregate.PetIds.Should().NotBeEmpty();
        petFieldsOnly.PetIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolvePetIdsByClientIds_Should_ReturnAllOwnedPets()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var pamuk = Row(tenant, "Pamuk", clientId: clientA);
        var boncuk = Row(tenant, "Boncuk", clientId: clientA);
        var karabas = Row(tenant, "Karabaş", clientId: clientB);
        await SeedAsync(queryDb, pamuk, boncuk, karabas);

        var result = await reader.ResolvePetIdsByClientIdsAsync(
            new PetIdsByClientIdsLookupRequest(tenant, [clientA]));

        result.PetIds.Should().Equal([boncuk.PetId, pamuk.PetId]);
    }

    [Fact]
    public async Task ResolvePetIdsByTextSearch_Should_ReturnOnlyRowsForRequestedTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var rowA = Row(tenantA, "Pamuk");
        await SeedAsync(queryDb,
            rowA,
            Row(tenantB, "Pamuk"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern("pamuk");
        var result = await reader.ResolvePetIdsByTextSearchAsync(
            new PetTextSearchLookupRequest(tenantA, pattern));

        result.PetIds.Should().ContainSingle().Which.Should().Be(rowA.PetId);
    }

    [Fact]
    public async Task ResolvePetIdsByTextSearch_Should_EscapeLikeWildcards()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenant, "100% Match"),
            Row(tenant, "Other Pet"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("100%")!);
        var result = await reader.ResolvePetIdsByTextSearchAsync(
            new PetTextSearchLookupRequest(tenant, pattern));

        result.PetIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetDisplayByIds_Should_ReturnEmpty_When_NoIds()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelLookupReader>();

        var result = await reader.GetDisplayByIdsAsync(
            new PetDisplayLookupRequest(Guid.NewGuid(), []));

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDisplayByIds_Should_ReturnOnlyTenantScopedRows()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        var rowA = Row(tenantA, "Pamuk", speciesId: speciesId, speciesName: "Kedi");
        var rowB = Row(tenantB, "Other Pet", speciesId: speciesId, speciesName: "Kedi");
        await SeedAsync(queryDb, rowA, rowB);

        var result = await reader.GetDisplayByIdsAsync(
            new PetDisplayLookupRequest(tenantA, [rowA.PetId, rowB.PetId]));

        result.Items.Should().ContainSingle();
        result.Items[0].PetId.Should().Be(rowA.PetId);
        result.Items[0].Name.Should().Be("Pamuk");
        result.Items[0].SpeciesName.Should().Be("Kedi");
    }

    [Fact]
    public async Task GetDisplayByIds_Should_OrderByNormalizedNameThenId()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelLookupReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        var cem = Row(tenant, "Cem", speciesId: speciesId, speciesName: "Kedi");
        var ali = Row(tenant, "Ali", speciesId: speciesId, speciesName: "Kedi");
        var bora = Row(tenant, "Bora", speciesId: speciesId, speciesName: "Kedi");
        await SeedAsync(queryDb, cem, ali, bora);

        var result = await reader.GetDisplayByIdsAsync(
            new PetDisplayLookupRequest(tenant, [cem.PetId, ali.PetId, bora.PetId]));

        result.Items.Select(x => x.Name).Should().Equal("Ali", "Bora", "Cem");
    }

    private static async Task ResetAsync(QueryDbContext queryDb)
    {
        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await queryDb.PetReadModels.ExecuteDeleteAsync();
    }

    private static async Task SeedAsync(QueryDbContext queryDb, params PetReadModel[] rows)
    {
        queryDb.PetReadModels.AddRange(rows);
        await queryDb.SaveChangesAsync();
    }

    private static PetReadModel Row(
        Guid tenantId,
        string name,
        Guid? clientId = null,
        string clientFullName = "Ayşe Yılmaz",
        Guid? speciesId = null,
        string speciesName = "Kedi",
        string? breed = null,
        string? breedRefName = null,
        decimal? weight = null)
    {
        var now = DateTime.UtcNow;
        clientId ??= Guid.NewGuid();
        speciesId ??= Guid.NewGuid();
        return new PetReadModel
        {
            PetId = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = clientId.Value,
            ClientFullName = clientFullName,
            ClientFullNameNormalized = clientFullName.Trim().ToLowerInvariant(),
            Name = name,
            NameNormalized = name.Trim().ToLowerInvariant(),
            SpeciesId = speciesId.Value,
            SpeciesName = speciesName,
            SpeciesNameNormalized = speciesName.Trim().ToLowerInvariant(),
            Breed = breed,
            BreedRefName = breedRefName,
            Weight = weight,
            LastEventId = Guid.NewGuid(),
            LastProjectedAtUtc = now,
            LastEventOccurredAtUtc = now
        };
    }
}
