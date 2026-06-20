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
public sealed class PetReadModelReaderIntegrationTests
{
    private readonly PetProjectionWebApplicationFactory _factory;

    public PetReadModelReaderIntegrationTests(PetProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetList_Should_ReturnOnlyRowsForRequestedTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelReader>();

        await ResetAsync(queryDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenantA, "Pamuk"),
            Row(tenantA, "Boncuk"),
            Row(tenantB, "Other Tenant Pet"));

        var result = await reader.GetListAsync(new PetListReadRequest(tenantA, 1, 20, null, null, null));

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(x => x.TenantId == tenantA);
    }

    [Fact]
    public async Task GetList_Should_ReturnEmpty_When_NoRowsForTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelReader>();

        await ResetAsync(queryDb);
        await SeedAsync(queryDb, Row(Guid.NewGuid(), "Someone Else Pet"));

        var result = await reader.GetListAsync(new PetListReadRequest(Guid.NewGuid(), 1, 20, null, null, null));

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetList_Should_OrderByNameThenSpeciesThenId_And_Paginate()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        var speciesCat = Guid.NewGuid();
        var speciesDog = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenant, "Cem", speciesName: "Köpek", speciesId: speciesDog),
            Row(tenant, "Ali", speciesName: "Kedi", speciesId: speciesCat),
            Row(tenant, "Bora", speciesName: "Kedi", speciesId: speciesCat));

        var page1 = await reader.GetListAsync(new PetListReadRequest(tenant, 1, 2, null, null, null));
        page1.TotalCount.Should().Be(3);
        page1.Items.Should().HaveCount(2);
        page1.Items[0].Name.Should().Be("Ali");
        page1.Items[1].Name.Should().Be("Bora");

        var page2 = await reader.GetListAsync(new PetListReadRequest(tenant, 2, 2, null, null, null));
        page2.TotalCount.Should().Be(3);
        page2.Items.Should().HaveCount(1);
        page2.Items[0].Name.Should().Be("Cem");
    }

    [Theory]
    [InlineData("pamuk", "Pamuk")]
    [InlineData("tekir", "Boncuk")]
    [InlineData("golden", "Max")]
    [InlineData("yılmaz", "Pamuk")]
    public async Task GetList_Should_MatchSearchAcrossPetAndClientFields(string term, string expectedPetName)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        await SeedAsync(queryDb,
            Row(tenant, "Pamuk", clientFullName: "Ayşe Yılmaz", speciesName: "Kedi", speciesId: speciesId),
            Row(tenant, "Boncuk", clientFullName: "Mehmet Demir", speciesName: "Kedi", speciesId: speciesId, breed: "Tekir"),
            Row(tenant, "Max", clientFullName: "Ali Veli", speciesName: "Köpek", speciesId: Guid.NewGuid(), breedRefName: "Golden Retriever"));

        var pattern = ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize(term)!);
        var result = await reader.GetListAsync(new PetListReadRequest(tenant, 1, 20, null, null, pattern));

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.Name == expectedPetName);
    }

    [Fact]
    public async Task GetList_Should_FilterByClientIdAndSpeciesId()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPetReadModelReader>();

        await ResetAsync(queryDb);

        var tenant = Guid.NewGuid();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var catSpecies = Guid.NewGuid();
        var dogSpecies = Guid.NewGuid();

        await SeedAsync(queryDb,
            Row(tenant, "Pamuk", clientId: clientA, speciesId: catSpecies, speciesName: "Kedi"),
            Row(tenant, "Karabaş", clientId: clientB, speciesId: dogSpecies, speciesName: "Köpek"),
            Row(tenant, "Boncuk", clientId: clientA, speciesId: dogSpecies, speciesName: "Köpek"));

        var byClient = await reader.GetListAsync(new PetListReadRequest(tenant, 1, 20, clientA, null, null));
        byClient.TotalCount.Should().Be(2);
        byClient.Items.Should().OnlyContain(x => x.ClientId == clientA);

        var bySpecies = await reader.GetListAsync(new PetListReadRequest(tenant, 1, 20, null, catSpecies, null));
        bySpecies.TotalCount.Should().Be(1);
        bySpecies.Items.Should().ContainSingle(x => x.Name == "Pamuk");
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
