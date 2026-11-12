using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.PetColors.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Pets.Commands;

/// <summary>
/// Hasta kaydının kürk alanları (oluşturma ve güncelleme aynı kural): renk alanına yalnız aktif renk,
/// desen alanına yalnız aktif desen satırı bağlanır (<see cref="PetColorKind"/>).
/// Katalogdan kaldırılan satır hastada zaten kayıtlıysa güncellemede değişmeden kalabilir.
/// </summary>
public static class PetCoatResolver
{
    public sealed record Coat(PetColor? Color, PetColor? Pattern);

    public static async Task<Result<Coat>> ResolveAsync(
        IReadRepository<PetColor> colors,
        Guid? colorId,
        Guid? patternId,
        CancellationToken ct,
        Guid? currentColorId = null,
        Guid? currentPatternId = null)
    {
        PetColor? color = null;
        if (colorId is { } cid)
        {
            color = await colors.FirstOrDefaultAsync(new PetColorByIdSpec(cid), ct);
            if (color is null || (!color.IsActive && cid != currentColorId))
                return Result<Coat>.Failure("Pets.ColorNotFound", "Renk bulunamadı veya pasif; geçerli bir ColorId gönderin.");
            if (color.Kind != PetColorKind.Color)
                return Result<Coat>.Failure("Pets.ColorIsPattern", "Seçilen kayıt bir desen; desen alanından seçin.");
        }

        PetColor? pattern = null;
        if (patternId is { } pid)
        {
            pattern = await colors.FirstOrDefaultAsync(new PetColorByIdSpec(pid), ct);
            if (pattern is null || (!pattern.IsActive && pid != currentPatternId))
                return Result<Coat>.Failure("Pets.PatternNotFound", "Desen bulunamadı veya pasif; geçerli bir PatternId gönderin.");
            if (pattern.Kind != PetColorKind.Pattern)
                return Result<Coat>.Failure("Pets.PatternIsColor", "Seçilen kayıt bir renk; renk alanından seçin.");
        }

        return Result<Coat>.Success(new Coat(color, pattern));
    }
}
