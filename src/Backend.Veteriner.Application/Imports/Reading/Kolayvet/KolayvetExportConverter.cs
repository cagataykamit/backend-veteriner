using System.Globalization;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Reading.EVet;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using ImportValueParsers = Backend.Veteriner.Application.Imports.Validation.ImportValueParsers;

namespace Backend.Veteriner.Application.Imports.Reading.Kolayvet;

/// <summary>
/// Kolayvet liste dışa aktarmalarını (Müşteri Listesi, Hasta Listesi, Aşı Takvimi, Raporlar &gt; Hasta Listesi (Detaylı)) kanonik
/// tablolara çevirir (IMPORT_API_CONTRACT.md §15). Kolayvet'te tam yedek yoktur; her ekran ayrı <c>.xlsx</c> verir. Dosya türü
/// başlık satırından tanınır (dosya adından değil); 1. satır birleştirilmiş sayfa başlığıdır, sütun adları altındadır. Yalnız biçim
/// ve Kolayvet'e özgü anlam burada çözülür; doğrulama ve yazma kuralları kanonik tablolar üzerinden aynen işler.
/// </summary>
internal static class KolayvetExportConverter
{
    /// <summary>Saatsiz aşı randevusunun saati (kullanıcı kararı 2026-11-28; notta saatin belirtilmediği yazılır).</summary>
    public static readonly TimeOnly DefaultAppointmentTime = new(10, 0);

    /// <summary>Başlık satırı ilk bu kadar satırda aranır (üstte sayfa başlığı var).</summary>
    private const int HeaderSearchRows = 5;

    /// <summary>Kolayvet'in sayfa boyutları: satır sayısı bunlardan biriyse "Tümünü listele" seçilmemiş olabilir.</summary>
    private static readonly int[] PageSizes = [10, 20, 50, 100];

    private static readonly string[] CustomerHeaders = ["MusteriNo", "AdSoyad", "Telefon", "AcikBakiye", "KimlikNo"];

    private static readonly string[] PetHeaders =
        ["HayvanNo", "MusteriNo", "SahipAdSoyad", "HayvanAdi", "Tur", "Irk", "Cinsiyet", "DogumTarihi", "Mikrocip", "Not"];

    private static readonly string[] VaccinationHeaders =
        ["HayvanNo", "SahipAdSoyad", "HayvanAdi", "AsiAdi", "UygulamaTarihi", "SonrakiAsiTarihi", "Seri", "Not"];

    private static readonly string[] AppointmentHeaders =
        ["HayvanNo", "SahipAdSoyad", "HayvanAdi", "Tarih", "Saat", "RandevuTuru", "Not"];

    /// <summary>Dosya türleri ve tanıyıcı başlıkları (katlanmış ad; hepsi bulunmalı).</summary>
    private static readonly (KolayvetFile Kind, string Display, string[] Required)[] Signatures =
    [
        (KolayvetFile.Vaccinations, KolayvetNotes.VaccineList, ["Hasta NO", "Aşı", "Aşı Tarihi", "Yapılma durumu"]),
        (KolayvetFile.Pets, KolayvetNotes.PetList, ["Müşteri NO", "Hasta NO", "Hasta Adı", "Türü"]),
        (KolayvetFile.Customers, KolayvetNotes.CustomerList, ["Müşteri NO", "Müşteri Adı", "Cep Telefonu"]),
        (KolayvetFile.PatientReport, KolayvetNotes.PatientReport, ["Müşteri Bilgileri", "Hasta Bilgileri", "Ek Bilgiler"]),
    ];

