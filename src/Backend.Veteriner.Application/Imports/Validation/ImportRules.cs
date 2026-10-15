using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.Veteriner.Application.Imports.Validation;

public enum ImportIssueSeverity
{
    /// <summary>Bilgi; satır durumunu değiştirmez.</summary>
    Info = 0,

    /// <summary>Uyarı; satır aktarılır, rapora düşer.</summary>
    Warning = 1,

    /// <summary>Hata; satır aktarılmaz.</summary>
    Error = 2,
}

/// <summary>Satır sorunu (<c>IssuesJson</c> öğesi). <see cref="Value"/> yalnız kullanıcının düzeltmesi için gereken kaynak değerdir.</summary>
public sealed record ImportIssue(ImportIssueSeverity Severity, string Code, string? Field, string Message, string? Value = null);

/// <summary>Kural tanımı: kod, önem ve varsayılan (kullanıcıya gösterilebilir, Türkçe) mesaj.</summary>
public sealed record ImportRule(string Code, ImportIssueSeverity Severity, string Message)
{
    public ImportIssue At(string? field = null, string? message = null, string? value = null)
        => new(Severity, Code, field, message ?? Message, value);
}

/// <summary>
/// Satır ve dosya düzeyi kural kodlarının tek yeri (IMPORT_API_CONTRACT.md §6.4). Kodlar, önem dereceleri ve varsayılan
/// mesajlar yalnız burada tanımlanır; doğrulama motoru, önizleme ve hata raporu bu kataloğu kullanır.
/// </summary>
public static class ImportRules
{
    // Zorunluluk / uzunluk
    public static readonly ImportRule RequiredMissing = new("Imports.RequiredMissing", ImportIssueSeverity.Error, "Zorunlu alan boş.");
    public static readonly ImportRule NameTooShort = new("Imports.NameTooShort", ImportIssueSeverity.Error, "Ad soyad en az 2 karakter olmalıdır.");
    public static readonly ImportRule FieldTooLong = new("Imports.FieldTooLong", ImportIssueSeverity.Error, "Değer izin verilen uzunluğu aşıyor.");

    // Telefon / e-posta (K5)
    public static readonly ImportRule PhoneInvalid = new("Imports.PhoneInvalid", ImportIssueSeverity.Warning,
        "Telefon Türkiye cep numarası biçiminde değil; telefonsuz aktarılacak.");
    public static readonly ImportRule EmailInvalid = new("Imports.EmailInvalid", ImportIssueSeverity.Warning,
        "E-posta adresi geçersiz; e-postasız aktarılacak.");

    // Tarih
    public static readonly ImportRule DateInvalid = new("Imports.DateInvalid", ImportIssueSeverity.Error,
        "Tarih okunamadı veya takvimde yok. Beklenen biçim: gg.aa.yyyy veya yyyy-aa-gg.");
    public static readonly ImportRule DateFormatAmbiguous = new("Imports.DateFormatAmbiguous", ImportIssueSeverity.Error,
        "Tarih biçimi belirsiz (iki haneli yıl, yalnız ay/yıl veya ay adı kabul edilmez). Beklenen biçim: gg.aa.yyyy.");
    public static readonly ImportRule DateOutOfRange = new("Imports.DateOutOfRange", ImportIssueSeverity.Error,
        "Tarih 01.01.1990'dan eski olamaz.");
    public static readonly ImportRule BirthDateInFuture = new("Pets.BirthDateInFuture", ImportIssueSeverity.Error,
        "Doğum tarihi gelecekte olamaz.");
    public static readonly ImportRule BirthDateImplausible = new("Imports.BirthDateImplausible", ImportIssueSeverity.Warning,
        "Doğum tarihi 40 yıldan eski; lütfen kontrol edin.");
    public static readonly ImportRule AppliedAtInFuture = new("Vaccinations.AppliedAtMustNotBeFuture", ImportIssueSeverity.Error,
        "Uygulanmış aşının uygulama tarihi gelecekte olamaz.");
    public static readonly ImportRule DueNotAfterApplied = new("Vaccinations.DueAtMustBeAfterAppliedAt", ImportIssueSeverity.Error,
        "Sonraki aşı tarihi uygulama tarihinden sonra olmalıdır.");
    public static readonly ImportRule DueTooFarInFuture = new("Vaccinations.DueTooFarInFuture", ImportIssueSeverity.Error,
        "Sonraki aşı tarihi en fazla 5 yıl ileriye girilebilir.");
    public static readonly ImportRule AppliedBeforeBirth = new("Imports.AppliedBeforeBirth", ImportIssueSeverity.Warning,
        "Uygulama tarihi hastanın doğum tarihinden önce.");

