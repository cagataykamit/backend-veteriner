using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Backend.IntegrationTests.Performance;

/// <summary>
/// İçe aktarma deneme/perf ölçeği (Dilim T). <c>IMPORT_PERF_CUSTOMERS</c> (varsayılan 500); hasta ve aşı sayısı pilot
/// oranıyla (5.000 / 8.000 / 20.000) türetilir, istenirse <c>IMPORT_PERF_PETS</c> / <c>IMPORT_PERF_VACCINATIONS</c> ile verilir.
/// </summary>
public sealed record ImportPilotScale(int Customers, int Pets, int Vaccinations)
{
    public static ImportPilotScale FromEnvironment()
    {
        var customers = Read("IMPORT_PERF_CUSTOMERS", 500);
        return new ImportPilotScale(
            customers,
            Read("IMPORT_PERF_PETS", (int)Math.Round(customers * 1.6)),
            Read("IMPORT_PERF_VACCINATIONS", customers * 4));
    }

    public override string ToString() => $"müşteri={Customers}, hasta={Pets}, aşı={Vaccinations}";

    private static int Read(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;
}

/// <summary>
/// Sentetik, pilot benzeri kirli içe aktarma dosyası üretici (gerçek kişisel veri YOK; adlar ortak ad listelerinden
/// birleştirilir, telefonlar sıra numarasından, e-postalar <c>example.com</c>). Sabit tohum: aynı ölçek aynı dosyayı
/// üretir. Kirli veri karışımı: büyük harfli adlar, sabit hat/geçersiz/baştaki sıfırı kaybolmuş telefon, bozuk/belirsiz
/// tarih, eksik zorunlu alan, dosya içi ve sistemle çakışan mükerrer müşteri, aynı mikroçip (dosya içi + sistem),
/// bilimsel gösterimli çip, bilinmeyen tür/aşı adı, aynı gün mükerrer aşı. Başlıklar kliniğin kendi yazımıyla (takma ad)
/// gelir; kanonik eşleme önerisi böylece denenir. Enjekte edilen kirli satır sayıları <see cref="Injected"/>'da tutulur.
/// </summary>
public sealed class ImportPilotFileGenerator
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[] FirstNames =
    [
        "Ayşe", "Fatma", "Emine", "Hatice", "Zeynep", "Elif", "Merve", "Şule", "Özlem", "Gül", "Çiğdem", "Büşra", "İpek",
        "Ece", "Derya", "Sibel", "Gizem", "Tuğba", "Ebru", "Nur", "Mehmet", "Mustafa", "Ahmet", "Ali", "Hüseyin", "Hasan",
        "İbrahim", "İsmail", "Osman", "Yusuf", "Murat", "Ömer", "Ramazan", "Halil", "Süleyman", "Abdullah", "Mahmut",
        "Recep", "Salih", "Kemal", "Can", "Cem", "Barış", "Uğur", "Tolga", "Serkan", "Burak", "Emre", "Oğuz", "Gökhan",
        "Ilgın", "Işık", "Irmak", "Çağrı", "Doğan", "Şükrü", "Gönül", "Ümran", "Ünal", "Öykü",
    ];

    private static readonly string[] LastNames =
    [
        "Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Yıldız", "Yıldırım", "Öztürk", "Aydın", "Özdemir", "Arslan",
        "Doğan", "Kılıç", "Aslan", "Çetin", "Kara", "Koç", "Kurt", "Özkan", "Şimşek", "Polat", "Özcan", "Korkmaz",
        "Çakır", "Erdoğan", "Yavuz", "Can", "Acar", "Şen", "Aktaş", "Güler", "Yalçın", "Güneş", "Bozkurt", "Bulut",
        "Keskin", "Ünal", "Turan", "Gül", "Özer", "Işık", "Kaplan", "Avcı", "Sarı", "Tekin", "Taş", "Köse", "Yüksel",
        "Ateş", "Aksoy", "Uçar", "İnce", "Ilgaz", "Ergün", "Uysal", "Altun", "Bayram", "Duman", "Karataş", "Sönmez",
    ];

