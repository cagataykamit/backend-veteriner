using System.Diagnostics;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Yük testi veritabanı (<c>VetinityCommandDb_LoadTest</c>) için güvenli sentetik operasyon verisi üretir.
/// Kısmi seed durumunda eksik aşamaları tamamlar; tam veri varsa atlar.
/// </summary>
public static class LoadTestDataSeeder
{
    public const string RequiredDatabaseName = "VetinityCommandDb_LoadTest";

    /// <summary>Load-test Query DB adı (migrate-query / rebuild için; seeder yalnızca command DB'ye yazar).</summary>
    public const string QueryDatabaseName = "VetinityQueryDb_LoadTest";
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

    private const int LoadTestClinicCount = 10;
    private const int AppointmentsPerClinic = AppointmentCount / LoadTestClinicCount;
    private const int ExaminationsPerClinic = ExaminationCount / LoadTestClinicCount;
    private const int VaccinationsPerClinic = VaccinationCount / LoadTestClinicCount;
    private const int PaymentsPerClinic = PaymentCount / LoadTestClinicCount;

    private static readonly string[] LoadTestClinicNames =
    [
        DataSeeder.DefaultSeedClinicName,
        "LT Klinik 02",
        "LT Klinik 03",
        "LT Klinik 04",
        "LT Klinik 05",
        "LT Klinik 06",
        "LT Klinik 07",
        "LT Klinik 08",
        "LT Klinik 09",
        "LT Klinik 10",
    ];

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

        var context = await ResolveSeedContextAsync(db, logger, ct);
        var before = await GetLoadTestCountsAsync(db, context, ct);

        EnsureCompatibleSeedDistributionOrThrow(context, before);

