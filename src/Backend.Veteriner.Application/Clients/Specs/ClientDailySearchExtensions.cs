using System.Linq.Expressions;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Clients;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Clients.Specs;

public static class ClientDailySearchExtensions
{
    /// <summary>
    /// Her token en az bir alanda (ad, e-posta, telefon) geçmelidir; token'lar arası sıra önemsizdir.
    /// Ad kolonu ı/İ katlanır ve <see cref="DailySearchTerm.ColumnCollation"/> ile karşılaştırılır.
    /// Kiracı filtresi çağıran spec'te ayrıca uygulanır.
    /// </summary>
    public static void WhereDailySearch(this ISpecificationBuilder<Client> query, DailySearchTerm term)
    {
        foreach (var token in term.Tokens)
            query.Where(MatchesToken(token));
    }

    /// <summary><see cref="WhereDailySearch(ISpecificationBuilder{Client}, DailySearchTerm)"/> ile aynı kural; spec dışı sorgular (raporlar) için.</summary>
    public static IQueryable<Client> WhereDailySearch(this IQueryable<Client> query, DailySearchTerm term)
        => term.Tokens.Aggregate(query, (q, token) => q.Where(MatchesToken(token)));

    private static Expression<Func<Client, bool>> MatchesToken(DailySearchTerm.Token token)
    {
        var text = token.TextPattern;
        var phone = token.NumericPattern ?? text;
        return c =>
            EF.Functions.Like(
                EF.Functions.Collate(c.FullName.Replace("ı", "i").Replace("İ", "I"), DailySearchTerm.ColumnCollation),
                text)
            || (c.Email != null && EF.Functions.Like(c.Email, text))
            || (c.Phone != null && EF.Functions.Like(c.Phone, phone))
            || (c.PhoneNormalized != null && EF.Functions.Like(c.PhoneNormalized, phone));
    }
}
