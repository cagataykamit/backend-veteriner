using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.ExampleClinic;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Örnek Veteriner Kliniği sentetik verisi (ADR-002, TRIAL-003). Gerçek kişi verisi yok; adlar, telefonlar ve adresler
/// uydurmadır. Sabit tohumlu rastgelelik: aynı "bugün" için aynı küme üretilir; tarihler <see cref="TimeProvider"/>'dan,
/// klinik günü Europe/Istanbul (<see cref="OperationDayBounds"/>).
/// <para>
/// Kayıtlar domain fabrikalarıyla doğrudan yazılır (komut handler'ları HTTP kiracı/klinik/kullanıcı bağlamına bağlıdır ve
/// kayıt akışında JWT yoktur); okuma modelleri için Client/Pet/Appointment/Payment "Created" outbox olayları üretim
/// handler'larıyla aynı snapshot fabrikalarından kuyruğa alınır. Çağıranın işlem (transaction) sınırında çalışır; ara
/// SaveChanges'ler aynı işlemde kalır.
/// </para>
/// <para>
/// Temizleme işareti: her kaydın not/açıklama alanı <see cref="Marker"/> ile başlar (müşteride adres, partide tedarikçi adı,
/// üründe açıklama ve <c>ORN-</c> SKU öneki).
/// </para>
/// </summary>
public sealed class ExampleClinicDataGenerator : IExampleClinicLoader
{
    public const string Marker = "ÖRNEK";

    /// <summary>İşaretli alanın başı; temizlik (<see cref="ExampleClinicDataCleaner"/>) örnek kaydı bununla tanır.</summary>
    public const string TagPrefix = Marker + " · ";

    private const int Seed = 20261024;
    private const int PastDays = 30;
    private const int FutureDays = 7;
    private const int SlotMinutes = 30;
    private const decimal ExaminationFee = 450m;

    private static readonly TimeOnly[] Slots =
    [
        new(9, 30), new(10, 0), new(10, 30), new(11, 0), new(11, 30),
        new(13, 0), new(13, 30), new(14, 0), new(14, 30), new(15, 0), new(15, 30), new(16, 0), new(16, 30), new(17, 0),
    ];

    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IClientIntegrationEventOutbox _clientEvents;
    private readonly IPetIntegrationEventOutbox _petEvents;
    private readonly IAppointmentIntegrationEventOutbox _appointmentEvents;
    private readonly IAppointmentProjectionSnapshotFactory _appointmentSnapshots;
    private readonly IPaymentIntegrationEventOutbox _paymentEvents;
    private readonly ILogger<ExampleClinicDataGenerator> _logger;

    public ExampleClinicDataGenerator(
        AppDbContext db,
        TimeProvider timeProvider,
        IClientIntegrationEventOutbox clientEvents,
        IPetIntegrationEventOutbox petEvents,
        IAppointmentIntegrationEventOutbox appointmentEvents,
        IAppointmentProjectionSnapshotFactory appointmentSnapshots,
        IPaymentIntegrationEventOutbox paymentEvents,
        ILogger<ExampleClinicDataGenerator> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _clientEvents = clientEvents;
        _petEvents = petEvents;
        _appointmentEvents = appointmentEvents;
        _appointmentSnapshots = appointmentSnapshots;
        _paymentEvents = paymentEvents;
        _logger = logger;
    }

    public async Task<ExampleClinicLoadSummaryDto> LoadAsync(Guid tenantId, Guid clinicId, Guid userId, CancellationToken ct)
    {
        var clinic = await _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId && c.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Örnek klinik: klinik bulunamadı (ClinicId={clinicId}).");

        var catalog = await ReferenceCatalog.LoadAsync(_db, ct);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var ctx = new Context(tenantId, clinicId, userId, clinic.Name, nowUtc, OperationDayBounds.ToLocalDate(nowUtc), new Random(Seed), catalog);

        var people = await WriteClientsAndPetsAsync(ctx, ct);
        var appointments = await WriteAppointmentsAsync(ctx, people, ct);
        var clinical = await WriteClinicalRecordsAsync(ctx, people, appointments, ct);
        var stock = await WriteProductsAndStockAsync(ctx, clinical, ct);
        var finance = await WriteChargesAndPaymentsAsync(ctx, clinical, ct);
        await MarkExampleClinicLoadedAsync(tenantId, nowUtc, ct);

        var summary = new ExampleClinicLoadSummaryDto(
            tenantId,
            clinicId,
            people.Clients.Count,
            people.Pets.Count,
            appointments.Count,
            clinical.Examinations.Count,
            clinical.Vaccinations.Count,
            clinical.Treatments.Count,
            clinical.LabResults,
            finance.Charges,
            finance.Payments,
            stock.Products,
            stock.Movements);

        _logger.LogInformation(
            "Örnek klinik yüklendi. TenantId={TenantId} ClinicId={ClinicId} Clients={Clients} Pets={Pets} Appointments={Appointments} Examinations={Examinations} Charges={Charges} Payments={Payments}",
            tenantId, clinicId, summary.Clients, summary.Pets, summary.Appointments, summary.Examinations, summary.Charges, summary.Payments);

        return summary;
    }

