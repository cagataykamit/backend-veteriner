using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

/// <summary>
/// KVKK rıza durumu değişikliği yanıtı (ADR-021); alan adları <see cref="ClientDetailDto"/> ile aynıdır.
/// <see cref="RowVersion"/> sonraki yazım için güncel sürümdür.
/// </summary>
public sealed record ClientKvkkConsentDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] KvkkConsentStatus KvkkConsentStatus,
    DateOnly? KvkkConsentRecordedOn,
    Guid? KvkkConsentRecordedByUserId,
    string? KvkkConsentRecordedByName,
    string RowVersion)
{
    public static ClientKvkkConsentDto From(Client client, string? recordedByName)
        => new(
            client.Id,
            client.KvkkConsentStatus,
            client.KvkkConsentRecordedOn,
            client.KvkkConsentRecordedByUserId,
            recordedByName,
            ExaminationRowVersion.Encode(client.RowVersion));
}
