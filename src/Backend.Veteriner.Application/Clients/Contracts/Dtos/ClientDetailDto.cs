using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

/// <param name="PreferredContactChannel">Tercih edilen kanal (yalnız bilgi); <c>None</c> = tercih yok.</param>
/// <param name="RemindersOptedOut">true ise otomatik aşı/randevu hatırlatması gönderilmez.</param>
public sealed record ClientDetailDto(
    Guid Id,
    Guid TenantId,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string FullName,
    string? Email,
    string? Phone,
    string? Address,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ContactChannel PreferredContactChannel,
    bool RemindersOptedOut);
