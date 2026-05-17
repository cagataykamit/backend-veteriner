using System.Reflection;
using Backend.Veteriner.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Klinik kullanımına uygun global Species / Breed kataloğu. <see cref="Species.Code"/> ve
/// (<see cref="Breed.SpeciesId"/>, <see cref="Breed.Name"/>) üzerinden idempotent.
/// </summary>
public static class SpeciesAndBreedCatalogSeeder
{
    private static readonly (string Code, string Name, int DisplayOrder, Guid Id)[] SpeciesCatalog =
    [
        ("CAT", "Kedi", 10, SpeciesSeedConstants.Cat),
        ("DOG", "Köpek", 20, SpeciesSeedConstants.Dog),
        ("BIRD", "Kuş", 30, SpeciesSeedConstants.Bird),
        ("RABBIT", "Tavşan", 40, SpeciesSeedConstants.Rabbit),
        ("GUINEA_PIG", "Gine Domuzu", 41, SpeciesSeedConstants.GuineaPig),
        ("FERRET", "Gelincik", 42, SpeciesSeedConstants.Ferret),
        ("CHINCHILLA", "Çinçilla", 43, SpeciesSeedConstants.Chinchilla),
        ("HEDGEHOG", "Kirpi", 44, SpeciesSeedConstants.Hedgehog),
        ("HAMSTER", "Hamster", 50, SpeciesSeedConstants.Hamster),
        ("TURTLE", "Kaplumbağa", 60, SpeciesSeedConstants.Turtle),
        ("LIZARD", "Kertenkele", 61, SpeciesSeedConstants.Lizard),
        ("SNAKE", "Yılan", 62, SpeciesSeedConstants.Snake),
        ("REPTILE", "Sürüngen", 80, SpeciesSeedConstants.Reptile),
        ("FISH", "Balık", 70, SpeciesSeedConstants.Fish),
        ("HORSE", "At", 71, SpeciesSeedConstants.Horse),
        ("DONKEY", "Eşek", 72, SpeciesSeedConstants.Donkey),
        ("MULE", "Katır", 73, SpeciesSeedConstants.Mule),
        ("CATTLE", "Sığır", 74, SpeciesSeedConstants.Cattle),
        ("SHEEP", "Koyun", 75, SpeciesSeedConstants.Sheep),
        ("GOAT", "Keçi", 76, SpeciesSeedConstants.Goat),
        ("PIG", "Domuz", 77, SpeciesSeedConstants.Pig),
        ("CHICKEN", "Tavuk", 78, SpeciesSeedConstants.Chicken),
        ("DUCK", "Ördek", 79, SpeciesSeedConstants.Duck),
        ("GOOSE", "Kaz", 80, SpeciesSeedConstants.Goose),
        ("TURKEY", "Hindi", 81, SpeciesSeedConstants.Turkey),
        ("OTHER", "Diğer", 999, SpeciesSeedConstants.Other),
    ];

    private static readonly (string SpeciesCode, string Name)[] BreedCatalog =
        BuildBreedCatalog();

    public static async Task SeedAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var speciesAdded = 0;
        foreach (var (code, name, displayOrder, id) in SpeciesCatalog)
        {
            var normalized = code.Trim().ToUpperInvariant();
            if (await db.Species.AnyAsync(s => s.Code == normalized, ct))
                continue;

            var entity = new Species(normalized, name, displayOrder);
            SetSpeciesId(entity, id);
            await db.Species.AddAsync(entity, ct);
            speciesAdded++;
        }

        if (speciesAdded > 0)
        {
            await db.SaveChangesAsync(ct);
            logger?.LogInformation(
                "SpeciesAndBreedCatalogSeeder: {Count} species row(s) added.",
                speciesAdded);
        }

        var catalogCodes = SpeciesCatalog.Select(x => x.Code.Trim().ToUpperInvariant()).ToArray();
        var speciesIds = await db.Species
            .AsNoTracking()
            .Where(s => catalogCodes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.Ordinal, ct);

        var breedAdded = 0;
        foreach (var (speciesCode, breedName) in BreedCatalog)
        {
            var sc = speciesCode.Trim().ToUpperInvariant();
            if (!speciesIds.TryGetValue(sc, out var speciesId))
            {
                logger?.LogWarning(
                    "SpeciesAndBreedCatalogSeeder: species code {Code} missing; breed {Breed} skipped.",
                    sc,
                    breedName);
                continue;
            }

            if (await db.Breeds.AnyAsync(
                    b => b.SpeciesId == speciesId && b.Name == breedName,
                    ct))
                continue;

            await db.Breeds.AddAsync(new Breed(speciesId, breedName), ct);
            breedAdded++;
        }

