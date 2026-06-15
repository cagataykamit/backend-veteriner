using System.Diagnostics;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Yük testi veritabanı (<c>VetinityLoadTestDb</c>) için güvenli sentetik operasyon verisi üretir.
/// Kısmi seed durumunda eksik aşamaları tamamlar; tam veri varsa atlar.
/// </summary>
public static class LoadTestDataSeeder
{
    public const string RequiredDatabaseName = "VetinityLoadTestDb";
    private const string LoadTestEmailDomain = "@loadtest.vetinity.invalid";
    private const string LoadTestMarker = "[LOADTEST]";
    private const int DeterministicSeed = 20260614;
    private const int BatchSize = 1000;

    private const int ClientCount = 5_000;
    private const int PetCount = 8_000;
    private const int AppointmentCount = 50_000;
    private const int ExaminationCount = 12_000;
    private const int VaccinationCount = 10_000;
    private const int PaymentCount = 20_000;
    private const int ProductCategoryCount = 20;
    private const int ProductCount = 300;

    private static readonly string[] IstanbulDistricts =
    [
        "Kadıköy", "Beşiktaş", "Üsküdar", "Şişli", "Bakırköy", "Maltepe", "Ataşehir", "Kartal",
        "Pendik", "Sarıyer", "Beyoğlu", "Fatih", "Ümraniye", "Bağcılar", "Esenyurt",
    ];

    private static readonly string[] PetDogNames =
    [
        "Paşa", "Karabaş", "Minnoş", "Şans", "Karamel", "Badem", "Zeytin", "Çiko", "Luna", "Max",
    ];

    private static readonly string[] PetCatNames =
    [
        "Pamuk", "Duman", "Sütlaç", "Mırmır", "Boncuk", "Tarçın", "Fındık", "Pati", "Mia", "Leo",
    ];

    private static readonly string[] ProductUnits = ["Adet", "Kutu", "Paket", "Şişe", "Tüp"];

    private static readonly AppointmentType[] AppointmentTypes = Enum.GetValues<AppointmentType>();

    private static readonly PaymentMethod[] PaymentMethods = Enum.GetValues<PaymentMethod>();

    public static async Task SeedSmallAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        EnsureTargetDatabase(db);

        var sw = Stopwatch.StartNew();
        var now = DateTime.UtcNow;

        var context = await ResolveSeedContextAsync(db, ct);
        var before = await GetLoadTestCountsAsync(db, context, ct);

        if (IsFullySeeded(before))
        {
            logger.LogInformation(
                "Load test verisi zaten tamamlandı. İşlem atlandı. " +
                "Clients: {Clients}, Pets: {Pets}, Appointments: {Appointments}, Examinations: {Examinations}, " +
                "Vaccinations: {Vaccinations}, Payments: {Payments}",
                before.Clients,
                before.Pets,
                before.Appointments,
                before.Examinations,
                before.Vaccinations,
                before.Payments);
            return;
        }

        if (HasPartialSeed(before))
        {
            logger.LogWarning(
                "Kısmi load test verisi algılandı. Eksik aşamalar tamamlanacak. " +
                "Clients: {Clients}/{ClientTarget}, Pets: {Pets}/{PetTarget}, " +
                "Appointments: {Appointments}/{AppointmentTarget}, Examinations: {Examinations}/{ExaminationTarget}, " +
                "Vaccinations: {Vaccinations}/{VaccinationTarget}, Payments: {Payments}/{PaymentTarget}",
                before.Clients, ClientCount,
                before.Pets, PetCount,
                before.Appointments, AppointmentCount,
                before.Examinations, ExaminationCount,
                before.Vaccinations, VaccinationCount,
                before.Payments, PaymentCount);
        }
        else
        {
            logger.LogInformation(
                "Load test seed başlıyor (profil: small, veritabanı: {Database}, tenant: {TenantId}, klinik: {ClinicId}).",
                RequiredDatabaseName,
                context.TenantId,
                context.ClinicId);
        }

        await EnsureProductCatalogAsync(db, context, before, now, logger, ct);
        before = await GetLoadTestCountsAsync(db, context, ct);

        await EnsureClientsAsync(db, context, before.Clients, logger, ct);
        before = await GetLoadTestCountsAsync(db, context, ct);

        var clientIds = await LoadClientIdsAsync(db, context, ct);
        if (clientIds.Count < ClientCount)
        {
            throw new InvalidOperationException(
                $"Load test müşteri kimlikleri yetersiz: {clientIds.Count}/{ClientCount}.");
        }

