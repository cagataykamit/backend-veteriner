using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Contracts.Dtos;

/// <summary>
/// Hayvanın kalıcı uyarıları: bayrak adları (sabit sırada) ve tek kısa not. Uyarı yoksa boş liste ve null not;
/// yanıtlarda hiçbir zaman null değildir.
/// </summary>
public sealed record PetAlertsDto(IReadOnlyList<string> Flags, string? Note)
{
    public static PetAlertsDto From(PetAlertFlags flags, string? note)
        => new(PetAlertFlagNames.ToNames(flags), flags == PetAlertFlags.None ? null : note);
}
