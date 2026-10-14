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
    bool PayloadPurged);

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
    bool ExcludedByDefault);

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
