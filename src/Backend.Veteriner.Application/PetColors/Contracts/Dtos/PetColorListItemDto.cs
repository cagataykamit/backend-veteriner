using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.PetColors.Contracts.Dtos;

/// <summary>Kürk kataloğu satırı; <see cref="Kind"/> renk mi desen mi olduğunu söyler (hasta kaydında ayrı alanlar).</summary>
public sealed record PetColorListItemDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    int DisplayOrder,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetColorKind Kind = PetColorKind.Color);