    /// <summary>Aktivasyon ölçümü (TRIAL-008): "örnek klinik seçildi" işareti, yüklemeyle aynı işlemde.</summary>
    private async Task MarkExampleClinicLoadedAsync(Guid tenantId, DateTime nowUtc, CancellationToken ct)
    {
        var activation = await _db.TenantActivations.FirstOrDefaultAsync(a => a.TenantId == tenantId, ct);
        if (activation is null)
        {
            activation = new TenantActivation(tenantId);
            _db.TenantActivations.Add(activation);
        }

        activation.MarkExampleClinicLoaded(nowUtc);
        await _db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------------------------------------
    // Müşteri + hayvan
    // ---------------------------------------------------------------------------------------------

    private static readonly (string Name, string Email, string Address)[] ClientSeeds =
    [
        ("Ayşe Yılmaz", "ayse.yilmaz@example.com", "Caferağa Mah. Moda Cad. No:12 D:4, Kadıköy/İstanbul"),
        ("Mehmet Demir", "mehmet.demir@example.com", "Kozyatağı Mah. Değirmen Sk. No:8, Kadıköy/İstanbul"),
        ("Elif Kaya", "elif.kaya@example.com", "Bostancı Mah. Çınar Sk. No:21 D:2, Kadıköy/İstanbul"),
        ("Burak Şahin", "", "Suadiye Mah. Bağdat Cad. No:410, Kadıköy/İstanbul"),
        ("Zeynep Çelik", "zeynep.celik@example.com", "Erenköy Mah. Ethemefendi Cad. No:65, Kadıköy/İstanbul"),
        ("Can Öztürk", "", "Göztepe Mah. Fahrettin Kerim Gökay Cad. No:120, Kadıköy/İstanbul"),
        ("Selin Aydın", "selin.aydin@example.com", "Fenerbahçe Mah. Dalyan Sk. No:7, Kadıköy/İstanbul"),
        ("Emre Yıldız", "", "Acıbadem Mah. Tekin Sk. No:14 D:6, Üsküdar/İstanbul"),
        ("Deniz Arslan", "deniz.arslan@example.com", "Koşuyolu Mah. Mahmut Yesari Sk. No:3, Kadıköy/İstanbul"),
        ("Fatma Koç", "", "Feneryolu Mah. Kuşdili Cad. No:33, Kadıköy/İstanbul"),
        ("Hakan Doğan", "hakan.dogan@example.com", "Sahrayıcedit Mah. Atatürk Cad. No:52, Kadıköy/İstanbul"),
        ("Merve Kılıç", "", "Merdivenköy Mah. Şair Arşi Cad. No:9 D:1, Kadıköy/İstanbul"),
    ];

    /// <summary>(müşteri sırası, ad, tür kodu, ırk, cinsiyet, yaş ay, kilo kg, kısır, çip var mı, not)</summary>
    private static readonly (int Owner, string Name, string Species, string Breed, PetGender Gender, int AgeMonths, decimal Weight, bool Neutered, bool Chip, string? Note)[] PetSeeds =
    [
        (0, "Pamuk", "CAT", "Tekir", PetGender.Female, 38, 4.2m, true, true, null),
        (0, "Karamel", "DOG", "Golden Retriever", PetGender.Male, 52, 31.5m, false, true, "Tüy dökümü mevsimsel; sahibi omega-3 takviyesi veriyor."),
        (1, "Zeytin", "CAT", "British Shorthair", PetGender.Male, 26, 5.1m, true, true, null),
        (2, "Max", "DOG", "Labrador Retriever", PetGender.Male, 70, 33.8m, true, true, "Eklem hassasiyeti; kilo takibi önerildi."),
        (2, "Cıvıl", "BIRD", "Muhabbet Kuşu", PetGender.Male, 18, 0.04m, false, false, null),
        (3, "Duman", "CAT", "Scottish Fold", PetGender.Male, 14, 3.9m, false, true, null),
        (4, "Çomar", "DOG", "Kangal", PetGender.Male, 44, 48.0m, false, true, "Bahçede yaşıyor; dış parazit koruması düzenli."),
        (4, "Boncuk", "CAT", "Van Kedisi", PetGender.Female, 61, 4.6m, true, false, null),
        (5, "Luna", "DOG", "Melez", PetGender.Female, 30, 18.2m, true, true, "Barınaktan sahiplenildi (2025)."),
        (6, "Şila", "CAT", "Siyam", PetGender.Female, 22, 3.6m, true, true, null),
        (6, "Limon", "BIRD", "Kanarya", PetGender.Male, 9, 0.02m, false, false, null),
        (7, "Rocky", "DOG", "Beagle", PetGender.Male, 36, 12.4m, false, true, null),
        (8, "Mia", "CAT", "Melez", PetGender.Female, 8, 2.8m, false, false, "Yavru; aşı takvimi devam ediyor."),
        (8, "Fındık", "DOG", "Poodle", PetGender.Female, 48, 7.1m, true, true, null),
        (9, "Tarçın", "CAT", "Tekir", PetGender.Male, 95, 5.4m, true, true, "Yaşlı hasta; yıllık kan tahlili önerilir."),
        (10, "Badem", "DOG", "Cocker Spaniel", PetGender.Female, 27, 13.0m, false, true, "Tekrarlayan kulak enfeksiyonu."),
        (10, "Maviş", "BIRD", "Sultan Papağanı", PetGender.Female, 30, 0.09m, false, false, null),
        (11, "Zeus", "DOG", "Alman Kurdu", PetGender.Male, 40, 36.2m, false, true, null),
        (11, "Lokum", "CAT", "Persian", PetGender.Female, 33, 4.0m, true, true, "Uzun tüy; düzenli tarama önerildi."),
        (1, "Toffee", "DOG", "Maltese Terrier", PetGender.Male, 20, 4.3m, false, true, null),
    ];

    private async Task<People> WriteClientsAndPetsAsync(Context ctx, CancellationToken ct)
    {
        var clients = new List<Client>(ClientSeeds.Length);
        for (var i = 0; i < ClientSeeds.Length; i++)
        {
            var (name, email, address) = ClientSeeds[i];
            var client = new Client(
                ctx.TenantId,
                name,
                phone: $"05321001{i + 1:D3}",
                email: string.IsNullOrEmpty(email) ? null : email,
                address: Tag(address));
            clients.Add(client);
            _db.Clients.Add(client);
            await _clientEvents.EnqueueAsync(
                ClientIntegrationEventTypes.Created,
                new ClientCreatedIntegrationEvent(Guid.NewGuid(), ctx.NowUtc, ClientProjectionSnapshotFactory.Create(client)),
                ct);
        }

        var pets = new List<PetRecord>(PetSeeds.Length);
        for (var i = 0; i < PetSeeds.Length; i++)
        {
            var seed = PetSeeds[i];
            var species = ctx.Catalog.Species[seed.Species];
            var breed = ctx.Catalog.FindBreed(species.Id, seed.Breed);
            var client = clients[seed.Owner];
            var pet = new Pet(
                ctx.TenantId,
                client.Id,
                seed.Name,
                species.Id,
                // Liste ve kart ırkı metin alanından okur (CreatePet de ad + kimlik birlikte yazar); kimlik katalog bağı.
                breed: breed?.Name ?? seed.Breed,
                birthDate: ctx.Today.AddMonths(-seed.AgeMonths).AddDays(-ctx.Random.Next(0, 28)),
                breedId: breed?.Id,
                gender: seed.Gender,
                colorId: ctx.Catalog.PickColor(ctx.Random, seed.Species),
                weight: seed.Weight,
                notes: Tag(seed.Note ?? "Örnek klinik hastası."),
                microchipNumber: seed.Chip ? $"9920000{i + 1:D8}" : null,
                isNeutered: seed.Neutered);
            _db.Pets.Add(pet);
            pets.Add(new PetRecord(pet, client, seed.Species, seed.Weight));
            await _petEvents.EnqueueAsync(
                PetIntegrationEventTypes.Created,
                new PetCreatedIntegrationEvent(Guid.NewGuid(), ctx.NowUtc, PetProjectionSnapshotFactory.Create(pet, client, species, breed)),
                ct);
        }

        await _db.SaveChangesAsync(ct);
        return new People(clients, pets);
    }

    // ---------------------------------------------------------------------------------------------
    // Randevular: son 30 gün + gelecek 7 gün, Pazar kapalı
    // ---------------------------------------------------------------------------------------------

    private async Task<List<AppointmentRecord>> WriteAppointmentsAsync(Context ctx, People people, CancellationToken ct)
    {
        var records = new List<AppointmentRecord>();
        var pastIndex = 0;
        for (var offset = -PastDays; offset <= FutureDays; offset++)
        {
            var day = ctx.Today.AddDays(offset);
            // Yükleme günü her zaman dolu: kullanıcı ilk açılışta Bugün ekranını boş görmesin (ADR-002, 5-10 dk değer);
            // diğer Pazarlar kapalı.
            if (day.DayOfWeek == DayOfWeek.Sunday && offset != 0)
                continue;

            var count = offset == 0 ? Slots.Length : ctx.Random.Next(0, 3);
            var usedPets = new HashSet<Guid>();
            foreach (var slot in PickSlots(ctx.Random, count))
            {
                var scheduledUtc = ctx.LocalToUtc(day, slot);
                if (offset == 0 && !PickTodaySlot(ctx, scheduledUtc, records))
                    continue;

                var pet = PickPet(ctx.Random, people.Pets, usedPets);
                var type = PickType(ctx.Random, pet.SpeciesCode);
                var status = scheduledUtc < ctx.NowUtc ? PastStatus(pastIndex++) : AppointmentStatus.Scheduled;

                var appointment = new Appointment(
                    ctx.TenantId, ctx.ClinicId, pet.Pet.Id, scheduledUtc, SlotMinutes, type, status,
                    Tag(AppointmentNote(type, status)));
                _db.Appointments.Add(appointment);

                var sequence = appointment.AdvanceMutationSequence();
                var snapshot = await _appointmentSnapshots.CreateAsync(appointment, ct);
                await _appointmentEvents.EnqueueAsync(
                    AppointmentIntegrationEventTypes.Created,
                    new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), ctx.NowUtc, sequence, snapshot),
                    ct);

                records.Add(new AppointmentRecord(appointment, pet));
            }
        }