    // Mikroçip (K10)
    public static readonly ImportRule MicrochipInvalid = new("Imports.MicrochipInvalid", ImportIssueSeverity.Error,
        "Mikroçip yalnız rakamlardan oluşmalıdır (en çok 50 hane).");
    public static readonly ImportRule MicrochipScientificNotation = new("Imports.MicrochipScientificNotation", ImportIssueSeverity.Error,
        "Mikroçip bilimsel gösterimde (örn. 9,85121E+14); Excel basamakları kaybetmiş olabilir. Hücreyi metin biçimine çevirip yeniden girin.");
    public static readonly ImportRule MicrochipLength = new("Imports.MicrochipLength", ImportIssueSeverity.Warning,
        "Mikroçip 15 haneli değil (eski çip olabilir).");
    public static readonly ImportRule MicrochipConflict = new("Imports.MicrochipConflict", ImportIssueSeverity.Error,
        "Aynı mikroçip başka bir hastada kayıtlı.");

    // Değer eşleme (K6)
    public static readonly ImportRule SpeciesUnmapped = new("Imports.SpeciesUnmapped", ImportIssueSeverity.Error,
        "Hayvan türü katalogda bulunamadı; değer eşleyin veya Ayarlar'dan tür ekleyin.");
    public static readonly ImportRule VaccineUnmapped = new("Imports.VaccineUnmapped", ImportIssueSeverity.Error,
        "Aşı adı katalogda bulunamadı; değer eşleyin veya Ayarlar'dan aşı tanımı ekleyin.");
    public static readonly ImportRule ValueSkipped = new("Imports.ValueSkipped", ImportIssueSeverity.Warning,
        "Bu değer eşlemede bilinçli olarak atlandı; satır aktarılmayacak.");
    public static readonly ImportRule ParentSkipped = new("Imports.ParentSkipped", ImportIssueSeverity.Warning,
        "Bağlı olduğu hasta satırı atlandığı için bu satır da atlanacak.");
    public static readonly ImportRule BreedFreeText = new("Imports.BreedFreeText", ImportIssueSeverity.Info,
        "Irk katalogda bulunamadı; serbest metin olarak aktarılacak.");
    public static readonly ImportRule ColorFreeText = new("Imports.ColorFreeText", ImportIssueSeverity.Info,
        "Renk katalogda bulunamadı; nota eklenecek.");
    public static readonly ImportRule BreedSpeciesMismatch = new("Pets.BreedSpeciesMismatch", ImportIssueSeverity.Error,
        "Eşlenen ırk bu hayvan türüne ait değil.");
    public static readonly ImportRule VaccineInvalidSpecies = new("VaccineDefinitions.InvalidSpecies", ImportIssueSeverity.Error,
        "Eşlenen aşı tanımı hayvan türü ile uyumlu değil.");

    // Hasta alanları
    public static readonly ImportRule GenderUnrecognized = new("Imports.GenderUnrecognized", ImportIssueSeverity.Warning,
        "Cinsiyet tanınmadı; boş aktarılacak.");
    public static readonly ImportRule NeuteredUnrecognized = new("Imports.NeuteredUnrecognized", ImportIssueSeverity.Warning,
        "Kısır değeri tanınmadı; 'Hayır' aktarılacak.");
    public static readonly ImportRule WeightInvalid = new("Imports.WeightInvalid", ImportIssueSeverity.Warning,
        "Ağırlık geçersiz (0 ile 9999,99 kg arasında bir sayı olmalı); boş aktarılacak.");

    // Mükerrer / eşleşme (K4)
    public static readonly ImportRule DuplicateReference = new("Imports.DuplicateReference", ImportIssueSeverity.Error,
        "Bu numara dosyada daha önce kullanılmış; numaralar benzersiz olmalıdır.");
    public static readonly ImportRule DuplicateInFile = new("Imports.DuplicateInFile", ImportIssueSeverity.Warning,
        "Aynı kayıt dosyada daha önce geçiyor; bu satır atlanacak.");
    public static readonly ImportRule DuplicateClient = new("Imports.DuplicateClient", ImportIssueSeverity.Error,
        "Aynı ad ve telefon/e-posta ile kayıtlı müşteri var (seçenek: mükerrerde hata).");
    public static readonly ImportRule MatchedExisting = new("Imports.MatchedExisting", ImportIssueSeverity.Info,
        "Mevcut kayıtla eşleşti; yeni kayıt oluşmayacak, mevcut bilgiler değişmeyecek.");
    public static readonly ImportRule SharedPhone = new("Imports.SharedPhone", ImportIssueSeverity.Info,
        "Aynı telefon başka bir müşteride de kayıtlı (aile bireyi olabilir); yeni müşteri oluşacak.");
    public static readonly ImportRule PossibleDuplicate = new("Imports.PossibleDuplicate", ImportIssueSeverity.Warning,
        "Telefon ve e-posta yok; aynı adlı bir müşteri zaten var. Ayırt edilemediği için yeni müşteri oluşacak.");
    public static readonly ImportRule DuplicateVaccination = new("Imports.DuplicateVaccination", ImportIssueSeverity.Info,
        "Aynı hasta, aynı aşı ve aynı uygulama günü için kayıt zaten var; bu satır atlanacak.");

