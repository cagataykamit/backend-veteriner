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
        ("GUINEA_PIG", "Kobay (Gine Domuzu)", 41, SpeciesSeedConstants.GuineaPig),
        ("FERRET", "Ferret (Gelincik)", 42, SpeciesSeedConstants.Ferret),
        ("CHINCHILLA", "Çinçilla", 43, SpeciesSeedConstants.Chinchilla),
        ("HEDGEHOG", "Kirpi", 44, SpeciesSeedConstants.Hedgehog),
        ("HAMSTER", "Hamster", 50, SpeciesSeedConstants.Hamster),
        ("SMALL_RODENT", "Küçük Kemirgen", 51, SpeciesSeedConstants.SmallRodent),
        ("TURTLE", "Kaplumbağa", 60, SpeciesSeedConstants.Turtle),
        ("LIZARD", "Kertenkele", 61, SpeciesSeedConstants.Lizard),
        ("SNAKE", "Yılan", 62, SpeciesSeedConstants.Snake),
        ("REPTILE", "Sürüngen", 63, SpeciesSeedConstants.Reptile),
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
        ("WATER_BUFFALO", "Manda", 82, SpeciesSeedConstants.WaterBuffalo),
        ("EXOTIC", "Egzotik", 90, SpeciesSeedConstants.Exotic),
        ("OTHER", "Diğer", 999, SpeciesSeedConstants.Other),
    ];

    /// <summary>
    /// Klinikte yaygın adı öne alınan ya da literatüre göre düzeltilen türler (2026-11-12: kobay; ferret yabani gelincikten
    /// ayrı türdür, <i>Mustela furo</i>).
    /// </summary>
    private static readonly (string Code, string OldName, string NewName)[] SpeciesRenames =
    [
        ("GUINEA_PIG", "Gine Domuzu", "Kobay (Gine Domuzu)"),
        ("FERRET", "Gelincik", "Ferret (Gelincik)"),
    ];

    /// <summary>
    /// Sırası düzeltilen türler (2026-11-12: Sürüngen, Kaz ile aynı sırada, 80, idi; sürüngenlerin yanına alınır).
    /// Yalnız sistemin verdiği eski sıra duruyorsa uygulanır; yöneticinin elle verdiği sıra korunur.
    /// </summary>
    private static readonly (string Code, int OldOrder, int NewOrder)[] SpeciesReorders =
    [
        ("REPTILE", 80, 63),
    ];

    /// <summary>
    /// FCI/FIFe adına göre düzeltilen ırklar (2026-11-12): Maltese terrier değil bişon (FCI 65), Vizsla tazı değil pointer
    /// (FCI 57), Siberian Husky (FCI 270), Dobermann (FCI 143), Drahthaar (FCI 98), Epagneul Breton (FCI 95), Lohmann Brown
    /// (Lohmann Tierzucht).
    /// </summary>
    private static readonly (string SpeciesCode, string OldName, string NewName)[] BreedRenames =
    [
        ("DOG", "Maltese Terrier", "Maltese"),
        ("DOG", "Macar Tazısı (Vizsla)", "Vizsla (Macar Kısa Tüylü Pointer)"),
        ("DOG", "Husky", "Sibirya Kurdu (Siberian Husky)"),
        ("DOG", "Doberman", "Dobermann"),
        ("DOG", "Drahthaar", "Alman Tel Tüylü Pointer (Drahthaar)"),
        ("CAT", "Norwegian Forest", "Norwegian Forest Cat"),
        ("CHICKEN", "Lohman Brown", "Lohmann Brown"),
        ("DOG", "Epanyol Breton", "Epagneul Breton (Brittany)"),
        // Kaynak araştırması (vetinity-product research/sistem-kataloglari-kaynak-arastirmasi-2026-11.md §4, 2026-11-13):
        // belirsiz adlar FCI/FIFe adıyla, yerli ırklar 2004/39 Tebliği adıyla.
        ("DOG", "Alman Kurdu", "Alman Çoban Köpeği (Alman Kurdu)"), // FCI 166
        ("DOG", "Kangal", "Kangal Çoban Köpeği (Kangal)"), // FCI 331
        ("DOG", "Cocker Spaniel", "İngiliz Cocker Spaniel"), // FCI 5
        ("DOG", "Pointer", "İngiliz Pointer (Pointer)"), // FCI 1
        ("DOG", "Pinscher", "Alman Pinscher"), // FCI 184
        ("DOG", "Spitz", "Alman Spitz"), // FCI 97
        ("DOG", "Belçika Malinois", "Belçika Çoban Köpeği (Malinois)"), // FCI 15
        ("DOG", "Mastiff", "İngiliz Mastifi (Mastiff)"), // FCI 264
        ("DOG", "Polonya Av Köpeği", "Polonya Av Köpeği (Gończy Polski)"), // FCI 354
        ("CAT", "Ankara Kedisi", "Ankara Kedisi (Turkish Angora)"), // FIFe TUA
        ("CAT", "Oriental", "Oriental Shorthair"), // FIFe OSH
        ("RABBIT", "Angora Tavşanı", "Ankara Tavşanı (Angora)"), // 2004/39 Ek-20
        ("HORSE", "İngiliz Atı", "Safkan İngiliz (Thoroughbred)"),
        ("HORSE", "Arap Atı", "Safkan Arap"),
        ("HORSE", "Pony", "Midilli (Pony)"),
        ("CATTLE", "Holstein", "Holştayn (Holstein)"),
        ("CATTLE", "Montofon", "Esmer (Montofon)"),
        ("SHEEP", "Merinos", "Merinos (Diğer)"),
        ("SHEEP", "İvesi", "İvesi (Anadolu İvesisi)"), // 2004/39 tescil adı
        ("FISH", "Beta", "Beta (Betta)"),
        ("FISH", "Ciklet", "Çiklit"),
    ];

    /// <summary>
    /// Katalogdan kaldırılan ırklar (silinmez, pasifleşir; hastadaki bağ kalır). Sürüngen altındaki üç satır ayrı türlerle
    /// yineleniyordu; Tekir, Sarman ve Smokin ırk değil kürk deseni/rengidir (TDK, FIFe EMS); "Yerli Irk" tescilli yerli
    /// ırklara, "Cüce Hamster" iki cüce türe ayrıldı (2026-11-13).
    /// </summary>
    private static readonly (string SpeciesCode, string Name)[] BreedDeactivations =
    [
        ("REPTILE", "Kaplumbağa"),
        ("REPTILE", "Kertenkele"),
        ("REPTILE", "Yılan"),
        ("CAT", "Tekir"),
        ("CAT", "Sarman"),
        ("CAT", "Smokin"),
        ("CATTLE", "Yerli Irk"),
        ("HAMSTER", "Cüce Hamster"),
    ];

    /// <summary>Ev kedisi ırkı: ırk yerine kürk adı girilmiş kedilerin ırkı (DSH karşılığı).</summary>
    private const string DomesticShorthairCat = "Ev Kedisi (Kısa Tüylü)";

    /// <summary>
    /// Irk yerine kürk adı taşıyan kedilerin dönüşümü: ırk "Ev Kedisi (Kısa Tüylü)" olur, renk ve desen boşsa doldurulur.
    /// Tekir = tekir deseni; Sarman = kızıl renk + tekir deseni (TDK "sarı tüylü kedi"); Smokin = siyah-beyaz renk.
    /// </summary>
    private static readonly (string OldBreed, Guid? ColorId, Guid? PatternId)[] CoatBreedReclassifications =
    [
        ("Tekir", null, PetColorSeedConstants.Tabby),
        ("Sarman", PetColorSeedConstants.Red, PetColorSeedConstants.Tabby),
        ("Smokin", PetColorSeedConstants.BlackWhite, null),
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

        // Katalog adı/sırası değişen türler: yalnız eski sistem değeri duruyorsa yeniye geçer (yöneticinin elle verdiği değer korunur).
        var renamed = 0;
        foreach (var (code, oldName, newName) in SpeciesRenames)
        {
            var row = await db.Species.FirstOrDefaultAsync(s => s.Code == code && s.Name == oldName, ct);
            if (row is null)
                continue;

            row.Update(row.Code, newName, row.DisplayOrder, row.IsActive);
            renamed++;
        }

        foreach (var (code, oldOrder, newOrder) in SpeciesReorders)
        {
            var row = await db.Species.FirstOrDefaultAsync(s => s.Code == code && s.DisplayOrder == oldOrder, ct);
            if (row is null)
                continue;

            row.Update(row.Code, row.Name, newOrder, row.IsActive);
            renamed++;
        }

        if (renamed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger?.LogInformation("SpeciesAndBreedCatalogSeeder: {Count} species row(s) renamed or reordered.", renamed);
        }

        var catalogCodes = SpeciesCatalog.Select(x => x.Code.Trim().ToUpperInvariant()).ToArray();
        var speciesIds = await db.Species
            .AsNoTracking()
            .Where(s => catalogCodes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.Ordinal, ct);

        // Irk adı düzeltmesi ekleme döngüsünden önce: yeni ad yoksa eski kayıt yeniden adlandırılır (hastaların ırk bağı korunur).
        var breedsRenamed = 0;
        foreach (var (speciesCode, oldName, newName) in BreedRenames)
        {
            if (!speciesIds.TryGetValue(speciesCode, out var speciesId)
                || await db.Breeds.AnyAsync(b => b.SpeciesId == speciesId && b.Name == newName, ct))
                continue;

            var row = await db.Breeds.FirstOrDefaultAsync(b => b.SpeciesId == speciesId && b.Name == oldName, ct);
            if (row is null)
                continue;

            row.Update(newName, row.IsActive);
            breedsRenamed++;
        }

        if (breedsRenamed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger?.LogInformation("SpeciesAndBreedCatalogSeeder: {Count} breed row(s) renamed.", breedsRenamed);
        }

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

        var breedsDeactivated = 0;
        foreach (var (speciesCode, name) in BreedDeactivations)
        {
            if (!speciesIds.TryGetValue(speciesCode, out var speciesId))
                continue;

            var row = await db.Breeds.FirstOrDefaultAsync(b => b.SpeciesId == speciesId && b.Name == name && b.IsActive, ct);
            if (row is null)
                continue;

            row.Update(row.Name, isActive: false);
            breedsDeactivated++;
        }

        if (breedsDeactivated > 0)
        {
            await db.SaveChangesAsync(ct);
            logger?.LogInformation("SpeciesAndBreedCatalogSeeder: {Count} breed row(s) deactivated.", breedsDeactivated);
        }

        if (speciesIds.TryGetValue("CAT", out var catId))
            await ReclassifyCoatBreedsAsync(db, catId, logger, ct);
    }

    /// <summary>
    /// <see cref="CoatBreedReclassifications"/> uygular. Hasta okuma modelindeki ırk/renk adı için seed sonrası
    /// <c>DbMigrator backfill-pet-projections</c> çalıştırılır.
    /// </summary>
    private static async Task ReclassifyCoatBreedsAsync(AppDbContext db, Guid catId, ILogger? logger, CancellationToken ct)
    {
        var target = await db.Breeds.FirstOrDefaultAsync(b => b.SpeciesId == catId && b.Name == DomesticShorthairCat, ct);
        if (target is null)
            return;

        var reclassified = 0;
        foreach (var (oldBreed, colorId, patternId) in CoatBreedReclassifications)
        {
            var oldBreedId = await db.Breeds
                .Where(b => b.SpeciesId == catId && b.Name == oldBreed)
                .Select(b => (Guid?)b.Id)
                .FirstOrDefaultAsync(ct);

            // Irk bağı olan ya da yalnız serbest metinde aynı adı taşıyan kediler (seed sistem kapsamında, AsSystem, çalışır).
            var pets = await db.Pets
                .Where(p => p.SpeciesId == catId
                            && ((oldBreedId != null && p.BreedId == oldBreedId)
                                || (p.BreedId == null && p.Breed == oldBreed)))
                .ToListAsync(ct);

            foreach (var pet in pets)
                pet.ReclassifyCoatFromBreed(target.Id, target.Name, colorId, patternId);

            reclassified += pets.Count;
        }

        if (reclassified > 0)
        {
            await db.SaveChangesAsync(ct);
            logger?.LogInformation(
                "SpeciesAndBreedCatalogSeeder: {Count} cat(s) moved from coat-name breed to {Breed} with colour/pattern.",
                reclassified,
                DomesticShorthairCat);
        }
    }

    private static void SetSpeciesId(Species species, Guid id)
    {
        typeof(Species).GetProperty(nameof(Species.Id), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(species, id);
    }

    /// <summary>
    /// Sistem ırk listesi. Görünen ad "Türkçe yaygın ad (uluslararası ad)"; köpekte FCI, kedide FIFe/CFA, tavşanda ARBA,
    /// çiftlik hayvanında 2004/39 Tebliği ve TÜBA yerli ırk listesi esas alınır. Kaynak ve gerekçe: vetinity-product
    /// research/sistem-kataloglari-kaynak-arastirmasi-2026-11.md §4. Yeniden adlandırma <see cref="BreedRenames"/>,
    /// kaldırma <see cref="BreedDeactivations"/> ile yapılır; buradaki ad değişikliği tek başına mevcut satırı değiştirmez.
    /// </summary>
    private static (string SpeciesCode, string Name)[] BuildBreedCatalog()
    {
        IEnumerable<(string, string)> B(string species, params string[] names) =>
            names.Select(n => (species, n));

        return
        [
            ..B("DOG",
                "Golden Retriever",
                "Labrador Retriever",
                "Alman Çoban Köpeği (Alman Kurdu)",
                "Kangal Çoban Köpeği (Kangal)",
                "Akbaş",
                "Anadolu Çoban Köpeği",
                "Maltese",
                "Poodle",
                "Toy Poodle",
                "French Bulldog",
                "English Bulldog",
                "Pomeranian",
                "Beagle",
                "Rottweiler",
                "Dobermann",
                "İngiliz Cocker Spaniel",
                "Sibirya Kurdu (Siberian Husky)",
                "Border Collie",
                "Chihuahua",
                "Dachshund",
                "Jack Russell Terrier",
                "Shih Tzu",
                "Yorkshire Terrier",
                "Cane Corso",
                "Pitbull Tipi",
                "Samoyed",
                // Türkiye'de yaygın, eski programlardan gelen kayıtlarda görülen ırklar (E-vet geçişi 2026-11-12)
                "İngiliz Setter",
                "İngiliz Pointer (Pointer)",
                "Alman Kısa Tüylü Pointer",
                "Alman Tel Tüylü Pointer (Drahthaar)",
                "Vizsla (Macar Kısa Tüylü Pointer)",
                "Zağar",
                "Afgan Tazısı",
                "Alman Pinscher",
                "Minyatür Pinscher",
                "Pekingese",
                "Pug",
                "Maltipoo",
                "Alman Spitz",
                "Chow Chow",
                "Akita",
                "Dogo Argentino",
                "Belçika Çoban Köpeği (Malinois)",
                "Kafkas Çoban Köpeği",
                "Avustralya Çoban Köpeği",
                "Boxer",
                "Dalmaçyalı",
                "Saint Bernard",
                "İngiliz Mastifi (Mastiff)",
                "Amerikan Staffordshire Terrier",
                "Bull Terrier",
                "Airedale Terrier",
                "Cavalier King Charles Spaniel",
                "King Charles Spaniel",
                "Amerikan Cocker Spaniel",
                "Epagneul Breton (Brittany)",
                "Polonya Av Köpeği (Gończy Polski)",
                "Rus Siyah Terrier",
                "Border Terrier",
                "Tibet Terrier",
                "Avustralya Terrier",
                "Affenpinscher",
                "Amerikan Akita",
                "Presa Canario",
                "Beyaz İsviçre Çoban Köpeği",
                "Avustralya Sığır Köpeği",
                "Eski İngiliz Çoban Köpeği (Bobtail)",
                // FCI kesin kabullü, Türkiye'de klinikte görülen ırklar (2026-11-13; FCI numarası yorumda)
                "Alaska Malamutu", // 243
                "Weimaraner", // 99
                "Bichon Frisé", // 215
                "Havanese", // 250
                "Lhasa Apso", // 227
                "Shiba", // 257
                "Japon Spitz", // 262
                "Welsh Corgi Pembroke", // 39
                "Danua (Great Dane)", // 235
                "Berner Dağ Köpeği", // 45
                "Newfoundland", // 50
                "Whippet", // 162
                "Greyhound", // 158
                "West Highland White Terrier", // 85
                "Minyatür Schnauzer", // 183
                "Schnauzer", // 182
                "Dev Schnauzer", // 181
                "Shar Pei", // 309
                "Bordo Dogu (Dogue de Bordeaux)", // 116
                "Shetland Çoban Köpeği", // 88
                "Collie (Uzun Tüylü)", // 156
                "İngiliz Springer Spaniel", // 125
                "Staffordshire Bull Terrier", // 76
                "Basset Hound", // 163
                "Orta Asya Çoban Köpeği", // 335
                // 5199 s. Kanun Geçici Md. 3'te sayılan ırklar: kayıtta seçilebilmeli
                "Japon Tosa", // 260
                "Fila Brasileiro", // 225
                // Türk ırkları
                "Aksaray Malaklısı",
                "Kars Çoban Köpeği",
                "Tarsus Çatalburun",
                "Türk Tazısı",
                "Melez",
                "Diğer"),
            ..B("CAT",
                "Ev Kedisi (Kısa Tüylü)",
                "Ev Kedisi (Uzun Tüylü)",
                "British Shorthair",
                "British Longhair",
                "Scottish Fold",
                "Scottish Straight",
                "Persian",
                "Himalayan",
                "Siyam",
                "Maine Coon",
                "Bengal",
                "Russian Blue",
                "Van Kedisi",
                "Ankara Kedisi (Turkish Angora)",
                "Sphynx",
                "Ragdoll",
                "Exotic Shorthair",
                "Norwegian Forest Cat",
                "Bombay",
                "Habeş (Abyssinian)",
                "American Shorthair",
                "American Curl",
                "Oriental Shorthair",
                "Khao Manee",
                // FIFe tanınan ırklar (2026-11-13)
                "Birman",
                "Devon Rex",
                "Cornish Rex",
                "Sibirya Kedisi",
                "Burmese",
                "Somali",
                "Selkirk Rex",
                "Chartreux",
                "Balinese",
                "Burmilla",
                "Neva Masquerade",
                "Melez",
                "Diğer"),
            ..B("RABBIT",
                "Holland Lop",
                "Netherland Dwarf",
                "Lionhead",
                "Ankara Tavşanı (Angora)",
                "Rex",
                "Mini Rex",
                "Flemish Giant",
                // ARBA tanınan ırklar (2026-11-13)
                "Mini Lop",
                "French Lop",
                "English Lop",
                "Dutch (Hollanda)",
                "New Zealand",
                "Californian",
                "Jersey Wooly",
                "Dwarf Hotot",
                "American Fuzzy Lop",
                "Melez",
                "Diğer"),
            ..B("GUINEA_PIG",
                "American",
                "Abyssinian",
                "Peruvian",
                "Teddy",
                "Silkie (Sheltie)",
                "Texel",
                "Coronet",
                "White Crested",
                "Tüysüz (Skinny)",
                "Diğer"),
            ..B("HAMSTER",
                "Suriye Hamsterı",
                "Campbell Cüce Hamsterı",
                "Kış Beyazı Cüce Hamsterı",
                "Roborovski",
                "Çin Hamsterı",
                "Diğer"),
            ..B("SMALL_RODENT",
                "Sıçan",
                "Fare",
                "Gerbil",
                "Degu",
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
            // Tür belirtilmemiş eski sürüngen kayıtları; kaplumbağa, kertenkele ve yılan ayrı türlerdir.
            ..B("REPTILE",
                "Bilinmeyen",
                "Diğer"),
            ..B("FISH",
                "Japon Balığı",
                "Beta (Betta)",
                "Lepistes",
                "Moli",
                "Plati",
                "Melek Balığı",
                "Discus",
                "Koi",
                "Çiklit",
                "Diğer"),
            ..B("HORSE",
                "Safkan Arap",
                "Safkan İngiliz (Thoroughbred)",
                "Ahal Teke",
                "Quarter Horse",
                "Midilli (Pony)",
                // TÜBA yerli at ırkları
                "Ayvacık Midillisi",
                "Canik",
                "Malakan",
                "Hınıs Kolu Kısası",
                "Çukurova",
                "Diğer"),
            ..B("DONKEY",
                "Standart",
                "Minyatür Eşek",
                "Diğer"),
            ..B("MULE",
                "Standart",
                "Diğer"),
            ..B("CATTLE",
                "Holştayn (Holstein)",
                "Simental",
                "Jersey",
                "Esmer (Montofon)",
                "Angus",
                "Şarole",
                "Hereford",
                "Limuzin",
                "Belçika Mavisi",
                // Tescilli yerli ırklar (2004/39 Tebliği, TÜBA)
                "Yerli Kara",
                "Boz Irk",
                "Doğu Anadolu Kırmızısı",
                "Güney Anadolu Kırmızısı",
                "Yerli Güney Sarısı",
                "Zavot",
                "Diğer"),
            ..B("WATER_BUFFALO",
                "Anadolu Mandası",
                "Diğer"),
            ..B("SHEEP",
                "Merinos (Diğer)",
                "Karacabey Merinosu",
                "Orta Anadolu Merinosu",
                "Anadolu Merinosu",
                "İvesi (Anadolu İvesisi)",
                "Kıvırcık",
                "Sakız",
                "Karayaka",
                "Dağlıç",
                "Morkaraman",
                "Akkaraman",
                "Gökçeada",
                "Norduz",
                "Tuj",
                "Hemşin",
                "Herik",
                "Karagül",
                "Çine Çaparı",
                "Dorper",
                "Diğer"),
            ..B("GOAT",
                "Saanen",
                "Ankara Keçisi",
                "Kıl Keçisi",
                "Şam Keçisi",
                "Malta Keçisi",
                "Kilis Keçisi",
                "Honamlı",
                "Norduz Keçisi",
                "Diğer"),
            ..B("PIG",
                "Large White",
                "Landrace",
                "Duroc",
                "Diğer"),
            ..B("CHICKEN",
                "Atak-S",
                "Atak",
                "Atabey",
                "Akbay",
                "Lohmann Brown",
                "Sussex",
                "Rhode Island Red",
                "Brahma",
                "Leghorn",
                "Denizli",
                "Gerze",
                "Etlik Piliç (Broiler)",
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
            ..B("EXOTIC",
                "Bilinmeyen",
                "Diğer"),
            ..B("OTHER",
                "Bilinmeyen",
                "Diğer"),
        ];
    }
}