        if (breedAdded > 0)
        {
            await db.SaveChangesAsync(ct);
            logger?.LogInformation(
                "SpeciesAndBreedCatalogSeeder: {Count} breed row(s) added.",
                breedAdded);
        }
    }

    private static void SetSpeciesId(Species species, Guid id)
    {
        typeof(Species).GetProperty(nameof(Species.Id), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(species, id);
    }

    private static (string SpeciesCode, string Name)[] BuildBreedCatalog()
    {
        IEnumerable<(string, string)> B(string species, params string[] names) =>
            names.Select(n => (species, n));

        return
        [
            ..B("DOG",
                "Golden Retriever",
                "Labrador Retriever",
                "Alman Kurdu",
                "Kangal",
                "Akbaş",
                "Anadolu Çoban Köpeği",
                "Maltese Terrier",
                "Poodle",
                "Toy Poodle",
                "French Bulldog",
                "English Bulldog",
                "Pomeranian",
                "Beagle",
                "Rottweiler",
                "Doberman",
                "Cocker Spaniel",
                "Husky",
                "Border Collie",
                "Chihuahua",
                "Dachshund",
                "Jack Russell Terrier",
                "Shih Tzu",
                "Yorkshire Terrier",
                "Cane Corso",
                "Pitbull Tipi",
                "Samoyed",
                "Melez",
                "Diğer"),
            ..B("CAT",
                "Tekir",
                "British Shorthair",
                "British Longhair",
                "Scottish Fold",
                "Scottish Straight",
                "Persian",
                "Siyam",
                "Maine Coon",
                "Bengal",
                "Russian Blue",
                "Van Kedisi",
                "Ankara Kedisi",
                "Sphynx",
                "Ragdoll",
                "Exotic Shorthair",
                "Norwegian Forest",
                "Melez",
                "Diğer"),
            ..B("RABBIT",
                "Holland Lop",
                "Netherland Dwarf",
                "Lionhead",
                "Angora Tavşanı",
                "Rex",
                "Mini Rex",
                "Flemish Giant",
                "Melez",
                "Diğer"),
            ..B("GUINEA_PIG",
                "American",
                "Abyssinian",
                "Peruvian",
                "Teddy",
                "Diğer"),
            ..B("HAMSTER",
                "Suriye Hamsterı",
                "Cüce Hamster",
                "Roborovski",
                "Çin Hamsterı",
                "Diğer"),
            ..B("FERRET",
                "Standart",
                "Angora",
                "Diğer"),
            ..B("CHINCHILLA",
                "Standart",
                "Diğer"),
            ..B("HEDGEHOG",
                "Afrika Cüce Kirpisi",
                "Diğer"),
            ..B("BIRD",
                "Muhabbet Kuşu",
                "Sultan Papağanı",
                "Kanarya",
                "İspinoz",
                "Sevda Papağanı",
                "Jako Papağanı",
                "Amazon Papağanı",
                "Kakadu",
                "Ara Papağanı",
                "Papağan - Diğer",
                "Güvercin",
                "Diğer"),
            ..B("TURTLE",
                "Kırmızı Yanaklı Su Kaplumbağası",
                "Su Kaplumbağası",
                "Kara Kaplumbağası",
                "Diğer"),
            ..B("LIZARD",
                "Sakallı Ejder",
                "Leopard Gecko",
                "İguana",
                "Bukalemun",
                "Diğer"),
            ..B("SNAKE",
                "Mısır Yılanı",
                "Ball Python",
                "Kral Yılanı",
                "Diğer"),
            ..B("REPTILE",
                "Kaplumbağa",
                "Kertenkele",
                "Yılan",
                "Bilinmeyen",
                "Diğer"),
            ..B("FISH",
                "Japon Balığı",
                "Beta",
                "Lepistes",
                "Moli",
                "Plati",
                "Melek Balığı",
                "Discus",
                "Koi",
                "Ciklet",
                "Diğer"),
            ..B("HORSE",
                "Arap Atı",
                "İngiliz Atı",
                "Ahal Teke",
                "Quarter Horse",
                "Pony",
                "Diğer"),
            ..B("DONKEY",
                "Standart",
                "Minyatür Eşek",
                "Diğer"),
            ..B("MULE",
                "Standart",
                "Diğer"),
            ..B("CATTLE",
                "Holstein",
                "Simental",
                "Jersey",
                "Montofon",
                "Angus",
                "Şarole",
                "Hereford",
                "Yerli Irk",
                "Diğer"),
            ..B("SHEEP",
                "Merinos",
                "İvesi",
                "Kıvırcık",
                "Sakız",
                "Karayaka",
                "Dağlıç",
                "Morkaraman",
                "Akkaraman",
                "Diğer"),
            ..B("GOAT",
                "Saanen",
                "Ankara Keçisi",
                "Kıl Keçisi",
                "Şam Keçisi",
                "Malta Keçisi",
                "Diğer"),
            ..B("PIG",
                "Large White",
                "Landrace",
                "Duroc",
                "Diğer"),
            ..B("CHICKEN",
                "Atak-S",
                "Lohman Brown",
                "Sussex",
                "Rhode Island Red",
                "Brahma",
                "Leghorn",
                "Diğer"),
            ..B("DUCK",
                "Pekin Ördeği",
                "Misk Ördeği",
                "Indian Runner",
                "Diğer"),
            ..B("GOOSE",
                "Embden",
                "Toulouse",
                "Çin Kazı",
                "Diğer"),
            ..B("TURKEY",
                "Bronz Hindi",
                "Beyaz Hindi",
                "Diğer"),
            ..B("OTHER",
                "Bilinmeyen",
                "Diğer"),
        ];
    }
}