    private static readonly string[] PetNames =
    [
        "Pamuk", "Boncuk", "Duman", "Karamel", "Zeytin", "Fıstık", "Tarçın", "Minnoş", "Paşa", "Çakıl", "Lokum", "Badem",
        "Kömür", "Bulut", "Şans", "Tekir", "Mırmır", "Garip", "Karabaş", "Çomar", "Rex", "Max", "Luna", "Bella", "Leo",
        "Mia", "Oscar", "Simba", "Nala", "Gofret", "Cips", "Pofuduk", "Şeker", "Kontes", "Prenses", "Haydut", "Yumak",
        "Bıdık", "Ponçik", "Sarman",
    ];

    private static readonly string[] DogSpellings = ["Köpek", "KÖPEK", "kopek", "Kopek", "köpek", "dog"];
    private static readonly string[] CatSpellings = ["Kedi", "KEDİ", "kedi", "Cat"];
    private static readonly string[] OtherSpecies = ["Kuş", "Tavşan", "Hamster"];
    private static readonly string[] UnknownSpecies = ["Ejderha", "Bilinmiyor"];
    private static readonly string[] Vaccines = ["Kuduz", "KUDUZ", "Karma", "karma aşısı", "Rabies", "Bronşin", "Lösemi", "İç Parazit", "Dış parazit"];
    private static readonly string[] UnknownVaccines = ["Parvo Özel", "Gençlik Aşısı"];
    private static readonly string[] Breeds = ["Golden", "Tekir", "Sokak", "Van", "Kangal", "Terrier Karışık", "Scottish", "", ""];
    private static readonly string[] Colors = ["Sarı", "Siyah", "Beyaz", "Kahverengi", "Gri", "", ""];

    private readonly ImportPilotScale _scale;
    private readonly DateOnly _today;
    private readonly Random _random = new(20261014);

    public ImportPilotFileGenerator(ImportPilotScale scale, DateOnly today)
    {
        _scale = scale;
        _today = today;
        Generate();
    }

    public List<CustomerRow> Customers { get; } = [];
    public List<PetRow> Pets { get; } = [];
    public List<VaccinationRow> Vaccinations { get; } = [];

    /// <summary>Kiracıda önceden var olan (sistemle çakışacak) müşteriler: ad (farklı büyük/küçük harf) + cep telefonu.</summary>
    public List<(string Name, string Phone)> ExistingClients { get; } = [];

    /// <summary>Kiracıda önceden var olan hayvanlara verilecek çipler (dosyadaki bazı hayvanlarla çakışır).</summary>
    public List<string> ExistingChips { get; } = [];

    /// <summary>Enjekte edilen kirli satır sayıları (rapor: beklenen ↔ doğrulamanın bulduğu).</summary>
    public SortedDictionary<string, int> Injected { get; } = new(StringComparer.Ordinal);

    /// <summary>A partisi aşıları (hasta numarasıyla bağlı): ilk yarı. B partisi: kalan yarı (sahip + hayvan adıyla bağlı).</summary>
    public int SplitIndex => Vaccinations.Count / 2;

    // ================================================================ Üretim

    private void Generate()
    {
        GenerateCustomers();
        GeneratePets();
        GenerateVaccinations();
    }

    private void Count(string key) => Injected[key] = Injected.GetValueOrDefault(key) + 1;

    private bool Chance(double p) => _random.NextDouble() < p;

    private static string Mobile(int i) => $"05{32 + i % 24}{1_000_000 + i:D7}";

