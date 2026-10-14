using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Contracts.Dtos;

/// <summary>POST .../commit sonucu; <see cref="Started"/> false ise aynı <c>validationId</c> ile tekrar istektir (200).</summary>
public sealed record ImportCommitResultDto(bool Started, ImportBatchDto Batch);

/// <summary>GET .../progress (IMPORT_API_CONTRACT.md §7.14): hafif yoklama yanıtı.</summary>
public sealed record ImportProgressDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ImportBatchStatus Status,
    string? Phase,
    int Processed,
    int Total,
    DateTime? UpdatedAtUtc,
    DateTime? CommittedAtUtc,
    bool LeaseActive);

/// <summary>POST .../release-reminders sonucu; <see cref="Note"/> yalnız kiracı aşı hatırlatması kapalıysa dolu.</summary>
public sealed record ImportReleaseRemindersResultDto(
    ImportBatchDto Batch,
    ImportReminderImpactDto ReminderImpact,
    bool AlreadyReleased,
    string? Note);
