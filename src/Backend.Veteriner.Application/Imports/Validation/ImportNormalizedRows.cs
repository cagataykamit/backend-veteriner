using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Imports.Validation;

/// <summary>Satırın planlanan eylemi (kuru çalıştırma); hatalı satırın eylemi yoktur.</summary>
public enum ImportRowAction
{
    /// <summary>Yeni kayıt oluşacak.</summary>
    Create = 1,

    /// <summary>Mevcut kayıtla eşleşecek (yeni kayıt yok, mevcut alanlar ezilmez).</summary>
    Match = 2,

    /// <summary>Atlanacak (dosya içi mükerrer, mevcut aşı, bilinçli atlama).</summary>
    Skip = 3,
}

/// <summary>
/// Normalize satır (<c>NormalizedJson</c>). <see cref="TargetId"/>: eşleşilen mevcut kayıt; <see cref="MergedIntoRow"/>:
/// dosya içi mükerrerde birleşilen ilk satır. B4 eşleşmeleri uygulama anında yeniden çözer.
/// </summary>
public abstract record ImportNormalizedRow
{
    public ImportRowAction? Action { get; init; }
    public Guid? TargetId { get; init; }
    public int? MergedIntoRow { get; init; }
}

/// <summary>
/// Müşteri satırı. <see cref="OpeningBalance"/> aktarılacak sıfır dışı devir bakiyesidir (ADR-048 T3): müşteri oluşturulursa
/// artı tutar devir ücreti, eksi tutar devir alacağı açar; eşleşen/mükerrer satırda null'dır. <see cref="NationalId"/> (bireysel) veya
/// <see cref="TaxNumber"/> (kurumsal) geçerli fatura kimliğidir; <see cref="RemindersOptedOut"/> eski programda pasif müşteridir.
/// <see cref="Notes"/> yalnız oluşturulan müşteriye yazılır.
/// </summary>
public sealed record ImportNormalizedCustomer(
    string? Reference,
    string? FullName,
    string? Phone,
    string? Email,
    string? Address,
    decimal? OpeningBalance = null,
    DateOnly? BalanceDate = null,
    string? NationalId = null,
    string? TaxNumber = null,
    string? TaxOffice = null,
    bool RemindersOptedOut = false,
    string? Notes = null)
    : ImportNormalizedRow;

/// <summary>
/// Hasta satırı. <see cref="AlertNote"/> doluysa hasta alerji uyarısı alır; <see cref="Deceased"/> hasta vefat olarak işaretlenir
/// (tarih <see cref="DeceasedOn"/>, isteğe bağlı).
/// </summary>
public sealed record ImportNormalizedPet(
    string? OwnerKey,
    string? Reference,
    string? Name,
    Guid? SpeciesId,
    Guid? BreedId,
    string? Breed,
    PetGender? Gender,
    DateOnly? BirthDate,
    bool IsNeutered,
    string? Microchip,
    Guid? ColorId,
    decimal? Weight,
    string? PassportOrTagNumber,
    string? Notes,
    string? AlertNote = null,
    bool Deceased = false,
    DateOnly? DeceasedOn = null,
    Guid? PatternId = null)
    : ImportNormalizedRow;

/// <summary>
/// Aşı satırı. <see cref="OwnerKey"/>/<see cref="OwnerHasEmail"/>/<see cref="DueAtUtc"/> hatırlatma etkisini
/// (<c>reminderImpact</c>) hesaplamak içindir; sahip anahtarı <c>file:&lt;satır&gt;</c> veya <c>db:&lt;müşteriId&gt;</c>.
/// </summary>
public sealed record ImportNormalizedVaccination(
    string? PetKey,
    string? OwnerKey,
    bool OwnerHasEmail,
    Guid? VaccineDefinitionId,
    string? VaccineName,
    DateOnly? AppliedAt,
    DateOnly? DueAt,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc,
    string? Notes)
    : ImportNormalizedRow;

/// <summary>Klinik geçmiş satırı (salt okunur eski program kaydı; ADR-048 T1). Sahip anahtarı hastanın sahibidir.</summary>
public sealed record ImportNormalizedClinicalRecord(
    string? PetKey,
    string? OwnerKey,
    DateOnly? RecordedOn,
    string? RecordType,
    string? Complaint,
    string? History,
    string? Findings,
    string? Diagnosis,
    string? Treatment,
    string? Prescription,
    string? LabResults,
    string? Notes,
    string? VeterinarianName,
    decimal? WeightKg,
    decimal? TemperatureC)
    : ImportNormalizedRow;

