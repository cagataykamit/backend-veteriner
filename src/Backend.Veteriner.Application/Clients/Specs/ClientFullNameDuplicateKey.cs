using System.Linq.Expressions;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Clients;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Clients.Specs;

/// <summary>
/// <see cref="Client.NormalizeFullNameForDuplicateCheck"/> anahtarının veritabanı tarafı: ad kolonu kırpılır, boşluk
/// tekleştirilir, ı/İ katlanır ve <see cref="DailySearchTerm.ColumnCollation"/> (büyük/küçük harf + aksan duyarsız) ile
/// karşılaştırılır. Anahtar tarafı her zaman <see cref="Client.NormalizeFullNameForDuplicateCheck"/> çıktısıdır.
/// </summary>
public static class ClientFullNameDuplicateKey
{
    public static Expression<Func<Client, bool>> Matches(string duplicateKey)
        => c => EF.Functions.Collate(
                    c.FullName.Trim().Replace("ı", "i").Replace("İ", "I").Replace("  ", " ").Replace("  ", " "),
                    DailySearchTerm.ColumnCollation)
                == duplicateKey;
}
