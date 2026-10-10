using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Pets;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Pets.Specs;

internal static class PetDailySearchExtensions
{
    /// <summary>
    /// Her token hayvan alanlarında (ad, ırk, tür, mikroçip) geçmelidir; ya da hayvan, tüm token'larla eşleşen
    /// sahiplerin hayvanları arasında olmalıdır (<paramref name="petIdsMatchingClientTextOrEmpty"/>).
    /// Kiracı filtresi çağıran spec'te ayrıca uygulanır.
    /// </summary>
    public static void WhereDailySearch(
        this ISpecificationBuilder<Pet> query,
        DailySearchTerm term,
        Guid[] petIdsMatchingClientTextOrEmpty)
    {
        var ownerPets = petIdsMatchingClientTextOrEmpty;
        foreach (var token in term.Tokens)
        {
            var text = token.TextPattern;
            var chip = token.NumericPattern ?? text;
            query.Where(p =>
                EF.Functions.Like(
                    EF.Functions.Collate(p.Name.Replace("ı", "i").Replace("İ", "I"), DailySearchTerm.ColumnCollation),
                    text)
                || (p.Breed != null
                    && EF.Functions.Like(
                        EF.Functions.Collate(p.Breed.Replace("ı", "i").Replace("İ", "I"), DailySearchTerm.ColumnCollation),
                        text))
                || EF.Functions.Like(
                    EF.Functions.Collate(p.Species!.Name.Replace("ı", "i").Replace("İ", "I"), DailySearchTerm.ColumnCollation),
                    text)
                || (p.BreedRef != null
                    && EF.Functions.Like(
                        EF.Functions.Collate(p.BreedRef.Name.Replace("ı", "i").Replace("İ", "I"), DailySearchTerm.ColumnCollation),
                        text))
                || (p.MicrochipNumber != null && EF.Functions.Like(p.MicrochipNumber, chip))
                || (ownerPets.Length > 0 && ownerPets.Contains(p.Id)));
        }
    }
}
