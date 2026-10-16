using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

/// <summary>
/// KVKK rıza durumu değişikliği yanıtı (ADR-021); alan adları <see cref="ClientDetailDto"/> ile aynıdır.
/// <see cref="RowVersion"/> sonraki yazım için güncel sürümdür.
/// </summary>
/// <param name="RemindersOptedOut">Yazımdan sonraki hatırlatma istemiyor bayrağı; Ret bunu açar (ADR-025).</param>
public sealed record ClientKvkkConsentDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] KvkkConsentStatus KvkkConsentStatus,
    DateOnly? KvkkConsentRecordedOn,
    Guid? KvkkConsentRecordedByUserId,
    string? KvkkConsentRecordedByName,
    bool RemindersOptedOut,
    string RowVersion)
{
    public static ClientKvkkConsentDto From(Client client, string? recordedByName)
        => new(
            client.Id,
            client.KvkkConsentStatus,
            client.KvkkConsentRecordedOn,
            client.KvkkConsentRecordedByUserId,
            recordedByName,
            client.RemindersOptedOut,
            ExaminationRowVersion.Encode(client.RowVersion));
}
