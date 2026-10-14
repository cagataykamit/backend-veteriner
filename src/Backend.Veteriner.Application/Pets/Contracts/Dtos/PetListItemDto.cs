using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Contracts.Dtos;

/// <summary>
/// Hayvan liste satırı. <see cref="Status"/> komut veritabanından gelir (Query DB okuma modelinde durum kolonu yoktur;
/// okuma modeli yolunda işleyici sayfadaki satırların durumunu ayrıca doldurur).
/// </summary>
public sealed record PetListItemDto(
    Guid Id,
    Guid TenantId,
    Guid ClientId,
    string Name,
    Guid SpeciesId,
    string SpeciesName,
    Guid? ColorId,
    string? ColorName,
    string? Breed,
    decimal? Weight,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetStatus Status = PetStatus.Active);