    public static Result<ImportReadResult> Convert(IReadOnlyList<ImportUploadedFile> files, ImportFileLimits limits, DateTime nowUtc)
    {
        var sheets = new Dictionary<KolayvetFile, EVetSheet>();
        foreach (var file in files)
        {
            var read = XlsxTableReader.Open(file.Content, limits, workbook =>
            {
                foreach (var worksheet in workbook.Worksheets)
                {
                    var rows = XlsxTableReader.ReadRows(worksheet).ToList();
                    if (Identify(rows) is not { } found)
                        continue;

                    if (sheets.ContainsKey(found.Kind))
                        return Fail($"{found.Display} dosyası birden fazla yüklendi ({file.FileName}).");

                    // Sayfa başlığı satırı atlanır: tablo, sütun adlarının satırından başlar.
                    var assembled = ImportTableAssembler.Assemble(found.Table, rows.Skip(found.HeaderIndex), limits);
                    if (!assembled.IsSuccess)
                        return Result<ImportReadResult>.Failure(assembled.Error);

                    sheets[found.Kind] = new EVetSheet(assembled.Value ?? new ImportSourceTable(found.Table, [], []));
                    return Result<ImportReadResult>.Success(new ImportReadResult([], []));
                }

                return Fail($"{file.FileName} Kolayvet dışa aktarması olarak tanınmadı. Müşteri Listesi, Hasta Listesi, Aşı Takvimi "
                            + "ya da Raporlar > Hasta Listesi (Detaylı) dosyası bekleniyor.");
            });
            if (!read.IsSuccess)
                return read;
        }

        if (sheets.ContainsKey(KolayvetFile.PatientReport) && !sheets.ContainsKey(KolayvetFile.Pets))
            return Fail("Hasta Listesi (Detaylı) raporu hasta numarası taşımaz; Hasta Listesi dosyasıyla birlikte yüklenir.");

        var nowLocal = OperationDayBounds.ToLocal(nowUtc);
        var output = new List<ImportSourceTable>();
        var warnings = new List<ImportReadWarning>();

        var customers = sheets.GetValueOrDefault(KolayvetFile.Customers);
        var pets = sheets.GetValueOrDefault(KolayvetFile.Pets);
        var vaccines = sheets.GetValueOrDefault(KolayvetFile.Vaccinations);

        if (customers is not null)
            output.Add(ConvertCustomers(customers));
        if (pets is not null)
            output.Add(ConvertPets(pets, customers, sheets.GetValueOrDefault(KolayvetFile.PatientReport), warnings));
        if (vaccines is not null)
        {
            var (vaccinations, appointments) = ConvertVaccines(vaccines, nowLocal, warnings);
            output.Add(vaccinations);
            output.Add(appointments);
        }

        WarnTruncation(customers, pets, vaccines, warnings);

        output.RemoveAll(t => t.Rows.Count == 0);
        foreach (var table in output)
        {
            if (table.Rows.Count > limits.MaxRowsPerTable)
                return Result<ImportReadResult>.Failure(ImportTableAssembler.TooManyRows(table.Table, limits).Error);
        }

        if (output.Sum(t => t.Rows.Count) > limits.MaxTotalRows)
            return Fail($"Parti toplamı en fazla {limits.MaxTotalRows} veri satırı olabilir.", "Imports.TooManyRows");

        // Tablo sırası doğrulama sırasıdır (ImportTables.All).
        output.Sort((a, b) => a.Table.CompareTo(b.Table));
        return Result<ImportReadResult>.Success(new ImportReadResult(output, warnings));
    }

    // ---------------------------------------------------------------- Müşteri

    private static ImportSourceTable ConvertCustomers(EVetSheet sheet)
    {
        var rows = sheet.Rows.Select(row => new ImportSourceRow(row.RowNumber,
        [
            sheet[row, "Müşteri NO"],
            sheet[row, "Müşteri Adı"],
            KolayvetCells.Phone(sheet[row, "Cep Telefonu"]),
            EVetCells.Amount(sheet[row, "Kalan bakiye"]),
            EVetCells.Digits(sheet[row, "TC Kimlik No"]),
        ])).ToList();
        return new ImportSourceTable(ImportTable.Customers, CustomerHeaders, rows);
    }

    // ---------------------------------------------------------------- Hasta