    // Bağlantılar
    public static readonly ImportRule OwnerNotFound = new("Imports.OwnerNotFound", ImportIssueSeverity.Error,
        "Hastanın sahibi dosyada veya kayıtlı müşterilerde bulunamadı.");
    public static readonly ImportRule OwnerRowInvalid = new("Imports.OwnerRowInvalid", ImportIssueSeverity.Error,
        "Sahip müşteri satırı hatalı olduğu için bu satır aktarılamaz.");
    public static readonly ImportRule PetNotFound = new("Imports.PetNotFound", ImportIssueSeverity.Error,
        "Aşının hastası dosyada veya kayıtlı hastalarda bulunamadı.");
    public static readonly ImportRule PetRowInvalid = new("Imports.PetRowInvalid", ImportIssueSeverity.Error,
        "Hasta satırı hatalı olduğu için bu aşı satırı aktarılamaz.");

    // Uygulama (B4; satır sonucu yazılırken sorun listesine eklenir, doğrulama durumu değişmez)
    public static readonly ImportRule OwnerUnitFailed = new("Imports.OwnerUnitFailed", ImportIssueSeverity.Error,
        "Aynı sahibe bağlı başka bir satır uygulanamadığı için bu satır da uygulanmadı (sahip birimi bütün olarak yazılır).");
    public static readonly ImportRule MatchedAtCommit = new("Imports.MatchedAtCommit", ImportIssueSeverity.Info,
        "Önizlemeden sonra eklenmiş mevcut kayıtla eşleşti; yeni kayıt oluşmadı.");
    public static readonly ImportRule ApplyFailed = new("Imports.ApplyFailed", ImportIssueSeverity.Error,
        "Kayıt yazılırken veritabanı çakışması oluştu ve yeniden deneme de başarısız oldu.");

    // Geri alma (B5; Kept satırına eklenir, value = virgülle ayrılmış neden kodları, IMPORT_API_CONTRACT.md §9)
    public static readonly ImportRule RollbackKept = new("Imports.RollbackKept", ImportIssueSeverity.Info,
        "Geri almada silinmedi: kayda bağlı başka kayıt var veya kayıt sonradan değiştirilmiş.");

    // Dosya düzeyi (satır sorunu değil; preview.blockers)
    public static readonly ImportRule OwnerLinkUnavailable = new("Imports.OwnerLinkUnavailable", ImportIssueSeverity.Error,
        "Hasta tablosu yalnız MusteriNo ile bağlanıyor ancak müşteri tablosu yok veya MusteriNo sütunu eşlenmemiş.");
    public static readonly ImportRule PetLinkUnavailable = new("Imports.PetLinkUnavailable", ImportIssueSeverity.Error,
        "Aşı tablosu yalnız HastaNo ile bağlanıyor ancak hasta tablosu yok veya HastaNo sütunu eşlenmemiş.");
    public static readonly ImportRule NoImportableRows = new("Imports.NoImportableRows", ImportIssueSeverity.Error,
        "Aktarılabilir (geçerli) satır yok.");

    /// <summary>Tüm kurallar (katalog); kodlar benzersizdir.</summary>
    public static IReadOnlyList<ImportRule> All { get; } = typeof(ImportRules)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(ImportRule))
        .Select(f => (ImportRule)f.GetValue(null)!)
        .ToList();

    public static ImportRule? Find(string? code)
        => All.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.Ordinal));
}

/// <summary>Satır sorunları listesinin (<c>IssuesJson</c>) serileştirilmesi; tek yer.</summary>
public static class ImportIssueJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Sorun yoksa null (sütun boş kalır).</summary>
    public static string? Serialize(IReadOnlyList<ImportIssue> issues)
        => issues.Count == 0 ? null : JsonSerializer.Serialize(issues, Options);

    public static IReadOnlyList<ImportIssue> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<ImportIssue>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Uygulama anı sorununu mevcut listeye ekler (doğrulama sorunları korunur).</summary>
    public static string? Append(string? json, ImportIssue? issue)
        => issue is null ? json : Serialize([.. Parse(json), issue]);

    /// <summary>Satır sorunlarından birinin kodunu <c>LIKE</c> ile aramak için JSON parçası.</summary>
    public static string CodeFragment(string code) => $"\"code\":\"{code}\"";
}
