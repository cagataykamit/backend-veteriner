using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Contracts.Dtos;

/// <summary>İçe aktarma partisi (ayrıntı ve liste öğesi). Satır verisi içermez.</summary>
public sealed record ImportBatchDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ImportBatchStatus Status,
    string? Name,
    Guid? ClinicId,
    Guid? ValidationId,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    DateTime? ValidatedAtUtc,
    DateTime? CommittedAtUtc,
    DateTime? RolledBackAtUtc,
    DateTime? RemindersReleasedAtUtc,
    DateTime? PayloadPurgedAtUtc,
    ImportBatchSummaryDto Summary);

/// <summary>Tablo başına sayaçlar.</summary>
public sealed record ImportBatchSummaryDto(
    ImportTableCountsDto Customers,
    ImportTableCountsDto Pets,
    ImportTableCountsDto Vaccinations);

/// <summary>Doğrulama tahmini (<c>Total..WillSkip</c>) ve uygulama sonucu (<c>Created..Failed</c>).</summary>
public sealed record ImportTableCountsDto(
    int Total,
    int Valid,
    int Warning,
    int Error,
    int WillCreate,
    int WillMatch,
    int WillSkip,
    int Created,
    int Matched,
    int Skipped,
    int Failed);

/// <summary>Parti oluşturma sonucu; <see cref="Created"/> false ise aynı <c>requestId</c> ile tekrar istek ilk partiyi döndürmüştür.</summary>
public sealed record ImportBatchCreateResultDto(bool Created, ImportBatchDto Batch);
