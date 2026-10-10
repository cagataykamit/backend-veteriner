using System.Linq.Expressions;
using Ardalis.Specification;

namespace Backend.Veteriner.Application.Pets.Timeline;

/// <summary>Bir kaynak için tarih+id sıralı, imleçli (keyset) sayfa sorgusu.</summary>
internal sealed class PetTimelineSpec<T> : Specification<T> where T : class
{
    public PetTimelineSpec(
        Expression<Func<T, bool>> filter,
        Func<ISpecificationBuilder<T>, ISpecificationBuilder<T>> applyClinicScope,
        Expression<Func<T, DateTime>> eventAt,
        Expression<Func<T, Guid>> id,
        int typeOrder,
        PetTimelineSourceQuery query)
    {
        Query.Where(filter);
        applyClinicScope(Query);

        if (query.FromUtc is { } from)
            Query.Where(PetTimelineKeyset.AtOrAfter(eventAt, from));
        if (query.ToUtc is { } to)
            Query.Where(PetTimelineKeyset.Before(eventAt, to));
        if (query.Cursor is { } cursor)
            Query.Where(PetTimelineKeyset.BeforeCursor(eventAt, id, typeOrder, cursor));

        Query.OrderByDescending(Boxed(eventAt))
            .ThenByDescending(Boxed(id))
            .Take(query.Take)
            .AsNoTracking();
    }

    // Ardalis sıralama imzası Func<T, object?> bekler; derleyicinin ürettiği Convert düğümü elle eklenir.
    private static Expression<Func<T, object?>> Boxed<TKey>(Expression<Func<T, TKey>> selector)
        => Expression.Lambda<Func<T, object?>>(Expression.Convert(selector.Body, typeof(object)), selector.Parameters);
}
