using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Contracts.Dtos;

/// <summary>Durum değişikliği yanıtı; <see cref="RowVersion"/> sonraki yazım için güncel sürümdür.</summary>
public sealed record PetStatusDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetStatus Status,
    DateOnly? DeceasedOn,
    string RowVersion)
{
    public static PetStatusDto From(Pet pet)
        => new(pet.Id, pet.Status, pet.DeceasedOn, ExaminationRowVersion.Encode(pet.RowVersion));
}
