using System.Security.Cryptography;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Tenants.ExampleClinic;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.CareInstructions;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Sales;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.VitalMeasurements;
using Backend.Veteriner.Domain.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Tanıtım (sunum) kliniği: verilen kiracının kliniğini her ekranı dolu olacak biçimde kurgusal veriyle doldurur
/// (DbMigrator <c>seed-demo-clinic</c>). Gerçek kişi verisi yoktur. Tekrar çalıştırılabilir: önce Örnek Klinik verisi ve ona
/// bağlı her kayıt temizlenir (<see cref="IExampleClinicCleaner"/>), sonra bugüne göre yeniden üretilir
/// (<see cref="IExampleClinicLoader"/>) ve Örnek Klinik'in kapsamadığı ekranlar (yatış, reçete, lab/görüntü eklentileri,
/// kontrollü ilaç defteri, satış, bugünkü gelişler, online talep, hatırlatma günlüğü, portal) eklenir. Klinik düzeyindeki
/// ayarlar (paket, yapay zekâ, e-belge mükellef ayarı, web sitesi) yoksa kurulur, varsa korunur.
/// <para>Tek işlemde çalışır; dosya içeriği diske işlem dışında yazılır (geri alınırsa sahipsiz dosyayı bakım işi siler).</para>
/// </summary>
public sealed class DemoClinicSeeder
{
    private const string AssetPrefix = "DemoClinic.";
    private const string AiGrantNote = "Tanıtım kliniği";
    private const string PortalEmail = "demo.sahip@example.com";

    /// <summary>Nilvera test ortamındaki e-Fatura mükellefi (kontrol hanesi geçerli); kurumsal alıcı örneği.</summary>
    private const string EInvoiceBuyerTaxNumber = "1111113181";

    /// <summary>Kontrol hanesi geçerli uydurma VKN (klinik mükellef ayarı; satıcı bilgisi entegratör hesabından gelir).</summary>
    private const string ClinicTaxNumber = "1234567808";

    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IExampleClinicCleaner _cleaner;
    private readonly IExampleClinicLoader _loader;
    private readonly IFileStorage _fileStorage;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPaymentIntegrationEventOutbox _paymentEvents;
    private readonly ILogger<DemoClinicSeeder> _logger;

    public DemoClinicSeeder(
        AppDbContext db,
        TimeProvider timeProvider,
        IExampleClinicCleaner cleaner,
        IExampleClinicLoader loader,
        IFileStorage fileStorage,
        IPasswordHasher passwordHasher,
        IPaymentIntegrationEventOutbox paymentEvents,
        ILogger<DemoClinicSeeder> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _cleaner = cleaner;
        _loader = loader;
        _fileStorage = fileStorage;
        _passwordHasher = passwordHasher;
        _paymentEvents = paymentEvents;
        _logger = logger;
    }

    /// <param name="portalPassword">Örnek sahibin portal parolası; boşsa portal hesabı kurulmaz.</param>
    /// <param name="aiCredits">Kliniğe bir kez verilecek yapay zekâ kredisi; 0 ise verilmez.</param>
    public async Task<IReadOnlyList<string>> RunAsync(Guid tenantId, string? portalPassword, int aiCredits, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new InvalidOperationException($"Kiracı bulunamadı: {tenantId}.");
        var clinic = await _db.Clinics.Where(c => c.TenantId == tenantId && c.IsActive).OrderBy(c => c.Name).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Kiracının etkin kliniği yok.");
        var userId = await _db.UserTenants.Where(u => u.TenantId == tenantId).OrderBy(u => u.CreatedAtUtc).Select(u => u.UserId).FirstOrDefaultAsync(ct);
        if (userId == Guid.Empty)
            throw new InvalidOperationException("Kiracının kullanıcısı yok.");

        var report = new List<string>();
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        report.Add(await RemoveTestEDocumentsAsync(tenantId, ct));
        var cleared = await _cleaner.ClearAsync(tenantId, ct);
        report.Add($"Önceki örnek veri temizlendi ({cleared.Clients} müşteri, {cleared.Pets} hasta).");
        var loaded = await _loader.LoadAsync(tenantId, clinic.Id, userId, ct);
        report.Add($"Örnek klinik: {loaded.Clients} müşteri, {loaded.Pets} hasta, {loaded.Appointments} randevu, {loaded.Examinations} muayene, {loaded.Payments} ödeme.");

        var ctx = await ContextAsync(tenant, clinic, userId, ct);
        report.Add(await EnsureClinicProfileAsync(ctx, ct));
        report.Add(await EnsurePremiumPlanAsync(ctx, ct));
        report.Add(await EnsureAiAsync(ctx, aiCredits, ct));
        report.Add(await EnsureEDocumentSetupAsync(ctx, ct));
        report.Add(await WriteHospitalizationsAsync(ctx, ct));
        report.Add(await WritePrescriptionsAsync(ctx, ct));
        report.Add(await WriteLabAndImagingAsync(ctx, ct));
        report.Add(await WriteControlledDrugAsync(ctx, ct));
        report.Add(await WriteVaccineAdministrationsAsync(ctx, ct));
        report.Add(await WriteSaleAsync(ctx, ct));
        report.Add(await WriteTodayVisitsAsync(ctx, ct));
        report.Add(await WriteAppointmentRequestsAsync(ctx, ct));
        report.Add(await WriteReminderLogsAsync(ctx, ct));
        report.Add(await EnsurePortalAsync(ctx, portalPassword, ct));
        report.Add(await EnsureSiteAsync(ctx, ct));

        await transaction.CommitAsync(ct);
        _logger.LogInformation("Tanıtım kliniği dolduruldu. TenantId={TenantId} ClinicId={ClinicId}", tenantId, clinic.Id);
        return report;
    }

