using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Hbs;

namespace Backend.Veteriner.Application.Hbs.Contracts;

public enum HbsRecordKind
{
    Prescription = 0,
    Vaccination = 1
}

/// <summary>
/// Kaydın HBS geri kaydı. <see cref="IsTracked"/>: kayıt HBS takibi kapsamında mı (takip dışı aşıda arayüz rozet göstermez).
/// </summary>
public sealed record HbsEntryDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] HbsEntryStatus Status,
    string? ReferenceNumber,
    string? NotRequiredReason,
    DateTime? MarkedAtUtc,
    Guid? MarkedByUserId,
    string? MarkedByName,
    bool IsTracked);

/// <summary>HBS ekranında girilecek tek alan; <see cref="Value"/> kopyalanacak metindir (boşsa null, satır yine döner).</summary>
public sealed record HbsDataFieldDto(string Key, string Label, string? Value);

public sealed record HbsDataSectionDto(string Key, string Title, IReadOnlyList<HbsDataFieldDto> Fields);

/// <summary>HBS'e girilecek verinin hazır dizisi (sözleşme: HBS_ASSISTANT_API_CONTRACT.md bölüm 4).</summary>
public sealed record HbsDataDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] HbsRecordKind RecordKind,
    Guid RecordId,
    HbsEntryDto Hbs,
    bool OwnerIdentityMasked,
    IReadOnlyList<HbsDataSectionDto> Sections,
    string CopyAllText);
