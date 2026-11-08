using Backend.Veteriner.Application.DataExports;
using Backend.Veteriner.Application.Users.Common;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.CareInstructions;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ClientCredits;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
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
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.DataExports;

/// <summary>Paketteki tek CSV dosyası: kiracı sorgusu ve sütunları.</summary>
public interface IDataExportTable
{
    string FileName { get; }
    string Description { get; }

    /// <summary>Başlık ve satırları yazar; BENIOKU için özet döner.</summary>
    Task<DataExportFileSummary> WriteAsync(AppDbContext db, Guid tenantId, TextWriter writer, CancellationToken ct);
}

public sealed class DataExportTable<TRow> : IDataExportTable
    where TRow : class
{
    private readonly Func<AppDbContext, Guid, IQueryable<TRow>> _query;
    private readonly IReadOnlyList<DataExportColumn<TRow>> _columns;

    public DataExportTable(
        string fileName,
        string description,
        Func<AppDbContext, Guid, IQueryable<TRow>> query,
        IReadOnlyList<DataExportColumn<TRow>> columns)
    {
        if (columns.Count == 0 || columns[0].Name != "Id")
            throw new ArgumentException("İlk sütun kayıt kimliği (Id) olmalı.", nameof(columns));

        FileName = fileName;
        Description = description;
        _query = query;
        _columns = columns;
    }

    public string FileName { get; }
    public string Description { get; }

    public IReadOnlyList<string> Headers => _columns.SelectMany(c => c.Headers).ToList();

    public async Task<DataExportFileSummary> WriteAsync(AppDbContext db, Guid tenantId, TextWriter writer, CancellationToken ct)
    {
        await writer.WriteAsync(DataExportFormat.HeaderLine(_columns) + DataExportFormat.LineEnding);

        long count = 0;
        await foreach (var row in _query(db, tenantId).AsNoTracking().AsAsyncEnumerable().WithCancellation(ct))
        {
            await writer.WriteAsync(DataExportFormat.RowLine(_columns, row) + DataExportFormat.LineEnding);
            count++;
        }

        return new DataExportFileSummary(FileName, Description, count, DataExportFormat.Describe(_columns));
    }
}

/// <summary>
/// Paket kapsamı — tek yer (DATA_EXPORT_API_CONTRACT.md §6.3). Her sorgu kiracıyı açıkça süzer (kiracı kapsamının filtresine ek).
/// Verilmeyen alanlar bilerek sütun listesinde yoktur: parola özeti, belirteçler, API anahtarı özeti, portal hesabı kimliği,
/// depo anahtarı, ileti içeriği, istek/idempotency anahtarları ve iç sıra sayaçları.
/// </summary>
public static class DataExportTables
{
    private const string ClinicRef = "Kayıtlı olduğu klinik (klinikler.csv Id).";
    private const string ClientRef = "Müşteri (musteriler.csv Id).";
    private const string PetRef = "Hasta (hastalar.csv Id).";
    private const string UserRef = "Kullanıcı (kullanicilar.csv Id).";
    private const string ExaminationRef = "Bağlı muayene (muayeneler.csv Id).";
    private const string VisitRef = "Bağlı geliş (gelisler.csv Id).";
    private const string ProductRef = "Ürün (urunler.csv Id).";
    private const string Currency = "Para birimi (ISO 4217, ör. TRY).";

    public sealed record StaffRow(Guid Id, string? DisplayName, string Email, DateTime JoinedAtUtc);

