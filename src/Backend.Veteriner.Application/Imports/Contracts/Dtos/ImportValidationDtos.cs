using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Contracts.Dtos;

/// <summary>Satır sorunu (IMPORT_API_CONTRACT.md §6.4); <see cref="Severity"/> <c>Info|Warning|Error</c>.</summary>
public sealed record ImportIssueDto(string Severity, string Code, string? Field, string Message, string? Value);

/// <summary>Önizleme (GET .../preview). Satır verisi temizlenmişse türetilmiş listeler boştur ve <see cref="PayloadPurged"/> true.</summary>
public sealed record ImportPreviewDto(
    Guid? ValidationId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ImportBatchStatus Status,
    ImportBatchSummaryDto Summary,
    IReadOnlyList<ImportUnmappedValueDto> UnmappedValues,
    ImportReminderImpactDto ReminderImpact,
    IReadOnlyList<ImportIssueCountDto> IssueCounts,
    bool CanCommit,
    IReadOnlyList<string> Blockers,
    bool PayloadPurged,
    IReadOnlyList<ImportNotImportedReasonDto> NotImportedReasons,
    IReadOnlyList<ImportPetCandidateDto> PetCandidates,
    IReadOnlyList<ImportPetRowDto> PetRows)
{
    /// <summary>Güncelleme özeti (ADR-051, §14.4).</summary>
    public ImportOverlapDto Overlap { get; init; } = ImportOverlapDto.None;

    /// <summary>Eşleşen müşterilerin bakiye farkları (ADR-051, §14.3).</summary>
    public ImportBalanceDifferencesDto BalanceDifferences { get; init; } = ImportBalanceDifferencesDto.None;
}

/// <summary>
/// Güncelleme özeti (§14.4): <see cref="IsUpdate"/> "zaten var" satırları yeni + zaten var toplamının en az yarısıysa; tablo başına
/// <c>willCreate</c> (tablo sayacı) ve <c>alreadyExists</c>.
/// </summary>
public sealed record ImportOverlapDto(bool IsUpdate, int MatchedBySourceId, IReadOnlyList<ImportOverlapTableDto> Tables)
{
    public static readonly ImportOverlapDto None = new(false, 0, []);
}

public sealed record ImportOverlapTableDto(string Table, int WillCreate, int AlreadyExists);

/// <summary>Bakiye farkı listesi (§14.3); <see cref="Items"/> mutlak farka göre azalan, en çok 200.</summary>
public sealed record ImportBalanceDifferencesDto(
    int Count, decimal TotalIncrease, decimal TotalDecrease, IReadOnlyList<ImportBalanceDifferenceDto> Items, bool Truncated)
{
    public static readonly ImportBalanceDifferencesDto None = new(0, 0m, 0m, [], false);
}

/// <summary>Eşleşen müşterinin bakiye farkı: <see cref="Difference"/> = dosyadaki − Vetinity'deki (artı borç artar).</summary>
public sealed record ImportBalanceDifferenceDto(
    int RowNumber, Guid? ClientId, string? ClientName, decimal CurrentBalance, decimal FileBalance, decimal Difference);

/// <summary>
/// Belirsiz hasta grubunun kaydı (bölüm 13.4; grup başına ilk birkaç satır): kullanıcı "hangisine ait?" sorusunu bu bilgiyle
/// yanıtlar. <see cref="Reference"/> grubun <c>source</c> değeridir; <see cref="Title"/> aşı adı ya da kayıt türü,
/// <see cref="Summary"/> muayene/randevu notunun ilk birkaç kelimesi.
/// </summary>
public sealed record ImportPetRowDto(string Reference, string Table, int RowNumber, string? Date, string? Title, string? Summary);

/// <summary>
/// Belirsiz hasta grubunun (<c>unmappedValues[kind=pet]</c>) adayı (bölüm 13.4): kullanıcı kayıtların hangisine ait olduğunu
/// bu bilgilerle seçer. Değerler hazırlık satırındaki gibidir (dosyadaki yazım).
/// </summary>
public sealed record ImportPetCandidateDto(
    string PetNo,
    int RowNumber,
    string? Name,
    string? OwnerName,
    string? Species,
    string? Breed,
    string? Color,
    string? Gender,
    string? BirthDate,
    string? Status,
    string? RegisteredOn);

/// <summary>
/// Tablo başına aktarılmayacak satırların nedeni (2026-11-14): <see cref="Kind"/> <c>Skipped</c> (bilerek atlanan: boş, tekrar,
/// kullanıcının atladığı değer) ya da <c>Error</c>; satır başına bir neden sayılır (atlamada ilk atlama kuralı, hatada ilk hata).
/// </summary>
public sealed record ImportNotImportedReasonDto(string Table, string Kind, string Code, int RowCount);

/// <summary>Eşlenemeyen benzersiz kaynak değer; <see cref="Blocking"/> tür/aşı için true (ırk/renk serbest metne düşer).</summary>
public sealed record ImportUnmappedValueDto(string Kind, string Source, int RowCount, bool Blocking);

/// <summary>
/// İçe aktarılan aşıların hatırlatma etkisi (ADR-019 K3). Yalnız bilgi: içe aktarılan aşılar varsayılan olarak
/// hatırlatma dışıdır (<see cref="ExcludedByDefault"/>).
/// </summary>
public sealed record ImportReminderImpactDto(
    int VaccinationsWithDueDate,
    int VaccinationsWithFutureDueDate,
    int VaccinationsWithPastDueDate,
    int ClientsWithEmail,
    int EmailsWithinReminderWindow,
    int ReminderWindowDays,
    bool TenantRemindersEnabled,
    bool ExcludedByDefault,
    int FutureAppointments = 0,
    int AppointmentEmailsWithinReminderWindow = 0,
    int AppointmentReminderWindowHours = ImportReminderImpactDto.DefaultAppointmentWindowHours,
    bool TenantAppointmentRemindersEnabled = false)
{
    /// <summary>Kiracı ayarı kaydı yoksa randevu hatırlatma penceresi (saat; <c>TenantReminderSettings</c> varsayılanı).</summary>
    public const int DefaultAppointmentWindowHours = 24;

    /// <summary>Serbest bırakma onayının karşılaştırıldığı toplam (aşı + randevu; ADR-048 §12.6).</summary>
    public int TotalEmailsWithinReminderWindow => EmailsWithinReminderWindow + AppointmentEmailsWithinReminderWindow;
}

public sealed record ImportIssueCountDto(string Code, string Severity, int RowCount);

/// <summary>Satır listesi öğesi (GET .../rows).</summary>
public sealed record ImportRowDto(
    string Table,
    int RowNumber,
    ImportRowValidationDto Validation,
    ImportRowApplyDto Apply,
    IReadOnlyList<ImportIssueDto> Issues,
    JsonElement? Values,
    IReadOnlyDictionary<string, string?> Source);

public sealed record ImportRowValidationDto(string Status, string? Action);

public sealed record ImportRowApplyDto(string Status, Guid? TargetId);

/// <summary>POST .../validate sonucu; <see cref="Started"/> false ise doğrulama zaten kuyrukta/sürüyordur (200).</summary>
public sealed record ImportValidationStartResultDto(bool Started, ImportBatchDto Batch);
