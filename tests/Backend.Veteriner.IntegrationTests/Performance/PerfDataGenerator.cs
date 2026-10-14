using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.IntegrationTests.Performance;

/// <summary>Pilot hacmi için sentetik veri boyutları (Aşama 9 Dilim 9).</summary>
public sealed record PerfScale(int Clients, int PetsPerClient, int Days, int VisitsPerDay, int Products)
{
    public static PerfScale FromEnvironment() => new(
        Read("PERF_CLIENTS", 1000),
        Read("PERF_PETS_PER_CLIENT", 2),
        Read("PERF_DAYS", 60),
        Read("PERF_VISITS_PER_DAY", 20),
        Read("PERF_PRODUCTS", 300));

    private static int Read(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;
}

public sealed record PerfSeedResult(
    Guid TenantId,
    Guid ClinicId,
    Guid Clinic500Id,
    Guid Clinic501Id,
    Guid UserId,
    Guid HotPetId,
    Guid TypicalPetId,
    Guid TypicalClientId,
    Guid ProductId,
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyDictionary<string, int> Counts);

/// <summary>
/// Yerel, tek kullanıcılı, deterministik (sabit tohum) test veri üreticisi. YALNIZ entegrasyon test
/// veritabanında çalışır: çağıran, IntegrationTestDatabaseGuard ile doğrulanmış bir <see cref="AppDbContext"/>
/// vermelidir. Alan kurallarını domain fabrikaları uygular; her gün tek SaveChanges ile yazılır ve izleyici
/// temizlenir (düşük bellek).
/// </summary>
internal static class PerfDataGenerator
{
    private static readonly string[] FirstNames =
        ["Ayşe", "Mehmet", "Fatma", "Mustafa", "Zeynep", "Ahmet", "Elif", "Hüseyin", "Şule", "İbrahim", "Çağla", "Ömer", "Gülşen", "Ünal", "Özlem"];

    private static readonly string[] LastNames =
        ["Yılmaz", "Kaya", "Demir", "Çelik", "Şahin", "Yıldız", "Öztürk", "Aydın", "Özdemir", "Arslan", "Doğan", "Kılıç", "Çetin", "Koç", "Kurt"];

    private static readonly string[] PetNames =
        ["Pamuk", "Karamel", "Zeytin", "Boncuk", "Çılgın", "Duman", "Şila", "Max", "Luna", "Tarçın", "Mia", "Rocky"];

    public static async Task<PerfSeedResult> SeedAsync(AppDbContext db, PerfScale scale, Guid tenantId, Guid clinicId, Guid userId)
    {
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        var rnd = new Random(20261014);
        var counts = new Dictionary<string, int>();
        void Bump(string key, int n = 1) => counts[key] = counts.GetValueOrDefault(key) + n;

        var speciesIds = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).Take(3).ToListAsync();
        var vaccineIds = await db.VaccineDefinitions.Select(v => v.Id).Take(8).ToListAsync();

        var clinic500 = new Clinic(tenantId, "Perf-500", "Izmir");
        var clinic501 = new Clinic(tenantId, "Perf-501", "Izmir");
        db.Clinics.AddRange(clinic500, clinic501);
        db.UserClinics.AddRange(new UserClinic(userId, clinic500.Id), new UserClinic(userId, clinic501.Id));
        await Flush(db);

        // Müşteri + hayvan
        var clientIds = new List<Guid>(scale.Clients);
        var petIds = new List<Guid>(scale.Clients * scale.PetsPerClient);
        var petClient = new Dictionary<Guid, Guid>();
        for (var i = 0; i < scale.Clients; i++)
        {
            var name = $"{FirstNames[rnd.Next(FirstNames.Length)]} {LastNames[rnd.Next(LastNames.Length)]} {i:D5}";
            var client = new Client(tenantId, name, $"90555{3_000_000 + i:D7}", i % 3 == 0 ? $"musteri{i}@example.com" : null);
            db.Clients.Add(client);
            clientIds.Add(client.Id);
            for (var p = 0; p < scale.PetsPerClient; p++)
            {
                var pet = new Pet(
                    tenantId, client.Id, $"{PetNames[rnd.Next(PetNames.Length)]}{i}-{p}", speciesIds[rnd.Next(speciesIds.Count)],
                    microchipNumber: p == 0 && i % 2 == 0 ? $"9000{i:D11}" : null);
                db.Pets.Add(pet);
                petIds.Add(pet.Id);
                petClient[pet.Id] = client.Id;
            }

            if (i % 500 == 499)
                await Flush(db);
        }