    public static readonly IReadOnlyList<IDataExportTable> All =
    [
        new DataExportTable<Clinic>("klinikler.csv", "Klinikler",
            (db, t) => db.Clinics.Where(x => x.TenantId == t),
            [
                DataExportColumn<Clinic>.Id("Id", "Klinik kimliği.", x => x.Id),
                DataExportColumn<Clinic>.Text("Ad", "Klinik adı.", x => x.Name),
                DataExportColumn<Clinic>.Text("Il", "İl.", x => x.City),
                DataExportColumn<Clinic>.Text("Ilce", "İlçe.", x => x.District),
                DataExportColumn<Clinic>.Text("Telefon", "Telefon.", x => x.Phone),
                DataExportColumn<Clinic>.Text("Eposta", "E-posta.", x => x.Email),
                DataExportColumn<Clinic>.Text("Adres", "Adres.", x => x.Address),
                DataExportColumn<Clinic>.Text("Aciklama", "Açıklama.", x => x.Description),
                DataExportColumn<Clinic>.Boolean("Etkin", "Klinik etkin mi.", x => x.IsActive),
            ]),

        new DataExportTable<StaffRow>("kullanicilar.csv", "Kurum kullanıcıları (personel; e-posta ve giriş bilgisi verilmez)",
            (db, t) => db.UserTenants.Where(x => x.TenantId == t)
                .Select(x => new StaffRow(x.UserId, x.User!.DisplayName, x.User!.Email, x.CreatedAtUtc)),
            [
                DataExportColumn<StaffRow>.Id("Id", "Kullanıcı kimliği; diğer dosyalardaki hekim/kaydeden sütunları buna bağlanır.", x => x.Id),
                DataExportColumn<StaffRow>.Text("AdSoyad", "Görünen ad (ad yoksa e-postanın @ öncesinden türetilir).", x => UserDisplayName.Resolve(x.DisplayName, x.Email)),
                DataExportColumn<StaffRow>.Instant("KurumaKatilim", "Kuruma katılma zamanı.", x => x.JoinedAtUtc),
            ]),

        new DataExportTable<Client>("musteriler.csv", "Müşteriler (hasta sahipleri)",
            (db, t) => db.Clients.Where(x => x.TenantId == t),
            [
                DataExportColumn<Client>.Id("Id", "Müşteri kimliği.", x => x.Id),
                DataExportColumn<Client>.Text("AdSoyad", "Ad soyad ya da unvan.", x => x.FullName),
                DataExportColumn<Client>.Text("Telefon", "Telefon.", x => x.Phone),
                DataExportColumn<Client>.Text("Eposta", "E-posta.", x => x.Email),
                DataExportColumn<Client>.Text("Adres", "Adres.", x => x.Address),
                DataExportColumn<Client>.Boolean("KayitsizGelis", "Hızlı (randevusuz, bilgisi eksik) kayıt.", x => x.IsWalkIn),
                DataExportColumn<Client>.Code("TercihEdilenKanal", "Tercih edilen iletişim kanalı.", x => x.PreferredContactChannel),
                DataExportColumn<Client>.Boolean("HatirlatmaIstemiyor", "Hatırlatma iletisi istemiyor.", x => x.RemindersOptedOut),
                DataExportColumn<Client>.Code("KvkkRizaDurumu", "KVKK açık rıza durumu.", x => x.KvkkConsentStatus),
                DataExportColumn<Client>.Date("KvkkRizaTarihi", "Rızanın kaydedildiği gün.", x => x.KvkkConsentRecordedOn),
                DataExportColumn<Client>.Id("KvkkRizaKaydedenId", UserRef, x => x.KvkkConsentRecordedByUserId),
                DataExportColumn<Client>.Number("KvkkFormSurumu", "Rıza formunun sürümü.", x => x.KvkkConsentTemplateVersion),
                DataExportColumn<Client>.Code("KisiTuru", "Fatura kişi türü.", x => x.PersonType),
                DataExportColumn<Client>.Text("TcKimlikNo", "T.C. kimlik numarası (fatura).", x => x.NationalId),
                DataExportColumn<Client>.Text("VergiNo", "Vergi numarası (fatura).", x => x.TaxNumber),
                DataExportColumn<Client>.Text("VergiDairesi", "Vergi dairesi.", x => x.TaxOffice),
                DataExportColumn<Client>.Text("Unvan", "Fatura unvanı.", x => x.LegalName),
                DataExportColumn<Client>.Text("FaturaIl", "Fatura ili.", x => x.BillingProvince),
                DataExportColumn<Client>.Text("FaturaIlce", "Fatura ilçesi.", x => x.BillingDistrict),
                DataExportColumn<Client>.Text("FaturaAdres", "Fatura adresi.", x => x.BillingAddressLine),
                DataExportColumn<Client>.Instant("Olusturma", "Kayıt zamanı.", x => x.CreatedAtUtc),
                DataExportColumn<Client>.Instant("Guncelleme", "Son güncelleme zamanı.", x => x.UpdatedAtUtc),
            ]),

        new DataExportTable<Pet>("hastalar.csv", "Hastalar (hayvanlar)",
            (db, t) => db.Pets.Where(x => x.TenantId == t).Include(x => x.Species).Include(x => x.BreedRef).Include(x => x.ColorRef),
            [
                DataExportColumn<Pet>.Id("Id", "Hasta kimliği.", x => x.Id),
                DataExportColumn<Pet>.Id("MusteriId", ClientRef, x => x.ClientId),
                DataExportColumn<Pet>.Text("HastaAdi", "Hasta adı.", x => x.Name),
                DataExportColumn<Pet>.Text("Tur", "Tür.", x => x.Species?.Name),
                DataExportColumn<Pet>.Text("Irk", "Irk (katalogdaki ad, yoksa serbest metin).", x => x.BreedRef?.Name ?? x.Breed),
                DataExportColumn<Pet>.Text("Renk", "Renk.", x => x.ColorRef?.Name),
                DataExportColumn<Pet>.Code("Cinsiyet", "Cinsiyet.", x => x.Gender),
                DataExportColumn<Pet>.Date("DogumTarihi", "Doğum tarihi.", x => x.BirthDate),
                DataExportColumn<Pet>.Number("KiloKg", "Kayıtlı kilo (kg).", x => x.Weight),
                DataExportColumn<Pet>.Boolean("Kisir", "Kısırlaştırılmış.", x => x.IsNeutered),
                DataExportColumn<Pet>.Text("CipNo", "Mikroçip numarası.", x => x.MicrochipNumber),
                DataExportColumn<Pet>.Text("PasaportKupeNo", "Pasaport ya da küpe numarası.", x => x.PassportOrTagNumber),
                DataExportColumn<Pet>.Text("OzelProtokolNo", "Özel protokol numarası.", x => x.SpecialProtocolNumber),
                DataExportColumn<Pet>.Text("HbsHayvanNo", "HBS hayvan kimliği.", x => x.HbsAnimalId),
                DataExportColumn<Pet>.Code("Uyarilar", "Hasta uyarıları (birden çoksa virgülle).", x => x.AlertFlags),
                DataExportColumn<Pet>.Text("UyariNotu", "Uyarı notu.", x => x.AlertNote),
                DataExportColumn<Pet>.Text("Not", "Hasta notu.", x => x.Notes),
                DataExportColumn<Pet>.Code("Durum", "Hasta durumu.", x => x.Status),
                DataExportColumn<Pet>.Date("VefatTarihi", "Vefat tarihi.", x => x.DeceasedOn),
            ]),

        new DataExportTable<AppointmentReason>("randevu_nedenleri.csv", "Randevu nedenleri",
            (db, t) => db.AppointmentReasons.Where(x => x.TenantId == t),
            [
                DataExportColumn<AppointmentReason>.Id("Id", "Randevu nedeni kimliği.", x => x.Id),
                DataExportColumn<AppointmentReason>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<AppointmentReason>.Text("Ad", "Neden adı.", x => x.Name),
                DataExportColumn<AppointmentReason>.Number("VarsayilanSureDk", "Varsayılan süre (dakika).", x => x.DefaultDurationMinutes),
                DataExportColumn<AppointmentReason>.Boolean("OnlineAlinabilir", "Online randevuda seçilebilir.", x => x.IsOnlineBookable),
                DataExportColumn<AppointmentReason>.Boolean("Etkin", "Etkin mi.", x => x.IsActive),
            ]),

        new DataExportTable<Appointment>("randevular.csv", "Randevular",
            (db, t) => db.Appointments.Where(x => x.TenantId == t),
            [
                DataExportColumn<Appointment>.Id("Id", "Randevu kimliği.", x => x.Id),
                DataExportColumn<Appointment>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<Appointment>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<Appointment>.Instant("Zaman", "Randevu başlangıcı.", x => x.ScheduledAtUtc),
                DataExportColumn<Appointment>.Number("SureDk", "Süre (dakika).", x => x.DurationMinutes),
                DataExportColumn<Appointment>.Code("Tur", "Randevu türü.", x => x.AppointmentType),
                DataExportColumn<Appointment>.Code("Durum", "Randevu durumu.", x => x.Status),
                DataExportColumn<Appointment>.Id("RandevuNedeniId", "Randevu nedeni (randevu_nedenleri.csv Id).", x => x.ReasonId),
                DataExportColumn<Appointment>.Id("HekimId", UserRef, x => x.VeterinarianUserId),
                DataExportColumn<Appointment>.Text("Not", "Randevu notu.", x => x.Notes),
                DataExportColumn<Appointment>.Instant("SahipOnayi", "Sahibin hatırlatmadan onayladığı zaman.", x => x.OwnerConfirmedAtUtc),
            ]),

        new DataExportTable<Visit>("gelisler.csv", "Gelişler (kliniğe kabul)",
            (db, t) => db.Visits.Where(x => x.TenantId == t),
            [
                DataExportColumn<Visit>.Id("Id", "Geliş kimliği.", x => x.Id),
                DataExportColumn<Visit>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<Visit>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<Visit>.Id("RandevuId", "Bağlı randevu (randevular.csv Id).", x => x.AppointmentId),
                DataExportColumn<Visit>.Id("SorumluHekimId", UserRef, x => x.ResponsibleVeterinarianUserId),
                DataExportColumn<Visit>.Instant("Gelis", "Geliş zamanı.", x => x.ArrivedAtUtc),
                DataExportColumn<Visit>.Code("BakimDurumu", "Bakım durumu.", x => x.CareStatus),
                DataExportColumn<Visit>.Instant("Baslama", "Muayeneye alınma zamanı.", x => x.StartedAtUtc),
                DataExportColumn<Visit>.Instant("Tamamlanma", "Tamamlanma zamanı.", x => x.CompletedAtUtc),
                DataExportColumn<Visit>.Boolean("Acil", "Acil geliş.", x => x.IsUrgent),
                DataExportColumn<Visit>.Text("GelisNedeni", "Geliş nedeni.", x => x.ArrivalReason),
                DataExportColumn<Visit>.Instant("Iptal", "Hatalı işaretlenme zamanı.", x => x.VoidedAtUtc),
                DataExportColumn<Visit>.Text("IptalNedeni", "Hatalı işaretleme gerekçesi.", x => x.VoidReason),
                DataExportColumn<Visit>.Id("KaydedenId", UserRef, x => x.CreatedByUserId),
            ]),

        new DataExportTable<Examination>("muayeneler.csv", "Muayeneler (metin alanları ve kesinleştirme dahil)",
            (db, t) => db.Examinations.Where(x => x.TenantId == t),
            [
                DataExportColumn<Examination>.Id("Id", "Muayene kimliği.", x => x.Id),
                DataExportColumn<Examination>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<Examination>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<Examination>.Id("RandevuId", "Bağlı randevu (randevular.csv Id).", x => x.AppointmentId),
                DataExportColumn<Examination>.Id("GelisId", VisitRef, x => x.VisitId),
                DataExportColumn<Examination>.Instant("Muayene", "Muayene zamanı.", x => x.ExaminedAtUtc),
                DataExportColumn<Examination>.Text("Sikayet", "Geliş nedeni / şikâyet.", x => x.VisitReason),
                DataExportColumn<Examination>.Text("Anamnez", "Anamnez.", x => x.Anamnesis),
                DataExportColumn<Examination>.Text("Bulgular", "Bulgular.", x => x.Findings),
                DataExportColumn<Examination>.Number("KiloKg", "Kilo (kg).", x => x.WeightKg),
                DataExportColumn<Examination>.Number("AtesC", "Ateş (°C).", x => x.TemperatureC),
                DataExportColumn<Examination>.Number("NabizDk", "Nabız (atım/dk).", x => x.HeartRateBpm),
                DataExportColumn<Examination>.Number("SolunumDk", "Solunum (/dk).", x => x.RespiratoryRatePerMin),
                DataExportColumn<Examination>.Instant("VitalOlcum", "Vital bulguların ölçüldüğü zaman.", x => x.VitalsMeasuredAtUtc),
                DataExportColumn<Examination>.Text("Degerlendirme", "Değerlendirme / tanı.", x => x.Assessment),
                DataExportColumn<Examination>.Text("Plan", "Tedavi planı.", x => x.Plan),
                DataExportColumn<Examination>.Text("Not", "Not.", x => x.Notes),
                DataExportColumn<Examination>.Code("Durum", "Muayene durumu.", x => x.Status),
                DataExportColumn<Examination>.Instant("Kesinlestirme", "Kesinleştirme zamanı.", x => x.FinalizedAtUtc),
                DataExportColumn<Examination>.Id("KesinlestirenId", UserRef, x => x.FinalizedByUserId),
                DataExportColumn<Examination>.Instant("Olusturma", "Kayıt zamanı.", x => x.CreatedAtUtc),
                DataExportColumn<Examination>.Instant("Guncelleme", "Son güncelleme zamanı.", x => x.UpdatedAtUtc),
            ]),

        new DataExportTable<ExaminationAddendum>("muayene_ek_notlari.csv", "Kesinleşmiş muayenelere eklenen ek notlar",
            (db, t) => db.ExaminationAddenda.Where(x => x.TenantId == t),
            [
                DataExportColumn<ExaminationAddendum>.Id("Id", "Ek not kimliği.", x => x.Id),
                DataExportColumn<ExaminationAddendum>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<ExaminationAddendum>.Id("MuayeneId", ExaminationRef, x => x.ExaminationId),
                DataExportColumn<ExaminationAddendum>.Text("Gerekce", "Ek not gerekçesi.", x => x.Reason),
                DataExportColumn<ExaminationAddendum>.Text("Metin", "Ek not metni.", x => x.Text),
                DataExportColumn<ExaminationAddendum>.Instant("Olusturma", "Eklenme zamanı.", x => x.CreatedAtUtc),
                DataExportColumn<ExaminationAddendum>.Id("EkleyenId", UserRef, x => x.CreatedByUserId),
            ]),

        new DataExportTable<Treatment>("tedaviler.csv", "Tedaviler",
            (db, t) => db.Treatments.Where(x => x.TenantId == t),
            [
                DataExportColumn<Treatment>.Id("Id", "Tedavi kimliği.", x => x.Id),
                DataExportColumn<Treatment>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<Treatment>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<Treatment>.Id("MuayeneId", ExaminationRef, x => x.ExaminationId),
                DataExportColumn<Treatment>.Instant("Tarih", "Tedavi zamanı.", x => x.TreatmentDateUtc),
                DataExportColumn<Treatment>.Text("Baslik", "Başlık.", x => x.Title),
                DataExportColumn<Treatment>.Text("Aciklama", "Açıklama.", x => x.Description),
                DataExportColumn<Treatment>.Text("Not", "Not.", x => x.Notes),
                DataExportColumn<Treatment>.Instant("Kontrol", "Kontrol (takip) zamanı.", x => x.FollowUpDateUtc),
                DataExportColumn<Treatment>.Instant("Olusturma", "Kayıt zamanı.", x => x.CreatedAtUtc),
            ]),

        new DataExportTable<Prescription>("receteler.csv", "Reçeteler",
            (db, t) => db.Prescriptions.Where(x => x.TenantId == t),
            [
                DataExportColumn<Prescription>.Id("Id", "Reçete kimliği.", x => x.Id),
                DataExportColumn<Prescription>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<Prescription>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<Prescription>.Id("MuayeneId", ExaminationRef, x => x.ExaminationId),
                DataExportColumn<Prescription>.Id("TedaviId", "Bağlı tedavi (tedaviler.csv Id).", x => x.TreatmentId),
                DataExportColumn<Prescription>.Instant("Tarih", "Reçete zamanı.", x => x.PrescribedAtUtc),
                DataExportColumn<Prescription>.Text("Baslik", "Başlık.", x => x.Title),
                DataExportColumn<Prescription>.Text("Icerik", "Reçete içeriği.", x => x.Content),
                DataExportColumn<Prescription>.Text("Not", "Not.", x => x.Notes),
                DataExportColumn<Prescription>.Instant("Kontrol", "Kontrol (takip) zamanı.", x => x.FollowUpDateUtc),
                DataExportColumn<Prescription>.Code("HbsDurumu", "HBS giriş durumu.", x => x.HbsStatus),
                DataExportColumn<Prescription>.Text("HbsReferansNo", "HBS referans numarası.", x => x.HbsReferenceNumber),
                DataExportColumn<Prescription>.Text("HbsGerekmezNedeni", "HBS girişi gerekmeme gerekçesi.", x => x.HbsNotRequiredReason),
                DataExportColumn<Prescription>.Instant("Iptal", "İptal zamanı.", x => x.CancelledAtUtc),
                DataExportColumn<Prescription>.Text("IptalNedeni", "İptal gerekçesi.", x => x.CancelReason),
                DataExportColumn<Prescription>.Boolean("KesinlestirmeSonrasi", "Muayene kesinleştikten sonra eklendi.", x => x.AddedAfterFinalization),
                DataExportColumn<Prescription>.Text("GecGirisNedeni", "Geç giriş gerekçesi.", x => x.LateEntryReason),
                DataExportColumn<Prescription>.Instant("Olusturma", "Kayıt zamanı.", x => x.CreatedAtUtc),
            ]),

        new DataExportTable<PrescriptionItem>("recete_satirlari.csv", "Reçete satırları (ilaçlar)",
            (db, t) => db.Prescriptions.Where(x => x.TenantId == t).SelectMany(x => x.Items),
            [
                DataExportColumn<PrescriptionItem>.Id("Id", "Satır kimliği.", x => x.Id),
                DataExportColumn<PrescriptionItem>.Id("ReceteId", "Reçete (receteler.csv Id).", x => x.PrescriptionId),
                DataExportColumn<PrescriptionItem>.Number("Sira", "Satır sırası.", x => x.SortOrder),
                DataExportColumn<PrescriptionItem>.Text("Ad", "İlaç / ürün adı.", x => x.Name),
                DataExportColumn<PrescriptionItem>.Id("UrunId", ProductRef, x => x.ProductId),
                DataExportColumn<PrescriptionItem>.Number("Miktar", "Miktar.", x => x.Quantity),
                DataExportColumn<PrescriptionItem>.Text("Doz", "Doz.", x => x.Dose),
                DataExportColumn<PrescriptionItem>.Text("Birim", "Birim.", x => x.Unit),
                DataExportColumn<PrescriptionItem>.Text("Kullanim", "Kullanım talimatı.", x => x.Instructions),
                DataExportColumn<PrescriptionItem>.Text("SeriNo", "Seri / parti numarası.", x => x.LotNumber),
            ]),

        new DataExportTable<Vaccination>("asilar.csv", "Aşılar (uygulanan ve planlanan)",
            (db, t) => db.Vaccinations.Where(x => x.TenantId == t),
            [
                DataExportColumn<Vaccination>.Id("Id", "Aşı kaydı kimliği.", x => x.Id),
                DataExportColumn<Vaccination>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<Vaccination>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<Vaccination>.Id("MuayeneId", ExaminationRef, x => x.ExaminationId),
                DataExportColumn<Vaccination>.Text("AsiAdi", "Aşı adı.", x => x.VaccineName),
                DataExportColumn<Vaccination>.Code("Durum", "Aşı durumu.", x => x.Status),
                DataExportColumn<Vaccination>.Instant("Uygulama", "Uygulama zamanı.", x => x.AppliedAtUtc),
                DataExportColumn<Vaccination>.Instant("SonrakiDoz", "Sonraki doz zamanı.", x => x.DueAtUtc),
                DataExportColumn<Vaccination>.Code("UygulamaYolu", "Uygulama yolu.", x => x.Route),
                DataExportColumn<Vaccination>.Text("UygulamaBolgesi", "Uygulama bölgesi.", x => x.Site),
                DataExportColumn<Vaccination>.Text("Not", "Not.", x => x.Notes),
                DataExportColumn<Vaccination>.Code("HbsDurumu", "HBS giriş durumu.", x => x.HbsStatus),
                DataExportColumn<Vaccination>.Text("HbsReferansNo", "HBS referans numarası.", x => x.HbsReferenceNumber),
                DataExportColumn<Vaccination>.Instant("Olusturma", "Kayıt zamanı.", x => x.CreatedAtUtc),
            ]),

        new DataExportTable<Administration>("uygulamalar.csv", "İlaç / malzeme uygulamaları",
            (db, t) => db.Administrations.Where(x => x.TenantId == t),
            [
                DataExportColumn<Administration>.Id("Id", "Uygulama kimliği.", x => x.Id),
                DataExportColumn<Administration>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<Administration>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<Administration>.Id("MuayeneId", ExaminationRef, x => x.ExaminationId),
                DataExportColumn<Administration>.Id("TedaviId", "Bağlı tedavi (tedaviler.csv Id).", x => x.TreatmentId),
                DataExportColumn<Administration>.Id("UrunId", ProductRef, x => x.ProductId),
                DataExportColumn<Administration>.Id("StokPartisiId", "Stok partisi (stok_partileri.csv Id).", x => x.BatchId),
                DataExportColumn<Administration>.Id("AsiId", "Bağlı aşı (asilar.csv Id).", x => x.VaccinationId),
                DataExportColumn<Administration>.Number("Miktar", "Miktar.", x => x.Quantity),
                DataExportColumn<Administration>.Instant("Uygulama", "Uygulama zamanı.", x => x.AdministeredAtUtc),
                DataExportColumn<Administration>.Code("UygulamaYolu", "Uygulama yolu.", x => x.Route),
                DataExportColumn<Administration>.Text("UygulamaBolgesi", "Uygulama bölgesi.", x => x.Site),
                DataExportColumn<Administration>.Text("Not", "Not.", x => x.Notes),
                DataExportColumn<Administration>.Instant("Iptal", "İptal zamanı.", x => x.VoidedAtUtc),
                DataExportColumn<Administration>.Text("IptalNedeni", "İptal gerekçesi.", x => x.VoidReason),
                DataExportColumn<Administration>.Boolean("KesinlestirmeSonrasi", "Muayene kesinleştikten sonra eklendi.", x => x.AddedAfterFinalization),
                DataExportColumn<Administration>.Text("GecGirisNedeni", "Geç giriş gerekçesi.", x => x.LateEntryReason),
                DataExportColumn<Administration>.Id("KaydedenId", UserRef, x => x.CreatedByUserId),
            ]),

        new DataExportTable<LabResult>("lab_sonuclari.csv", "Laboratuvar istek ve sonuçları (ekli dosyalar hariç)",
            (db, t) => db.LabResults.Where(x => x.TenantId == t),
            [
                DataExportColumn<LabResult>.Id("Id", "Lab kaydı kimliği.", x => x.Id),
                DataExportColumn<LabResult>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<LabResult>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<LabResult>.Id("MuayeneId", ExaminationRef, x => x.ExaminationId),
                DataExportColumn<LabResult>.Id("GelisId", VisitRef, x => x.VisitId),
                DataExportColumn<LabResult>.Text("TestAdi", "Test adı.", x => x.TestName),
                DataExportColumn<LabResult>.Code("Durum", "Lab durumu.", x => x.Status),
                DataExportColumn<LabResult>.Instant("Istek", "İstek zamanı.", x => x.RequestedAtUtc),
                DataExportColumn<LabResult>.Id("IsteyenId", UserRef, x => x.RequestedByUserId),
                DataExportColumn<LabResult>.Instant("Sonuc", "Sonuç zamanı.", x => x.ResultDateUtc),
                DataExportColumn<LabResult>.Text("SonucMetni", "Sonuç metni.", x => x.ResultText),
                DataExportColumn<LabResult>.Text("Yorum", "Hekim yorumu.", x => x.Interpretation),
                DataExportColumn<LabResult>.Text("Not", "Not.", x => x.Notes),
                DataExportColumn<LabResult>.Instant("Iptal", "İptal zamanı.", x => x.CancelledAtUtc),
                DataExportColumn<LabResult>.Text("IptalNedeni", "İptal gerekçesi.", x => x.CancelReason),
            ]),

        new DataExportTable<ImagingRecord>("goruntuleme.csv", "Görüntüleme kayıtları (görüntü dosyaları hariç)",
            (db, t) => db.ImagingRecords.Where(x => x.TenantId == t),
            [
                DataExportColumn<ImagingRecord>.Id("Id", "Görüntüleme kaydı kimliği.", x => x.Id),
                DataExportColumn<ImagingRecord>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<ImagingRecord>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<ImagingRecord>.Id("MuayeneId", ExaminationRef, x => x.ExaminationId),
                DataExportColumn<ImagingRecord>.Instant("Cekim", "Çekim zamanı.", x => x.PerformedAtUtc),
                DataExportColumn<ImagingRecord>.Code("Yontem", "Görüntüleme yöntemi.", x => x.Modality),
                DataExportColumn<ImagingRecord>.Text("Aciklama", "Açıklama.", x => x.Description),
                DataExportColumn<ImagingRecord>.Text("KlinikNot", "Klinik not.", x => x.ClinicalNote),
                DataExportColumn<ImagingRecord>.Code("Durum", "Kayıt durumu.", x => x.Status),
                DataExportColumn<ImagingRecord>.Instant("Iptal", "İptal zamanı.", x => x.CancelledAtUtc),
                DataExportColumn<ImagingRecord>.Text("IptalNedeni", "İptal gerekçesi.", x => x.CancelReason),
            ]),

        new DataExportTable<StoredFile>("dosyalar.csv", "Yüklenen dosyaların kaydı (içerik pakette yok)",
            (db, t) => db.StoredFiles.Where(x => x.TenantId == t),
            [
                DataExportColumn<StoredFile>.Id("Id", "Dosya kimliği.", x => x.Id),
                DataExportColumn<StoredFile>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<StoredFile>.Code("SahipKayitTuru", "Dosyanın bağlı olduğu kayıt türü.", x => x.OwnerType),
                DataExportColumn<StoredFile>.Id("SahipKayitId", "Bağlı kaydın kimliği (ör. lab_sonuclari.csv ya da goruntuleme.csv Id).", x => x.OwnerId),
                DataExportColumn<StoredFile>.Text("DosyaAdi", "Yüklenen dosyanın adı.", x => x.OriginalFileName),
                DataExportColumn<StoredFile>.Text("IcerikTuru", "İçerik türü (MIME).", x => x.ContentType),
                DataExportColumn<StoredFile>.Number("BoyutBayt", "Boyut (bayt).", x => x.SizeBytes),
                DataExportColumn<StoredFile>.Code("Durum", "Dosya durumu.", x => x.Status),
                DataExportColumn<StoredFile>.Instant("Yukleme", "Yükleme zamanı.", x => x.CreatedAtUtc),
                DataExportColumn<StoredFile>.Instant("Silinme", "Silinme zamanı.", x => x.DeletedAtUtc),
            ]),

        new DataExportTable<Hospitalization>("yatislar.csv", "Yatışlar",
            (db, t) => db.Hospitalizations.Where(x => x.TenantId == t),
            [
                DataExportColumn<Hospitalization>.Id("Id", "Yatış kimliği.", x => x.Id),
                DataExportColumn<Hospitalization>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<Hospitalization>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<Hospitalization>.Id("MuayeneId", ExaminationRef, x => x.ExaminationId),
                DataExportColumn<Hospitalization>.Id("GelisId", VisitRef, x => x.VisitId),
                DataExportColumn<Hospitalization>.Instant("Yatis", "Yatış zamanı.", x => x.AdmittedAtUtc),
                DataExportColumn<Hospitalization>.Instant("PlanlananTaburcu", "Planlanan taburcu zamanı.", x => x.PlannedDischargeAtUtc),
                DataExportColumn<Hospitalization>.Instant("Taburcu", "Taburcu zamanı.", x => x.DischargedAtUtc),
                DataExportColumn<Hospitalization>.Text("Neden", "Yatış nedeni.", x => x.Reason),
                DataExportColumn<Hospitalization>.Text("Konum", "Konum.", x => x.Location),
                DataExportColumn<Hospitalization>.Text("Not", "Not.", x => x.Notes),
                DataExportColumn<Hospitalization>.Text("TaburcuNotu", "Taburcu notu.", x => x.DischargeNote),
            ]),

        new DataExportTable<CareInstruction>("bakim_talimatlari.csv", "Bakım talimatları",
            (db, t) => db.CareInstructions.Where(x => x.TenantId == t),
            [
                DataExportColumn<CareInstruction>.Id("Id", "Talimat kimliği.", x => x.Id),
                DataExportColumn<CareInstruction>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<CareInstruction>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<CareInstruction>.Id("YatisId", "Bağlı yatış (yatislar.csv Id).", x => x.HospitalizationId),
                DataExportColumn<CareInstruction>.Id("MuayeneId", ExaminationRef, x => x.ExaminationId),
                DataExportColumn<CareInstruction>.Id("GelisId", VisitRef, x => x.VisitId),
                DataExportColumn<CareInstruction>.Text("Metin", "Talimat metni.", x => x.Text),
                DataExportColumn<CareInstruction>.Instant("Olusturma", "Kayıt zamanı.", x => x.CreatedAtUtc),
                DataExportColumn<CareInstruction>.Instant("Guncelleme", "Son güncelleme zamanı.", x => x.UpdatedAtUtc),
            ]),

        new DataExportTable<Charge>("ucretler.csv", "Ücretler (hizmet ve ürün bedelleri)",
            (db, t) => db.Charges.Where(x => x.TenantId == t),
            [
                DataExportColumn<Charge>.Id("Id", "Ücret kimliği.", x => x.Id),
                DataExportColumn<Charge>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<Charge>.Id("MusteriId", ClientRef, x => x.ClientId),
                DataExportColumn<Charge>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<Charge>.Id("GelisId", VisitRef, x => x.VisitId),
                DataExportColumn<Charge>.Id("MuayeneId", ExaminationRef, x => x.ExaminationId),
                DataExportColumn<Charge>.Text("Aciklama", "Açıklama.", x => x.Description),
                DataExportColumn<Charge>.Code("SatirTuru", "Satır türü.", x => x.LineKind),
                DataExportColumn<Charge>.Number("Miktar", "Miktar.", x => x.Quantity),
                DataExportColumn<Charge>.Number("BirimFiyat", "Birim fiyat.", x => x.UnitPrice),
                DataExportColumn<Charge>.Number("Tutar", "Tutar.", x => x.Amount),
                DataExportColumn<Charge>.Number("KdvOrani", "KDV oranı (yüzde).", x => x.VatRate),
                DataExportColumn<Charge>.Text("ParaBirimi", Currency, x => x.Currency),
                DataExportColumn<Charge>.Number("OdenenTutar", "Ödemelerle ve kredilerle kapanan tutar.", x => x.AllocatedAmount),
                DataExportColumn<Charge>.Instant("Olusturma", "Kayıt zamanı.", x => x.CreatedAtUtc),
                DataExportColumn<Charge>.Id("KaydedenId", UserRef, x => x.CreatedByUserId),
                DataExportColumn<Charge>.Instant("Iptal", "İptal zamanı.", x => x.VoidedAtUtc),
                DataExportColumn<Charge>.Text("IptalNedeni", "İptal gerekçesi.", x => x.VoidReason),
            ]),

        new DataExportTable<Payment>("odemeler.csv", "Ödemeler (tahsilatlar)",
            (db, t) => db.Payments.Where(x => x.TenantId == t),
            [
                DataExportColumn<Payment>.Id("Id", "Ödeme kimliği.", x => x.Id),
                DataExportColumn<Payment>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<Payment>.Id("MusteriId", ClientRef, x => x.ClientId),
                DataExportColumn<Payment>.Id("HastaId", PetRef, x => x.PetId),
                DataExportColumn<Payment>.Id("RandevuId", "Bağlı randevu (randevular.csv Id).", x => x.AppointmentId),
                DataExportColumn<Payment>.Id("MuayeneId", ExaminationRef, x => x.ExaminationId),
                DataExportColumn<Payment>.Number("Tutar", "Tutar.", x => x.Amount),
                DataExportColumn<Payment>.Text("ParaBirimi", Currency, x => x.Currency),
                DataExportColumn<Payment>.Code("Yontem", "Ödeme yöntemi.", x => x.Method),
                DataExportColumn<Payment>.Instant("Odeme", "Ödeme zamanı.", x => x.PaidAtUtc),
                DataExportColumn<Payment>.Number("UcreteDagitilan", "Ücretlere dağıtılan tutar.", x => x.AllocatedAmount),
                DataExportColumn<Payment>.Number("KredideKalan", "Müşteri kredisine aktarılan tutar.", x => x.CreditedAmount),
                DataExportColumn<Payment>.Text("Not", "Not.", x => x.Notes),
                DataExportColumn<Payment>.Instant("Iptal", "İptal zamanı.", x => x.VoidedAtUtc),
                DataExportColumn<Payment>.Text("IptalNedeni", "İptal gerekçesi.", x => x.VoidReason),
            ]),

        new DataExportTable<PaymentAllocation>("odeme_dagitimlari.csv", "Ödemenin ücretlere dağıtımı",
            (db, t) => db.PaymentAllocations.Where(x => x.TenantId == t),
            [
                DataExportColumn<PaymentAllocation>.Id("Id", "Dağıtım kimliği.", x => x.Id),
                DataExportColumn<PaymentAllocation>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<PaymentAllocation>.Id("OdemeId", "Ödeme (odemeler.csv Id).", x => x.PaymentId),
                DataExportColumn<PaymentAllocation>.Id("UcretId", "Ücret (ucretler.csv Id).", x => x.ChargeId),
                DataExportColumn<PaymentAllocation>.Number("Tutar", "Dağıtılan tutar.", x => x.Amount),
                DataExportColumn<PaymentAllocation>.Instant("Olusturma", "Kayıt zamanı.", x => x.CreatedAtUtc),
                DataExportColumn<PaymentAllocation>.Instant("GeriAlma", "Geri alınma zamanı.", x => x.ReversedAtUtc),
            ]),

        new DataExportTable<ClientCredit>("krediler.csv", "Müşteri kredileri ve iadeleri",
            (db, t) => db.ClientCredits.Where(x => x.TenantId == t),
            [
                DataExportColumn<ClientCredit>.Id("Id", "Kredi kimliği.", x => x.Id),
                DataExportColumn<ClientCredit>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<ClientCredit>.Id("MusteriId", ClientRef, x => x.ClientId),
                DataExportColumn<ClientCredit>.Id("KaynakOdemeId", "Krediyi doğuran ödeme (odemeler.csv Id).", x => x.SourcePaymentId),
                DataExportColumn<ClientCredit>.Code("Kaynak", "Kredi kaynağı.", x => x.Source),
                DataExportColumn<ClientCredit>.Number("Tutar", "Kredi tutarı.", x => x.Amount),
                DataExportColumn<ClientCredit>.Number("KullanilanTutar", "Ücretlere uygulanan tutar.", x => x.UsedAmount),
                DataExportColumn<ClientCredit>.Text("ParaBirimi", Currency, x => x.Currency),
                DataExportColumn<ClientCredit>.Instant("Olusturma", "Kayıt zamanı.", x => x.CreatedAtUtc),
                DataExportColumn<ClientCredit>.Instant("Iade", "Müşteriye iade zamanı.", x => x.RefundedAtUtc),
                DataExportColumn<ClientCredit>.Number("IadeTutari", "İade edilen tutar.", x => x.RefundedAmount),
                DataExportColumn<ClientCredit>.Code("IadeYontemi", "İade yöntemi.", x => x.RefundMethod),
                DataExportColumn<ClientCredit>.Text("IadeNedeni", "İade gerekçesi.", x => x.RefundReason),
                DataExportColumn<ClientCredit>.Id("IadeEdenId", UserRef, x => x.RefundedByUserId),
                DataExportColumn<ClientCredit>.Instant("Iptal", "İptal zamanı.", x => x.VoidedAtUtc),
            ]),

        new DataExportTable<ClientCreditApplication>("kredi_kullanimlari.csv", "Kredinin ücretlere uygulanması",
            (db, t) => db.ClientCreditApplications.Where(x => x.TenantId == t),
            [
                DataExportColumn<ClientCreditApplication>.Id("Id", "Uygulama kimliği.", x => x.Id),
                DataExportColumn<ClientCreditApplication>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<ClientCreditApplication>.Id("KrediId", "Kredi (krediler.csv Id).", x => x.CreditId),
                DataExportColumn<ClientCreditApplication>.Id("UcretId", "Ücret (ucretler.csv Id).", x => x.ChargeId),
                DataExportColumn<ClientCreditApplication>.Number("Tutar", "Uygulanan tutar.", x => x.Amount),
                DataExportColumn<ClientCreditApplication>.Instant("Olusturma", "Kayıt zamanı.", x => x.CreatedAtUtc),
                DataExportColumn<ClientCreditApplication>.Instant("GeriAlma", "Geri alınma zamanı.", x => x.ReversedAtUtc),
                DataExportColumn<ClientCreditApplication>.Text("GeriAlmaNedeni", "Geri alma gerekçesi.", x => x.ReverseReason),
            ]),

        new DataExportTable<Product>("urunler.csv", "Ürünler ve hizmet kalemleri",
            (db, t) => db.Products.Where(x => x.TenantId == t).Include(x => x.Category),
            [
                DataExportColumn<Product>.Id("Id", "Ürün kimliği.", x => x.Id),
                DataExportColumn<Product>.Text("Ad", "Ürün adı.", x => x.Name),
                DataExportColumn<Product>.Text("Kategori", "Ürün kategorisi.", x => x.Category?.Name),
                DataExportColumn<Product>.Code("Tur", "Ürün türü.", x => x.Kind),
                DataExportColumn<Product>.Text("StokKodu", "Stok kodu (SKU).", x => x.Sku),
                DataExportColumn<Product>.Text("Barkod", "Barkod.", x => x.Barcode),
                DataExportColumn<Product>.Text("Birim", "Birim.", x => x.Unit),
                DataExportColumn<Product>.Number("BirimFiyat", "Satış birim fiyatı.", x => x.UnitPrice),
                DataExportColumn<Product>.Text("ParaBirimi", Currency, x => x.Currency),
                DataExportColumn<Product>.Code("KontrolluMaddeSinifi", "Kontrollü madde sınıfı.", x => x.ControlledClass),
                DataExportColumn<Product>.Text("FarmasotikForm", "Farmasötik form.", x => x.PharmacologicalForm),
                DataExportColumn<Product>.Text("Sunum", "Sunum şekli.", x => x.PresentationForm),
                DataExportColumn<Product>.Text("Ambalaj", "Ambalaj.", x => x.Packaging),
                DataExportColumn<Product>.Text("Uretici", "Üretici.", x => x.Manufacturer),
                DataExportColumn<Product>.Text("Aciklama", "Açıklama.", x => x.Description),
                DataExportColumn<Product>.Boolean("Etkin", "Etkin mi.", x => x.IsActive),
            ]),

        new DataExportTable<ProductStock>("stok_durumu.csv", "Klinik bazında stok miktarları",
            (db, t) => db.ProductStocks.Where(x => x.TenantId == t),
            [
                DataExportColumn<ProductStock>.Id("Id", "Stok satırı kimliği.", x => x.Id),
                DataExportColumn<ProductStock>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<ProductStock>.Id("UrunId", ProductRef, x => x.ProductId),
                DataExportColumn<ProductStock>.Number("Miktar", "Eldeki miktar.", x => x.QuantityOnHand),
                DataExportColumn<ProductStock>.Number("AsgariStok", "Asgari stok seviyesi.", x => x.MinimumStockLevel),
                DataExportColumn<ProductStock>.Instant("Guncelleme", "Son güncelleme zamanı.", x => x.UpdatedAtUtc),
            ]),

        new DataExportTable<StockBatch>("stok_partileri.csv", "Stok partileri (seri, son kullanma, tedarik)",
            (db, t) => db.StockBatches.Where(x => x.TenantId == t),
            [
                DataExportColumn<StockBatch>.Id("Id", "Parti kimliği.", x => x.Id),
                DataExportColumn<StockBatch>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<StockBatch>.Id("UrunId", ProductRef, x => x.ProductId),
                DataExportColumn<StockBatch>.Text("SeriNo", "Seri / parti numarası.", x => x.LotNumber),
                DataExportColumn<StockBatch>.Date("SonKullanma", "Son kullanma tarihi.", x => x.ExpiryDate),
                DataExportColumn<StockBatch>.Number("IlkMiktar", "Giriş miktarı.", x => x.InitialQuantity),
                DataExportColumn<StockBatch>.Number("KalanMiktar", "Kalan miktar.", x => x.RemainingQuantity),
                DataExportColumn<StockBatch>.Text("Tedarikci", "Tedarikçi.", x => x.SupplierName),
                DataExportColumn<StockBatch>.Text("FaturaNo", "Fatura numarası.", x => x.InvoiceNumber),
                DataExportColumn<StockBatch>.Date("FaturaTarihi", "Fatura tarihi.", x => x.InvoiceDate),
                DataExportColumn<StockBatch>.Date("TeslimTarihi", "Teslim alma tarihi.", x => x.ReceivedDate),
                DataExportColumn<StockBatch>.Date("SevkTarihi", "Sevk (gönderim) tarihi.", x => x.DispatchedDate),
                DataExportColumn<StockBatch>.Text("TasimaSekli", "Taşıma şekli.", x => x.TransportMethod),
                DataExportColumn<StockBatch>.Text("SogukZincirIzleme", "Soğuk zincir izleme bilgisi.", x => x.ColdChainMonitor),
                DataExportColumn<StockBatch>.Instant("Olusturma", "Kayıt zamanı.", x => x.CreatedAtUtc),
            ]),

        new DataExportTable<StockMovement>("stok_hareketleri.csv", "Stok hareketleri",
            (db, t) => db.StockMovements.Where(x => x.TenantId == t),
            [
                DataExportColumn<StockMovement>.Id("Id", "Hareket kimliği.", x => x.Id),
                DataExportColumn<StockMovement>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<StockMovement>.Id("UrunId", ProductRef, x => x.ProductId),
                DataExportColumn<StockMovement>.Code("HareketTuru", "Hareket türü.", x => x.MovementType),
                DataExportColumn<StockMovement>.Number("Miktar", "Miktar.", x => x.Quantity),
                DataExportColumn<StockMovement>.Number("BirimMaliyet", "Birim maliyet.", x => x.UnitCost),
                DataExportColumn<StockMovement>.Text("Neden", "Neden.", x => x.Reason),
                DataExportColumn<StockMovement>.Text("KaynakTuru", "Hareketi doğuran kayıt türü.", x => x.ReferenceType),
                DataExportColumn<StockMovement>.Id("KaynakId", "Hareketi doğuran kaydın kimliği.", x => x.ReferenceId),
                DataExportColumn<StockMovement>.Id("TersHareketId", "Geri aldığı hareket (stok_hareketleri.csv Id).", x => x.ReversesMovementId),
                DataExportColumn<StockMovement>.Id("StokPartisiId", "Stok partisi (stok_partileri.csv Id).", x => x.BatchId),
                DataExportColumn<StockMovement>.Instant("Zaman", "Hareket zamanı.", x => x.OccurredAtUtc),
                DataExportColumn<StockMovement>.Id("KaydedenId", UserRef, x => x.CreatedByUserId),
                DataExportColumn<StockMovement>.Text("Not", "Not.", x => x.Notes),
            ]),

        new DataExportTable<Sale>("satislar.csv", "Satışlar",
            (db, t) => db.Sales.Where(x => x.TenantId == t),
            [
                DataExportColumn<Sale>.Id("Id", "Satış kimliği.", x => x.Id),
                DataExportColumn<Sale>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<Sale>.Id("MusteriId", ClientRef, x => x.ClientId),
                DataExportColumn<Sale>.Instant("Satis", "Satış zamanı.", x => x.SoldAtUtc),
                DataExportColumn<Sale>.Number("ToplamTutar", "Toplam tutar.", x => x.TotalAmount),
                DataExportColumn<Sale>.Id("UcretId", "Oluşan ücret (ucretler.csv Id).", x => x.ChargeId),
                DataExportColumn<Sale>.Id("OdemeId", "Alınan ödeme (odemeler.csv Id).", x => x.PaymentId),
                DataExportColumn<Sale>.Text("Not", "Not.", x => x.Notes),
                DataExportColumn<Sale>.Instant("Iptal", "İptal zamanı.", x => x.VoidedAtUtc),
                DataExportColumn<Sale>.Text("IptalNedeni", "İptal gerekçesi.", x => x.VoidReason),
                DataExportColumn<Sale>.Id("KaydedenId", UserRef, x => x.CreatedByUserId),
            ]),

        new DataExportTable<SaleLine>("satis_satirlari.csv", "Satış satırları",
            (db, t) => db.Sales.Where(x => x.TenantId == t).SelectMany(x => x.Lines),
            [
                DataExportColumn<SaleLine>.Id("Id", "Satır kimliği.", x => x.Id),
                DataExportColumn<SaleLine>.Id("SatisId", "Satış (satislar.csv Id).", x => x.SaleId),
                DataExportColumn<SaleLine>.Id("UrunId", ProductRef, x => x.ProductId),
                DataExportColumn<SaleLine>.Text("UrunAdi", "Satış anındaki ürün adı.", x => x.ProductName),
                DataExportColumn<SaleLine>.Text("Birim", "Birim.", x => x.Unit),
                DataExportColumn<SaleLine>.Number("Miktar", "Miktar.", x => x.Quantity),
                DataExportColumn<SaleLine>.Number("BirimFiyat", "Birim fiyat.", x => x.UnitPrice),
                DataExportColumn<SaleLine>.Number("Tutar", "Satır tutarı.", x => x.LineAmount),
                DataExportColumn<SaleLine>.Id("StokPartisiId", "Stok partisi (stok_partileri.csv Id).", x => x.BatchId),
            ]),

        new DataExportTable<ReminderDispatchLog>("hatirlatma_gunlugu.csv", "Hatırlatma gönderim günlüğü (ileti içeriği ve alıcı adresi olmadan)",
            (db, t) => db.ReminderDispatchLogs.Where(x => x.TenantId == t),
            [
                DataExportColumn<ReminderDispatchLog>.Id("Id", "Günlük satırı kimliği.", x => x.Id),
                DataExportColumn<ReminderDispatchLog>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<ReminderDispatchLog>.Code("HatirlatmaTuru", "Hatırlatma türü.", x => x.ReminderType),
                DataExportColumn<ReminderDispatchLog>.Code("KaynakTuru", "Hatırlatmayı doğuran kayıt türü.", x => x.SourceEntityType),
                DataExportColumn<ReminderDispatchLog>.Id("KaynakId", "Hatırlatmayı doğuran kaydın kimliği (ör. randevular.csv ya da asilar.csv Id).", x => x.SourceEntityId),
                DataExportColumn<ReminderDispatchLog>.Instant("Planlanan", "Gönderimin planlandığı zaman.", x => x.ScheduledForUtc),
                DataExportColumn<ReminderDispatchLog>.Instant("HatirlatilanZaman", "Hatırlatılan randevu ya da aşı zamanı.", x => x.ReminderDueAtUtc),
                DataExportColumn<ReminderDispatchLog>.Code("Durum", "Gönderim durumu.", x => x.Status),
                DataExportColumn<ReminderDispatchLog>.Instant("Gonderim", "Gönderilme zamanı.", x => x.SentAtUtc),
                DataExportColumn<ReminderDispatchLog>.Instant("Hata", "Başarısız olma zamanı.", x => x.FailedAtUtc),
            ]),

        new DataExportTable<OwnerAccountClientLink>("portal_baglari.csv", "Hasta sahibi portalı bağları (yalnız durum; hesap ve giriş bilgisi verilmez)",
            (db, t) => db.OwnerAccountClientLinks.Where(x => x.TenantId == t),
            [
                DataExportColumn<OwnerAccountClientLink>.Id("Id", "Bağ kimliği.", x => x.Id),
                DataExportColumn<OwnerAccountClientLink>.Id("KlinikId", ClinicRef, x => x.ClinicId),
                DataExportColumn<OwnerAccountClientLink>.Id("MusteriId", ClientRef, x => x.ClientId),
                DataExportColumn<OwnerAccountClientLink>.Code("Durum", "Bağ durumu.", x => x.Status),
                DataExportColumn<OwnerAccountClientLink>.Instant("Olusturma", "Bağın kurulduğu zaman.", x => x.CreatedAtUtc),
                DataExportColumn<OwnerAccountClientLink>.Instant("Kapanma", "Bağın kapandığı zaman.", x => x.ClosedAtUtc),
                DataExportColumn<OwnerAccountClientLink>.Code("KapanmaNedeni", "Kapanma nedeni.", x => x.CloseReason),
                DataExportColumn<OwnerAccountClientLink>.Instant("AydinlatmaOnayi", "Sahibin aydınlatma metnini onayladığı zaman.", x => x.DisclosureAcknowledgedAtUtc),
                DataExportColumn<OwnerAccountClientLink>.Instant("TicariIletiIzni", "Ticari ileti izninin verildiği zaman.", x => x.MarketingConsentAtUtc),
                DataExportColumn<OwnerAccountClientLink>.Instant("TicariIletiRet", "Ticari ileti izninin geri alındığı zaman.", x => x.MarketingOptOutAtUtc),
            ]),
    ];
}