        await _db.SaveChangesAsync(ct);
        return records;
    }

    /// <summary>Bugün: en fazla iki geçmiş (tamamlanmış) ve iki gelecek (planlı) randevu; saat kaynağına göre seçilir.</summary>
    private static bool PickTodaySlot(Context ctx, DateTime scheduledUtc, List<AppointmentRecord> records)
    {
        var todayStart = OperationDayBounds.ForLocalDate(ctx.Today).DayStartUtc;
        var isPast = scheduledUtc.AddMinutes(SlotMinutes) <= ctx.NowUtc;
        if (!isPast && scheduledUtc <= ctx.NowUtc)
            return false; // şu an süren slot: ne tamamlanmış ne planlı sayılır

        var todays = records.Where(r => r.Appointment.ScheduledAtUtc >= todayStart).ToList();
        return isPast
            ? todays.Count(r => r.Appointment.ScheduledAtUtc < ctx.NowUtc) < 2
            : todays.Count(r => r.Appointment.ScheduledAtUtc > ctx.NowUtc) < 2;
    }

    /// <summary>Geçmiş randevu durumu: çoğu tamamlandı; ilk ikisi iptal/gelmedi örneği, sonrası ~%15 iptal, ~%8 gelmedi.</summary>
    private static AppointmentStatus PastStatus(int index) => index switch
    {
        1 => AppointmentStatus.Cancelled,
        3 => AppointmentStatus.NoShow,
        _ when index % 7 == 6 => AppointmentStatus.Cancelled,
        _ when index % 13 == 12 => AppointmentStatus.NoShow,
        _ => AppointmentStatus.Completed,
    };

    private static IEnumerable<TimeOnly> PickSlots(Random random, int count)
        => Slots.OrderBy(_ => random.Next()).Take(count).OrderBy(s => s);

    private static PetRecord PickPet(Random random, IReadOnlyList<PetRecord> pets, HashSet<Guid> usedToday)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = pets[random.Next(pets.Count)];
            if (usedToday.Add(candidate.Pet.Id))
                return candidate;
        }

        return pets[random.Next(pets.Count)];
    }

    private static AppointmentType PickType(Random random, string speciesCode)
    {
        if (speciesCode == "BIRD")
            return random.Next(2) == 0 ? AppointmentType.Examination : AppointmentType.Checkup;

        return random.Next(10) switch
        {
            0 or 1 or 2 or 3 => AppointmentType.Examination,
            4 or 5 => AppointmentType.Vaccination,
            6 or 7 => AppointmentType.Checkup,
            8 => AppointmentType.Consultation,
            _ => AppointmentType.Grooming,
        };
    }

    private static string AppointmentNote(AppointmentType type, AppointmentStatus status)
    {
        var text = type switch
        {
            AppointmentType.Examination => "Şikâyetle başvuru; muayene randevusu.",
            AppointmentType.Vaccination => "Yıllık aşı uygulaması.",
            AppointmentType.Checkup => "Genel kontrol.",
            AppointmentType.Consultation => "Beslenme ve bakım danışmanlığı.",
            AppointmentType.Grooming => "Tıraş ve tırnak kesimi.",
            _ => "Randevu.",
        };
        return status switch
        {
            AppointmentStatus.Cancelled => $"{text} Sahibi aradı, iptal etti.",
            AppointmentStatus.NoShow => $"{text} Hasta gelmedi.",
            _ => text,
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Muayene, aşı, tedavi, laboratuvar
    // ---------------------------------------------------------------------------------------------

    private static readonly (string Reason, string Findings, string Assessment, string Plan, string? Treatment, string? TreatmentDescription, string? Medicine)[] Cases =
    [
        ("Kusma ve iştahsızlık", "Hafif dehidratasyon, abdominal palpasyonda hassasiyet yok. Mukozalar pembe.",
            "Akut gastrit şüphesi.", "Antiemetik, 12 saat açlık, az ve sık beslenme; 48 saatte düzelmezse kan tahlili.",
            "Antiemetik ve sıvı tedavisi", "Maropitant 1 mg/kg SC tek doz; 200 ml subkutan izotonik. Yarın kontrol.", null),
        ("Topallama (sağ arka bacak)", "Sağ dizde ağrı, krepitasyon yok. Ortopedik muayenede patella stabil.",
            "Yumuşak doku zedelenmesi.", "7 gün istirahat, antienflamatuar; iyileşme yoksa röntgen.",
            "Antienflamatuar tedavi", "Meloksikam 0,1 mg/kg PO günde 1, 5 gün. Egzersiz kısıtlaması.", "MELOKSIKAM"),
        ("Kaşıntı ve tüy dökülmesi", "Sırt ve kuyruk dibinde alopesi, pire dışkısı görüldü. Deri kızarık.",
            "Pire alerjik dermatiti.", "Dış parazit uygulaması, medikal şampuan; ev ortamı ilaçlaması önerildi.",
            "Antiparaziter uygulama ve medikal şampuan", "Spot-on dış parazit damlası uygulandı; haftada 2 klorheksidinli şampuan.", null),
        ("Öksürük", "Trakeal palpasyonda öksürük refleksi pozitif, akciğer sesleri normal. Ateş yok.",
            "Üst solunum yolu enfeksiyonu.", "Antibiyotik kürü 7 gün, buhar; kontrol 1 hafta sonra.",
            "Antibiyotik kürü", "Amoksisilin-klavulanik asit 12,5 mg/kg PO günde 2, 7 gün.", "AMOKSISILIN"),
        ("Kulak kaşıntısı ve koku", "Sol kulakta kahverengi akıntı, kanal hiperemik. Otoskopide zar sağlam.",
            "Otitis eksterna.", "Kulak temizliği ve topikal damla; 10 gün sonra kontrol.",
            "Kulak temizliği ve topikal tedavi", "Kulak kanalı solüsyonla temizlendi; günde 2 kez damla, 10 gün.", "KULAK"),
        ("İshal", "Sulu dışkı, karın hassasiyeti hafif. Genel durum iyi, ateş yok.",
            "Diyet kaynaklı enterit.", "Probiyotik, sindirimi kolay mama; dışkı parazit muayenesi.",
            "Probiyotik ve diyet düzenlemesi", "Probiyotik pasta 5 gün; 3 gün haşlanmış tavuk-pirinç diyeti.", null),
        ("Diş taşı kontrolü", "Premolar ve molarlarda orta derecede diş taşı, diş eti kızarık.",
            "Periodontal hastalık evre 2.", "Ultrasonik diş taşı temizliği planlandı; evde diş fırçalama eğitimi.",
            "Diş taşı temizliği", "Genel anestezi altında ultrasonik temizlik ve polisaj yapıldı.", null),
        ("Yıllık genel kontrol", "Genel durum iyi; kalp ve akciğer sesleri normal, lenf nodları normal boyutta.",
            "Sağlıklı.", "Yıllık aşı ve parazit programı güncellendi.", null, null, null),
    ];

    private async Task<Clinical> WriteClinicalRecordsAsync(Context ctx, People people, List<AppointmentRecord> appointments, CancellationToken ct)
    {
        var result = new Clinical();
        var completed = appointments
            .Where(a => a.Appointment.Status == AppointmentStatus.Completed && a.Appointment.AppointmentType != AppointmentType.Grooming)
            .OrderBy(a => a.Appointment.ScheduledAtUtc)
            .ToList();

        foreach (var record in completed)
            WriteExamination(ctx, result, record.Pet, record.Appointment);

        // Randevusuz gelişler: muayene sayısını ~25'e tamamlar ve "randevusuz hasta" örneği verir.
        var walkInDays = new[] { -27, -19, -12, -6 };
        foreach (var offset in walkInDays)
        {
            var day = ctx.Today.AddDays(offset);
            if (day.DayOfWeek == DayOfWeek.Sunday)
                day = day.AddDays(1);
            var pet = PickPet(ctx.Random, people.Pets, []);
            WriteExamination(ctx, result, pet, appointment: null, examinedAtUtc: ctx.LocalToUtc(day, new TimeOnly(12, 0)));
        }

        WriteScheduledVaccinations(ctx, result, people);
        WriteLabResults(ctx, result);

        await _db.SaveChangesAsync(ct);
        return result;
    }

    private void WriteExamination(Context ctx, Clinical result, PetRecord pet, Appointment? appointment, DateTime? examinedAtUtc = null)
    {
        var examinedAt = examinedAtUtc ?? appointment!.ScheduledAtUtc.AddMinutes(5);
        var type = appointment?.AppointmentType ?? AppointmentType.Examination;
        var isBird = pet.SpeciesCode == "BIRD";

        var caseIndex = type switch
        {
            AppointmentType.Examination when !isBird => ctx.Random.Next(0, Cases.Length - 1),
            _ => Cases.Length - 1,
        };
        var c = Cases[caseIndex];
        var reason = type switch
        {
            AppointmentType.Vaccination => "Yıllık aşı uygulaması",
            AppointmentType.Checkup => "Genel kontrol",
            AppointmentType.Consultation => "Beslenme danışmanlığı",
            _ => c.Reason,
        };

        var weight = Math.Round(pet.WeightKg * (1 + (decimal)(ctx.Random.NextDouble() * 0.06 - 0.03)), 2);
        var examination = new Examination(
            ctx.TenantId, ctx.ClinicId, pet.Pet.Id, appointment?.Id, examinedAt,
            reason, c.Findings, c.Assessment, Tag(appointment is null ? "Randevusuz geliş." : "Randevulu muayene."),
            anamnesis: appointment is null ? "Sahibi bugün fark etmiş, randevusuz geldi." : "Sahibinin ifadesiyle şikâyet 2-3 gündür sürüyor.",
            plan: c.Plan,
            weightKg: weight,
            temperatureC: isBird ? null : Math.Round(38.2m + (decimal)ctx.Random.NextDouble() * 0.9m, 1),
            heartRateBpm: isBird ? null : pet.SpeciesCode == "CAT" ? 150 + ctx.Random.Next(0, 50) : 80 + ctx.Random.Next(0, 40),
            respiratoryRatePerMin: isBird ? null : 20 + ctx.Random.Next(0, 12),
            vitalsMeasuredAtUtc: examinedAt);

        // 3 günden eski muayeneler kesinleşmiş; son günlerdekiler taslak kalır (düzenlenebilir örnek).
        if (examinedAt < ctx.NowUtc.AddDays(-3))
        {
            var finalized = examination.Finalize(ctx.UserId, examinedAt.AddMinutes(20));
            if (!finalized.IsSuccess)
                throw new InvalidOperationException($"Örnek muayene kesinleştirilemedi: {finalized.Error.Code}");
        }

        _db.Examinations.Add(examination);
        var exam = new ExaminationRecord(examination, pet, appointment, c.Medicine);
        result.Examinations.Add(exam);

        if (type == AppointmentType.Vaccination || (type == AppointmentType.Checkup && !isBird && ctx.Random.Next(2) == 0))
            WriteAppliedVaccination(ctx, result, exam);

        if (c.Treatment is not null)
        {
            var treatment = new Treatment(
                ctx.TenantId, ctx.ClinicId, pet.Pet.Id, examination.Id, examinedAt.AddMinutes(15),
                c.Treatment, c.TreatmentDescription!, Tag("Muayene sırasında uygulandı."),
                followUpDateUtc: examinedAt.AddDays(7 + ctx.Random.Next(0, 8)));
            _db.Treatments.Add(treatment);
            result.Treatments.Add(treatment);
        }
    }

    private void WriteAppliedVaccination(Context ctx, Clinical result, ExaminationRecord exam)
    {
        var code = exam.Pet.SpeciesCode == "CAT"
            ? (ctx.Random.Next(3) == 0 ? "LEUKEMIA" : ctx.Random.Next(2) == 0 ? "MIXED" : "RABIES")
            : ctx.Random.Next(2) == 0 ? "MIXED" : "RABIES";
        if (!ctx.Catalog.Vaccines.TryGetValue(code, out var definition))
            return;

        var appliedAt = exam.Examination.ExaminedAtUtc.AddMinutes(10);
        var vaccination = new Vaccination(
            ctx.TenantId, exam.Pet.Pet.Id, ctx.ClinicId, exam.Examination.Id, definition.Id, definition.Name,
            VaccinationStatus.Applied, appliedAt, appliedAt.AddYears(1), Tag("Uygulandı; bir sonraki doz 1 yıl sonra."));
        _db.Vaccinations.Add(vaccination);
        result.Vaccinations.Add(new VaccinationRecord(vaccination, exam, code));
    }

    /// <summary>Planlı aşılar: önümüzdeki günlerde dört hatırlatma ve bir gecikmiş rapel (hatırlatma listesi için).</summary>
    private void WriteScheduledVaccinations(Context ctx, Clinical result, People people)
    {
        if (!ctx.Catalog.Vaccines.TryGetValue("RABIES", out var rabies) || !ctx.Catalog.Vaccines.TryGetValue("MIXED", out var mixed))
            return;

        var candidates = people.Pets.Where(p => p.SpeciesCode != "BIRD").ToList();
        var dueOffsets = new[] { 2, 5, 9, 21 };
        foreach (var offset in dueOffsets)
        {
            var pet = PickPet(ctx.Random, candidates, []);
            var definition = offset % 2 == 0 ? rabies : mixed;
            var due = OperationDayBounds.NoonUtcForLocalDate(ctx.Today.AddDays(offset));
            var vaccination = new Vaccination(
                ctx.TenantId, pet.Pet.Id, ctx.ClinicId, null, definition.Id, definition.Name,
                VaccinationStatus.Scheduled, null, due, Tag("Planlı rapel; sahibine hatırlatma gönderilecek."));
            _db.Vaccinations.Add(vaccination);
            result.Vaccinations.Add(new VaccinationRecord(vaccination, null, definition.Code));
        }

        var overduePet = PickPet(ctx.Random, candidates, []);
        var overdue = new Vaccination(
            ctx.TenantId, overduePet.Pet.Id, ctx.ClinicId, null, mixed.Id, mixed.Name,
            VaccinationStatus.Scheduled, null, OperationDayBounds.NoonUtcForLocalDate(ctx.Today.AddDays(-6)),
            Tag("Gecikmiş rapel; sahibine ulaşılamadı."));
        _db.Vaccinations.Add(overdue);
        result.Vaccinations.Add(new VaccinationRecord(overdue, null, mixed.Code));
    }

    /// <summary>Lab: dört sonuçlanmış (biri yorum bekliyor) ve bir istenmiş/bekleyen kayıt.</summary>
    private void WriteLabResults(Context ctx, Clinical result)
    {
        var exams = result.Examinations
            .Where(e => e.Pet.SpeciesCode != "BIRD")
            .OrderBy(e => e.Examination.ExaminedAtUtc)
            .ToList();
        if (exams.Count < 5)
            return;

        var picks = new[] { exams[0], exams[exams.Count / 4], exams[exams.Count / 2], exams[(exams.Count * 3) / 4], exams[^1] };
        var tests = new (string Name, string Result, string? Interpretation)[]
        {
            ("Tam kan sayımı (CBC)", "WBC 11,2 x10^9/L; RBC 7,1 x10^12/L; HCT %41; PLT 310 x10^9/L.", "Referans aralığında; enfeksiyon bulgusu yok."),
            ("Biyokimya paneli", "ALT 48 U/L; ALP 95 U/L; BUN 22 mg/dL; Kreatinin 1,1 mg/dL; Glukoz 98 mg/dL.", "Karaciğer ve böbrek değerleri normal."),
            ("Dışkı parazit muayenesi", "Flotasyonda Giardia kisti görüldü; helmint yumurtası yok.", "Giardiasis; antiprotozoal tedavi başlandı."),
            ("İdrar tahlili", "Dansite 1,035; pH 6,5; protein negatif; sediment temiz.", null),
        };

        for (var i = 0; i < tests.Length; i++)
        {
            var exam = picks[i];
            var resultAt = exam.Examination.ExaminedAtUtc.AddHours(3);
            _db.LabResults.Add(new LabResult(
                ctx.TenantId, ctx.ClinicId, exam.Pet.Pet.Id, exam.Examination.Id, null, ctx.UserId,
                resultAt, tests[i].Name, tests[i].Result, tests[i].Interpretation,
                Tag(tests[i].Interpretation is null ? "Sonuç geldi; hekim yorumu bekleniyor." : "Klinik içi cihazda çalışıldı.")));
            result.LabResults++;
            result.LabCharges.Add((exam, tests[i].Name, 400m));
        }

        var pending = picks[^1];
        _db.LabResults.Add(new LabResult(
            ctx.TenantId, ctx.ClinicId, pending.Pet.Pet.Id, pending.Examination.Id, null, ctx.UserId,
            null, "Tiroid paneli (T4)", null, null, Tag("Dış laboratuvara gönderildi; sonuç bekleniyor.")));
        result.LabResults++;
    }

    // ---------------------------------------------------------------------------------------------
    // Ürün, parti, stok hareketi
    // ---------------------------------------------------------------------------------------------

    /// <summary>(anahtar, ad, birim, fiyat, sınıf, SKU, min stok, giriş adedi, lot var mı, SKT ay, form)</summary>
    private static readonly (string Key, string Name, string Unit, decimal Price, ProductKind Kind, string Sku, decimal MinStock, decimal Qty, bool Batch, int ExpiryMonths, string? Form)[] ProductSeeds =
    [
        ("RABIES", "Kuduz Aşısı", "doz", 350m, ProductKind.Vaccine, "ORN-ASI-KUDUZ", 5m, 20m, true, 14, "Enjeksiyonluk süspansiyon"),
        ("MIXED_DOG", "Karma Aşı (Köpek)", "doz", 450m, ProductKind.Vaccine, "ORN-ASI-KARMA-K", 5m, 15m, true, 11, "Enjeksiyonluk süspansiyon"),
        ("MIXED_CAT", "Karma Aşı (Kedi)", "doz", 420m, ProductKind.Vaccine, "ORN-ASI-KARMA-KD", 5m, 15m, true, 9, "Enjeksiyonluk süspansiyon"),
        ("LEUKEMIA", "Lösemi Aşısı (Kedi)", "doz", 480m, ProductKind.Vaccine, "ORN-ASI-LOSEMI", 3m, 10m, true, 1, "Enjeksiyonluk süspansiyon"),
        ("AMOKSISILIN", "Amoksisilin-Klavulanik Asit 250 mg Tablet", "kutu", 180m, ProductKind.Medicine, "ORN-ILAC-AMOKLAV", 3m, 12m, true, 18, "Tablet"),
        ("MELOKSIKAM", "Meloksikam Oral Süspansiyon 10 ml", "şişe", 220m, ProductKind.Medicine, "ORN-ILAC-MELOKS", 3m, 4m, true, 16, "Oral süspansiyon"),
        ("PARASITE", "İç Parazit Tableti (Kedi/Köpek)", "tablet", 60m, ProductKind.Medicine, "ORN-ILAC-ICPARAZIT", 10m, 40m, true, 24, "Tablet"),
        ("KULAK", "Kulak Temizleme Solüsyonu 100 ml", "şişe", 240m, ProductKind.Other, "ORN-BAKIM-KULAK", 2m, 8m, false, 0, null),
        ("FOOD_CAT", "Yavru Kedi Maması 2 kg", "paket", 520m, ProductKind.Other, "ORN-MAMA-KEDI-2KG", 2m, 10m, false, 0, null),
        ("FOOD_DOG", "Yetişkin Köpek Maması 15 kg", "paket", 1850m, ProductKind.Other, "ORN-MAMA-KOPEK-15KG", 2m, 6m, false, 0, null),
    ];

    private async Task<Stock> WriteProductsAndStockAsync(Context ctx, Clinical clinical, CancellationToken ct)
    {
        var receivedAt = ctx.LocalToUtc(ctx.Today.AddDays(-PastDays - 2), new TimeOnly(10, 0));
        var receivedDay = DateOnly.FromDateTime(OperationDayBounds.ToLocal(receivedAt));
        var products = new Dictionary<string, ProductRecord>();
        var movements = 0;

        foreach (var seed in ProductSeeds)
        {
            var product = new Product(
                ctx.TenantId, seed.Name, seed.Unit, seed.Price, Charge.DefaultCurrency,
                sku: seed.Sku, description: Tag("Örnek klinik ürünü."), kind: seed.Kind, pharmacologicalForm: seed.Form);
            _db.Products.Add(product);

            StockBatch? batch = null;
            if (seed.Batch)
            {
                // Lösemi aşısı partisi 1 ay içinde SKT'ye yaklaşır: "yaklaşan SKT" uyarısı için.
                var details = new StockBatchDetails(
                    LotNumber: $"LOT-{receivedDay:yyMM}-{seed.Sku[^4..]}",
                    ExpiryDate: ctx.Today.AddMonths(seed.ExpiryMonths).AddDays(ctx.Random.Next(0, 20)),
                    SupplierName: Tag("Anadolu Veteriner Ecza Deposu"),
                    InvoiceNumber: $"AVE-{receivedDay:yyyyMMdd}-{products.Count + 1:D3}",
                    InvoiceDate: receivedDay,
                    ReceivedDate: receivedDay);
                var created = StockBatch.Create(ctx.TenantId, ctx.ClinicId, product, seed.Qty, details, ctx.UserId, receivedAt);
                if (!created.IsSuccess)
                    throw new InvalidOperationException($"Örnek parti oluşturulamadı ({seed.Sku}): {created.Error.Code}");
                batch = created.Value!;
                _db.StockBatches.Add(batch);
            }

            var entry = new StockMovement(
                ctx.TenantId, ctx.ClinicId, product.Id, StockMovementType.In, seed.Qty, receivedAt,
                unitCost: Math.Round(seed.Price * 0.6m, 2), reason: "Tedarikçi girişi", referenceType: "GoodsReceipt",
                createdByUserId: ctx.UserId, notes: Tag("Açılış stok girişi."));
            if (batch is not null)
                entry.LinkBatch(batch);
            _db.StockMovements.Add(entry);
            movements++;

            products[seed.Key] = new ProductRecord(product, batch, seed.Qty, seed.MinStock);
        }

        // Çıkışlar: uygulanan aşılar ve tedavide kullanılan ilaçlar; iki de tezgâh satışı (mama).
        foreach (var vaccination in clinical.Vaccinations.Where(v => v.Examination is not null))
        {
            var key = vaccination.VaccineCode switch
            {
                "MIXED" => vaccination.Examination!.Pet.SpeciesCode == "CAT" ? "MIXED_CAT" : "MIXED_DOG",
                var other => other,
            };
            if (products.TryGetValue(key, out var product))
                movements += WriteOut(ctx, product, 1m, vaccination.Vaccination.AppliedAtUtc!.Value, "Vaccination", vaccination.Vaccination.Id, "Aşı uygulaması");
        }

        foreach (var exam in clinical.Examinations.Where(e => e.MedicineKey is not null))
        {
            if (products.TryGetValue(exam.MedicineKey!, out var product))
                movements += WriteOut(ctx, product, 1m, exam.Examination.ExaminedAtUtc.AddMinutes(15), "Examination", exam.Examination.Id, "Tedavide kullanım");
        }

        movements += WriteOut(ctx, products["FOOD_CAT"], 2m, ctx.LocalToUtc(ctx.Today.AddDays(-9), new TimeOnly(15, 10)), null, null, "Tezgâh satışı");
        movements += WriteOut(ctx, products["FOOD_DOG"], 1m, ctx.LocalToUtc(ctx.Today.AddDays(-2), new TimeOnly(11, 40)), null, null, "Tezgâh satışı");
        movements += WriteOut(ctx, products["PARASITE"], 6m, ctx.LocalToUtc(ctx.Today.AddDays(-15), new TimeOnly(16, 20)), null, null, "Reçeteli satış");

        foreach (var product in products.Values)
            _db.ProductStocks.Add(new ProductStock(ctx.TenantId, ctx.ClinicId, product.Product.Id, product.OnHand, product.MinStock));

        await _db.SaveChangesAsync(ct);
        return new Stock(products.Count, movements);
    }

    private int WriteOut(Context ctx, ProductRecord product, decimal quantity, DateTime occurredAtUtc, string? referenceType, Guid? referenceId, string reason)
    {
        if (product.OnHand < quantity)
            return 0;

        var movement = new StockMovement(
            ctx.TenantId, ctx.ClinicId, product.Product.Id, StockMovementType.Out, quantity, occurredAtUtc,
            reason: reason, referenceType: referenceType, referenceId: referenceId, createdByUserId: ctx.UserId, notes: Tag(reason));
        if (product.Batch is not null)
        {
            product.Batch.Decrease(quantity);
            movement.LinkBatch(product.Batch);
        }

        product.OnHand -= quantity;
        _db.StockMovements.Add(movement);
        return 1;
    }

    // ---------------------------------------------------------------------------------------------
    // Ücret ve ödeme: çoğu tam ödenmiş; kısmi ödeme ve açık borç örnekleri
    // ---------------------------------------------------------------------------------------------

    private async Task<Finance> WriteChargesAndPaymentsAsync(Context ctx, Clinical clinical, CancellationToken ct)
    {
        var charges = 0;
        var payments = 0;
        var exams = clinical.Examinations.OrderBy(e => e.Examination.ExaminedAtUtc).ToList();
        for (var i = 0; i < exams.Count; i++)
        {
            var exam = exams[i];
            var lines = new List<Charge> { CreateCharge(ctx, exam, "Muayene ücreti", ExaminationFee) };

            foreach (var vaccination in clinical.Vaccinations.Where(v => v.Examination == exam))
            {
                var price = vaccination.VaccineCode switch { "RABIES" => 350m, "LEUKEMIA" => 480m, _ => exam.Pet.SpeciesCode == "CAT" ? 420m : 450m };
                lines.Add(CreateCharge(ctx, exam, $"{vaccination.Vaccination.VaccineName} aşısı uygulaması", price));
            }

            foreach (var treatment in clinical.Treatments.Where(t => t.ExaminationId == exam.Examination.Id))
                lines.Add(CreateCharge(ctx, exam, treatment.Title, 300m + ctx.Random.Next(0, 5) * 50m));

            foreach (var (_, testName, price) in clinical.LabCharges.Where(l => l.Exam == exam))
                lines.Add(CreateCharge(ctx, exam, testName, price));

            _db.Charges.AddRange(lines);
            charges += lines.Count;

            // Senaryo: her 6. muayene açık borç, her 9. kısmi ödeme, bugünkü taslaklar henüz ödenmemiş; kalanı tam ödenmiş.
            var isToday = exam.Examination.ExaminedAtUtc >= OperationDayBounds.ForLocalDate(ctx.Today).DayStartUtc;
            if (i % 6 == 5 || isToday)
                continue;

            var paidAt = exam.Examination.ExaminedAtUtc.AddMinutes(40);
            var method = ctx.Random.Next(3) == 0 ? PaymentMethod.Cash : ctx.Random.Next(4) == 0 ? PaymentMethod.Transfer : PaymentMethod.Card;
            var allocations = i % 9 == 8
                ? new List<(Charge Charge, decimal Amount)> { (lines[0], 200m) }
                : lines.Select(l => (Charge: l, Amount: l.Amount)).ToList();
            var amount = allocations.Sum(a => a.Amount);

            var payment = new Payment(
                ctx.TenantId, ctx.ClinicId, exam.Pet.Client.Id, exam.Pet.Pet.Id, exam.Appointment?.Id, exam.Examination.Id,
                amount, Charge.DefaultCurrency, method, paidAt,
                Tag(i % 9 == 8 ? "Kısmi ödeme; kalan tutar sonraki ziyarette alınacak." : "Muayene çıkışında tahsil edildi."));
            var allocated = payment.AllocateFully(allocations, ctx.UserId, paidAt);
            if (!allocated.IsSuccess)
                throw new InvalidOperationException($"Örnek ödeme tahsis edilemedi: {allocated.Error.Code}");

            _db.Payments.Add(payment);
            _db.PaymentAllocations.AddRange(allocated.Value!);
            payments++;

            await _paymentEvents.EnqueueAsync(
                PaymentIntegrationEventTypes.Created,
                new PaymentCreatedIntegrationEvent(
                    Guid.NewGuid(), ctx.NowUtc,
                    PaymentProjectionSnapshotFactory.Create(payment, exam.Pet.Client.FullName, ctx.ClinicName, exam.Pet.Pet.Name)),
                ct);
        }

        await _db.SaveChangesAsync(ct);
        return new Finance(charges, payments);
    }

    private static Charge CreateCharge(Context ctx, ExaminationRecord exam, string description, decimal unitPrice)
    {
        var created = Charge.Create(
            ctx.TenantId, ctx.ClinicId, exam.Pet.Client.Id, exam.Pet.Pet.Id, null, exam.Examination.Id,
            Tag(description), 1m, unitPrice, ctx.UserId, exam.Examination.ExaminedAtUtc.AddMinutes(30), null);
        if (!created.IsSuccess)
            throw new InvalidOperationException($"Örnek ücret oluşturulamadı ({description}): {created.Error.Code}");
        return created.Value!;
    }

    private static string Tag(string text) => TagPrefix + text;

    // ---------------------------------------------------------------------------------------------
    // Yardımcı tipler
    // ---------------------------------------------------------------------------------------------

    private sealed record Context(
        Guid TenantId,
        Guid ClinicId,
        Guid UserId,
        string ClinicName,
        DateTime NowUtc,
        DateOnly Today,
        Random Random,
        ReferenceCatalog Catalog)
    {
        /// <summary>İstanbul duvar saatini UTC'ye çevirir (gün sınırı <see cref="OperationDayBounds"/> ile aynı).</summary>
        public DateTime LocalToUtc(DateOnly day, TimeOnly time)
            => OperationDayBounds.ForLocalDate(day).DayStartUtc.Add(time.ToTimeSpan());
    }

    /// <summary>Tür/ırk ve global aşı kataloğu: temel seed'den gelir (<see cref="SpeciesAndBreedCatalogSeeder"/>, <see cref="VaccineDefinitionSeeder"/>).</summary>
    private sealed class ReferenceCatalog
    {
        public required IReadOnlyDictionary<string, Species> Species { get; init; }
        public required IReadOnlyList<Breed> Breeds { get; init; }
        public required IReadOnlyDictionary<string, VaccineDefinition> Vaccines { get; init; }
        /// <summary>Aktif renk kataloğu ("Bilinmiyor" ve "Diğer" hariç); boşsa renk atanmaz.</summary>
        public required IReadOnlyList<PetColor> Colors { get; init; }

        public Breed? FindBreed(Guid speciesId, string name)
            => Breeds.FirstOrDefault(b => b.SpeciesId == speciesId && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Türe uygun renk: kuşlarda sarı/yeşil/mavi tonları yoksa katalogdan rastgele; deterministik (sabit tohum).</summary>
        public Guid? PickColor(Random random, string speciesCode)
        {
            if (Colors.Count == 0)
                return null;

            var preferred = speciesCode == "BIRD"
                ? Colors.Where(c => c.Id == PetColorSeedConstants.Yellow || c.Id == PetColorSeedConstants.MultiColor || c.Id == PetColorSeedConstants.White).ToList()
                : Colors.Where(c => c.Id != PetColorSeedConstants.Yellow).ToList();
            var pool = preferred.Count > 0 ? preferred : Colors;
            return pool[random.Next(pool.Count)].Id;
        }

        public static async Task<ReferenceCatalog> LoadAsync(AppDbContext db, CancellationToken ct)
        {
            string[] speciesCodes = ["CAT", "DOG", "BIRD"];
            var species = await db.Species.AsNoTracking().Where(s => speciesCodes.Contains(s.Code)).ToListAsync(ct);
            var missing = speciesCodes.Except(species.Select(s => s.Code)).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Örnek klinik için tür kataloğu eksik ({string.Join(", ", missing)}); temel seed (SpeciesAndBreedCatalogSeeder) çalışmamış.");
            }

            var speciesIds = species.Select(s => s.Id).ToArray();
            var breeds = await db.Breeds.AsNoTracking().Where(b => speciesIds.Contains(b.SpeciesId) && b.IsActive).ToListAsync(ct);
            var vaccines = await db.VaccineDefinitions.AsNoTracking()
                .Where(v => v.TenantId == null && v.IsActive)
                .ToListAsync(ct);
            var colors = await db.PetColors.AsNoTracking()
                .Where(c => c.IsActive && c.Id != PetColorSeedConstants.Unknown && c.Id != PetColorSeedConstants.Other)
                .OrderBy(c => c.Code)
                .ToListAsync(ct);

            return new ReferenceCatalog
            {
                Species = species.ToDictionary(s => s.Code, StringComparer.Ordinal),
                Breeds = breeds,
                Vaccines = vaccines.GroupBy(v => v.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
                Colors = colors,
            };
        }
    }

    private sealed record PetRecord(Pet Pet, Client Client, string SpeciesCode, decimal WeightKg);

    private sealed record People(IReadOnlyList<Client> Clients, IReadOnlyList<PetRecord> Pets);

    private sealed record AppointmentRecord(Appointment Appointment, PetRecord Pet);

    private sealed record ExaminationRecord(Examination Examination, PetRecord Pet, Appointment? Appointment, string? MedicineKey);

    private sealed record VaccinationRecord(Vaccination Vaccination, ExaminationRecord? Examination, string VaccineCode);

    private sealed class Clinical
    {
        public List<ExaminationRecord> Examinations { get; } = [];
        public List<VaccinationRecord> Vaccinations { get; } = [];
        public List<Treatment> Treatments { get; } = [];
        public List<(ExaminationRecord Exam, string TestName, decimal Price)> LabCharges { get; } = [];
        public int LabResults { get; set; }
    }

    private sealed class ProductRecord(Product product, StockBatch? batch, decimal onHand, decimal minStock)
    {
        public Product Product { get; } = product;
        public StockBatch? Batch { get; } = batch;
        public decimal OnHand { get; set; } = onHand;
        public decimal MinStock { get; } = minStock;
    }

    private sealed record Stock(int Products, int Movements);

    private sealed record Finance(int Charges, int Payments);
}