        await Flush(db);
        Bump("Clients", clientIds.Count);
        Bump("Pets", petIds.Count);

        // Ürün, parti, stok
        var products = new List<Product>();
        for (var i = 0; i < scale.Products; i++)
        {
            var kind = i % 10 == 0 ? ProductKind.Vaccine : i % 3 == 0 ? ProductKind.Medicine : ProductKind.Other;
            var controlled = kind == ProductKind.Medicine && i % 9 == 0 ? ControlledSubstanceClass.Narcotic : ControlledSubstanceClass.None;
            products.Add(new Product(tenantId, $"Ürün {i:D4}", "adet", 50 + i, "TRY", sku: $"SKU{i:D5}", kind: kind, controlledClass: controlled));
        }

        db.Products.AddRange(products);
        await Flush(db);
        var movementCount = 0;
        var batchesByProduct = new Dictionary<Guid, Guid>(); // ürün → bir parti kimliği (çıkış hareketleri için)
        foreach (var product in products)
        {
            db.ProductStocks.Add(new ProductStock(tenantId, clinicId, product.Id, 500, 20));
            for (var b = 0; b < 4; b++)
            {
                var batch = StockBatch.Create(
                    tenantId, clinicId, product, 125,
                    new StockBatchDetails($"LOT-{LotSuffix(product)}-{b}", DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6 + b * 3)), "Tedarikçi A.Ş."),
                    userId, DateTime.UtcNow.AddDays(-scale.Days)).Value!;
                db.StockBatches.Add(batch);
                var entry = new StockMovement(
                    tenantId, clinicId, product.Id, StockMovementType.In, 125, DateTime.UtcNow.AddDays(-scale.Days - 1));
                entry.LinkBatch(batch);
                db.StockMovements.Add(entry);
                movementCount++;
                batchesByProduct[product.Id] = batch.Id;
            }
        }

        await Flush(db);
        Bump("Products", products.Count);
        Bump("StockBatches", products.Count * 4);

        // Günlük akış
        var hotPet = petIds[0];
        var activeCursor = 0;
        var todayUtc = DateTime.UtcNow.Date;
        var from = todayUtc.AddDays(-(scale.Days - 1));
        for (var d = 0; d < scale.Days; d++)
        {
            var day = from.AddDays(d);
            var isToday = d == scale.Days - 1;
            for (var v = 0; v < scale.VisitsPerDay; v++)
            {
                var state = isToday ? v % 3 : 2; // bugün: bekliyor/devam/tamamlandı karışık
                // Hayvan başına tek aktif geliş kuralı (UX_Visits_TenantId_PetId_Active): aktif gelişler benzersiz hayvan alır.
                var petId = state != 2 ? NextActivePet(petIds, ref activeCursor)
                    : v == 0 && d % 3 == 0 ? hotPet : petIds[rnd.Next(petIds.Count)];
                var clientId = petClient[petId];
                var arrived = day.AddHours(1 + rnd.NextDouble() * 9);
                var visit = new Visit(tenantId, clinicId, petId, null, null, userId, arrived);
                if (state >= 1) visit.Start(arrived.AddMinutes(5));
                if (state == 2) visit.Complete(arrived.AddMinutes(25));
                db.Visits.Add(visit);
                Bump("Visits");

                if (state != 2)
                    continue;

                var exam = new Examination(
                    tenantId, clinicId, petId, null, arrived.AddMinutes(10), "Genel kontrol", "Bulgu metni", "Değerlendirme", null,
                    weightKg: 5 + (decimal)rnd.NextDouble() * 20, visitId: visit.Id);
                if (rnd.Next(10) < 9) exam.Finalize(userId, arrived.AddMinutes(24));
                db.Examinations.Add(exam);
                Bump("Examinations");

                if (rnd.Next(100) < 30)
                {
                    var rx = new Prescription(tenantId, clinicId, petId, exam.Id, null, arrived.AddMinutes(20), "Reçete", "Günde 2 kez", null, null);
                    var prod = products[rnd.Next(products.Count)];
                    rx.ReplaceItems([new PrescriptionItemDraft(prod.Name, prod.Id, "Yemekle birlikte", 1)]);
                    db.Prescriptions.Add(rx);
                    Bump("Prescriptions");
                }

                if (vaccineIds.Count > 0 && rnd.Next(100) < 25)
                {
                    db.Vaccinations.Add(new Vaccination(
                        tenantId, petId, clinicId, null, vaccineIds[rnd.Next(vaccineIds.Count)], "Karma aşı",
                        VaccinationStatus.Applied, arrived.AddMinutes(15), arrived.AddYears(1), null));
                    Bump("Vaccinations");
                }

                // Ücret(ler) + ödeme (≈%8 borçlu kalır)
                var charges = new List<Charge>();
                var chargeCount = 1 + rnd.Next(3);
                for (var c = 0; c < chargeCount; c++)
                {
                    charges.Add(Charge.Create(
                        tenantId, clinicId, clientId, petId, visit.Id, null, "Hizmet", 1m, 100 + rnd.Next(1900), userId, arrived.AddMinutes(25), null).Value!);
                }

                if (rnd.Next(100) >= 8)
                {
                    var total = charges.Sum(c => c.Amount);
                    var payment = new Payment(
                        tenantId, clinicId, clientId, petId, null, null, total, "TRY",
                        rnd.Next(2) == 0 ? PaymentMethod.Cash : PaymentMethod.Card, arrived.AddMinutes(30), null);
                    var allocation = payment.AllocateFully(charges.Select(c => (c, c.Amount)).ToList(), userId, arrived.AddMinutes(30));
                    db.Payments.Add(payment);
                    db.PaymentAllocations.AddRange(allocation.Value!);
                    Bump("Payments");
                }

                db.Charges.AddRange(charges);
                Bump("Charges", charges.Count);

                // Stok çıkışı
                if (rnd.Next(100) < 70)
                {
                    var product = products[rnd.Next(products.Count)];
                    var outMovement = new StockMovement(
                        tenantId, clinicId, product.Id, StockMovementType.Out, 1, arrived.AddMinutes(20),
                        referenceType: "Visit", referenceId: visit.Id, createdByUserId: userId);
                    // İzleyici her gün temizlendiği için parti bağı gezinti özelliği olmadan, yalnız kimlikle kurulur.
                    typeof(StockMovement).GetProperty(nameof(StockMovement.BatchId))!.SetValue(outMovement, batchesByProduct[product.Id]);
                    typeof(StockMovement).GetProperty(nameof(StockMovement.BatchBalanceAfter))!.SetValue(outMovement, 100m);
                    db.StockMovements.Add(outMovement);
                    movementCount++;
                }
            }

            // Randevular
            for (var a = 0; a < scale.VisitsPerDay / 2; a++)
            {
                var status = (AppointmentStatus)(rnd.Next(10) < 7 ? 1 : rnd.Next(3) == 0 ? 3 : 2);
                db.Appointments.Add(new Appointment(
                    tenantId, clinicId, petIds[rnd.Next(petIds.Count)], day.AddHours(1 + rnd.NextDouble() * 9),
                    appointmentType: AppointmentType.Examination, initialStatus: isToday ? AppointmentStatus.Scheduled : status));
                Bump("Appointments");
            }

            await Flush(db);
        }

        Bump("StockMovements", movementCount);

        // Bugün 500 / 501 sınırı (ayrı klinikler)
        foreach (var (target, count) in new[] { (clinic500.Id, 500), (clinic501.Id, 501) })
        {
            for (var i = 0; i < count; i++)
            {
                db.Visits.Add(new Visit(tenantId, target, NextActivePet(petIds, ref activeCursor), null, null, userId, todayUtc.AddMinutes(1 + i)));
                if (i % 500 == 499) await Flush(db);
            }

            await Flush(db);
        }

        var typical = petIds[petIds.Count / 2];
        return new PerfSeedResult(
            tenantId, clinicId, clinic500.Id, clinic501.Id, userId, hotPet, typical, petClient[typical], products[1].Id,
            from, todayUtc.AddDays(1), counts);
    }

    private static Guid NextActivePet(List<Guid> petIds, ref int cursor)
        => cursor < petIds.Count - 1
            ? petIds[^(++cursor)]
            : throw new InvalidOperationException("Aktif geliş için yeterli benzersiz hayvan yok; PERF_CLIENTS artırın (≥ 1101 hayvan gerekir).");

    private static string LotSuffix(Product p) => p.Id.ToString("N")[..8];

    private static async Task Flush(AppDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}
