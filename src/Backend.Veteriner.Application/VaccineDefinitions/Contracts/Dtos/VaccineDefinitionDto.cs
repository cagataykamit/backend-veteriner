using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.VaccineDefinitions.Contracts.Dtos;

/// <summary>
/// Aşı tanımı. <see cref="IsCore"/> sistem satırı kilidi (kiracı değiştiremez); klinik sınıf <see cref="Category"/>
/// (WSAVA çekirdek / çekirdek dışı / önerilmeyen ya da antiparaziter; kiracı tanımında boş olabilir).
/// </summary>
public sealed record VaccineDefinitionDto(
    Guid Id,
    Guid? TenantId,
    Guid? SpeciesId,
    string Name,
    string Code,
    string? Description,
    int? DefaultNextDueDays,
    bool IsCore,
    bool IsActive,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] VaccineCategory? Category = null);