    private void GenerateCustomers()
    {
        for (var i = 0; i < _scale.Customers; i++)
        {
            var name = $"{FirstNames[i % FirstNames.Length]} {LastNames[(i / FirstNames.Length + i * 7) % LastNames.Length]}";
            if (i % 9 == 0)
                name = $"{FirstNames[(i / 3) % FirstNames.Length]} {name}"; // ikinci ad
            var mobile = Mobile(i);
            var row = new CustomerRow { No = $"M-{i + 1}", Name = name, Mobile = mobile };

            // Önceki bir satırın dosya içi mükerreri (aynı ad büyük harfle + aynı numara başka biçimde).
            if (i > 20 && Chance(0.03))
            {
                var original = Customers[_random.Next(Math.Max(0, i - 200), i)];
                if (original.Name is { Length: > 0 } && original.Mobile is not null && original.PhoneWritten == original.Mobile)
                {
                    row.Name = original.Name.ToUpper(new CultureInfo("tr-TR"));
                    row.Mobile = original.Mobile;
                    row.PhoneWritten = $"+90 {original.Mobile[1..4]} {original.Mobile[4..7]} {original.Mobile[7..9]} {original.Mobile[9..]}";
                    row.DuplicateOf = original;
                    Count("customers.duplicateInFile");
                    Customers.Add(row);
                    continue;
                }
            }

            if (Chance(0.15))
            {
                row.Name = name.ToUpper(new CultureInfo("tr-TR"));
                Count("customers.upperCaseName");
            }

            var p = _random.NextDouble();
            if (p < 0.08)
            {
                row.PhoneWritten = $"0212 {2_000_000 + i:D7}";
                row.Mobile = null;
                Count("customers.landlinePhone");
            }
            else if (p < 0.11)
            {
                row.PhoneWritten = i % 2 == 0 ? "12345" : "yok";
                row.Mobile = null;
                Count("customers.invalidPhone");
            }
            else if (p < 0.16)
            {
                row.PhoneWritten = null;
                row.Mobile = null;
                Count("customers.emptyPhone");
            }
            else if (p < 0.21)
            {
                row.PhoneWritten = mobile[1..]; // Excel baştaki 0'ı kaybetti
                Count("customers.leadingZeroLost");
            }
            else if (p < 0.40)
            {
                row.PhoneWritten = $"{mobile[..4]} {mobile[4..7]} {mobile[7..9]} {mobile[9..]}";
            }
            else
            {
                row.PhoneWritten = mobile;
            }

            if (Chance(0.12))
            {
                row.Email = Chance(0.08) ? $"musteri{i}@" : $"musteri{i}@example.com";
                if (row.Email.EndsWith('@'))
                    Count("customers.invalidEmail");
            }

            if (Chance(0.3))
                row.Address = $"Örnek Mah. {i % 90 + 1}. Sok. No:{i % 40 + 1} Karşıyaka";

            if (Chance(0.02))
            {
                row.Name = null;
                Count("customers.missingName");
            }
            else if (row.Mobile is not null && Chance(0.05))
            {
                // Kiracıda aynı kişi zaten kayıtlı (ad farklı yazımla, aynı cep): Matched beklenir.
                ExistingClients.Add((ToTitleCase(row.Name!), row.Mobile));
                Count("customers.existsInSystem");
            }

            Customers.Add(row);
        }
    }