    /// <summary>
    /// Hasta Listesi; detaylı rapor varsa doğum tarihi, mikroçip ve kayıt tarihi ondan eklenir. Rapor hasta numarası taşımaz: satır,
    /// sahip adı + telefonla müşteri numarasına (müşteri dosyası yoksa hasta listesindeki sahip adına), sonra o müşterideki hasta adı +
    /// türle tek hastaya bağlanır; bağlanamayan ya da birden çok hastaya uyan satır eklenmez.
    /// </summary>
    private static ImportSourceTable ConvertPets(EVetSheet sheet, EVetSheet? customers, EVetSheet? report, List<ImportReadWarning> warnings)
    {
        var details = report is null ? null : MatchPatientReport(report, sheet, customers, warnings);
        if (report is null)
            warnings.Add(KolayvetNotes.PatientReportMissing(sheet.Rows.Count));

        var rows = new List<ImportSourceRow>();
        foreach (var row in sheet.Rows)
        {
            var petNo = sheet[row, "Hasta NO"];
            var detail = petNo is null ? default : details?.GetValueOrDefault(petNo) ?? default;
            var notes = EVetCells.Join("\n",
                detail.Registered is { } registered ? $"Eski programa kayıt: {KolayvetCells.NoteDate(registered)}" : null,
                detail.Tarbil is { } tarbil ? $"Tarbil (eski program): {tarbil}" : null);

            rows.Add(new ImportSourceRow(row.RowNumber,
            [
                petNo,
                sheet[row, "Müşteri NO"],
                sheet[row, "Müşteri Adı"],
                sheet[row, "Hasta Adı"],
                sheet[row, "Türü"],
                sheet[row, "Irkı"],
                sheet[row, "Cinsiyet"],
                KolayvetCells.IsoDate(detail.BirthDate),
                // Hasta listesinin mikroçip sütunu gerçek dosyada boş geldi (2026-11-28); dolu gelirse o kullanılır.
                sheet[row, "Mikroçip"] ?? detail.Chip,
                notes,
            ]));
        }

        return new ImportSourceTable(ImportTable.Pets, PetHeaders, rows);
    }

    private static Dictionary<string, PetDetail> MatchPatientReport(
        EVetSheet report, EVetSheet pets, EVetSheet? customers, List<ImportReadWarning> warnings)
    {
        // Sahip: ad + telefon → müşteri no (müşteri dosyasından); dosya yoksa hasta listesindeki sahip adından.
        var ownersByName = new Dictionary<string, List<(string No, string? Phone)>>(StringComparer.Ordinal);
        foreach (var row in customers?.Rows ?? (IEnumerable<ImportSourceRow>)[])
        {
            if (customers![row, "Müşteri NO"] is not { } no)
                continue;
            Append(ownersByName, EVetCells.OwnerKey(customers[row, "Müşteri Adı"]), (no, PhoneKey(customers[row, "Cep Telefonu"])));
        }

        if (customers is null)
        {
            foreach (var row in pets.Rows)
            {
                if (pets[row, "Müşteri NO"] is { } no)
                    Append(ownersByName, EVetCells.OwnerKey(pets[row, "Müşteri Adı"]), (no, null));
            }

            foreach (var key in ownersByName.Keys.ToList())
                ownersByName[key] = ownersByName[key].DistinctBy(o => o.No).ToList();
        }

        var petsByOwner = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var row in pets.Rows)
        {
            if (pets[row, "Hasta NO"] is { } petNo && pets[row, "Müşteri NO"] is { } ownerNo)
                Append(petsByOwner, PetKey(ownerNo, pets[row, "Hasta Adı"], pets[row, "Türü"]), petNo);
        }

        var details = new Dictionary<string, PetDetail>(StringComparer.Ordinal);
        var unmatched = 0;
        foreach (var row in report.Rows)
        {
            var (ownerName, phone) = KolayvetCells.CustomerInfo(report[row, "Müşteri Bilgileri"]);
            var (petName, species) = KolayvetCells.PetInfo(report[row, "Hasta Bilgileri"]);
            var candidates = ownersByName.GetValueOrDefault(EVetCells.OwnerKey(ownerName)) ?? [];
            var phoneKey = PhoneKey(phone);
            if (candidates.Count > 1 && phoneKey is not null)
                candidates = candidates.Where(o => o.Phone == phoneKey).ToList();

            var petNos = candidates is [var owner] ? petsByOwner.GetValueOrDefault(PetKey(owner.No, petName, species)) : null;
            if (petNos is not [var petNo] || details.ContainsKey(petNo))
            {
                unmatched++;
                continue;
            }

            var (birthDate, chip, tarbil, registered) = KolayvetCells.PetDetails(report[row, "Ek Bilgiler"]);
            details[petNo] = new PetDetail(birthDate, chip, tarbil, registered);
        }

