using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Projections.Pets;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Pets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Pets;

[Collection("pet-projection")]
public sealed class PetReadModelBackfillIntegrationTests
{
    private readonly PetProjectionWebApplicationFactory _factory;

    public PetReadModelBackfillIntegrationTests(PetProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Backfill_WithEmptyQueryDb_Should_FillReadModels_AndBeInSync()
    {
        await ResetQuerySideAsync();
        var tenantId = Guid.NewGuid();

        await SeedCommandPetsAsync(tenantId, "Pamuk", "Boncuk", "Karabaş");

        var result = await RunBackfillAsync(tenantId);

        result.Success.Should().BeTrue();
        result.ScopeTenantId.Should().Be(tenantId);
        result.CommandPetCount.Should().Be(3);
        result.QueryPetCount.Should().Be(3);
        result.InsertedCount.Should().Be(3);
        result.UpdatedCount.Should().Be(0);
        result.SkippedStaleCount.Should().Be(0);
        result.ParityInSync.Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var rows = await queryDb.PetReadModels.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync();
        rows.Should().HaveCount(3);
        rows.Select(x => x.PetId).Should().OnlyHaveUniqueItems();
        rows.Should().AllSatisfy(x =>
        {
            x.LastEventId.Should().Be(PetReadModelBackfillService.BackfillEventId);
            x.LastEventOccurredAtUtc.Should().Be(PetReadModelBackfillPlanner.BackfillBaselineOccurredAtUtc);
            x.LastProjectedAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(-5));
        });
    }

