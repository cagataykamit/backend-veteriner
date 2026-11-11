using System.Globalization;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using ClosedXML.Excel;

namespace Backend.Veteriner.Application.Imports.Reading.EVet;

/// <summary>
/// E-vet "Genel &gt; Yedek Al" çalışma kitabını (Müşteri / Hasta / Randevu / Muayene Listesi sayfaları, düz tablolar) kanonik
/// tablolara çevirir (IMPORT_API_CONTRACT.md §13). Yalnız biçim ve E-vet'e özgü anlam burada çözülür (sayfa ve sütun adları,
/// sahip bağlantısı, cinsiyet hücresindeki kısırlık, görev türleri, sonraki aşı tarihi); doğrulama ve yazma kuralları kanonik
/// tablolar üzerinden aynen işler. Çıkan satır numarası kaynak sayfanın Excel satırıdır.
/// </summary>
internal static class EVetBackupConverter
{
    /// <summary>Dönüştürme bilgisi (okuma uyarısı; parti oluşturma yanıtındaki <c>warnings</c>).</summary>
    public const string WarningCode = "Imports.EVetConversion";

    private const string CustomerSheet = "musterilistesi";
    private const string PetSheet = "hastalistesi";
    private const string TaskSheet = "randevulistesi";
    private const string ExaminationSheet = "muayenelistesi";

    /// <summary>Hasta işi olmayan görev türleri (katlanmış): klinik içi ve finans hatırlatmaları.</summary>
    private static readonly HashSet<string> NonPatientTaskTypes = new(StringComparer.Ordinal)
    {
        "maas", "borchatirlatma", "odemesozu", "bilgi", "hatirlatma", "randevutalep",
    };

    private static readonly string[] CustomerHeaders =
        ["MusteriNo", "AdSoyad", "Telefon", "Eposta", "Adres", "AcikBakiye", "KimlikNo", "VergiDairesi", "Durum"];

    private static readonly string[] PetHeaders =
    [
        "HayvanNo", "MusteriNo", "SahipAdSoyad", "HayvanAdi", "Tur", "Irk", "Cinsiyet", "Kisir", "DogumTarihi", "Mikrocip",
        "KarneNo", "Renk", "Agirlik", "Alerji", "Durum", "VefatTarihi", "Not",
    ];

    private static readonly string[] ClinicalHeaders =
    [
        "HayvanNo", "SahipAdSoyad", "HayvanAdi", "Tarih", "KayitTuru", "Hekim", "Kilo", "Ates", "Sikayet", "Anamnez", "Bulgular",
        "Tani", "Tedavi", "Not",
    ];

    private static readonly string[] VaccinationHeaders =
        ["HayvanNo", "SahipAdSoyad", "HayvanAdi", "AsiAdi", "UygulamaTarihi", "SonrakiAsiTarihi", "Seri", "Uygulayan", "Not"];

    private static readonly string[] AppointmentHeaders =
        ["HayvanNo", "SahipAdSoyad", "HayvanAdi", "Tarih", "Saat", "Hekim", "Neden", "RandevuTuru", "Not"];

    /// <summary>Çalışma kitabında E-vet yedeğinin sayfa adlarından biri var mı.</summary>
    public static bool IsBackup(XLWorkbook workbook)
        => workbook.Worksheets.Any(s => SheetKey(s.Name) is CustomerSheet or PetSheet or TaskSheet or ExaminationSheet);

