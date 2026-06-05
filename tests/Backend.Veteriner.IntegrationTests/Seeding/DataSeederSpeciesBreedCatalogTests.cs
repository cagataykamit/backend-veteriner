using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.IntegrationTests.Seeding;

[CollectionDefinition("data-seeder-species-breed-catalog", DisableParallelization = true)]
public sealed class DataSeederSpeciesBreedCatalogSeedCollection;

[Collection("data-seeder-species-breed-catalog")]
public sealed class DataSeederSpeciesBreedCatalogTests
{
    private const string ConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=VeterinerDb_DataSeederSpeciesBreed;Trusted_Connection=True;MultipleActiveResultSets=true";

    private static DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .ConfigureWarnings(w => w
                .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
                .Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    private static Task ResetDatabaseAsync(AppDbContext db)
        => IntegrationTestDatabaseReset.ResetAndMigrateAsync(db);

    private sealed class StubBcryptPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) =>
            "$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

        public bool Verify(string password, string hash) => true;
    }

    [Fact]
    public async Task SeedAsync_Should_Create_DefaultSpecies()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        await DataSeeder.SeedAsync(db, new StubBcryptPasswordHasher());

        var speciesCodes = await db.Species.AsNoTracking().Select(s => s.Code).ToListAsync();
        speciesCodes.Should().Contain("DOG");
        speciesCodes.Should().Contain("CAT");
        speciesCodes.Should().Contain("GUINEA_PIG");
        speciesCodes.Should().Contain("LIZARD");
        speciesCodes.Should().Contain("CATTLE");

        // Migration: 9 (REPTILE dahil) + katalog: 17 yeni = 26
        (await db.Species.CountAsync()).Should().Be(26);
    }

    [Fact]
    public async Task SeedAsync_Should_Create_DefaultBreeds()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        await DataSeeder.SeedAsync(db, new StubBcryptPasswordHasher());

        (await db.Breeds.CountAsync()).Should().Be(171);

        var dogId = await db.Species.AsNoTracking().Where(s => s.Code == "DOG").Select(s => s.Id).SingleAsync();
        var golden = await db.Breeds.AsNoTracking().SingleAsync(b =>
            b.SpeciesId == dogId && b.Name == "Golden Retriever");
        golden.SpeciesId.Should().Be(SpeciesSeedConstants.Dog);

        var guineaId = await db.Species.AsNoTracking()
            .Where(s => s.Code == "GUINEA_PIG")
            .Select(s => s.Id)
            .SingleAsync();
        (await db.Breeds.CountAsync(b => b.SpeciesId == guineaId)).Should().Be(5);

        var reptileId = await db.Species.AsNoTracking()
            .Where(s => s.Code == "REPTILE")
            .Select(s => s.Id)
            .SingleAsync();
        reptileId.Should().Be(SpeciesSeedConstants.Reptile);
        var reptileBreedNames = await db.Breeds.AsNoTracking()
            .Where(b => b.SpeciesId == reptileId)
            .Select(b => b.Name)
            .OrderBy(n => n)
            .ToListAsync();
        reptileBreedNames.Should().BeEquivalentTo(["Bilinmeyen", "Diğer", "Kaplumbağa", "Kertenkele", "Yılan"]);
    }

    [Fact]
    public async Task SeedAsync_Twice_Should_Not_Duplicate_DefaultSpeciesAndBreeds()
    {
        await using var db = new AppDbContext(CreateOptions());
        await ResetDatabaseAsync(db);

        var hasher = new StubBcryptPasswordHasher();
        await DataSeeder.SeedAsync(db, hasher);
        await DataSeeder.SeedAsync(db, hasher);

        (await db.Species.CountAsync()).Should().Be(26);
        (await db.Breeds.CountAsync()).Should().Be(171);
    }
}