        warnings.Add(KolayvetNotes.PatientReportMatched(details.Count, unmatched));
        return details;
    }

    private static string PetKey(string ownerNo, string? petName, string? species)
        => $"{ownerNo}\u001f{Pet.NormalizeNameForDuplicateCheck(ImportValueParsers.NormalizeText(petName) ?? string.Empty)}\u001f{ImportTextFolding.Fold(species)}";

    private static string? PhoneKey(string? value) => ImportValueParsers.ParsePhone(KolayvetCells.Phone(value)).Normalized;

    private static void Append<T>(Dictionary<string, List<T>> index, string key, T value)
    {
        if (!index.TryGetValue(key, out var list))
            index[key] = list = [];
        list.Add(value);
    }

    // ---------------------------------------------------------------- Aşı

    /// <summary>
    /// Aşı Takvimi: "Yapıldı" → aşı geçmişi (tarih Yapılma Tarihi, yoksa Aşı Tarihi). "Yapılmadı" → aynı serinin son uygulamasının sonraki
    /// aşı tarihi; seride uygulama yoksa ve gelecekteyse saat 10:00'da aşı randevusu (<see cref="ImportVaccineSchedule"/>).
    /// </summary>
    private static (ImportSourceTable Vaccinations, ImportSourceTable Appointments) ConvertVaccines(
        EVetSheet sheet, DateTime nowLocal, List<ImportReadWarning> warnings)
    {
        var applied = new List<AppliedVaccine>();
        var planned = new List<ImportPlannedVaccine<ImportSourceRow>>();
        var skipped = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in sheet.Rows)
        {
            var vaccine = sheet[row, "Aşı"];
            var petKey = sheet[row, "Hasta NO"] ?? EVetCells.PairKey(sheet[row, "Müşteri Adı"], sheet[row, "Hasta Adı"]);
            var status = ImportTextFolding.Fold(sheet[row, "Yapılma durumu"]);
            var plannedOn = KolayvetCells.ParseDate(sheet[row, "Aşı Tarihi"]);
            if (vaccine is null) { Skip("aşı adı boş"); continue; }

            if (status == "yapildi")
            {
                var appliedOn = KolayvetCells.ParseDate(sheet[row, "Yapılma Tarihi"]) ?? plannedOn;
                if (appliedOn is null) { Skip("tarihi okunamayan"); continue; }

                applied.Add(new AppliedVaccine(sheet, row, petKey, vaccine, appliedOn.Value.ToDateTime(TimeOnly.MinValue)));
                continue;
            }

            if (status != "yapilmadi") { Skip("durumu tanınmayan"); continue; }
            if (plannedOn is null) { Skip("tarihi okunamayan"); continue; }

            planned.Add(new(row, petKey, vaccine, plannedOn.Value.ToDateTime(DefaultAppointmentTime)));
        }

        var schedule = ImportVaccineSchedule.Resolve(applied, planned, nowLocal);
        Skip("sonraki aşı tarihine dönüşen ya da yinelenen yapılacak aşı", schedule.Skipped.GetValueOrDefault(ImportPlannedVaccineSkip.DueDateOrRepeat));
        Skip("hiç uygulanmamış ve tarihi geçmiş aşı", schedule.Skipped.GetValueOrDefault(ImportPlannedVaccineSkip.NeverAppliedPast));
        Skip("aynı aşı için birden çok yapılacak kayıt", schedule.Skipped.GetValueOrDefault(ImportPlannedVaccineSkip.ExtraPlanned));

        var appointments = schedule.Appointments.Select(p => new ImportSourceRow(p.Source.RowNumber,
        [
            sheet[p.Source, "Hasta NO"],
            sheet[p.Source, "Müşteri Adı"],
            sheet[p.Source, "Hasta Adı"],
            p.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            p.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
            "Aşı",
            $"Aşı: {p.Vaccine}; Kolayvet aşı takviminden, saat belirtilmemiş",
        ])).ToList();

        warnings.Add(KolayvetNotes.VaccineConversion(applied.Count, appointments.Count, schedule.DueDates));
        if (skipped.Count > 0)
            warnings.Add(KolayvetNotes.VaccinesSkipped(skipped.OrderByDescending(s => s.Value).Select(s => (s.Key, s.Value)).ToList()));

        return (
            new ImportSourceTable(ImportTable.Vaccinations, VaccinationHeaders, applied.Select(a => a.ToRow()).ToList()),
            new ImportSourceTable(ImportTable.Appointments, AppointmentHeaders, appointments));

        void Skip(string reason, int count = 1)
        {
            if (count > 0)
                skipped[reason] = skipped.GetValueOrDefault(reason) + count;
        }
    }

    // ---------------------------------------------------------------- Eksik dosya uyarısı

    /// <summary>
    /// "Tümünü listele" seçilmeden indirilen dosya yalnız ilk sayfayı taşır: satır sayısı Kolayvet'in sayfa boyutlarından biriyse ya da
    /// bir dosyanın bağlandığı kayıt diğer dosyada yoksa uyarılır.
    /// </summary>
    private static void WarnTruncation(EVetSheet? customers, EVetSheet? pets, EVetSheet? vaccines, List<ImportReadWarning> warnings)
    {
        foreach (var (sheet, table, source) in new[]
                 {
                     (customers, ImportTable.Customers, KolayvetNotes.CustomerList),
                     (pets, ImportTable.Pets, KolayvetNotes.PetList),
                     (vaccines, ImportTable.Vaccinations, KolayvetNotes.VaccineList),
                 })
        {
            if (sheet is not null && PageSizes.Contains(sheet.Rows.Count))
                warnings.Add(KolayvetNotes.PossiblyTruncated(table, source, sheet.Rows.Count));
        }

        if (customers is not null && pets is not null)
        {
            var known = customers.Rows.Select(r => customers[r, "Müşteri NO"]).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var missing = pets.Rows.Count(r => pets[r, "Müşteri NO"] is { } no && !known.Contains(no));
            if (missing > 0)
                warnings.Add(KolayvetNotes.MissingLinks(ImportTable.Pets, KolayvetNotes.PetList, KolayvetNotes.CustomerList, missing));
        }

        if (pets is not null && vaccines is not null)
        {
            var known = pets.Rows.Select(r => pets[r, "Hasta NO"]).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var missing = vaccines.Rows.Count(r => vaccines[r, "Hasta NO"] is { } no && !known.Contains(no));
            if (missing > 0)
                warnings.Add(KolayvetNotes.MissingLinks(ImportTable.Vaccinations, KolayvetNotes.VaccineList, KolayvetNotes.PetList, missing));
        }
    }

    // ---------------------------------------------------------------- Ortak

    /// <summary>Başlık satırını ilk <see cref="HeaderSearchRows"/> satırda imzadan bulur.</summary>
    private static (KolayvetFile Kind, string Display, ImportTable Table, int HeaderIndex)? Identify(IReadOnlyList<ImportRawRow> rows)
    {
        for (var i = 0; i < Math.Min(rows.Count, HeaderSearchRows); i++)
        {
            var cells = rows[i].Cells.Select(ImportTextFolding.Fold).ToHashSet(StringComparer.Ordinal);
            foreach (var (kind, display, required) in Signatures)
            {
                if (required.All(h => cells.Contains(ImportTextFolding.Fold(h))))
                    return (kind, display, TableOf(kind), i);
            }
        }

        return null;
    }

    private static ImportTable TableOf(KolayvetFile kind) => kind switch
    {
        KolayvetFile.Customers => ImportTable.Customers,
        KolayvetFile.Vaccinations => ImportTable.Vaccinations,
        _ => ImportTable.Pets,
    };

    private static Result<ImportReadResult> Fail(string message, string code = "Imports.UnsupportedFile")
        => Result<ImportReadResult>.Failure(code, message);

    private enum KolayvetFile
    {
        Customers,
        Pets,
        Vaccinations,
        PatientReport,
    }

    private readonly record struct PetDetail(DateOnly? BirthDate, string? Chip, string? Tarbil, DateOnly? Registered);

    private sealed class AppliedVaccine(EVetSheet sheet, ImportSourceRow row, string petKey, string vaccine, DateTime start)
        : IImportAppliedVaccine
    {
        public string PetKey { get; } = petKey;
        public string Vaccine { get; } = vaccine;
        public DateTime Start { get; } = start;
        public DateTime? DueAt { get; set; }

        public ImportSourceRow ToRow()
            => new(row.RowNumber,
            [
                sheet[row, "Hasta NO"],
                sheet[row, "Müşteri Adı"],
                sheet[row, "Hasta Adı"],
                Vaccine,
                Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DueAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                sheet[row, "Seri No"],
                KolayvetCells.ParseDate(sheet[row, "Yapılma Tarihi"]) is null ? "Kolayvet'te yapılma tarihi yok; planlanan tarih yazıldı" : null,
            ]);
    }
}