    public static Result<ImportReadResult> Convert(XLWorkbook workbook, EVetReports reports, ImportFileLimits limits, DateTime nowUtc)
    {
        var sheets = new Dictionary<string, EVetSheet>(StringComparer.Ordinal);
        foreach (var worksheet in workbook.Worksheets)
        {
            var key = SheetKey(worksheet.Name);
            var table = key switch
            {
                CustomerSheet => ImportTable.Customers,
                PetSheet => ImportTable.Pets,
                TaskSheet => ImportTable.Appointments,
                ExaminationSheet => ImportTable.ClinicalRecords,
                _ => (ImportTable?)null,
            };
            if (table is null || sheets.ContainsKey(key))
                continue;

            var assembled = ImportTableAssembler.Assemble(table.Value, XlsxTableReader.ReadRows(worksheet), limits);
            if (!assembled.IsSuccess)
                return Result<ImportReadResult>.Failure(assembled.Error);
            if (assembled.Value is { } source)
                sheets[key] = new EVetSheet(source);
        }

        var state = new ConversionState(reports, OperationDayBounds.ToLocal(nowUtc));
        var output = new List<ImportSourceTable>();
        var warnings = new List<ImportReadWarning>();

        if (reports.OwnerByPetProtocol is null && sheets.ContainsKey(PetSheet))
        {
            warnings.Add(new ImportReadWarning(WarningCode, ImportTable.Pets,
                "Hasta Listesi raporu yüklenmedi: hastalar sahibine yedekteki sahip adıyla bağlanacak; aynı adlı müşterilerin hastaları hata alır."));
        }

        if (sheets.TryGetValue(CustomerSheet, out var customers))
            Add(output, ConvertCustomers(customers, state));
        if (sheets.TryGetValue(PetSheet, out var pets))
            Add(output, ConvertPets(pets, state, warnings));
        if (sheets.TryGetValue(TaskSheet, out var tasks))
        {
            var (vaccinations, appointments) = ConvertTasks(tasks, state, warnings);
            Add(output, vaccinations);
            Add(output, appointments);
        }

        if (sheets.TryGetValue(ExaminationSheet, out var examinations))
            Add(output, ConvertExaminations(examinations, state));

        foreach (var table in output)
        {
            if (table.Rows.Count > limits.MaxRowsPerTable)
                return Result<ImportReadResult>.Failure(ImportTableAssembler.TooManyRows(table.Table, limits).Error);
        }

        if (output.Sum(t => t.Rows.Count) > limits.MaxTotalRows)
            return Result<ImportReadResult>.Failure("Imports.TooManyRows", $"Parti toplamı en fazla {limits.MaxTotalRows} veri satırı olabilir.");

        // Tablo sırası doğrulama sırasıdır (ImportTables.All); okuyucu çıktısı da aynı sırada olsun.
        output.Sort((a, b) => a.Table.CompareTo(b.Table));
        return Result<ImportReadResult>.Success(new ImportReadResult(output, warnings));
    }

    // ---------------------------------------------------------------- Müşteri

    private static ImportSourceTable ConvertCustomers(EVetSheet sheet, ConversionState state)
    {
        var rows = new List<ImportSourceRow>();
        var duplicated = DuplicatedProtocols(sheet);
        state.DuplicatedCustomerProtocols = duplicated.Count;
        foreach (var row in sheet.Rows)
        {
            var protocol = RowKey(sheet, row, duplicated);
            var name = sheet[row, "Müşteriler"];
            if (protocol is not null)
                state.CustomerProtocols.Add(protocol);
            if (name is not null)
                state.CustomersByName.Append(EVetCells.OwnerKey(name), protocol);

            // Vergi numarası (kurumsal) doluysa fatura kimliği odur; yoksa T.C. kimlik.
            var identity = EVetCells.Digits(sheet[row, "Vergi No"]) ?? EVetCells.Digits(sheet[row, "Kimlik No"]);
            var address = EVetCells.Join(", ",
                sheet[row, "Adres"],
                sheet[row, "Köy - Mahalle"],
                EVetCells.Join(" / ", sheet[row, "İlçe"], sheet[row, "Şehir"]));

            rows.Add(new ImportSourceRow(row.RowNumber,
            [
                protocol,
                name,
                sheet[row, "Gsm"] ?? sheet[row, "Tel1"],
                sheet[row, "Email"],
                address,
                EVetCells.Amount(sheet[row, "Bakiye"]),
                identity,
                sheet[row, "Vergi Dairesi"],
                sheet[row, "Durum"],
            ]));
        }

        return new ImportSourceTable(ImportTable.Customers, CustomerHeaders, rows);
    }

    // ---------------------------------------------------------------- Hasta