        if (await IsFullySeededAsync(db, context, before, ct))
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
                "Load test seed başlıyor (profil: small, veritabanı: {Database}, tenant: {TenantId}, klinik sayısı: {ClinicCount}).",
                RequiredDatabaseName,
                context.TenantId,
                context.ClinicIds.Count);
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
        await LogClinicDistributionAsync(db, context, logger, ct);

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

    private static bool HasPartialSeed(LoadTestTableCounts counts)
        => counts.Clients > 0
           || counts.Pets > 0
           || counts.Appointments > 0
           || counts.Examinations > 0
           || counts.Vaccinations > 0
           || counts.Payments > 0
           || counts.ProductCategories > 0
           || counts.Products > 0;

    private static async Task<bool> IsFullySeededAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        LoadTestTableCounts counts,
        CancellationToken ct)
    {
        if (counts.Clients < ClientCount
            || counts.Pets < PetCount
            || counts.Appointments < AppointmentCount
            || counts.Examinations < ExaminationCount
            || counts.Vaccinations < VaccinationCount
            || counts.Payments < PaymentCount
            || counts.ProductCategories < ProductCategoryCount
            || counts.Products < ProductCount
            || counts.ProductStocks < ProductCount
            || counts.StockMovements < ProductCount)
        {
            return false;
        }

        if (context.ClinicIds.Count != LoadTestClinicCount)
            return false;

        return IsExpectedClinicDistribution(counts);
    }

    private static void EnsureCompatibleSeedDistributionOrThrow(
        LoadTestSeedContext context,
        LoadTestTableCounts counts)
    {
        if (context.ClinicIds.Count != LoadTestClinicCount)
        {
            throw new InvalidOperationException(
                $"Load test seed {LoadTestClinicCount} klinik bekliyor; bulunan: {context.ClinicIds.Count}. " +
                "VetinityCommandDb_LoadTest reset ve yeniden seed edilmeli.");
        }

        if (counts.Appointments == 0
            && counts.Examinations == 0
            && counts.Vaccinations == 0
            && counts.Payments == 0)
        {
            return;
        }

        if (counts.Appointments >= AppointmentCount
            || counts.Examinations >= ExaminationCount
            || counts.Vaccinations >= VaccinationCount
            || counts.Payments >= PaymentCount)
        {
            if (!IsExpectedTotalDistribution(counts))
            {
                throw CreateLegacyDistributionException();
            }
        }

        if (counts.Appointments > 0 && counts.Appointments < AppointmentCount)
        {
            var clinicsWithAppointments = counts.AppointmentsPerClinic.Count(c => c.Value > 0);
            if (clinicsWithAppointments == 1)
                throw CreateLegacyDistributionException();

            if (counts.AppointmentsPerClinic.Values.Any(c => c > AppointmentsPerClinic))
                throw CreateLegacyDistributionException();
        }
    }

    private static bool IsExpectedTotalDistribution(LoadTestTableCounts counts)
        => counts.Appointments == AppointmentCount
           && counts.Examinations == ExaminationCount
           && counts.Vaccinations == VaccinationCount
           && counts.Payments == PaymentCount
           && IsExpectedClinicDistribution(counts);

    private static bool IsExpectedClinicDistribution(LoadTestTableCounts counts)
        => counts.AppointmentsPerClinic.Values.All(c => c == AppointmentsPerClinic)
           && counts.ExaminationsPerClinic.Values.All(c => c == ExaminationsPerClinic)
           && counts.VaccinationsPerClinic.Values.All(c => c == VaccinationsPerClinic)
           && counts.PaymentsPerClinic.Values.All(c => c == PaymentsPerClinic)
           && counts.AppointmentsPerClinic.Count == LoadTestClinicCount
           && counts.ExaminationsPerClinic.Count == LoadTestClinicCount
           && counts.VaccinationsPerClinic.Count == LoadTestClinicCount
           && counts.PaymentsPerClinic.Count == LoadTestClinicCount;

    private static InvalidOperationException CreateLegacyDistributionException()
        => new(
            "Mevcut load test verisi eski tek-klinik veya uyumsuz klinik dağılımına sahip. " +
            "VetinityCommandDb_LoadTest reset ve yeniden seed edilmeli.");

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

        if (errors.Count == 0 && !IsExpectedClinicDistribution(counts))
        {
            errors.Add(
                $"Klinik dağılımı hedefe uymuyor (beklenen: appointment {AppointmentsPerClinic}/klinik, " +
                $"examination {ExaminationsPerClinic}/klinik, vaccination {VaccinationsPerClinic}/klinik, " +
                $"payment {PaymentsPerClinic}/klinik).");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Load test seed hedef sayılara ulaşamadı: " + string.Join(", ", errors));
        }
    }

    private static async Task<LoadTestSeedContext> ResolveSeedContextAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken ct)
    {
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Name == DataSeeder.DefaultTenantName, ct);

        if (tenant is null)
            throw new InvalidOperationException("Önce DbMigrator all çalıştırılmalı.");

        var clinicSlots = await EnsureLoadTestClinicsAsync(db, tenant.Id, logger, ct);
        var clinicIds = clinicSlots.Select(s => s.ClinicId).ToArray();
        var primaryClinicId = clinicSlots[0].ClinicId;

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
            primaryClinicId,
            clinicIds,
            clinicSlots,
            speciesIds,
            vaccineDefinitions);
    }

    private static async Task<IReadOnlyList<LoadTestClinicSlot>> EnsureLoadTestClinicsAsync(
        AppDbContext db,
        Guid tenantId,
        ILogger logger,
        CancellationToken ct)
    {
        if (LoadTestClinicNames.Length != LoadTestClinicCount)
        {
            throw new InvalidOperationException(
                $"Load test klinik adları {LoadTestClinicCount} slot ile uyumlu değil.");
        }

        var slots = new List<LoadTestClinicSlot>(LoadTestClinicCount);

        for (var slot = 1; slot <= LoadTestClinicCount; slot++)
        {
            var name = LoadTestClinicNames[slot - 1];
            var clinic = await db.Clinics
                .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Name == name, ct);

            if (clinic is null)
            {
                clinic = new Clinic(tenantId, name, "İstanbul");
                await db.Clinics.AddAsync(clinic, ct);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();

                clinic = await db.Clinics
                    .AsNoTracking()
                    .FirstAsync(c => c.TenantId == tenantId && c.Name == name, ct);

                logger.LogInformation("Load test klinik oluşturuldu: {ClinicName} (slot {Slot}).", name, slot);
            }

            slots.Add(new LoadTestClinicSlot(slot, name, clinic.Id));
        }

        return slots;
    }

    private static async Task<LoadTestTableCounts> GetLoadTestCountsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        CancellationToken ct)
    {
        var clinicDistribution = await GetClinicEntityCountsAsync(db, context, ct);

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
            Appointments = clinicDistribution.AppointmentsPerClinic.Values.Sum(),
            Examinations = clinicDistribution.ExaminationsPerClinic.Values.Sum(),
            Vaccinations = clinicDistribution.VaccinationsPerClinic.Values.Sum(),
            Payments = clinicDistribution.PaymentsPerClinic.Values.Sum(),
            AppointmentsPerClinic = clinicDistribution.AppointmentsPerClinic,
            ExaminationsPerClinic = clinicDistribution.ExaminationsPerClinic,
            VaccinationsPerClinic = clinicDistribution.VaccinationsPerClinic,
            PaymentsPerClinic = clinicDistribution.PaymentsPerClinic,
            ProductCategories = await db.ProductCategories.CountAsync(
                c => c.TenantId == context.TenantId && c.Name.StartsWith("LT-Kategori-"),
                ct),
            Products = await db.Products.CountAsync(
                p => p.TenantId == context.TenantId
                     && p.Sku != null
                     && p.Sku.StartsWith("LT-SKU-"),
                ct),
            ProductStocks = await db.ProductStocks.CountAsync(
                s => s.TenantId == context.TenantId && s.ClinicId == context.PrimaryClinicId,
                ct),
            StockMovements = await db.StockMovements.CountAsync(
                m => m.TenantId == context.TenantId
                     && m.ClinicId == context.PrimaryClinicId
                     && m.Notes != null
                     && m.Notes.Contains(LoadTestMarker),
                ct),
        };
    }

    private static async Task<LoadTestClinicDistribution> GetClinicEntityCountsAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        CancellationToken ct)
    {
        var appointmentsPerClinic = await db.Appointments
            .AsNoTracking()
            .Where(a => a.TenantId == context.TenantId
                        && context.ClinicIds.Contains(a.ClinicId)
                        && a.Notes != null
                        && a.Notes.Contains(LoadTestMarker))
            .GroupBy(a => a.ClinicId)
            .Select(g => new { ClinicId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClinicId, x => x.Count, ct);

        var examinationsPerClinic = await db.Examinations
            .AsNoTracking()
            .Where(e => e.TenantId == context.TenantId
                        && context.ClinicIds.Contains(e.ClinicId)
                        && e.Notes != null
                        && e.Notes.Contains(LoadTestMarker))
            .GroupBy(e => e.ClinicId)
            .Select(g => new { ClinicId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClinicId, x => x.Count, ct);

        var vaccinationsPerClinic = await db.Vaccinations
            .AsNoTracking()
            .Where(v => v.TenantId == context.TenantId
                        && context.ClinicIds.Contains(v.ClinicId)
                        && v.Notes != null
                        && v.Notes.Contains(LoadTestMarker))
            .GroupBy(v => v.ClinicId)
            .Select(g => new { ClinicId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClinicId, x => x.Count, ct);

        var paymentsPerClinic = await db.Payments
            .AsNoTracking()
            .Where(p => p.TenantId == context.TenantId
                        && context.ClinicIds.Contains(p.ClinicId)
                        && p.Notes != null
                        && p.Notes.Contains(LoadTestMarker))
            .GroupBy(p => p.ClinicId)
            .Select(g => new { ClinicId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClinicId, x => x.Count, ct);

        EnsureAllClinicKeys(context, appointmentsPerClinic);
        EnsureAllClinicKeys(context, examinationsPerClinic);
        EnsureAllClinicKeys(context, vaccinationsPerClinic);
        EnsureAllClinicKeys(context, paymentsPerClinic);

        return new LoadTestClinicDistribution(
            appointmentsPerClinic,
            examinationsPerClinic,
            vaccinationsPerClinic,
            paymentsPerClinic);
    }

    private static void EnsureAllClinicKeys(
        LoadTestSeedContext context,
        Dictionary<Guid, int> counts)
    {
        foreach (var clinicId in context.ClinicIds)
        {
            counts.TryAdd(clinicId, 0);
        }
    }

    private static async Task LogClinicDistributionAsync(
        AppDbContext db,
        LoadTestSeedContext context,
        ILogger logger,
        CancellationToken ct)
    {
        var distribution = await GetClinicEntityCountsAsync(db, context, ct);

        foreach (var slot in context.ClinicSlots)
        {
            logger.LogInformation(
                "Load test klinik dağılımı slot {Slot} ({ClinicName}): Appointments={Appointments}, " +
                "Examinations={Examinations}, Vaccinations={Vaccinations}, Payments={Payments}",
                slot.Slot,
                slot.Name,
                distribution.AppointmentsPerClinic[slot.ClinicId],
                distribution.ExaminationsPerClinic[slot.ClinicId],
                distribution.VaccinationsPerClinic[slot.ClinicId],
                distribution.PaymentsPerClinic[slot.ClinicId]);
        }
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
                  && context.ClinicIds.Contains(a.ClinicId)
                  && a.Status == AppointmentStatus.Completed
                  && a.Notes != null
                  && a.Notes.Contains(LoadTestMarker)
            orderby a.ScheduledAtUtc, a.Id
            select new CompletedAppointmentRef(a.Id, a.ClinicId, a.PetId, p.ClientId, a.ScheduledAtUtc)
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
                  && context.ClinicIds.Contains(e.ClinicId)
                  && e.Notes != null
                  && e.Notes.Contains(LoadTestMarker)
            orderby e.ExaminedAtUtc, e.Id
            select new ExaminationRef(
                e.Id,
                e.ClinicId,
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
                .Where(s => s.TenantId == context.TenantId && s.ClinicId == context.PrimaryClinicId)
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
                    new ProductStock(context.TenantId, context.PrimaryClinicId, productId, quantity, minimum),
                    ct);

                await db.StockMovements.AddAsync(
                    new StockMovement(
                        context.TenantId,
                        context.PrimaryClinicId,
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
            var random = CreateSeededRandom(i + 1_000_000);
            var pet = petRefs[random.Next(petRefs.Count)];
            var clinicId = context.ClinicIds[i % context.ClinicIds.Count];
            var scheduledAt = ResolveAppointmentTime(i, random, now, todayStart);
            var status = ResolveAppointmentStatus(scheduledAt, random, now);
            var duration = ResolveDurationMinutes(random);
            var appointmentType = AppointmentTypes[random.Next(AppointmentTypes.Length)];

            await db.Appointments.AddAsync(
                new Appointment(
                    context.TenantId,
                    clinicId,
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

        var candidatesByClinic = completedAppointments
            .GroupBy(a => a.ClinicId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.ScheduledAtUtc).ThenBy(a => a.AppointmentId).ToList());

        foreach (var clinicId in context.ClinicIds)
        {
            if (!candidatesByClinic.TryGetValue(clinicId, out var clinicCandidates)
                || clinicCandidates.Count < ExaminationsPerClinic)
            {
                throw new InvalidOperationException(
                    $"Klinik {clinicId} için yeterli tamamlanmış randevu yok. " +
                    $"Gerekli: {ExaminationsPerClinic}, bulunan: {clinicCandidates?.Count ?? 0}.");
            }
        }

        var start = existingCount;
        var batchCounter = 0;
        var visitReasons = new[] { "Kontrol", "İshal", "Aşı sonrası takip", "Kulak kaşıntısı", "İştahsızlık" };
        var findings = new[] { "Genel durum iyi", "Hafif dehidrasyon", "Ateş yok", "Deri lezyonu yok" };

        for (var i = start; i < ExaminationCount; i++)
        {
            var clinicIndex = i / ExaminationsPerClinic;
            var localIndex = i % ExaminationsPerClinic;
            var clinicId = context.ClinicIds[clinicIndex];
            var clinicCandidates = candidatesByClinic[clinicId];
            var step = Math.Max(1, clinicCandidates.Count / ExaminationsPerClinic);
            var candidate = clinicCandidates[Math.Min(localIndex * step, clinicCandidates.Count - 1)];
            var random = CreateSeededRandom(i + 2_000_000);
            var examinedAt = candidate.ScheduledAtUtc.AddMinutes(random.Next(5, 45));

            await db.Examinations.AddAsync(
                new Examination(
                    context.TenantId,
                    candidate.ClinicId,
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

        var examinationsByClinic = examinationRefs
            .GroupBy(e => e.ClinicId)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.ExaminedAtUtc).ThenBy(e => e.ExaminationId).ToList());

        foreach (var clinicId in context.ClinicIds)
        {
            if (!examinationsByClinic.TryGetValue(clinicId, out var clinicExams)
                || clinicExams.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Klinik {clinicId} için muayene referansı bulunamadı; aşı oluşturulamıyor.");
            }
        }

        var start = existingCount;
        var batchCounter = 0;

        for (var i = start; i < VaccinationCount; i++)
        {
            var clinicIndex = i / VaccinationsPerClinic;
            var localIndex = i % VaccinationsPerClinic;
            var clinicId = context.ClinicIds[clinicIndex];
            var vaccine = context.VaccineDefinitions[i % context.VaccineDefinitions.Count];
            var random = CreateSeededRandom(i + 3_000_000);
            var isApplied = i % 10 < 6;

            DateTime? appliedAt = null;
            DateTime? dueAt = null;
            VaccinationStatus status;
            Guid? examinationId = null;
            Guid petId;
            Guid vaccinationClinicId;

            if (isApplied)
            {
                status = VaccinationStatus.Applied;
                appliedAt = now.AddDays(-random.Next(1, 365));
                var clinicExams = examinationsByClinic[clinicId];
                var exam = clinicExams[localIndex % clinicExams.Count];
                examinationId = exam.ExaminationId;
                petId = exam.PetId;
                vaccinationClinicId = exam.ClinicId;
            }
            else
            {
                status = VaccinationStatus.Scheduled;
                dueAt = now.AddDays(random.Next(1, 91));
                var pet = petRefs[random.Next(petRefs.Count)];
                petId = pet.PetId;
                vaccinationClinicId = clinicId;
            }

            await db.Vaccinations.AddAsync(
                new Vaccination(
                    context.TenantId,
                    petId,
                    vaccinationClinicId,
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

        var appointmentsByClinic = completedAppointments
            .GroupBy(a => a.ClinicId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.ScheduledAtUtc).ThenBy(a => a.AppointmentId).ToList());

        var examinationsByClinic = examinationRefs
            .GroupBy(e => e.ClinicId)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.ExaminedAtUtc).ThenBy(e => e.ExaminationId).ToList());

        foreach (var clinicId in context.ClinicIds)
        {
            if (!appointmentsByClinic.ContainsKey(clinicId) || !examinationsByClinic.ContainsKey(clinicId))
            {
                throw new InvalidOperationException(
                    $"Klinik {clinicId} için ödeme kaynağı (randevu/muayene) bulunamadı.");
            }
        }

        var start = existingCount;
        var batchCounter = 0;

        for (var i = start; i < PaymentCount; i++)
        {
            var clinicIndex = i / PaymentsPerClinic;
            var localIndex = i % PaymentsPerClinic;
            var clinicId = context.ClinicIds[clinicIndex];
            var random = CreateSeededRandom(i + 4_000_000);
            var paidAt = now.AddDays(-random.Next(0, 180)).AddHours(random.Next(8, 20));
            var amount = Math.Round((decimal)random.Next(5_000, 200_000) / 100m, 2);
            var method = PaymentMethods[random.Next(PaymentMethods.Length)];

            Guid paymentClinicId;
            Guid clientId;
            Guid? petId;
            Guid? appointmentId = null;
            Guid? examinationId = null;

            var clinicExams = examinationsByClinic[clinicId];
            var clinicAppts = appointmentsByClinic[clinicId];

            if (localIndex % 5 == 0)
            {
                var exam = clinicExams[localIndex % clinicExams.Count];
                paymentClinicId = exam.ClinicId;
                clientId = exam.ClientId;
                petId = exam.PetId;
                examinationId = exam.ExaminationId;
                appointmentId = exam.AppointmentId == Guid.Empty ? null : exam.AppointmentId;
            }
            else if (localIndex % 5 == 1)
            {
                var appointment = clinicAppts[localIndex % clinicAppts.Count];
                paymentClinicId = appointment.ClinicId;
                clientId = appointment.ClientId;
                petId = appointment.PetId;
                appointmentId = appointment.AppointmentId;
            }
            else
            {
                var pet = petRefs[random.Next(petRefs.Count)];
                paymentClinicId = clinicId;
                clientId = pet.ClientId;
                petId = pet.PetId;
            }

            await db.Payments.AddAsync(
                new Payment(
                    context.TenantId,
                    paymentClinicId,
                    clientId,
                    petId,
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
        Guid PrimaryClinicId,
        IReadOnlyList<Guid> ClinicIds,
        IReadOnlyList<LoadTestClinicSlot> ClinicSlots,
        IReadOnlyList<Guid> SpeciesIds,
        IReadOnlyList<VaccineDefinitionRef> VaccineDefinitions);

    private sealed record LoadTestClinicSlot(int Slot, string Name, Guid ClinicId);

    private sealed record LoadTestClinicDistribution(
        Dictionary<Guid, int> AppointmentsPerClinic,
        Dictionary<Guid, int> ExaminationsPerClinic,
        Dictionary<Guid, int> VaccinationsPerClinic,
        Dictionary<Guid, int> PaymentsPerClinic);

    private readonly record struct VaccineDefinitionRef(Guid Id, string Name);

    private readonly record struct PetRef(Guid PetId, Guid ClientId);

    private readonly record struct CompletedAppointmentRef(
        Guid AppointmentId,
        Guid ClinicId,
        Guid PetId,
        Guid ClientId,
        DateTime ScheduledAtUtc);

    private readonly record struct ExaminationRef(
        Guid ExaminationId,
        Guid ClinicId,
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
        public Dictionary<Guid, int> AppointmentsPerClinic { get; set; } = [];
        public Dictionary<Guid, int> ExaminationsPerClinic { get; set; } = [];
        public Dictionary<Guid, int> VaccinationsPerClinic { get; set; } = [];
        public Dictionary<Guid, int> PaymentsPerClinic { get; set; } = [];
        public int ProductCategories { get; set; }
        public int Products { get; set; }
        public int ProductStocks { get; set; }
        public int StockMovements { get; set; }
    }
}