    [Fact]
    public async Task Backfill_RunTwice_Should_BeIdempotent()
    {
        await ResetQuerySideAsync();
        var tenantId = Guid.NewGuid();
        await SeedCommandPetsAsync(tenantId, "Idem One", "Idem Two");

        var first = await RunBackfillAsync(tenantId);
        var second = await RunBackfillAsync(tenantId);

        first.InsertedCount.Should().Be(2);
        second.InsertedCount.Should().Be(0);
        second.UpdatedCount.Should().Be(2, "aynı baseline occurredAt'te re-run güvenli update yapar, duplicate üretmez");
        second.QueryPetCount.Should().Be(2);
        second.ParityInSync.Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.PetReadModels.CountAsync(x => x.TenantId == tenantId)).Should().Be(2);
    }

    [Fact]
    public async Task Backfill_Should_UpdateExistingRow_WhenCommandRowChanged()
    {
        await ResetQuerySideAsync();
        var tenantId = Guid.NewGuid();
        var petId = await SeedSingleCommandPetAsync(tenantId, "Önceki İsim");

        await RunBackfillAsync(tenantId);

        await using (var mutateScope = _factory.Services.CreateAsyncScope())
        {
            var commandDb = mutateScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pet = await commandDb.Pets
                .Include(p => p.Species)
                .SingleAsync(p => p.Id == petId);
            pet.UpdateDetails("Yeni İsim", pet.SpeciesId, pet.Breed, pet.BirthDate, pet.BreedId, pet.Gender, pet.ColorId, pet.Weight, pet.Notes);
            await commandDb.SaveChangesAsync();
        }

        var second = await RunBackfillAsync(tenantId);
        second.UpdatedCount.Should().Be(1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var row = await queryDb.PetReadModels.AsNoTracking().SingleAsync(x => x.PetId == petId);
        row.Name.Should().Be("Yeni İsim");
        (await queryDb.PetReadModels.CountAsync(x => x.PetId == petId)).Should().Be(1);
    }

    [Fact]
    public async Task Backfill_TenantScoped_Should_IsolateTenants()
    {
        await ResetQuerySideAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await SeedCommandPetsAsync(tenantA, "A One", "A Two");
        await SeedCommandPetsAsync(tenantB, "B One", "B Two", "B Three");

        var result = await RunBackfillAsync(tenantA);

        result.ScopeTenantId.Should().Be(tenantA);
        result.CommandPetCount.Should().Be(2);
        result.InsertedCount.Should().Be(2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.PetReadModels.CountAsync(x => x.TenantId == tenantA)).Should().Be(2);
        (await queryDb.PetReadModels.CountAsync(x => x.TenantId == tenantB)).Should().Be(0, "tenant scope diğer tenant'ı yazmamalı");
    }

    [Fact]
    public async Task Backfill_Should_NotWriteProcessedProjectionEvents()
    {
        await ResetQuerySideAsync();
        var tenantId = Guid.NewGuid();
        await SeedCommandPetsAsync(tenantId, "No Fake Event");

        await RunBackfillAsync(tenantId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.ProcessedProjectionEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Backfill_Should_NotOverwriteRow_WrittenByNewerEvent()
    {
        await ResetQuerySideAsync();
        var tenantId = Guid.NewGuid();
        var petId = await SeedSingleCommandPetAsync(tenantId, "Command Side");

        var newerOccurredAt = DateTime.UtcNow.AddDays(1);
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var queryDb = seedScope.ServiceProvider.GetRequiredService<QueryDbContext>();
            queryDb.PetReadModels.Add(new PetReadModel
            {
                PetId = petId,
                TenantId = tenantId,
                ClientId = Guid.NewGuid(),
                ClientFullName = "Newer Event Owner",
                ClientFullNameNormalized = "newer event owner",
                Name = "Newer Event Name",
                NameNormalized = "newer event name",
                SpeciesId = Guid.NewGuid(),
                SpeciesName = "Kedi",
                SpeciesNameNormalized = "kedi",
                LastEventId = Guid.NewGuid(),
                LastProjectedAtUtc = DateTime.UtcNow,
                LastEventOccurredAtUtc = newerOccurredAt
            });
            await queryDb.SaveChangesAsync();
        }

        var result = await RunBackfillAsync(tenantId);
        result.SkippedStaleCount.Should().Be(1);
        result.UpdatedCount.Should().Be(0);

        await using var scope = _factory.Services.CreateAsyncScope();
        var verifyDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var row = await verifyDb.PetReadModels.AsNoTracking().SingleAsync(x => x.PetId == petId);
        row.Name.Should().Be("Newer Event Name", "daha yeni event ile yazılmış satır backfill tarafından ezilmemeli");
        row.LastEventOccurredAtUtc.Should().Be(newerOccurredAt);
    }

    [Fact]
    public async Task Backfill_AllTenants_Should_ProduceGlobalParityInSync()
    {
        await ResetQuerySideAsync();
        await SeedCommandPetsAsync(Guid.NewGuid(), "Global One", "Global Two");

        var result = await RunBackfillAsync(tenantId: null);

        result.ScopeTenantId.Should().BeNull();
        result.ParityInSync.Should().BeTrue();
        result.CommandPetCount.Should().Be(result.QueryPetCount);

        await using var scope = _factory.Services.CreateAsyncScope();
        var parity = scope.ServiceProvider.GetRequiredService<IPetReadModelParityReader>();
        (await parity.GetGlobalParityAsync()).InSync.Should().BeTrue();
    }

    [Fact]
    public async Task Backfill_Should_PopulateDenormalizedFields_FromCommandDb()
    {
        await ResetQuerySideAsync();
        var tenantId = Guid.NewGuid();

        await using var seedScope = _factory.Services.CreateAsyncScope();
        var commandDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var speciesId = await commandDb.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var speciesName = await commandDb.Species.Where(s => s.Id == speciesId).Select(s => s.Name).FirstAsync();
        var breed = await commandDb.Breeds.AsNoTracking().FirstAsync();
        var color = await commandDb.PetColors.AsNoTracking().FirstAsync();
        var client = new Client(tenantId, "Ayşe Yılmaz");
        commandDb.Clients.Add(client);
        await commandDb.SaveChangesAsync();

        var pet = new Pet(tenantId, client.Id, "Pamuk", speciesId, breed: "Tekir", breedId: breed.Id, colorId: color.Id);
        commandDb.Pets.Add(pet);
        await commandDb.SaveChangesAsync();

        var result = await RunBackfillAsync(tenantId);
        result.InsertedCount.Should().Be(1);
        result.ParityInSync.Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var row = await queryDb.PetReadModels.AsNoTracking().SingleAsync(x => x.PetId == pet.Id);
        row.ClientFullName.Should().Be("Ayşe Yılmaz");
        row.ClientFullNameNormalized.Should().Be(Client.NormalizeFullNameForDuplicateCheck("Ayşe Yılmaz"));
        row.SpeciesName.Should().Be(speciesName);
        row.Breed.Should().Be("Tekir");
        row.BreedRefName.Should().Be(breed.Name);
        row.ColorName.Should().Be(color.Name);
    }

    private async Task<PetReadModelBackfillResult> RunBackfillAsync(Guid? tenantId, int batchSize = 500)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var backfill = scope.ServiceProvider.GetRequiredService<IPetReadModelBackfillService>();
        return await backfill.BackfillAsync(tenantId, batchSize, CancellationToken.None);
    }

    private async Task ResetQuerySideAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
    }

    private async Task SeedCommandPetsAsync(Guid tenantId, params string[] petNames)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var speciesId = await commandDb.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var client = new Client(tenantId, "Test Owner");
        commandDb.Clients.Add(client);
        await commandDb.SaveChangesAsync();

        foreach (var name in petNames)
            commandDb.Pets.Add(new Pet(tenantId, client.Id, name, speciesId));

        await commandDb.SaveChangesAsync();
    }

    private async Task<Guid> SeedSingleCommandPetAsync(Guid tenantId, string petName)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var speciesId = await commandDb.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var client = new Client(tenantId, "Single Owner");
        commandDb.Clients.Add(client);
        await commandDb.SaveChangesAsync();

        var pet = new Pet(tenantId, client.Id, petName, speciesId);
        commandDb.Pets.Add(pet);
        await commandDb.SaveChangesAsync();
        return pet.Id;
    }
}