    private static ImportSourceTable ConvertPets(EVetSheet sheet, ConversionState state, List<ImportReadWarning> warnings)
    {
        var rows = new List<ImportSourceRow>();
        var chipOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        var movedChips = 0;
        var reportLinked = 0;
        var duplicated = DuplicatedProtocols(sheet);

        foreach (var row in sheet.Rows)
        {
            var protocol = RowKey(sheet, row, duplicated);
            var reportProtocol = duplicated.Contains(sheet[row, "Protokol No"] ?? string.Empty) ? null : protocol;
            var ownerName = sheet[row, "Müşteriler"];
            var petName = sheet[row, "Hastalar"];
            var pair = EVetCells.PairKey(ownerName, petName);
            state.PetsByPair.Append(pair, protocol);

            var status = ImportTextFolding.Fold(sheet[row, "Durum"]);
            if (protocol is not null && status is "olu" or "pasif")
                state.InactivePets.Add(protocol);

            // Sahip: rapordaki protokol eşlemesi (kesin), yoksa yedekteki sahip adıyla tek müşteri.
            string? owner = null;
            if (reportProtocol is not null && state.Reports.OwnerByPetProtocol?.GetValueOrDefault(reportProtocol) is { } linked
                && state.CustomerProtocols.Contains(linked))
            {
                owner = linked;
                reportLinked++;
            }
            else if (state.CustomersByName.Single(EVetCells.OwnerKey(ownerName)) is { } byName)
            {
                owner = byName;
            }

            var (gender, neutered) = SplitGender(sheet[row, "Cinsiyet"]);
            var notes = new List<string?>
            {
                sheet[row, "Bilgi"],
                sheet[row, "Açıklama"],
                Labeled("Kalıtsal hastalık", sheet[row, "Kalıtsal Hastalık"]),
                Labeled("Kan grubu", sheet[row, "Kan Grubu"]),
                Labeled("Besin tipi", sheet[row, "Besin Tipi"]),
            };

            // Rakam dışı ya da başka hastada da kayıtlı çip (E-vet'te benzersizlik yok) nota taşınır: hasta düşmesin, çip
            // çakışması (K10) yayılmasın. Aynı sahip + adın tekrarı (aynı hasta) çakışma sayılmaz.
            var chip = EVetCells.Clean(sheet[row, "Çip No"]);
            if (chip is not null)
            {
                var digits = new string(chip.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '.').ToArray());
                var conflict = !digits.All(char.IsAsciiDigit)
                               || (chipOwners.TryGetValue(digits, out var first) && first != pair);
                if (conflict)
                {
                    notes.Add($"Mikroçip (eski program): {chip}");
                    chip = null;
                    movedChips++;
                }
                else
                {
                    chipOwners.TryAdd(digits, pair);
                }
            }

            var deceased = status == "olu";
            if (status == "pasif")
                notes.Add(EVetCells.Join(" ", "Eski programda pasif.", sheet[row, "Pasif Nedeni"]));
            else if (deceased)
                notes.Add(Labeled("Eski program vefat notu", sheet[row, "Pasif Nedeni"]));

            rows.Add(new ImportSourceRow(row.RowNumber,
            [
                protocol,
                owner,
                ownerName,
                petName,
                sheet[row, "Hasta Türü"],
                sheet[row, "Irk"],
                gender,
                neutered ? "Evet" : null,
                EVetCells.Date(sheet[row, "Doğum Tarihi"]),
                chip,
                sheet[row, "Kart No"],
                sheet[row, "Renk"],
                EVetCells.Weight(sheet[row, "Ağırlık"]),
                sheet[row, "Alerjik Durumu"],
                deceased ? "Vefat" : null,
                deceased ? EVetCells.Date(sheet[row, "Ölüm Zamanı"]) : null,
                EVetCells.Join("\n", notes.ToArray()),
            ]));
        }

        if (state.Reports.OwnerByPetProtocol is not null)
        {
            warnings.Add(new ImportReadWarning(WarningCode, ImportTable.Pets,
                $"{reportLinked} hasta sahibine Hasta Listesi raporundaki protokolle, {rows.Count - reportLinked} hasta sahip adıyla bağlanacak."));
        }

        if (movedChips > 0)
        {
            warnings.Add(new ImportReadWarning(WarningCode, ImportTable.Pets,
                $"{movedChips} hastanın mikroçipi rakam dışı karakter içeriyor ya da başka hastada da kayıtlı; numara hastanın notuna yazılacak."));
        }

        if (duplicated.Count > 0 || state.DuplicatedCustomerProtocols > 0)
        {
            warnings.Add(new ImportReadWarning(WarningCode, ImportTable.Pets,
                $"Yedekte {state.DuplicatedCustomerProtocols} müşteri ve {duplicated.Count} hasta protokol numarası birden çok kayıtta geçiyor; "
                + "bu kayıtların numarasına satır numarası eklendi (ör. 1234#56) ve sahipleri adla bağlanacak."));
        }