/// <summary>Hesap hareketi satırı (salt okunur; bakiyeyi etkilemez; ADR-048 T4).</summary>
public sealed record ImportNormalizedAccountEntry(
    string? OwnerKey,
    DateOnly? OccurredOn,
    ImportedAccountEntryKind? Kind,
    decimal? Amount,
    string? Description,
    string? PaymentMethod,
    string? DocumentNumber,
    string? PetName)
    : ImportNormalizedRow;

/// <summary>
/// Gelecek randevu satırı (ADR-048 T5). <see cref="OwnerHasEmail"/>/<see cref="ScheduledAtUtc"/> randevu hatırlatma etkisini
/// hesaplamak içindir. <see cref="Notes"/> hekim/neden eklerini de taşır.
/// </summary>
public sealed record ImportNormalizedAppointment(
    string? PetKey,
    string? OwnerKey,
    bool OwnerHasEmail,
    DateTime? ScheduledAtUtc,
    int DurationMinutes,
    AppointmentType AppointmentType,
    Guid? VeterinarianUserId,
    Guid? ReasonId,
    string? Notes)
    : ImportNormalizedRow;

/// <summary>
/// Ürün/hizmet kartı satırı (stok miktarı yok; ADR-048 T7). <c>TargetId</c> eşleşen mevcut üründür. <see cref="VatRate"/> boşsa
/// klinik varsayılanı; <see cref="PurchasePrice"/> bilgi amaçlıdır. İkisi de yalnız oluşturulan ürüne yazılır.
/// </summary>
public sealed record ImportNormalizedProduct(
    string? Name,
    ProductKind Kind,
    Guid? CategoryId,
    string Unit,
    decimal UnitPrice,
    string? Barcode,
    string? Sku,
    string? Manufacturer,
    string? Description,
    decimal? VatRate = null,
    decimal? PurchasePrice = null)
    : ImportNormalizedRow;

/// <summary><c>NormalizedJson</c> okuma/yazma; tek yer.</summary>
public static class ImportNormalizedJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ImportNormalizedRow row)
        => JsonSerializer.Serialize(row, row.GetType(), Options);

    public static ImportNormalizedVaccination? ParseVaccination(string? json)
        => Parse<ImportNormalizedVaccination>(json);

    /// <summary>Tablonun normalize satırı; boş veya bozuksa null.</summary>
    public static ImportNormalizedRow? Parse(ImportTable table, string? json) => table switch
    {
        ImportTable.Customers => Parse<ImportNormalizedCustomer>(json),
        ImportTable.Pets => Parse<ImportNormalizedPet>(json),
        ImportTable.Vaccinations => Parse<ImportNormalizedVaccination>(json),
        ImportTable.ClinicalRecords => Parse<ImportNormalizedClinicalRecord>(json),
        ImportTable.AccountEntries => Parse<ImportNormalizedAccountEntry>(json),
        ImportTable.Appointments => Parse<ImportNormalizedAppointment>(json),
        ImportTable.Products => Parse<ImportNormalizedProduct>(json),
        _ => null,
    };

    private static T? Parse<T>(string? json)
        where T : ImportNormalizedRow
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Bir tablonun kaynak satırı: kullanıcıya gösterilen satır numarası ve başlık → değer.</summary>
public sealed record ImportSourceRow(int RowNumber, IReadOnlyDictionary<string, string?> Values)
{
    public static ImportSourceRow FromJson(int rowNumber, string? sourceJson)
        => new(rowNumber, ImportSourceJson.Parse(sourceJson));
}

/// <summary>Hazırlık satırı kaynağını (<c>SourceJson</c>) başlık → metin sözlüğüne çevirir.</summary>
public static class ImportSourceJson
{
    public static IReadOnlyDictionary<string, string?> Parse(string? sourceJson)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(sourceJson))
            return values;

        using var doc = JsonDocument.Parse(sourceJson);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in doc.RootElement.EnumerateObject())
                values[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        }

        return values;
    }
}

/// <summary>Bir satırın doğrulama sonucu.</summary>
public sealed record ImportRowResult(
    ImportTable Table,
    int RowNumber,
    ImportRowValidationStatus Status,
    IReadOnlyList<ImportIssue> Issues,
    ImportNormalizedRow Normalized);