        await EnsurePetsAsync(db, context, clientIds, before.Pets, logger, ct);
        before = await GetLoadTestCountsAsync(db, context, ct);

        var petRefs = await LoadPetRefsAsync(db, context, ct);
        if (petRefs.Count < PetCount)
        {
            throw new InvalidOperationException(
                $"Load test pet kimlikleri yetersiz: {petRefs.Count}/{PetCount}.");
        }

        await EnsureAppointmentsAsync(db, context, petRefs, before.Appointments, now, logger, ct);
        before = await GetLoadTestCountsAsync(db, context, ct);

        var completedAppointments = await LoadCompletedAppointmentRefsAsync(db, context, ct);
        await EnsureExaminationsAsync(db, context, completedAppointments, before.Examinations, logger, ct);
        before = await GetLoadTestCountsAsync(db, context, ct);

        var examinationRefs = await LoadExaminationRefsAsync(db, context, ct);
        await EnsureVaccinationsAsync(db, context, petRefs, examinationRefs, before.Vaccinations, now, logger, ct);
        before = await GetLoadTestCountsAsync(db, context, ct);

        await EnsurePaymentsAsync(
            db, context, petRefs, completedAppointments, examinationRefs, before.Payments, now, logger, ct);

        sw.Stop();
        var final = await GetLoadTestCountsAsync(db, context, ct);
        VerifyTargetsOrThrow(final);