    /// <summary>
    /// Sunumda örnek müşterilere kesilen e-belgeler müşteri ve ücret silmeyi engeller (kısıtlı yabancı anahtar). Yalnız test
    /// ortamı belgeleri silinir; GİB'e gitmiş (canlı) belge varsa hiçbir şey silinmez, komut durur.
    /// </summary>
    private async Task<string> RemoveTestEDocumentsAsync(Guid tenantId, CancellationToken ct)
    {
        var exampleClientIds = _db.Clients
            .Where(c => c.TenantId == tenantId && c.Address != null && c.Address.StartsWith(ExampleClinicDataGenerator.TagPrefix))
            .Select(c => c.Id);
        var documents = _db.EDocuments.Where(d => d.TenantId == tenantId && exampleClientIds.Contains(d.ClientId));
        if (await documents.AnyAsync(d => d.Environment != EDocumentEnvironment.Test, ct))
            throw new InvalidOperationException("Örnek müşterilerde canlı (GİB'e gitmiş) e-belge var; tanıtım verisi temizlenmedi.");

        var documentIds = documents.Select(d => d.Id);
        await _db.Charges.Where(c => c.EDocumentId != null && documentIds.Contains(c.EDocumentId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.EDocumentId, (Guid?)null), ct);
        var removed = await documents.ExecuteDeleteAsync(ct);
        return $"Önceki test e-belgeleri silindi: {removed}.";
    }

    // ---------------------------------------------------------------------------------------------
    // Klinik düzeyi ayarlar (yoksa kurulur)
    // ---------------------------------------------------------------------------------------------

    private async Task<string> EnsureClinicProfileAsync(Ctx ctx, CancellationToken ct)
    {
        var clinic = ctx.Clinic;
        if (string.IsNullOrWhiteSpace(clinic.Phone) || string.IsNullOrWhiteSpace(clinic.Address))
        {
            clinic.UpdateDetails(
                clinic.Name, clinic.City,
                string.IsNullOrWhiteSpace(clinic.Phone) ? "0216 000 00 00" : clinic.Phone,
                clinic.Email,
                string.IsNullOrWhiteSpace(clinic.Address) ? "Caferağa Mah. Örnek Sok. No:1" : clinic.Address,
                clinic.Description,
                clinic.District ?? "Kadıköy");
        }

        var hasHours = await _db.ClinicWorkingHours.AnyAsync(h => h.ClinicId == clinic.Id, ct);
        if (!hasHours)
        {
            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                var hours = day switch
                {
                    DayOfWeek.Sunday => ClinicWorkingHour.Create(ctx.TenantId, clinic.Id, day, true, null, null, null, null, ctx.NowUtc),
                    DayOfWeek.Saturday => ClinicWorkingHour.Create(ctx.TenantId, clinic.Id, day, false, new TimeOnly(10, 0), new TimeOnly(16, 0), null, null, ctx.NowUtc),
                    _ => ClinicWorkingHour.Create(ctx.TenantId, clinic.Id, day, false, new TimeOnly(9, 0), new TimeOnly(19, 0), new TimeOnly(13, 0), new TimeOnly(14, 0), ctx.NowUtc),
                };
                _db.ClinicWorkingHours.Add(hours);
            }
        }