    private void GeneratePets()
    {
        var chipSeq = 0L;
        for (var p = 0; p < _scale.Pets; p++)
        {
            var owner = p < Customers.Count ? Customers[p] : Customers[_random.Next(Customers.Count)];
            var row = new PetRow { No = $"H-{p + 1}", Owner = owner, Name = PetNames[_random.Next(PetNames.Length)] };

            var s = _random.NextDouble();
            row.SpeciesWritten = s < 0.55 ? DogSpellings[_random.Next(DogSpellings.Length)]
                : s < 0.93 ? CatSpellings[_random.Next(CatSpellings.Length)]
                : s < 0.98 ? OtherSpecies[_random.Next(OtherSpecies.Length)]
                : UnknownSpecies[_random.Next(UnknownSpecies.Length)];
            if (s >= 0.98)
                Count("pets.unknownSpecies");
            row.SpeciesKey = s < 0.55 ? "dog" : s < 0.93 ? "cat" : row.SpeciesWritten;

            row.Breed = Breeds[_random.Next(Breeds.Length)];
            row.Gender = _random.Next(10) switch { 0 => "E", 1 => "D", 2 => "erkek", 3 => "Dişi", 4 => "", 5 => "?", _ => _random.Next(2) == 0 ? "Erkek" : "Dişi" };
            if (row.Gender == "?")
                Count("pets.genderUnrecognized");

            var birth = _today.AddDays(-_random.Next(200, 6000));
            var b = _random.NextDouble();
            if (b < 0.008) { row.BirthDate = "31.02.2020"; Count("pets.dateInvalid"); }
            else if (b < 0.016) { row.BirthDate = $"{birth.Day}.{birth.Month}.{birth.Year % 100:D2}"; Count("pets.dateAmbiguous"); }
            else if (b < 0.019) { row.BirthDate = _today.AddDays(30).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture); Count("pets.birthInFuture"); }
            else if (b < 0.15) row.BirthDate = null;
            else if (b < 0.25) row.BirthDate = birth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            else row.BirthDate = birth.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

            row.Neutered = _random.Next(5) switch { 0 => "Evet", 1 => "Hayır", 2 => "E", 3 => "H", _ => "" };

            var c = _random.NextDouble();
            if (c < 0.40)
            {
                row.Chip = $"9851{1_000_000_000 + chipSeq++:D11}";
            }
            else if (c < 0.407 && Pets.Any(x => x.Chip is { Length: 15 } && x.Owner != owner))
            {
                row.Chip = Pets.Last(x => x.Chip is { Length: 15 } && x.Owner != owner).Chip; // başka hayvanın çipi
                Count("pets.microchipDuplicateInFile");
            }
            else if (c < 0.412)
            {
                row.Chip = $"9852{2_000_000_000 + chipSeq++:D11}";
                ExistingChips.Add(row.Chip);
                Count("pets.microchipConflictSystem");
            }
            else if (c < 0.415) { row.Chip = "9,85112E+14"; Count("pets.microchipScientific"); }
            else if (c < 0.422) { row.Chip = $"{1_000_000_000 + p:D10}"; Count("pets.microchipShort"); }
            else if (c < 0.424) { row.Chip = $"98511ABC{p:D4}"; Count("pets.microchipInvalid"); }

            row.Color = Colors[_random.Next(Colors.Length)];
            row.Weight = _random.Next(20) switch { 0 => "abc", 1 or 2 => "", 3 => $"{_random.Next(2, 40)}.{_random.Next(10)} kg", _ => $"{_random.Next(1, 45)},{_random.Next(10)}" };
            if (row.Weight == "abc")
                Count("pets.weightInvalid");

            if (Chance(0.01))
            {
                row.Name = null;
                Count("pets.missingName");
            }
            else if (p > 0 && Chance(0.01))
            {
                // Aynı sahip + ad + tür (dosya içi mükerrer hayvan).
                var original = Pets[_random.Next(Pets.Count)];
                if (original.Name is not null)
                {
                    row.Owner = original.Owner;
                    row.Name = original.Name.ToUpperInvariant();
                    row.SpeciesWritten = original.SpeciesWritten;
                    row.SpeciesKey = original.SpeciesKey;
                    row.Chip = null;
                    Count("pets.duplicateInFile");
                }
            }

            Pets.Add(row);
        }
    }

    private void GenerateVaccinations()
    {
        for (var v = 0; v < _scale.Vaccinations; v++)
        {
            var pet = v < Pets.Count ? Pets[v] : Pets[_random.Next(Pets.Count)];
            var row = new VaccinationRow { Pet = pet };

            if (v > 0 && Chance(0.01) && Vaccinations[^1].Pet == pet)
            {
                // Aynı hasta + aşı + gün (dosya içi mükerrer aşı).
                var prev = Vaccinations[^1];
                row.Vaccine = prev.Vaccine;
                row.Applied = prev.Applied;
                row.Due = prev.Due;
                Count("vaccinations.duplicateSameDay");
                Vaccinations.Add(row);
                continue;
            }

            row.Vaccine = Chance(0.02) ? UnknownVaccines[_random.Next(UnknownVaccines.Length)] : Vaccines[_random.Next(Vaccines.Length)];
            if (UnknownVaccines.Contains(row.Vaccine))
                Count("vaccinations.unknownVaccine");

            var applied = _today.AddDays(-_random.Next(1, 2900));
            if (Chance(0.005))
            {
                applied = _today.AddDays(-363); // vade 2 gün sonra: hatırlatma penceresinde
                Count("vaccinations.dueInReminderWindow");
            }

            var due = Chance(0.2) ? (DateOnly?)null : applied.AddDays(365);
            row.Applied = applied.ToString(_random.Next(4) == 0 ? "yyyy-MM-dd" : "dd.MM.yyyy", CultureInfo.InvariantCulture);
            row.Due = due?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

            var d = _random.NextDouble();
            if (d < 0.008) { row.Applied = "2023.13.45"; Count("vaccinations.dateInvalid"); }
            else if (d < 0.015) { row.Applied = $"{applied.Month}/{applied.Day + 12}/{applied.Year}"; Count("vaccinations.dateInvalidUsOrder"); }
            else if (d < 0.022) { row.Applied = null; Count("vaccinations.missingAppliedDate"); }
            else if (d < 0.027) { row.Due = applied.AddDays(-30).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture); Count("vaccinations.dueBeforeApplied"); }
            else if (d < 0.030) { row.Applied = _today.AddDays(10).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture); row.Due = null; Count("vaccinations.appliedInFuture"); }

            if (Chance(0.3))
                row.Lot = $"L-{_random.Next(1000, 9999)}";
            if (Chance(0.3))
                row.Vet = "Dr. Örnek";

            Vaccinations.Add(row);
        }
    }

    private static string ToTitleCase(string name)
        => new CultureInfo("tr-TR").TextInfo.ToTitleCase(name.ToLower(new CultureInfo("tr-TR")));

    // ================================================================ Tablolar (başlık + satırlar)

    private static readonly string[] CustomerHeaders = ["Müşteri No", "Ad Soyad", "Cep Telefonu", "E-posta", "Adres"];
    private static readonly string[] PetHeaders = ["Müşteri No", "Hasta No", "Hayvan Adı", "Tür", "Cins", "Cinsiyet", "Doğum Tarihi", "Kısır", "Mikroçip No", "Renk", "Kilo", "Notlar"];
    private static readonly string[] VaccinationByPetHeaders = ["Hasta No", "Aşı", "Aşı Tarihi", "Sonraki Aşı", "Lot", "Hekim"];
    private static readonly string[] VaccinationByOwnerHeaders = ["Sahip Adı", "Sahip Tel", "Hayvan Adı", "Tür", "Aşı", "Aşı Tarihi", "Sonraki Aşı"];

    private IEnumerable<string?[]> CustomerTable()
    {
        yield return CustomerHeaders;
        foreach (var c in Customers)
            yield return [c.No, c.Name, c.PhoneWritten, c.Email, c.Address];
    }

    private IEnumerable<string?[]> PetTable()
    {
        yield return PetHeaders;
        foreach (var p in Pets)
            yield return [p.Owner.No, p.No, p.Name, p.SpeciesWritten, p.Breed, p.Gender, p.BirthDate, p.Neutered, p.Chip, p.Color, p.Weight, null];
    }

    private IEnumerable<string?[]> VaccinationByPetTable(int from, int to)
    {
        yield return VaccinationByPetHeaders;
        for (var i = from; i < to; i++)
        {
            var v = Vaccinations[i];
            yield return [v.Pet.No, v.Vaccine, v.Applied, v.Due, v.Lot, v.Vet];
        }
    }

    /// <summary>B partisi: hasta numarası yok; sahip adı + telefonu + hayvan adı + tür ile kiracıdaki hastaya bağlanır.</summary>
    private IEnumerable<string?[]> VaccinationByOwnerTable(int from, int to)
    {
        yield return VaccinationByOwnerHeaders;
        for (var i = from; i < to; i++)
        {
            var v = Vaccinations[i];
            var owner = v.Pet.Owner.DuplicateOf ?? v.Pet.Owner;
            yield return [owner.Name, owner.PhoneWritten, v.Pet.Name, v.Pet.SpeciesWritten, v.Vaccine, v.Applied, v.Due];
        }
    }

    // ================================================================ Dosyalar

    /// <summary>A partisi XLSX (üç sayfa, aşıların ilk yarısı). <paramref name="allVaccinations"/>: tek dosya (sınır denemesi).</summary>
    public byte[] BatchAXlsx(bool allVaccinations = false)
        => Xlsx(
            ("Musteriler", CustomerTable()),
            ("Hayvanlar", PetTable()),
            ("Asilar", VaccinationByPetTable(0, allVaccinations ? Vaccinations.Count : SplitIndex)));

    /// <summary>A partisinin CSV varyantı (Windows-1254, <c>;</c> ayırıcı, BOM yok; Türkçe Excel'in "CSV" çıktısı gibi).</summary>
    public (byte[] Customers, byte[] Pets, byte[] Vaccinations) BatchACsv1254()
        => (Csv1254(CustomerTable()), Csv1254(PetTable()), Csv1254(VaccinationByPetTable(0, SplitIndex)));

    /// <summary>B partisi: kalan aşılar, CSV Windows-1254.</summary>
    public byte[] BatchBVaccinationsCsv1254() => Csv1254(VaccinationByOwnerTable(SplitIndex, Vaccinations.Count));

    private static byte[] Xlsx(params (string Sheet, IEnumerable<string?[]> Rows)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, rows) in sheets)
        {
            var sheet = workbook.Worksheets.Add(name);
            var r = 1;
            foreach (var row in rows)
            {
                for (var c = 0; c < row.Length; c++)
                {
                    if (row[c] is { Length: > 0 } value)
                        sheet.Cell(r, c + 1).SetValue(value); // metin hücre (klinik dosyası telefon/çip/tarihi metin tutmuş)
                }

                r++;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] Csv1254(IEnumerable<string?[]> rows)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sb = new StringBuilder();
        foreach (var row in rows)
            sb.Append(string.Join(';', row.Select(Quote))).Append("\r\n");
        return Encoding.GetEncoding(1254).GetBytes(sb.ToString());

        static string Quote(string? value)
            => value is null ? "" : value.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    // ================================================================ Satır modelleri

    public sealed class CustomerRow
    {
        public required string No { get; init; }
        public string? Name { get; set; }
        public string? Mobile { get; set; }
        public string? PhoneWritten { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public CustomerRow? DuplicateOf { get; set; }
    }

    public sealed class PetRow
    {
        public required string No { get; init; }
        public required CustomerRow Owner { get; set; }
        public string? Name { get; set; }
        public string SpeciesWritten { get; set; } = "";
        public string SpeciesKey { get; set; } = "";
        public string? Breed { get; set; }
        public string? Gender { get; set; }
        public string? BirthDate { get; set; }
        public string? Neutered { get; set; }
        public string? Chip { get; set; }
        public string? Color { get; set; }
        public string? Weight { get; set; }
    }

    public sealed class VaccinationRow
    {
        public required PetRow Pet { get; init; }
        public string? Vaccine { get; set; }
        public string? Applied { get; set; }
        public string? Due { get; set; }
        public string? Lot { get; set; }
        public string? Vet { get; set; }
    }
}
