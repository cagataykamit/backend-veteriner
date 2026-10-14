using System.Linq.Expressions;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Pets;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>
/// <see cref="Pet.NormalizeNameForDuplicateCheck"/> anahtarının veritabanı tarafı (müşteri adındaki
/// <c>ClientFullNameDuplicateKey</c> ile aynı kural): ad kolonu kırpılır, boşluk tekleştirilir, ı/İ katlanır ve
/// <see cref="DailySearchTerm.ColumnCollation"/> (büyük/küçük harf + aksan duyarsız) ile karşılaştırılır.
/// </summary>
public static class PetNameDuplicateKey
{
    public static Expression<Func<Pet, bool>> Matches(string duplicateKey)
        => p => EF.Functions.Collate(
                    p.Name.Trim().Replace("ı", "i").Replace("İ", "I").Replace("  ", " ").Replace("  ", " "),
                    DailySearchTerm.ColumnCollation)
                == duplicateKey;
}