        await _db.SaveChangesAsync(ct);
        return hasHours ? "Klinik bilgileri tamam." : "Klinik çalışma saatleri eklendi.";
    }

    private async Task<string> EnsurePremiumPlanAsync(Ctx ctx, CancellationToken ct)
    {
        var subscription = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == ctx.TenantId, ct);
        if (subscription is null)
            return "Abonelik kaydı yok; paket değiştirilmedi.";
        if (subscription is { Status: TenantSubscriptionStatus.Active, PlanCode: SubscriptionPlanCode.Premium })
            return "Paket zaten Kurumsal (etkin).";

        subscription.ActivatePaidPlan(SubscriptionPlanCode.Premium, BillingPeriodKind.Yearly, ctx.NowUtc);
        await _db.SaveChangesAsync(ct);
        return "Paket Kurumsal (yıllık, etkin) yapıldı.";
    }

    private async Task<string> EnsureAiAsync(Ctx ctx, int credits, CancellationToken ct)
    {
        var settings = await _db.ClinicAiSettings.FirstOrDefaultAsync(s => s.ClinicId == ctx.Clinic.Id, ct);
        if (settings is null)
        {
            settings = ClinicAiSettings.Create(ctx.TenantId, ctx.Clinic.Id, ctx.NowUtc);
            _db.ClinicAiSettings.Add(settings);
        }

        settings.SetEnabled(true, ctx.UserId, ctx.NowUtc);

        var granted = credits > 0
            && !await _db.AiCreditGrants.AnyAsync(g => g.ClinicId == ctx.Clinic.Id && g.Note == AiGrantNote, ct);
        if (granted)
            _db.AiCreditGrants.Add(AiCreditGrant.CreateManual(ctx.TenantId, ctx.Clinic.Id, credits, AiGrantNote, ctx.UserId, ctx.NowUtc));

        await _db.SaveChangesAsync(ct);
        return granted ? $"Yapay zekâ açık; {credits} kredi verildi." : "Yapay zekâ açık (kredi daha önce verilmiş).";
    }

    private async Task<string> EnsureEDocumentSetupAsync(Ctx ctx, CancellationToken ct)
    {
        if (!await _db.ClinicTaxpayerSettings.AnyAsync(s => s.ClinicId == ctx.Clinic.Id, ct))
        {
            var created = ClinicTaxpayerSettings.Create(
                ctx.TenantId, ctx.Clinic.Id, ClinicTaxpayerType.Company, ClinicTaxNumber, "Kadıköy",
                $"{ctx.Clinic.Name} Veteriner Hizmetleri Ltd. Şti.", "İstanbul", "Kadıköy", ctx.Clinic.Address,
                TaxRatePercent.DefaultVatRate, null, ctx.UserId, ctx.NowUtc);
            if (!created.IsSuccess)
                throw new InvalidOperationException($"Mükellef ayarı yazılamadı: {created.Error.Code}");
            _db.ClinicTaxpayerSettings.Add(created.Value!);
        }

        if (!await _db.ClinicEDocumentSettings.AnyAsync(s => s.ClinicId == ctx.Clinic.Id, ct))
            _db.ClinicEDocumentSettings.Add(ClinicEDocumentSettings.Create(ctx.TenantId, ctx.Clinic.Id, ctx.UserId, ctx.NowUtc));

        // Bireysel müşterilere fatura adresi (entegratör alıcıda il/ilçe ister).
        foreach (var client in ctx.Clients)
        {
            var (addressLine, district) = SplitAddress(client.Address);
            var applied = client.ApplyInvoicingIdentity(ClientPersonType.Individual, null, null, null, null, "İstanbul", district, addressLine, ctx.NowUtc);
            if (!applied.IsSuccess)
                throw new InvalidOperationException($"Fatura bilgisi yazılamadı ({client.FullName}): {applied.Error.Code}");
        }

        // e-Fatura mükellefi kurumsal müşteri: ödenmemiş ücretiyle e-Fatura kesimi gösterilir.
        var company = new Client(ctx.TenantId, "Demo Tarım Hayvancılık Ltd. Şti.", ctx.NowUtc, phone: "05000000099",
            email: "muhasebe@example.com", address: Tag("Fikirtepe Mah. Örnek Cad. No:5, Kadıköy/İstanbul"));
        var corporate = company.ApplyInvoicingIdentity(
            ClientPersonType.Corporate, null, EInvoiceBuyerTaxNumber, "Kadıköy", "Demo Tarım Hayvancılık Ltd. Şti.",
            "İstanbul", "Kadıköy", "Fikirtepe Mah. Örnek Cad. No:5", ctx.NowUtc);
        if (!corporate.IsSuccess)
            throw new InvalidOperationException($"Kurumsal müşteri fatura bilgisi yazılamadı: {corporate.Error.Code}");
        _db.Clients.Add(company);
        var dog = ctx.Pets.First(p => p.SpeciesId == ctx.DogSpeciesId);
        var guard = new Pet(ctx.TenantId, company.Id, "Paşa", ctx.DogSpeciesId, breed: "Kangal Çoban Köpeği (Kangal)",
            birthDate: ctx.Today.AddYears(-5), gender: PetGender.Male, weight: 46m, notes: Tag("Çiftlik bekçi köpeği."), colorId: dog.ColorId);
        _db.Pets.Add(guard);
        AddCharge(ctx, company.Id, guard.Id, null, "Sürü sağlık kontrolü ve aşı planlaması", 2500m, ctx.NowUtc.AddHours(-2));

        await _db.SaveChangesAsync(ct);
        return "e-Belge mükellef ayarı ve fatura bilgileri hazır (otomatik kesim ayarı korunur).";
    }

    // ---------------------------------------------------------------------------------------------
    // Klinik kayıtlar
    // ---------------------------------------------------------------------------------------------

    private async Task<string> WriteHospitalizationsAsync(Ctx ctx, CancellationToken ct)
    {
        var ward = await _db.HospitalizationWards.FirstOrDefaultAsync(w => w.ClinicId == ctx.Clinic.Id && w.Name == "Köpek servisi", ct);
        HospitalizationRoom? room = null;
        if (ward is null)
        {
            ward = HospitalizationWard.Create(ctx.TenantId, ctx.Clinic.Id, "Köpek servisi", 1, ctx.NowUtc);
            _db.HospitalizationWards.Add(ward);
            room = HospitalizationRoom.Create(ward, "K-1", 1, ctx.NowUtc);
            _db.HospitalizationRooms.Add(room);
            _db.HospitalizationRooms.Add(HospitalizationRoom.Create(ward, "K-2", 2, ctx.NowUtc));
        }
        else
        {
            room = await _db.HospitalizationRooms.FirstOrDefaultAsync(r => r.WardId == ward.Id, ct);
        }

        var max = ctx.Pet("Max");
        var admittedAt = ctx.NowUtc.AddHours(-20);
        var active = new Hospitalization(ctx.TenantId, ctx.Clinic.Id, max.Id, ctx.LatestExamId(max.Id), admittedAt, ctx.NowUtc.AddDays(1),
            "Akut gastroenterit, sıvı tedavisi ve gözlem", Tag("Kusma ve ishal; dehidrasyon %6."), ctx.NowUtc);
        if (room is not null)
            active.AssignRoom(room, ward);
        _db.Hospitalizations.Add(active);

        var vitals = new (int Hours, decimal Temp, int Hr, int Rr, string Note)[]
        {
            (1, 39.6m, 128, 32, "Kabulde ateşli, mukozalar kuru."),
            (7, 39.1m, 116, 28, "Sıvı tedavisi sürüyor; kusma yok."),
            (13, 38.8m, 104, 24, "Su içti, iştah az."),
            (19, 38.5m, 96, 22, "Genel durum iyi; yumuşak gaita."),
        };
        foreach (var v in vitals)
        {
            var values = VitalSigns.Create(v.Temp, v.Hr, v.Rr, null);
            var measurement = VitalMeasurement.Create(ctx.TenantId, ctx.Clinic.Id, max.Id, active.Id, null, admittedAt.AddHours(v.Hours),
                values.Value!, v.Note, ctx.UserId, ctx.NowUtc);
            if (!measurement.IsSuccess)
                throw new InvalidOperationException($"Vital ölçüm yazılamadı: {measurement.Error.Code}");
            _db.VitalMeasurements.Add(measurement.Value!);
        }

        _db.CareInstructions.Add(new CareInstruction(ctx.TenantId, ctx.Clinic.Id, max.Id, active.Id, null, null, ctx.UserId,
            "Ringer laktat 60 ml/saat IV.\nMaropitant 1 mg/kg SC günde bir.\n12 saat yiyecek yok, sonra az az bağırsak dostu mama.\n6 saatte bir ateş, nabız, solunum.",
            ctx.NowUtc));

        var tarcin = ctx.Pet("Tarçın");
        var past = new Hospitalization(ctx.TenantId, ctx.Clinic.Id, tarcin.Id, ctx.LatestExamId(tarcin.Id), ctx.NowUtc.AddDays(-6), null,
            "Kronik böbrek yetmezliği, sıvı tedavisi", Tag("Yaşlı hasta; iştahsızlık."), ctx.NowUtc);
        var discharged = past.Discharge(ctx.NowUtc.AddDays(-4), "Kreatinin düştü; evde subkutan sıvı ve böbrek diyeti.", ctx.NowUtc);
        if (!discharged.IsSuccess)
            throw new InvalidOperationException($"Taburcu yazılamadı: {discharged.Error.Code}");
        _db.Hospitalizations.Add(past);

        await _db.SaveChangesAsync(ct);
        return "Yatış: 1 yatan (vital ölçüm ve bakım planıyla), 1 taburcu.";
    }

    private async Task<string> WritePrescriptionsAsync(Ctx ctx, CancellationToken ct)
    {
        var drafts = new (string Title, string Content, PrescriptionItemDraft[] Items)[]
        {
            ("Antibiyotik tedavisi", "7 gün düzenli kullanınız; kontrol muayenesine geliniz.",
                [new("Amoksisilin-Klavulanik Asit 250 mg Tablet", null, "Günde 2 kez, yemekle", 14m, "1 tablet", "tablet")]),
            ("Ağrı kesici", "Aç karna vermeyiniz.",
                [new("Meloksikam Oral Süspansiyon 10 ml", null, "Günde 1 kez, mamayla", 1m, "0,1 ml/kg", "şişe")]),
            ("Kulak tedavisi", "Temizlikten sonra damlatınız.",
                [new("Kulak Temizleme Solüsyonu 100 ml", null, "Günde 1 kez", 1m, "1 ml", "şişe"),
                 new("Kulak damlası (antibiyotikli)", null, "Günde 2 kez, 5 damla", 1m, "5 damla", "şişe")]),
        };

        var exams = ctx.Examinations.Where(e => e.Status == ExaminationStatus.Draft).OrderByDescending(e => e.ExaminedAtUtc).Take(drafts.Length).ToList();
        for (var i = 0; i < drafts.Length; i++)
        {
            var exam = i < exams.Count ? exams[i] : null;
            var petId = exam?.PetId ?? ctx.Pets[i].Id;
            var at = exam?.ExaminedAtUtc.AddMinutes(25) ?? ctx.NowUtc.AddDays(-i - 1);
            var prescription = new Prescription(ctx.TenantId, ctx.Clinic.Id, petId, exam?.Id, null, at, drafts[i].Title, drafts[i].Content,
                Tag("Tanıtım reçetesi."), at.AddDays(10), ctx.NowUtc);
            var items = prescription.ReplaceItems(drafts[i].Items);
            if (!items.IsSuccess)
                throw new InvalidOperationException($"Reçete kalemi yazılamadı: {items.Error.Code}");
            _db.Prescriptions.Add(prescription);
        }

        await _db.SaveChangesAsync(ct);
        return $"Reçete: {drafts.Length}.";
    }

    private async Task<string> WriteLabAndImagingAsync(Ctx ctx, CancellationToken ct)
    {
        var karamel = ctx.Pet("Karamel");
        var examId = ctx.LatestExamId(karamel.Id);
        var resultAt = ctx.NowUtc.AddDays(-1);

        var lab = new LabResult(ctx.TenantId, ctx.Clinic.Id, karamel.Id, examId, null, ctx.UserId, resultAt,
            "Hemogram ve biyokimya", "WBC 18,4 (yüksek); nötrofil 13,6 (yüksek); glukoz 68 (düşük); ALT, üre, kreatinin normal.",
            "Hafif nötrofilik lökositoz; akut inflamasyonla uyumlu.", Tag("Rapor PDF olarak eklendi."), ctx.NowUtc);
        _db.LabResults.Add(lab);
        await _db.SaveChangesAsync(ct);
        await AttachAsync(ctx, StoredFileOwnerType.LabResult, lab.Id, "hemogram.pdf", "Hemogram-Karamel.pdf", ct);

        var examFinalized = examId is { } id && ctx.Examinations.Any(e => e.Id == id && e.Status != ExaminationStatus.Draft);
        var studies = new (ImagingModality Modality, string Asset, string FileName, string Description, string Note)[]
        {
            (ImagingModality.XRay, "rontgen.png", "Toraks-LL.png", "Toraks röntgeni (laterolateral)", "Akciğer alanları temiz; kalp silueti normal sınırlarda."),
            (ImagingModality.Ultrasound, "ultrason.png", "Abdomen-USG.png", "Abdominal ultrason", "İdrar kesesi duvarı düzgün, içerik anekoik; taş görülmedi."),
        };
        foreach (var study in studies)
        {
            var created = ImagingRecord.Create(ctx.TenantId, ctx.Clinic.Id, karamel.Id, examId, examFinalized, resultAt,
                study.Modality, study.Description, study.Note, ctx.UserId, ctx.NowUtc);
            if (!created.IsSuccess)
                throw new InvalidOperationException($"Görüntüleme kaydı yazılamadı: {created.Error.Code}");
            _db.ImagingRecords.Add(created.Value!);
            await _db.SaveChangesAsync(ct);
            await AttachAsync(ctx, StoredFileOwnerType.ImagingRecord, created.Value!.Id, study.Asset, study.FileName, ct);
        }

        return "Laboratuvar: PDF raporlu hemogram; görüntüleme: röntgen ve ultrason (Karamel).";
    }

    /// <summary>Yükleme akışıyla aynı sıra: kayıt (Uploading) → içerik diske → hazır.</summary>
    private async Task AttachAsync(Ctx ctx, StoredFileOwnerType ownerType, Guid ownerId, string asset, string fileName, CancellationToken ct)
    {
        var bytes = ReadAsset(asset);
        var type = StoredFileType.Detect(bytes)
            ?? throw new InvalidOperationException($"Desteklenmeyen demo dosyası: {asset}");
        var file = new StoredFile(ctx.TenantId, ctx.Clinic.Id, ownerType, ownerId, fileName, type, ctx.UserId, ctx.NowUtc);
        _db.StoredFiles.Add(file);
        await _db.SaveChangesAsync(ct);

        using (var stream = new MemoryStream(bytes, writable: false))
            await _fileStorage.WriteAsync(file.StorageKey, stream, ct);

        var ready = file.MarkReady(bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), ctx.NowUtc);
        if (!ready.IsSuccess)
            throw new InvalidOperationException($"Dosya hazırlanamadı: {ready.Error.Code}");
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Narkotik defteri: kontrollü ürün, partili giriş ve iki uygulama (stok çıkışıyla).</summary>
    private async Task<string> WriteControlledDrugAsync(Ctx ctx, CancellationToken ct)
    {
        var product = new Product(ctx.TenantId, "Ketamin 100 mg/ml Enjeksiyonluk Çözelti 10 ml", "flakon", 650m, Charge.DefaultCurrency, ctx.NowUtc,
            description: Tag("Tanıtım kliniği kontrollü ürünü."), kind: ProductKind.Medicine, controlledClass: ControlledSubstanceClass.Narcotic,
            pharmacologicalForm: "Enjeksiyonluk çözelti");
        _db.Products.Add(product);

        var receivedAt = ctx.NowUtc.AddDays(-20);
        var receivedDay = DateOnly.FromDateTime(OperationDayBounds.ToLocal(receivedAt));
        var batch = StockBatch.Create(ctx.TenantId, ctx.Clinic.Id, product, 5m,
            new StockBatchDetails($"KTM-{receivedDay:yyMM}-01", ctx.Today.AddMonths(18), Tag("Anadolu Veteriner Ecza Deposu"),
                $"AVE-{receivedDay:yyyyMMdd}-099", receivedDay, receivedDay),
            ctx.UserId, receivedAt);
        if (!batch.IsSuccess)
            throw new InvalidOperationException($"Kontrollü ürün partisi yazılamadı: {batch.Error.Code}");
        _db.StockBatches.Add(batch.Value!);
        var entry = new StockMovement(ctx.TenantId, ctx.Clinic.Id, product.Id, StockMovementType.In, 5m, receivedAt, ctx.NowUtc,
            unitCost: 390m, reason: "Tedarikçi girişi", referenceType: "GoodsReceipt", createdByUserId: ctx.UserId, notes: Tag("Kontrollü ürün girişi."));
        entry.LinkBatch(batch.Value!);
        _db.StockMovements.Add(entry);

        var onHand = 5m;
        var dogExams = ctx.Examinations
            .Where(e => e.Status == ExaminationStatus.Draft && ctx.Pets.Any(p => p.Id == e.PetId && p.SpeciesId == ctx.DogSpeciesId))
            .OrderByDescending(e => e.ExaminedAtUtc).Take(2).ToList();
        foreach (var exam in dogExams)
        {
            const decimal quantity = 0.2m;
            var at = exam.ExaminedAtUtc.AddMinutes(10);
            var administration = Administration.Create(ctx.TenantId, ctx.Clinic.Id, exam.PetId, exam.Id, null, product.Id, quantity, at,
                Tag("Sedasyon (kısa işlem öncesi)."), false, null, ctx.UserId, ctx.NowUtc, null, route: AdministrationRoute.Intramuscular);
            if (!administration.IsSuccess)
                throw new InvalidOperationException($"Uygulama yazılamadı: {administration.Error.Code}");
            administration.Value!.LinkBatch(batch.Value!.Id);
            _db.Administrations.Add(administration.Value!);

            var movement = new StockMovement(ctx.TenantId, ctx.Clinic.Id, product.Id, StockMovementType.Out, quantity, at, ctx.NowUtc,
                reason: "Uygulama", referenceType: Administration.StockReferenceType, referenceId: administration.Value!.Id,
                createdByUserId: ctx.UserId, notes: Tag("Uygulama"));
            batch.Value!.Decrease(quantity);
            movement.LinkBatch(batch.Value!);
            _db.StockMovements.Add(movement);
            onHand -= quantity;
        }

        _db.ProductStocks.Add(new ProductStock(ctx.TenantId, ctx.Clinic.Id, product.Id, onHand, 2m, ctx.NowUtc));
        await _db.SaveChangesAsync(ct);
        return $"Narkotik defteri: 1 kontrollü ürün, {dogExams.Count} uygulama.";
    }

    /// <summary>
    /// Aşı Bilgi raporu uygulama kaydından okunur; Örnek Klinik uygulanan aşıya yalnız stok çıkışı yazar. Muayenede uygulanan
    /// aşılara partili uygulama kaydı eklenir (stok çıkışı zaten var, yeniden düşülmez).
    /// </summary>
    private async Task<string> WriteVaccineAdministrationsAsync(Ctx ctx, CancellationToken ct)
    {
        var skuByVaccine = new (string NamePrefix, string Sku)[]
        {
            ("Kuduz", "ORN-ASI-KUDUZ"), ("Köpek Karma", "ORN-ASI-KARMA-K"), ("Kedi Karma", "ORN-ASI-KARMA-KD"),
        };
        var skus = skuByVaccine.Select(s => s.Sku).ToList();
        var products = await _db.Products.Where(p => p.TenantId == ctx.TenantId && p.Sku != null && skus.Contains(p.Sku)).ToListAsync(ct);
        var productIds = products.Select(p => p.Id).ToList();
        var batches = await _db.StockBatches.Where(b => b.ClinicId == ctx.Clinic.Id && productIds.Contains(b.ProductId)).ToListAsync(ct);
        var petIds = ctx.Pets.Select(p => p.Id).ToList();
        var applied = await _db.Vaccinations
            .Where(v => petIds.Contains(v.PetId) && v.Status == VaccinationStatus.Applied && v.ExaminationId != null && v.AppliedAtUtc != null)
            .ToListAsync(ct);

        var count = 0;
        foreach (var vaccination in applied)
        {
            var sku = skuByVaccine.FirstOrDefault(s => vaccination.VaccineName.StartsWith(s.NamePrefix, StringComparison.Ordinal)).Sku;
            var product = products.FirstOrDefault(p => p.Sku == sku);
            if (product is null)
                continue;

            var administration = Administration.Create(ctx.TenantId, ctx.Clinic.Id, vaccination.PetId, vaccination.ExaminationId, null, product.Id, 1m,
                vaccination.AppliedAtUtc!.Value, Tag("Aşı uygulaması."), false, null, ctx.UserId, ctx.NowUtc, null, vaccinationId: vaccination.Id);
            if (!administration.IsSuccess)
                throw new InvalidOperationException($"Aşı uygulaması yazılamadı: {administration.Error.Code}");
            if (batches.FirstOrDefault(b => b.ProductId == product.Id) is { } batch)
                administration.Value!.LinkBatch(batch.Id);
            _db.Administrations.Add(administration.Value!);
            count++;
        }

        await _db.SaveChangesAsync(ct);
        return $"Aşı Bilgi raporu: {count} uygulama kaydı.";
    }

    /// <summary>Tezgâh satışı: satış + stok çıkışı + ücret + nakit ödeme (satış ekranı ve kasa için).</summary>
    private async Task<string> WriteSaleAsync(Ctx ctx, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.TenantId == ctx.TenantId && p.Sku == "ORN-MAMA-KEDI-2KG" && p.IsActive, ct);
        var stock = product is null ? null : await _db.ProductStocks.FirstOrDefaultAsync(s => s.ProductId == product.Id && s.ClinicId == ctx.Clinic.Id, ct);
        if (product is null || stock is null || stock.QuantityOnHand < 1m)
            return "Satış: uygun ürün bulunamadı, atlandı.";

        var client = ctx.Clients[1];
        var soldAt = ctx.NowUtc.AddHours(-3);
        var created = Sale.Create(ctx.TenantId, ctx.Clinic.Id, client.Id, soldAt, Tag("Tezgâh satışı."),
            [new SaleLineDraft(product.Id, product.Name, product.Unit, 1m, product.UnitPrice)], ctx.UserId, ctx.NowUtc, Guid.NewGuid(), "demo-klinik");
        if (!created.IsSuccess)
            throw new InvalidOperationException($"Satış yazılamadı: {created.Error.Code}");
        var sale = created.Value!;
        _db.Sales.Add(sale);

        foreach (var line in sale.Lines)
        {
            _db.StockMovements.Add(new StockMovement(ctx.TenantId, ctx.Clinic.Id, product.Id, StockMovementType.Out, line.Quantity, soldAt, ctx.NowUtc,
                reason: "Satış", referenceType: Sale.StockReferenceType, referenceId: line.Id, createdByUserId: ctx.UserId, notes: Tag("Satış")));
            stock.DecreaseQuantity(line.Quantity, ctx.NowUtc);
        }

        var charge = Charge.Create(ctx.TenantId, ctx.Clinic.Id, client.Id, null, null, null, Tag("Ürün satışı"), 1m, product.UnitPrice,
            ctx.UserId, soldAt, sale.Id);
        if (!charge.IsSuccess)
            throw new InvalidOperationException($"Satış ücreti yazılamadı: {charge.Error.Code}");
        _db.Charges.Add(charge.Value!);

        var payment = new Payment(ctx.TenantId, ctx.Clinic.Id, client.Id, null, null, null, product.UnitPrice, Charge.DefaultCurrency,
            PaymentMethod.Cash, soldAt, Tag("Tezgâhta nakit tahsil edildi."));
        var allocated = payment.AllocateFully([(charge.Value!, product.UnitPrice)], ctx.UserId, soldAt);
        if (!allocated.IsSuccess)
            throw new InvalidOperationException($"Satış ödemesi tahsis edilemedi: {allocated.Error.Code}");
        _db.Payments.Add(payment);
        _db.PaymentAllocations.AddRange(allocated.Value!);
        sale.AttachFinance(charge.Value!.Id, payment.Id);

        await _paymentEvents.EnqueueAsync(
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(Guid.NewGuid(), ctx.NowUtc,
                PaymentProjectionSnapshotFactory.Create(payment, client.FullName, ctx.Clinic.Name, null)),
            ct);

        await _db.SaveChangesAsync(ct);
        return "Satış: 1 tezgâh satışı (nakit).";
    }

    /// <summary>Bugün ekranı: erken gelen randevulu hasta bekliyor, randevusuz acil hasta muayenede.</summary>
    private async Task<string> WriteTodayVisitsAsync(Ctx ctx, CancellationToken ct)
    {
        var dayEnd = OperationDayBounds.ForLocalDate(ctx.Today).DayStartUtc.AddDays(1);
        var upcoming = await _db.Appointments
            .Where(a => a.ClinicId == ctx.Clinic.Id && a.Status == AppointmentStatus.Scheduled && a.ScheduledAtUtc > ctx.NowUtc && a.ScheduledAtUtc < dayEnd)
            .OrderBy(a => a.ScheduledAtUtc).FirstOrDefaultAsync(ct);
        var count = 0;
        if (upcoming is not null && ctx.Pets.Any(p => p.Id == upcoming.PetId))
        {
            _db.Visits.Add(new Visit(ctx.TenantId, ctx.Clinic.Id, upcoming.PetId, upcoming.Id, ctx.UserId, ctx.UserId,
                ctx.NowUtc.AddMinutes(-12), arrivalReason: "Randevusundan erken geldi."));
            count++;
        }

        var walkIn = new Visit(ctx.TenantId, ctx.Clinic.Id, ctx.Pet("Rocky").Id, null, ctx.UserId, ctx.UserId, ctx.NowUtc.AddMinutes(-25),
            isUrgent: true, arrivalReason: "Yabancı cisim yutma şüphesi.");
        var started = walkIn.Start(ctx.NowUtc.AddMinutes(-15));
        if (!started.IsSuccess)
            throw new InvalidOperationException($"Geliş başlatılamadı: {started.Error.Code}");
        _db.Visits.Add(walkIn);
        count++;

        await _db.SaveChangesAsync(ct);
        return $"Bugün: {count} geliş (1 acil, muayenede).";
    }

    private async Task<string> WriteAppointmentRequestsAsync(Ctx ctx, CancellationToken ct)
    {
        var tomorrow = OperationDayBounds.ForLocalDate(ctx.Today.AddDays(ctx.Today.DayOfWeek == DayOfWeek.Saturday ? 2 : 1)).DayStartUtc;
        var requests = new[]
        {
            AppointmentRequest.CreateFromLink(ctx.TenantId, ctx.Clinic.Id, null, new PetText("Mırmır", "Kedi", "Tekir"), null,
                tomorrow.AddHours(8), "Gizem Tunç", "gizem.tunc@example.com", "05000000001", "Yıllık aşısı için randevu istiyorum.", ctx.NowUtc),
            AppointmentRequest.CreateFromLink(ctx.TenantId, ctx.Clinic.Id, null, new PetText("Bulut", "Köpek", "Samoyed"), null,
                tomorrow.AddHours(11), "Kerem Ateş", "kerem.ates@example.com", "05000000002", "Son iki gündür topallıyor.", ctx.NowUtc),
        };
        foreach (var request in requests)
        {
            if (!request.IsSuccess)
                throw new InvalidOperationException($"Online randevu talebi yazılamadı: {request.Error.Code}");
            _db.AppointmentRequests.Add(request.Value!);
        }

        await _db.SaveChangesAsync(ct);
        return $"Online randevu talebi: {requests.Length} (bekliyor).";
    }

    /// <summary>Hatırlatma günlüğü: yaklaşan aşı ve randevular için gönderilmiş kayıtlar (anahtar biçimi işlemciyle aynı).</summary>
    private async Task<string> WriteReminderLogsAsync(Ctx ctx, CancellationToken ct)
    {
        var count = 0;
        var vaccinations = await _db.Vaccinations
            .Where(v => v.ClinicId == ctx.Clinic.Id && v.Status == VaccinationStatus.Scheduled && v.DueAtUtc > ctx.NowUtc)
            .OrderBy(v => v.DueAtUtc).Take(3).ToListAsync(ct);
        foreach (var vaccination in vaccinations)
        {
            var client = ctx.OwnerOf(vaccination.PetId);
            if (client?.Email is null)
                continue;
            var log = new ReminderDispatchLog(ctx.TenantId, ctx.Clinic.Id, ReminderType.Vaccination, ReminderSourceEntityType.Vaccination, vaccination.Id,
                client.Email, client.FullName, ctx.NowUtc.AddHours(-6), vaccination.DueAtUtc!.Value, ReminderDispatchStatus.Pending,
                $"vaccination:{vaccination.Id:D}:days-before:3", ctx.NowUtc);
            log.MarkSent(ctx.NowUtc.AddHours(-6), ctx.NowUtc);
            _db.ReminderDispatchLogs.Add(log);
            count++;
        }

        var appointments = await _db.Appointments
            .Where(a => a.ClinicId == ctx.Clinic.Id && a.Status == AppointmentStatus.Scheduled && a.ScheduledAtUtc > ctx.NowUtc)
            .OrderBy(a => a.ScheduledAtUtc).Take(3).ToListAsync(ct);
        foreach (var appointment in appointments)
        {
            var client = ctx.OwnerOf(appointment.PetId);
            if (client?.Email is null)
                continue;
            var log = new ReminderDispatchLog(ctx.TenantId, ctx.Clinic.Id, ReminderType.Appointment, ReminderSourceEntityType.Appointment, appointment.Id,
                client.Email, client.FullName, appointment.ScheduledAtUtc.AddHours(-24), appointment.ScheduledAtUtc, ReminderDispatchStatus.Pending,
                $"appointment:{appointment.Id:D}:at:{appointment.ScheduledAtUtc:yyyyMMddHHmmssfffffff}:hours-before:24", ctx.NowUtc);
            log.MarkSent(Min(ctx.NowUtc, appointment.ScheduledAtUtc.AddHours(-24)), ctx.NowUtc);
            _db.ReminderDispatchLogs.Add(log);
            count++;
        }

        await _db.SaveChangesAsync(ct);
        return $"Hatırlatma günlüğü: {count} gönderim.";
    }

    // ---------------------------------------------------------------------------------------------
    // Portal ve web sitesi
    // ---------------------------------------------------------------------------------------------

    private async Task<string> EnsurePortalAsync(Ctx ctx, string? password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "Portal: parola verilmedi, hesap kurulmadı.";

        var client = ctx.Clients[0];
        var account = await _db.OwnerAccounts.FirstOrDefaultAsync(a => a.Email == PortalEmail, ct);
        if (account is null)
        {
            account = OwnerAccount.Create(PortalEmail, client.FullName, ctx.NowUtc);
            _db.OwnerAccounts.Add(account);
            _db.LegalAcceptances.AddRange(LegalDocumentCatalog.CreateOwnerAccountAcceptances(account.Id, LegalDocumentCatalog.RequiredForOwnerAccount, ctx.NowUtc));
        }

        account.SetPasswordHash(_passwordHasher.Hash(password));
        var link = OwnerAccountClientLink.Create(account.Id, ctx.TenantId, ctx.Clinic.Id, client.Id, ctx.UserId, ctx.NowUtc);
        link.AcknowledgeDisclosure(ctx.NowUtc);
        _db.OwnerAccountClientLinks.Add(link);

        await _db.SaveChangesAsync(ct);
        return $"Portal: {PortalEmail} ({client.FullName} ve hayvanları).";
    }

    private async Task<string> EnsureSiteAsync(Ctx ctx, CancellationToken ct)
    {
        var site = await _db.ClinicSites.FirstOrDefaultAsync(s => s.ClinicId == ctx.Clinic.Id, ct);
        if (site is { PublishedVersionNumber: not null })
            return $"Web sitesi zaten yayında: /k/{site.Slug}";

        if (site is null)
        {
            var config = ClinicSiteConfig.Parse(System.Text.Encoding.UTF8.GetString(ReadAsset("site.json")));
            if (!config.IsSuccess)
                throw new InvalidOperationException($"Demo site ayarı geçersiz: {config.Error.Code}");

            var slug = ClinicSiteSlug.Suggest(ctx.Clinic.Name);
            for (var i = 2; await _db.ClinicSiteAddresses.AnyAsync(a => a.Slug == slug, ct); i++)
                slug = $"{ClinicSiteSlug.Suggest(ctx.Clinic.Name)}-{i}";

            site = ClinicSite.Create(ctx.TenantId, ctx.Clinic.Id, slug, config.Value!, ctx.NowUtc);
            _db.ClinicSites.Add(site);
            _db.ClinicSiteAddresses.Add(ClinicSiteAddress.Create(ctx.TenantId, site.Id, slug, ctx.NowUtc));
        }

        var published = site.Publish(ctx.UserId, ctx.NowUtc);
        if (!published.IsSuccess)
            throw new InvalidOperationException($"Web sitesi yayınlanamadı: {published.Error.Code}");
        _db.ClinicSiteVersions.Add(published.Value!);

        await _db.SaveChangesAsync(ct);
        return $"Web sitesi yayında: /k/{site.Slug}";
    }

    // ---------------------------------------------------------------------------------------------
    // Yardımcılar
    // ---------------------------------------------------------------------------------------------

    private async Task<Ctx> ContextAsync(Tenant tenant, Clinic clinic, Guid userId, CancellationToken ct)
    {
        var clients = await _db.Clients
            .Where(c => c.TenantId == tenant.Id && c.Address != null && c.Address.StartsWith(ExampleClinicDataGenerator.TagPrefix))
            .OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Phone).ToListAsync(ct);
        var clientIds = clients.Select(c => c.Id).ToList();
        var pets = await _db.Pets.Where(p => clientIds.Contains(p.ClientId)).ToListAsync(ct);
        var petIds = pets.Select(p => p.Id).ToList();
        var examinations = await _db.Examinations.Where(e => petIds.Contains(e.PetId)).ToListAsync(ct);
        var dogSpeciesId = await _db.Species.Where(s => s.Code == "DOG").Select(s => s.Id).FirstAsync(ct);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        return new Ctx(tenant.Id, clinic, userId, nowUtc, OperationDayBounds.ToLocalDate(nowUtc), clients, pets, examinations, dogSpeciesId);
    }

    private void AddCharge(Ctx ctx, Guid clientId, Guid? petId, Guid? examinationId, string description, decimal amount, DateTime atUtc)
    {
        var charge = Charge.Create(ctx.TenantId, ctx.Clinic.Id, clientId, petId, null, examinationId, Tag(description), 1m, amount, ctx.UserId, atUtc, null);
        if (!charge.IsSuccess)
            throw new InvalidOperationException($"Ücret yazılamadı ({description}): {charge.Error.Code}");
        _db.Charges.Add(charge.Value!);
    }

    /// <summary>"ÖRNEK · Sokak No, İlçe/İl" → (sokak no, ilçe).</summary>
    private static (string? AddressLine, string? District) SplitAddress(string? address)
    {
        var text = address?.Replace(ExampleClinicDataGenerator.TagPrefix, string.Empty, StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(text))
            return (null, null);
        var comma = text.LastIndexOf(',');
        if (comma < 0)
            return (text, null);
        var district = text[(comma + 1)..].Split('/')[0].Trim();
        return (text[..comma].Trim(), district.Length == 0 ? null : district);
    }

    private static byte[] ReadAsset(string name)
    {
        using var stream = typeof(DemoClinicSeeder).Assembly.GetManifestResourceStream(AssetPrefix + name)
            ?? throw new InvalidOperationException($"Demo dosyası bulunamadı: {name}");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static string Tag(string text) => ExampleClinicDataGenerator.TagPrefix + text;

    private sealed record Ctx(
        Guid TenantId,
        Clinic Clinic,
        Guid UserId,
        DateTime NowUtc,
        DateOnly Today,
        IReadOnlyList<Client> Clients,
        IReadOnlyList<Pet> Pets,
        IReadOnlyList<Examination> Examinations,
        Guid DogSpeciesId)
    {
        public Pet Pet(string name) => Pets.First(p => p.Name == name);

        public Client? OwnerOf(Guid petId)
        {
            var pet = Pets.FirstOrDefault(p => p.Id == petId);
            return pet is null ? null : Clients.FirstOrDefault(c => c.Id == pet.ClientId);
        }

        public Guid? LatestExamId(Guid petId)
            => Examinations.Where(e => e.PetId == petId).OrderByDescending(e => e.ExaminedAtUtc).Select(e => (Guid?)e.Id).FirstOrDefault();
    }
}
