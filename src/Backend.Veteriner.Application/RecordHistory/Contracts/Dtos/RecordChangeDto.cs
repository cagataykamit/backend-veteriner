using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.RecordHistory;

namespace Backend.Veteriner.Application.RecordHistory.Contracts.Dtos;

/// <summary>Kayıt değişiklik geçmişi satırı (RECORD-002). <see cref="Changes"/> yalnız <c>Updated</c> için doludur.</summary>
public sealed record RecordChangeDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] RecordChangeKind Kind,
    DateTime OccurredAtUtc,
    Guid ActorUserId,
    string? ActorName,
    IReadOnlyList<RecordFieldChangeDto> Changes);

public sealed record RecordFieldChangeDto(string Field, string? OldValue, string? NewValue);