        logger.LogInformation(
            "Load test seed tamamlandı. Süre: {ElapsedSeconds:F1}s | Clients: {Clients} | Pets: {Pets} | " +
            "Appointments: {Appointments} | Examinations: {Examinations} | Vaccinations: {Vaccinations} | " +
            "Payments: {Payments} | ProductCategories: {ProductCategories} | Products: {Products} | " +
            "ProductStocks: {ProductStocks} | StockMovements: {StockMovements}",
            sw.Elapsed.TotalSeconds,
            final.Clients,
            final.Pets,
            final.Appointments,
            final.Examinations,
            final.Vaccinations,
            final.Payments,
            final.ProductCategories,
            final.Products,
            final.ProductStocks,
            final.StockMovements);
    }

    private static void EnsureTargetDatabase(AppDbContext db)
    {
        var activeDatabase = db.Database.GetDbConnection().Database;
        if (!string.Equals(activeDatabase, RequiredDatabaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bu seeder yalnızca '{RequiredDatabaseName}' veritabanında çalıştırılabilir. " +
                $"Aktif veritabanı: '{activeDatabase}'. Hiçbir kayıt yazılmadı.");
        }
    }

    private static bool IsFullySeeded(LoadTestTableCounts counts)
        => counts.Clients >= ClientCount
           && counts.Pets >= PetCount
           && counts.Appointments >= AppointmentCount
           && counts.Examinations >= ExaminationCount
           && counts.Vaccinations >= VaccinationCount
           && counts.Payments >= PaymentCount
           && counts.ProductCategories >= ProductCategoryCount
           && counts.Products >= ProductCount
           && counts.ProductStocks >= ProductCount
           && counts.StockMovements >= ProductCount;

    private static bool HasPartialSeed(LoadTestTableCounts counts)
        => counts.Clients > 0
           || counts.Pets > 0
           || counts.Appointments > 0
           || counts.Examinations > 0
           || counts.Vaccinations > 0
           || counts.Payments > 0
           || counts.ProductCategories > 0
           || counts.Products > 0;

    private static void VerifyTargetsOrThrow(LoadTestTableCounts counts)
    {
        var errors = new List<string>();

        if (counts.Clients < ClientCount)
            errors.Add($"Clients: {counts.Clients}/{ClientCount}");
        if (counts.Pets < PetCount)
            errors.Add($"Pets: {counts.Pets}/{PetCount}");
        if (counts.Appointments < AppointmentCount)
            errors.Add($"Appointments: {counts.Appointments}/{AppointmentCount}");
        if (counts.Examinations < ExaminationCount)
            errors.Add($"Examinations: {counts.Examinations}/{ExaminationCount}");
        if (counts.Vaccinations < VaccinationCount)
            errors.Add($"Vaccinations: {counts.Vaccinations}/{VaccinationCount}");
        if (counts.Payments < PaymentCount)
            errors.Add($"Payments: {counts.Payments}/{PaymentCount}");
        if (counts.ProductCategories < ProductCategoryCount)
            errors.Add($"ProductCategories: {counts.ProductCategories}/{ProductCategoryCount}");
        if (counts.Products < ProductCount)
            errors.Add($"Products: {counts.Products}/{ProductCount}");
        if (counts.ProductStocks < ProductCount)
            errors.Add($"ProductStocks: {counts.ProductStocks}/{ProductCount}");
        if (counts.StockMovements < ProductCount)
            errors.Add($"StockMovements: {counts.StockMovements}/{ProductCount}");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Load test seed hedef sayılara ulaşamadı: " + string.Join(", ", errors));
        }
    }

    private static async Task<LoadTestSeedContext> ResolveSeedContextAsync(AppDbContext db, CancellationToken ct)
    {
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Name == DataSeeder.DefaultTenantName, ct);

        if (tenant is null)
            throw new InvalidOperationException("Önce DbMigrator all çalıştırılmalı.");

        var clinic = await db.Clinics
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName,
                ct);

        if (clinic is null)
            throw new InvalidOperationException("Önce DbMigrator all çalıştırılmalı.");

        var speciesIds = await db.Species
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (speciesIds.Count == 0)
            throw new InvalidOperationException("Önce DbMigrator all çalıştırılmalı.");

        var vaccineDefinitions = await db.VaccineDefinitions
            .AsNoTracking()
            .Where(v => v.TenantId == null && v.IsActive)
            .Select(v => new VaccineDefinitionRef(v.Id, v.Name))
            .ToListAsync(ct);

        if (vaccineDefinitions.Count == 0)
            throw new InvalidOperationException("Önce DbMigrator all çalıştırılmalı.");

        return new LoadTestSeedContext(
            tenant.Id,
            clinic.Id,
            speciesIds,
            vaccineDefinitions);
    }

    private static async Task<LoadTestTableCounts> GetLoadTestCountsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        CancellationToken ct)
    {
        return new LoadTestTableCounts
        {
            Clients = await db.Clients.CountAsync(
                c => c.TenantId == context.TenantId
                     && c.Email != null
                     && c.Email.EndsWith(LoadTestEmailDomain),
                ct),
            Pets = await db.Pets.CountAsync(
                p => p.TenantId == context.TenantId
                     && p.Notes != null
                     && p.Notes.Contains(LoadTestMarker),
                ct),
            Appointments = await db.Appointments.CountAsync(
                a => a.TenantId == context.TenantId
                     && a.ClinicId == context.ClinicId
                     && a.Notes != null
                     && a.Notes.Contains(LoadTestMarker),
                ct),
            Examinations = await db.Examinations.CountAsync(
                e => e.TenantId == context.TenantId
                     && e.ClinicId == context.ClinicId
                     && e.Notes != null
                     && e.Notes.Contains(LoadTestMarker),
                ct),
            Vaccinations = await db.Vaccinations.CountAsync(
                v => v.TenantId == context.TenantId
                     && v.ClinicId == context.ClinicId
                     && v.Notes != null
                     && v.Notes.Contains(LoadTestMarker),
                ct),
            Payments = await db.Payments.CountAsync(
                p => p.TenantId == context.TenantId
                     && p.ClinicId == context.ClinicId
                     && p.Notes != null
                     && p.Notes.Contains(LoadTestMarker),
                ct),
            ProductCategories = await db.ProductCategories.CountAsync(
                c => c.TenantId == context.TenantId && c.Name.StartsWith("LT-Kategori-"),
                ct),
            Products = await db.Products.CountAsync(
                p => p.TenantId == context.TenantId
                     && p.Sku != null
                     && p.Sku.StartsWith("LT-SKU-"),
                ct),
            ProductStocks = await db.ProductStocks.CountAsync(
                s => s.TenantId == context.TenantId && s.ClinicId == context.ClinicId,
                ct),
            StockMovements = await db.StockMovements.CountAsync(
                m => m.TenantId == context.TenantId
                     && m.ClinicId == context.ClinicId
                     && m.Notes != null
                     && m.Notes.Contains(LoadTestMarker),
                ct),
        };
    }

    private static async Task<List<Guid>> LoadClientIdsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        CancellationToken ct)
    {
        return await db.Clients
            .AsNoTracking()
            .Where(c => c.TenantId == context.TenantId
                        && c.Email != null
                        && c.Email.EndsWith(LoadTestEmailDomain))
            .OrderBy(c => c.Email)
            .Select(c => c.Id)
            .Take(ClientCount)
            .ToListAsync(ct);
    }

    private static async Task<List<PetRef>> LoadPetRefsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        CancellationToken ct)
    {
        return await db.Pets
            .AsNoTracking()
            .Where(p => p.TenantId == context.TenantId
                        && p.Notes != null
                        && p.Notes.Contains(LoadTestMarker))
            .OrderBy(p => p.Name)
            .Select(p => new PetRef(p.Id, p.ClientId))
            .Take(PetCount)
            .ToListAsync(ct);
    }

    private static async Task<List<CompletedAppointmentRef>> LoadCompletedAppointmentRefsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        CancellationToken ct)
    {
        return await (
            from a in db.Appointments.AsNoTracking()
            join p in db.Pets.AsNoTracking() on a.PetId equals p.Id
            where a.TenantId == context.TenantId
                  && a.ClinicId == context.ClinicId
                  && a.Status == AppointmentStatus.Completed
                  && a.Notes != null
                  && a.Notes.Contains(LoadTestMarker)
            orderby a.ScheduledAtUtc, a.Id
            select new CompletedAppointmentRef(a.Id, a.PetId, p.ClientId, a.ScheduledAtUtc)
        ).ToListAsync(ct);
    }

    private static async Task<List<ExaminationRef>> LoadExaminationRefsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        CancellationToken ct)
    {
        return await (
            from e in db.Examinations.AsNoTracking()
            join p in db.Pets.AsNoTracking() on e.PetId equals p.Id
            where e.TenantId == context.TenantId
                  && e.ClinicId == context.ClinicId
                  && e.Notes != null
                  && e.Notes.Contains(LoadTestMarker)
            orderby e.ExaminedAtUtc, e.Id
            select new ExaminationRef(
                e.Id,
                e.PetId,
                p.ClientId,
                e.AppointmentId ?? Guid.Empty,
                e.ExaminedAtUtc)
        ).Take(ExaminationCount).ToListAsync(ct);
    }

    private static async Task EnsureProductCatalogAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        LoadTestTableCounts existing,
        DateTime now,
        ILogger logger,
        CancellationToken ct)
    {
        if (existing.ProductCategories >= ProductCategoryCount
            && existing.Products >= ProductCount
            && existing.ProductStocks >= ProductCount
            && existing.StockMovements >= ProductCount)
        {
            logger.LogInformation(
                "Product catalog zaten tamam ({Categories}, {Products}, {Stocks}, {Movements}).",
                existing.ProductCategories,
                existing.Products,
                existing.ProductStocks,
                existing.StockMovements);
            return;
        }

        if (existing.ProductCategories < ProductCategoryCount)
        {
            var start = existing.ProductCategories + 1;
            for (var i = start; i <= ProductCategoryCount; i++)
            {
                await db.ProductCategories.AddAsync(
                    new ProductCategory(
                        context.TenantId,
                        $"LT-Kategori-{i:00}",
                        $"{LoadTestMarker} Yük testi ürün kategorisi {i}"),
                    ct);

                if (i % BatchSize == 0 || i == ProductCategoryCount)
                {
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                }
            }

            logger.LogInformation("ProductCategories tamamlandı ({Count}).", ProductCategoryCount);
        }

        var categoryIds = await db.ProductCategories
            .AsNoTracking()
            .Where(c => c.TenantId == context.TenantId && c.Name.StartsWith("LT-Kategori-"))
            .OrderBy(c => c.Name)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (existing.Products < ProductCount)
        {
            var start = existing.Products + 1;
            for (var i = start; i <= ProductCount; i++)
            {
                var random = CreateSeededRandom(i);
                var categoryId = categoryIds[(i - 1) % categoryIds.Count];
                var unit = ProductUnits[i % ProductUnits.Length];
                var unitPrice = Math.Round((decimal)(random.Next(500, 50_000) / 100.0), 2);

                await db.Products.AddAsync(
                    new Product(
                        context.TenantId,
                        $"LT Ürün {i:000}",
                        unit,
                        unitPrice,
                        "TRY",
                        categoryId,
                        sku: $"LT-SKU-{i:000000}",
                        description: $"{LoadTestMarker} Sentetik ürün"),
                    ct);

                if (i % BatchSize == 0 || i == ProductCount)
                {
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                }
            }

            logger.LogInformation("Products tamamlandı ({Count}).", ProductCount);
        }

        if (existing.ProductStocks < ProductCount || existing.StockMovements < ProductCount)
        {
            var productIds = await db.Products
                .AsNoTracking()
                .Where(p => p.TenantId == context.TenantId
                            && p.Sku != null
                            && p.Sku.StartsWith("LT-SKU-"))
                .OrderBy(p => p.Sku)
                .Select(p => p.Id)
                .Take(ProductCount)
                .ToListAsync(ct);

            var stockedProductIds = await db.ProductStocks
                .AsNoTracking()
                .Where(s => s.TenantId == context.TenantId && s.ClinicId == context.ClinicId)
                .Select(s => s.ProductId)
                .ToListAsync(ct);

            var stockedSet = stockedProductIds.ToHashSet();
            var belowMinimumCount = (int)Math.Round(ProductCount * 0.15);
            var nearMinimumCount = (int)Math.Round(ProductCount * 0.10);
            var stockBaseTime = now.AddDays(-90);
            var added = 0;

            for (var i = 0; i < productIds.Count; i++)
            {
                var productId = productIds[i];
                if (stockedSet.Contains(productId))
                    continue;

                var minimum = 10m + (i % 5);
                decimal quantity;

                if (i < belowMinimumCount)
                    quantity = Math.Round(minimum * 0.5m, 3);
                else if (i < belowMinimumCount + nearMinimumCount)
                    quantity = Math.Round(minimum * 1.02m, 3);
                else
                    quantity = minimum * (3m + (i % 4));

                await db.ProductStocks.AddAsync(
                    new ProductStock(context.TenantId, context.ClinicId, productId, quantity, minimum),
                    ct);

                await db.StockMovements.AddAsync(
                    new StockMovement(
                        context.TenantId,
                        context.ClinicId,
                        productId,
                        StockMovementType.Initial,
                        quantity,
                        stockBaseTime.AddHours(i % 72),
                        reason: $"{LoadTestMarker} İlk stok",
                        notes: LoadTestMarker),
                    ct);

                added++;

                if (added % BatchSize == 0)
                {
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                }
            }

            if (added % BatchSize != 0)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }

            logger.LogInformation("ProductStocks ve StockMovements tamamlandı ({Added} yeni kayıt).", added);
        }
    }

    private static async Task EnsureClientsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        int existingCount,
        ILogger logger,
        CancellationToken ct)
    {
        if (existingCount >= ClientCount)
        {
            logger.LogInformation("Clients zaten tamam ({Count}).", existingCount);
            return;
        }

        var start = existingCount + 1;
        for (var i = start; i <= ClientCount; i++)
        {
            var email = $"lt.client.{i:000000}{LoadTestEmailDomain}";
            var phone = FormatLoadTestPhone(i);
            var district = IstanbulDistricts[i % IstanbulDistricts.Length];
            var address =
                $"{district} Mah. Load Test Sok. No:{(i % 200) + 1} Daire:{(i % 30) + 1}, İstanbul";

            await db.Clients.AddAsync(
                new Client(
                    context.TenantId,
                    $"LT Müşteri {i:000000}",
                    phone,
                    email,
                    address),
                ct);

            if (i % BatchSize == 0 || i == ClientCount)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
        }

        logger.LogInformation("Clients tamamlandı ({Count}).", ClientCount);
    }

    private static string FormatLoadTestPhone(int index)
        => $"905530{index:000000}";

    /// <summary>
    /// Global pet indeksi (1..8000) → müşteri indeksi (0-based).
    /// İlk 6000 pet: müşteri 0–2999, her biri 2 pet. Kalan 2000 pet: müşteri 3000–4999, her biri 1 pet.
    /// </summary>
    private static int ResolveClientIndexForGlobalPetIndex(int globalPetIndex)
    {
        if (globalPetIndex <= 6_000)
            return (globalPetIndex - 1) / 2;

        return 3_000 + (globalPetIndex - 6_001);
    }

    private static async Task EnsurePetsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        IReadOnlyList<Guid> clientIds,
        int existingCount,
        ILogger logger,
        CancellationToken ct)
    {
        if (existingCount >= PetCount)
        {
            logger.LogInformation("Pets zaten tamam ({Count}).", existingCount);
            return;
        }

        var start = existingCount + 1;
        var batchCounter = 0;

        for (var globalPetIndex = start; globalPetIndex <= PetCount; globalPetIndex++)
        {
            var clientIndex = ResolveClientIndexForGlobalPetIndex(globalPetIndex);
            var clientId = clientIds[clientIndex];
            var random = CreateSeededRandom(globalPetIndex);

            var speciesId = PickSpeciesId(context, random);
            var isDog = speciesId == SpeciesSeedConstants.Dog;
            var namePool = isDog ? PetDogNames : PetCatNames;
            var name = $"{namePool[globalPetIndex % namePool.Length]} {globalPetIndex:0000}";

            PetGender? gender = globalPetIndex % 3 == 0
                ? null
                : globalPetIndex % 2 == 0 ? PetGender.Male : PetGender.Female;

            var birthDate = DateOnly.FromDateTime(
                DateTime.UtcNow.AddDays(-random.Next(365, 365 * 15)));

            var weight = isDog
                ? Math.Round((decimal)(random.Next(500, 4000) / 100.0), 1)
                : Math.Round((decimal)(random.Next(200, 800) / 100.0), 1);

            await db.Pets.AddAsync(
                new Pet(
                    context.TenantId,
                    clientId,
                    name,
                    speciesId,
                    breed: isDog ? "Karışık" : "Tekir",
                    birthDate,
                    gender: gender,
                    weight: weight,
                    notes: LoadTestMarker),
                ct);

            batchCounter++;

            if (batchCounter % BatchSize == 0 || globalPetIndex == PetCount)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();

                if (globalPetIndex % 2_000 == 0 || globalPetIndex == PetCount)
                    logger.LogInformation("Pets {Current:N0}/{Total:N0}", globalPetIndex, PetCount);
            }
        }

        logger.LogInformation("Pets tamamlandı ({Count}).", PetCount);
    }

    private static Guid PickSpeciesId(LoadTestSeedContext context, Random random)
    {
        var roll = random.Next(100);
        if (roll < 45 && context.SpeciesIds.Contains(SpeciesSeedConstants.Dog))
            return SpeciesSeedConstants.Dog;
        if (roll < 85 && context.SpeciesIds.Contains(SpeciesSeedConstants.Cat))
            return SpeciesSeedConstants.Cat;

        return context.SpeciesIds[random.Next(context.SpeciesIds.Count)];
    }

    private static async Task EnsureAppointmentsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        IReadOnlyList<PetRef> petRefs,
        int existingCount,
        DateTime now,
        ILogger logger,
        CancellationToken ct)
    {
        if (existingCount >= AppointmentCount)
        {
            logger.LogInformation("Appointments zaten tamam ({Count}).", existingCount);
            return;
        }

        if (petRefs.Count == 0)
            throw new InvalidOperationException("Randevu oluşturmak için load test pet kaydı bulunamadı.");

        var todayStart = now.Date;
        var batchCounter = 0;

        for (var i = existingCount; i < AppointmentCount; i++)
        {
            var pet = petRefs[i % petRefs.Count];
            var random = CreateSeededRandom(i + 1_000_000);
            var scheduledAt = ResolveAppointmentTime(i, random, now, todayStart);
            var status = ResolveAppointmentStatus(scheduledAt, random, now);
            var duration = ResolveDurationMinutes(random);
            var appointmentType = AppointmentTypes[random.Next(AppointmentTypes.Length)];

            await db.Appointments.AddAsync(
                new Appointment(
                    context.TenantId,
                    context.ClinicId,
                    pet.PetId,
                    scheduledAt,
                    duration,
                    appointmentType,
                    status,
                    $"{LoadTestMarker} Sentetik randevu"),
                ct);

            batchCounter++;

            if (batchCounter % BatchSize == 0 || i == AppointmentCount - 1)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();

                if ((i + 1) % 10_000 == 0 || i == AppointmentCount - 1)
                    logger.LogInformation("Appointments {Current:N0}/{Total:N0}", i + 1, AppointmentCount);
            }
        }

        logger.LogInformation("Appointments tamamlandı ({Count}).", AppointmentCount);
    }

    private static DateTime ResolveAppointmentTime(
        int index,
        Random random,
        DateTime now,
        DateTime todayStart)
    {
        // Panel/calendar yük testi: derin geçmiş (31–730 gün), yakın geçmiş (1–30 gün),
        // yakın gelecek (1–60 gün), bugün minimal (50 kayıt).
        const int deepPastCount = 44_000;
        const int recentPastCount = 3_000;
        const int nearFutureCount = 2_950;

        if (index < deepPastCount)
        {
            var daysAgo = random.Next(31, 731);
            return todayStart.AddDays(-daysAgo).AddHours(random.Next(8, 19)).AddMinutes(random.Next(0, 60));
        }

        if (index < deepPastCount + recentPastCount)
        {
            var daysAgo = random.Next(1, 31);
            return todayStart.AddDays(-daysAgo).AddHours(random.Next(8, 19)).AddMinutes(random.Next(0, 60));
        }

        if (index < deepPastCount + recentPastCount + nearFutureCount)
        {
            var daysAhead = random.Next(1, 61);
            return todayStart.AddDays(daysAhead).AddHours(random.Next(8, 19)).AddMinutes(random.Next(0, 60));
        }

        return todayStart.AddHours(random.Next(8, 19)).AddMinutes(random.Next(0, 60));
    }

    private static AppointmentStatus ResolveAppointmentStatus(DateTime scheduledAt, Random random, DateTime now)
    {
        if (scheduledAt < now.AddHours(-2))
        {
            var roll = random.Next(100);
            if (roll < 85) return AppointmentStatus.Completed;
            if (roll < 95) return AppointmentStatus.Cancelled;
            return AppointmentStatus.Scheduled;
        }

        if (scheduledAt > now.AddHours(2))
        {
            return random.Next(100) < 90
                ? AppointmentStatus.Scheduled
                : AppointmentStatus.Cancelled;
        }

        var todayRoll = random.Next(100);
        if (todayRoll < 60) return AppointmentStatus.Scheduled;
        if (todayRoll < 90) return AppointmentStatus.Completed;
        return AppointmentStatus.Cancelled;
    }

    private static int ResolveDurationMinutes(Random random)
    {
        var options = new[] { 15, 20, 30, 45, 60, 90 };
        return options[random.Next(options.Length)];
    }

    private static async Task EnsureExaminationsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        IReadOnlyList<CompletedAppointmentRef> completedAppointments,
        int existingCount,
        ILogger logger,
        CancellationToken ct)
    {
        if (existingCount >= ExaminationCount)
        {
            logger.LogInformation("Examinations zaten tamam ({Count}).", existingCount);
            return;
        }

        if (completedAppointments.Count == 0)
        {
            throw new InvalidOperationException(
                "Muayene oluşturmak için tamamlanmış load test randevusu bulunamadı.");
        }

        var candidates = completedAppointments
            .OrderBy(a => a.ScheduledAtUtc)
            .ThenBy(a => a.AppointmentId)
            .ToList();

        var step = Math.Max(1, candidates.Count / ExaminationCount);
        var start = existingCount;
        var batchCounter = 0;

        for (var i = start; i < ExaminationCount; i++)
        {
            var candidate = candidates[Math.Min(i * step, candidates.Count - 1)];
            var random = CreateSeededRandom(i + 2_000_000);
            var examinedAt = candidate.ScheduledAtUtc.AddMinutes(random.Next(5, 45));
            var visitReasons = new[] { "Kontrol", "İshal", "Aşı sonrası takip", "Kulak kaşıntısı", "İştahsızlık" };
            var findings = new[] { "Genel durum iyi", "Hafif dehidrasyon", "Ateş yok", "Deri lezyonu yok" };

            await db.Examinations.AddAsync(
                new Examination(
                    context.TenantId,
                    context.ClinicId,
                    candidate.PetId,
                    candidate.AppointmentId,
                    examinedAt,
                    visitReasons[i % visitReasons.Length],
                    findings[i % findings.Length],
                    assessment: "Stabil",
                    notes: $"{LoadTestMarker} Sentetik muayene"),
                ct);

            batchCounter++;

            if (batchCounter % BatchSize == 0 || i == ExaminationCount - 1)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
        }

        logger.LogInformation("Examinations tamamlandı ({Count}).", ExaminationCount);
    }

    private static async Task EnsureVaccinationsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        IReadOnlyList<PetRef> petRefs,
        IReadOnlyList<ExaminationRef> examinationRefs,
        int existingCount,
        DateTime now,
        ILogger logger,
        CancellationToken ct)
    {
        if (existingCount >= VaccinationCount)
        {
            logger.LogInformation("Vaccinations zaten tamam ({Count}).", existingCount);
            return;
        }

        if (petRefs.Count == 0)
            throw new InvalidOperationException("Aşı oluşturmak için load test pet kaydı bulunamadı.");

        var start = existingCount;
        var batchCounter = 0;

        for (var i = start; i < VaccinationCount; i++)
        {
            var pet = petRefs[i % petRefs.Count];
            var vaccine = context.VaccineDefinitions[i % context.VaccineDefinitions.Count];
            var random = CreateSeededRandom(i + 3_000_000);
            var isApplied = i % 10 < 6;

            DateTime? appliedAt = null;
            DateTime? dueAt = null;
            VaccinationStatus status;
            Guid? examinationId = null;

            if (isApplied)
            {
                status = VaccinationStatus.Applied;
                appliedAt = now.AddDays(-random.Next(1, 365));
                if (i < examinationRefs.Count)
                    examinationId = examinationRefs[i].ExaminationId;
            }
            else
            {
                status = VaccinationStatus.Scheduled;
                dueAt = now.AddDays(random.Next(1, 91));
            }

            await db.Vaccinations.AddAsync(
                new Vaccination(
                    context.TenantId,
                    pet.PetId,
                    context.ClinicId,
                    examinationId,
                    vaccine.Id,
                    vaccine.Name,
                    status,
                    appliedAt,
                    dueAt,
                    $"{LoadTestMarker} Sentetik aşı"),
                ct);

            batchCounter++;

            if (batchCounter % BatchSize == 0 || i == VaccinationCount - 1)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
        }

        logger.LogInformation("Vaccinations tamamlandı ({Count}).", VaccinationCount);
    }

    private static async Task EnsurePaymentsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        IReadOnlyList<PetRef> petRefs,
        IReadOnlyList<CompletedAppointmentRef> completedAppointments,
        IReadOnlyList<ExaminationRef> examinationRefs,
        int existingCount,
        DateTime now,
        ILogger logger,
        CancellationToken ct)
    {
        if (existingCount >= PaymentCount)
        {
            logger.LogInformation("Payments zaten tamam ({Count}).", existingCount);
            return;
        }

        if (petRefs.Count == 0)
            throw new InvalidOperationException("Ödeme oluşturmak için load test pet kaydı bulunamadı.");

        var start = existingCount;
        var batchCounter = 0;

        for (var i = start; i < PaymentCount; i++)
        {
            var pet = petRefs[i % petRefs.Count];
            var random = CreateSeededRandom(i + 4_000_000);
            var paidAt = now.AddDays(-random.Next(0, 180)).AddHours(random.Next(8, 20));
            var amount = Math.Round((decimal)random.Next(5_000, 200_000) / 100m, 2);
            var method = PaymentMethods[random.Next(PaymentMethods.Length)];

            Guid? appointmentId = null;
            Guid? examinationId = null;

            if (i % 3 == 0 && completedAppointments.Count > 0)
                appointmentId = completedAppointments[i % completedAppointments.Count].AppointmentId;

            if (i % 5 == 0 && examinationRefs.Count > 0)
                examinationId = examinationRefs[i % examinationRefs.Count].ExaminationId;

            await db.Payments.AddAsync(
                new Payment(
                    context.TenantId,
                    context.ClinicId,
                    pet.ClientId,
                    pet.PetId,
                    appointmentId,
                    examinationId,
                    amount,
                    "TRY",
                    method,
                    paidAt,
                    $"{LoadTestMarker} Sentetik ödeme"),
                ct);

            batchCounter++;

            if (batchCounter % BatchSize == 0 || i == PaymentCount - 1)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
        }

        logger.LogInformation("Payments tamamlandı ({Count}).", PaymentCount);
    }

    private static Random CreateSeededRandom(int sequenceIndex)
        => new(DeterministicSeed + sequenceIndex);

    private sealed record LoadTestSeedContext(
        Guid TenantId,
        Guid ClinicId,
        IReadOnlyList<Guid> SpeciesIds,
        IReadOnlyList<VaccineDefinitionRef> VaccineDefinitions);

    private readonly record struct VaccineDefinitionRef(Guid Id, string Name);

    private readonly record struct PetRef(Guid PetId, Guid ClientId);

    private readonly record struct CompletedAppointmentRef(
        Guid AppointmentId,
        Guid PetId,
        Guid ClientId,
        DateTime ScheduledAtUtc);

    private readonly record struct ExaminationRef(
        Guid ExaminationId,
        Guid PetId,
        Guid ClientId,
        Guid AppointmentId,
        DateTime ExaminedAtUtc);

    private sealed class LoadTestTableCounts
    {
        public int Clients { get; set; }
        public int Pets { get; set; }
        public int Appointments { get; set; }
        public int Examinations { get; set; }
        public int Vaccinations { get; set; }
        public int Payments { get; set; }
        public int ProductCategories { get; set; }
        public int Products { get; set; }
        public int ProductStocks { get; set; }
        public int StockMovements { get; set; }
    }
}