        return new ImportSourceTable(ImportTable.Pets, PetHeaders, rows);
    }

    /// <summary>Sayfada birden çok satırda geçen protokol numaraları (E-vet benzersizlik denetlemiyor).</summary>
    private static HashSet<string> DuplicatedProtocols(EVetSheet sheet)
        => sheet.Rows
            .Select(r => sheet[r, "Protokol No"])
            .OfType<string>()
            .GroupBy(p => p, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Satırın bağlantı anahtarı: protokol tekse kendisi; tekrar ediyorsa <c>protokol#satır</c> (her tekrar ayrı kayıt kalır,
    /// <c>DuplicateReference</c> ile düşmez; rapordaki protokol eşlemesi bu kayıtlar için kullanılmaz).
    /// </summary>
    private static string? RowKey(EVetSheet sheet, ImportSourceRow row, HashSet<string> duplicated)
        => sheet[row, "Protokol No"] is { } protocol && duplicated.Contains(protocol)
            ? $"{protocol}#{row.RowNumber}"
            : sheet[row, "Protokol No"];

    /// <summary>E-vet cinsiyet hücresi kısırlığı da taşır (<c>Kısırlaştırılmış Dişi</c>, <c>Kısır</c>, <c>Bilinmiyor</c>).</summary>
    private static (string? Gender, bool Neutered) SplitGender(string? value)
    {
        var key = ImportTextFolding.Fold(value);
        var neutered = key.Contains("kisir", StringComparison.Ordinal);
        if (key.Contains("disi", StringComparison.Ordinal))
            return ("Dişi", neutered);
        if (key.Contains("erkek", StringComparison.Ordinal))
            return ("Erkek", neutered);
        return (neutered || key is "" or "bilinmiyor" ? null : value, neutered);
    }

    // ---------------------------------------------------------------- Muayene

    private static ImportSourceTable ConvertExaminations(EVetSheet sheet, ConversionState state)
    {
        var rows = new List<ImportSourceRow>();
        foreach (var row in sheet.Rows)
        {
            var ownerName = sheet[row, "Hasta Sahibi"];
            var petName = sheet[row, "Hasta Adı"];
            rows.Add(new ImportSourceRow(row.RowNumber,
            [
                state.PetsByPair.Single(EVetCells.PairKey(ownerName, petName)),
                ownerName,
                petName,
                EVetCells.Date(sheet[row, "Tarih"]),
                "Muayene",
                sheet[row, "Veteriner Hekim"],
                EVetCells.Weight(sheet[row, "Ağırlık"]),
                EVetCells.Temperature(sheet[row, "Vücut Sıcaklığı"]),
                sheet[row, "Semptom"],
                sheet[row, "Anamnez"],
                Findings(sheet[row, "Diğer Parametreler"]),
                sheet[row, "Hastalık Teşhisi"],
                sheet[row, "Tedavi Şekli"],
                sheet[row, "Açıklama"],
            ]));
        }

        return new ImportSourceTable(ImportTable.ClinicalRecords, ClinicalHeaders, rows);
    }

    /// <summary>
    /// "Diğer Parametreler" muayene bulgusu şablonudur (<c>Genel Durum: NORMAL, Pulzasyon: -, …</c>); yalnız dolu olanlar
    /// <c>; </c> ile birleşir, hepsi <c>-</c> ise boş.
    /// </summary>
    private static string? Findings(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = new List<string>();
        foreach (var piece in value.Split(", "))
        {
            if (parts.Count > 0 && !piece.Contains(':'))
                parts[^1] += ", " + piece; // değerin içindeki virgül
            else
                parts.Add(piece);
        }

        var filled = parts
            .Select(p => p.Trim())
            .Where(p => p.Split(':', 2) is { Length: 2 } kv ? kv[1].Trim() is not ("" or "-") : p.Length > 0)
            .ToList();
        return filled.Count == 0 ? null : string.Join("; ", filled);
    }

    // ---------------------------------------------------------------- Randevu (görev)

    /// <summary>
    /// Görevler: aşı paketi dolu ve tamamlanmış → aşı geçmişi; yapılacak aşı görevi → aynı aşı serisinin son uygulamasının
    /// sonraki aşı tarihi (son uygulama yoksa ve tarih gelecekteyse randevu); diğer gelecek görevler → randevu. Hastasız,
    /// iptal, hasta işi olmayan tür ve geçmişte kalan aşı dışı görev aktarılmaz. Vefat/pasif hastaya gelecek iş yazılmaz.
    /// </summary>
    private static (ImportSourceTable? Vaccinations, ImportSourceTable? Appointments) ConvertTasks(
        EVetSheet sheet, ConversionState state, List<ImportReadWarning> warnings)
    {
        var applied = new List<AppliedVaccine>();
        var pending = new List<(ImportSourceRow Row, string Group, DateTime Start)>();
        var appointments = new List<ImportSourceRow>();
        var skipped = new SkipCounter();

        foreach (var row in sheet.Rows)
        {
            var ownerName = sheet[row, "Müşteriler"];
            var petName = sheet[row, "Hastalar"];
            var type = ImportTextFolding.Fold(sheet[row, "Görev Tipi"]);
            var status = ImportTextFolding.Fold(sheet[row, "Durum"]);
            var vaccine = sheet[row, "Aşı Paketi"];
            var start = EVetCells.ParseDateTime(sheet[row, "Başlangıç Tarihi"]);

            if (petName is null) { skipped.Add("hastası olmayan"); continue; }
            if (status == "iptal") { skipped.Add("iptal edilmiş"); continue; }
            if (NonPatientTaskTypes.Contains(type)) { skipped.Add("hasta işi olmayan türde (maaş, borç/ödeme hatırlatması, bilgi)"); continue; }
            if (start is null) { skipped.Add("tarihi okunamayan"); continue; }

            var petProtocol = state.PetsByPair.Single(EVetCells.PairKey(ownerName, petName));
            var petKey = petProtocol ?? EVetCells.PairKey(ownerName, petName);

            if (vaccine is not null && status == "tamamlandi")
            {
                applied.Add(new AppliedVaccine(row, petKey, petProtocol, ownerName, petName, vaccine, start.Value, sheet));
                continue;
            }

            if (vaccine is not null && status == "yapilacak")
            {
                pending.Add((row, $"{petKey}\u001f{ImportValueResolver.VaccineSeriesKey(vaccine)}", start.Value));
                continue;
            }

            if (status != "yapilacak" || start.Value <= state.NowLocal)
            {
                skipped.Add(vaccine is null && type == "asilama" && status == "tamamlandi"
                    ? "aşı adı boş tamamlanmış aşılama"
                    : "geçmişte kalmış ya da tamamlanmış aşı dışı");
                continue;
            }

            if (petProtocol is not null && state.InactivePets.Contains(petProtocol)) { skipped.Add("vefat ya da pasif hastaya ait gelecek"); continue; }
            appointments.Add(Appointment(sheet, row, petProtocol, ownerName, petName, start.Value, vaccine: null));
        }

        // Yapılacak aşı görevi → serinin son uygulamasının sonraki aşı tarihi; uygulama yoksa gelecek randevu.
        var lastApplied = applied
            .GroupBy(a => $"{a.PetKey}\u001f{ImportValueResolver.VaccineSeriesKey(a.Vaccine)}")
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Start).Last(), StringComparer.Ordinal);
        var dueDates = 0;
        foreach (var group in pending.GroupBy(p => p.Group, StringComparer.Ordinal))
        {
            var first = group.First();
            var protocol = state.PetsByPair.Single(EVetCells.PairKey(sheet[first.Row, "Müşteriler"], sheet[first.Row, "Hastalar"]));
            if (protocol is not null && state.InactivePets.Contains(protocol))
            {
                skipped.Add("vefat ya da pasif hastaya ait yapılacak aşı", group.Count());
                continue;
            }

            if (lastApplied.TryGetValue(group.Key, out var last))
            {
                var due = group.Where(p => p.Start.Date > last.Start.Date).OrderBy(p => p.Start).FirstOrDefault();
                if (due.Row is not null)
                {
                    last.DueAt = due.Start;
                    dueDates++;
                }

                skipped.Add("sonraki aşı tarihine dönüşen ya da yinelenen yapılacak aşı", group.Count() - (due.Row is null ? 0 : 1));
                continue;
            }

            var future = group.Where(p => p.Start > state.NowLocal).OrderBy(p => p.Start).FirstOrDefault();
            if (future.Row is null)
            {
                skipped.Add("hiç uygulanmamış ve tarihi geçmiş aşı", group.Count());
                continue;
            }

            appointments.Add(Appointment(sheet, future.Row, protocol, sheet[future.Row, "Müşteriler"], sheet[future.Row, "Hastalar"],
                future.Start, sheet[future.Row, "Aşı Paketi"]));
            skipped.Add("aynı aşı için birden çok yapılacak görev", group.Count() - 1);
        }

        var (serialMatched, serialUnmatched) = MatchVaccineReport(applied, state.Reports);
        var vaccinationRows = applied.Select(a => a.ToRow()).ToList();
        warnings.Add(new ImportReadWarning(WarningCode, ImportTable.Vaccinations,
            $"Randevu Listesi: {vaccinationRows.Count} tamamlanmış aşı aşı geçmişine, {appointments.Count} gelecek görev randevuya dönüşecek; "
            + $"{dueDates} aşının sonraki aşı tarihi E-vet'teki yapılacak aşı görevinden alındı."));
        if (skipped.Total > 0)
        {
            warnings.Add(new ImportReadWarning(WarningCode, ImportTable.Appointments,
                $"Randevu Listesi: {skipped.Total} görev aktarılmayacak ({skipped.Describe()})."));
        }

        if (state.Reports.Vaccines is not null)
        {
            warnings.Add(new ImportReadWarning(WarningCode, ImportTable.Vaccinations,
                $"Aşı Bilgi raporu: {serialMatched} aşıya ürün adı ve seri numarası eklendi; {serialUnmatched} rapor satırı yedekteki "
                + "tamamlanmış aşılarla eşleşmedi (o gün tamamlanmış aşı yok ya da ürün adından ayırt edilemedi)."));
        }

        return (
            vaccinationRows.Count == 0 ? null : new ImportSourceTable(ImportTable.Vaccinations, VaccinationHeaders, vaccinationRows),
            appointments.Count == 0 ? null : new ImportSourceTable(ImportTable.Appointments, AppointmentHeaders, appointments));
    }

    /// <summary>
    /// Aşı Bilgi satırını aynı sahip + hasta + gündeki tamamlanmış aşıya bağlar: o gün tek aşı ve tek rapor satırı varsa doğrudan;
    /// birden çoksa raporun ürün adı aşı serisini içeriyorsa (<c>Rabisin ( Kuduz )</c> ↔ <c>KUDUZ</c>) ve eşleşme tekse.
    /// Döner: eşleşen aşı sayısı, eşleşmeyen rapor satırı sayısı.
    /// </summary>
    private static (int Matched, int Unmatched) MatchVaccineReport(List<AppliedVaccine> applied, EVetReports reports)
    {
        if (reports.Vaccines is null)
            return (0, 0);

        var matched = 0;
        foreach (var day in applied.GroupBy(a => a.DayKey))
        {
            if (!reports.Vaccines.TryGetValue(day.Key, out var lines))
                continue;

            var vaccines = day.ToList();
            if (vaccines.Count == 1 && lines.Count == 1)
            {
                vaccines[0].Report = lines[0];
                matched++;
                continue;
            }

            var hits = lines
                .Select(line => (Line: line, Product: ImportTextFolding.Fold(line.Product)))
                .Select(x => (x.Line, Vaccines: vaccines
                    .Where(v => ImportValueResolver.VaccineSeriesKey(v.Vaccine) is { Length: >= 4 } series
                                && x.Product.Contains(series, StringComparison.Ordinal))
                    .ToList()))
                .Where(x => x.Vaccines.Count == 1)
                .ToList();
            var claimed = hits.GroupBy(h => h.Vaccines[0]).Where(g => g.Count() == 1).Select(g => g.Single()).ToList();
            foreach (var (line, single) in claimed)
                single[0].Report = line;

            matched += claimed.Count;
        }

        return (matched, reports.Vaccines.Values.Sum(lines => lines.Count) - matched);
    }

    private static ImportSourceRow Appointment(
        EVetSheet sheet, ImportSourceRow row, string? petProtocol, string? ownerName, string? petName, DateTime start, string? vaccine)
    {
        var type = ImportTextFolding.Fold(sheet[row, "Görev Tipi"]);
        return new ImportSourceRow(row.RowNumber,
        [
            petProtocol,
            ownerName,
            petName,
            start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            start.ToString("HH:mm", CultureInfo.InvariantCulture),
            sheet[row, "Veteriner"],
            sheet[row, "Görev Tipi"],
            vaccine is not null ? "Aşı" : type.StartsWith("kontrol", StringComparison.Ordinal) ? "Kontrol" : null,
            EVetCells.Join("; ", vaccine is null ? null : $"Aşı: {vaccine}", sheet[row, "Bilgi"], sheet[row, "Açıklama"]),
        ]);
    }

    // ---------------------------------------------------------------- Ortak

    private static string? Labeled(string label, string? value) => EVetCells.Clean(value) is { } text ? $"{label}: {text}" : null;

    private static string SheetKey(string name) => ImportTextFolding.Fold(name);

    private static void Add(List<ImportSourceTable> output, ImportSourceTable? table)
    {
        if (table is { Rows.Count: > 0 })
            output.Add(table);
    }

    /// <summary>Tamamlanmış aşı görevi; sonraki aşı tarihi yapılacak görevlerden sonra atanır.</summary>
    private sealed class AppliedVaccine(
        ImportSourceRow row, string petKey, string? petProtocol, string? ownerName, string? petName, string vaccine, DateTime start, EVetSheet sheet)
    {
        public string PetKey { get; } = petKey;
        public string Vaccine { get; } = vaccine;
        public DateTime Start { get; } = start;
        public DateTime? DueAt { get; set; }

        /// <summary>Aşı Bilgi raporundaki karşılığı (ürün adı, seri no); eşleşmediyse null.</summary>
        public EVetVaccineReportRow? Report { get; set; }

        public ImportSourceRow ToRow()
            => new(row.RowNumber,
            [
                petProtocol,
                ownerName,
                petName,
                Vaccine,
                Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DueAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Report?.Serial,
                sheet[row, "Veteriner"],
                EVetCells.Join("; ", Report?.Product is { } product ? $"Ürün: {product}" : null, sheet[row, "Bilgi"], sheet[row, "Açıklama"]),
            ]);

        public (string Pair, DateOnly Day) DayKey => (EVetCells.PairKey(ownerName, petName), DateOnly.FromDateTime(Start));
    }

    private sealed class ConversionState(EVetReports reports, DateTime nowLocal)
    {
        public EVetReports Reports { get; } = reports;
        public DateTime NowLocal { get; } = nowLocal;
        public HashSet<string> CustomerProtocols { get; } = new(StringComparer.Ordinal);
        public Index CustomersByName { get; } = new();
        public Index PetsByPair { get; } = new();
        public HashSet<string> InactivePets { get; } = new(StringComparer.Ordinal);
        public int DuplicatedCustomerProtocols { get; set; }
    }

    /// <summary>Anahtar → protokol; tek ve dolu protokol varsa onu verir (belirsiz veya protokolsüz kayıt null).</summary>
    private sealed class Index
    {
        private readonly Dictionary<string, List<string?>> _items = new(StringComparer.Ordinal);

        public void Append(string key, string? protocol)
        {
            if (!_items.TryGetValue(key, out var list))
                _items[key] = list = [];
            list.Add(protocol);
        }

        public string? Single(string key)
            => _items.TryGetValue(key, out var list) && list.Count == 1 ? list[0] : null;
    }

    private sealed class SkipCounter
    {
        private readonly Dictionary<string, int> _reasons = new(StringComparer.Ordinal);

        public int Total => _reasons.Values.Sum();

        public void Add(string reason, int count = 1)
        {
            if (count > 0)
                _reasons[reason] = _reasons.GetValueOrDefault(reason) + count;
        }

        public string Describe()
            => string.Join(", ", _reasons.OrderByDescending(r => r.Value).Select(r => $"{r.Key}: {r.Value}"));
    }
}

/// <summary>E-vet sayfası: sütunlara katlanmış başlık adıyla erişilir (eksik sütun boş döner).</summary>
internal sealed class EVetSheet
{
    private readonly Dictionary<string, int> _columns;

    public EVetSheet(ImportSourceTable source)
    {
        Rows = source.Rows;
        _columns = source.Headers
            .Select((header, index) => (Key: ImportTextFolding.Fold(header), Index: index))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Index, StringComparer.Ordinal);
    }

    public IReadOnlyList<ImportSourceRow> Rows { get; }

    public string? this[ImportSourceRow row, string header]
        => _columns.TryGetValue(ImportTextFolding.Fold(header), out var index) && index < row.Cells.Count
            ? EVetCells.Clean(row.Cells[index])
            : null;
}
