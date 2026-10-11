using System.Linq.Expressions;
using System.Reflection;

namespace Backend.Veteriner.Application.Pets.Timeline;

/// <summary>Tarih aralığı ve imleç koşullarını (EF'in çevirebileceği) ifade ağacı olarak üretir.</summary>
internal static class PetTimelineKeyset
{
    private static readonly MethodInfo GuidCompareTo =
        typeof(Guid).GetMethod(nameof(Guid.CompareTo), [typeof(Guid)])!;

    /// <summary>
    /// <c>(date, typeOrder, id)</c> sıralamasında imlecten <b>kesin önce</b> gelen satırlar.
    /// Aynı <c>typeOrder</c> yalnız tek kaynakta bulunduğundan id karşılaştırması yalnızca o kaynakta gerekir.
    /// </summary>
    public static Expression<Func<T, bool>> BeforeCursor<T>(
        Expression<Func<T, DateTime>> date,
        Expression<Func<T, Guid>> id,
        int typeOrder,
        PetTimelineCursor cursor)
    {
        var p = date.Parameters[0];
        var idBody = new ReplaceParameter(id.Parameters[0], p).Visit(id.Body)!;
        var cursorDate = Closure(cursor.EventAtUtc);

        Expression body;
        if (typeOrder < cursor.TypeOrder)
        {
            body = Expression.LessThanOrEqual(date.Body, cursorDate);
        }
        else if (typeOrder > cursor.TypeOrder)
        {
            body = Expression.LessThan(date.Body, cursorDate);
        }
        else
        {
            var idBefore = Expression.LessThan(
                Expression.Call(idBody, GuidCompareTo, Closure(cursor.SourceId)),
                Expression.Constant(0));
            body = Expression.OrElse(
                Expression.LessThan(date.Body, cursorDate),
                Expression.AndAlso(Expression.Equal(date.Body, cursorDate), idBefore));
        }

        return Expression.Lambda<Func<T, bool>>(body, p);
    }

    public static Expression<Func<T, bool>> AtOrAfter<T>(Expression<Func<T, DateTime>> date, DateTime fromUtc)
        => Expression.Lambda<Func<T, bool>>(
            Expression.GreaterThanOrEqual(date.Body, Closure(fromUtc)),
            date.Parameters[0]);

    public static Expression<Func<T, bool>> Before<T>(Expression<Func<T, DateTime>> date, DateTime toUtc)
        => Expression.Lambda<Func<T, bool>>(
            Expression.LessThan(date.Body, Closure(toUtc)),
            date.Parameters[0]);

    // Sabit yerine closure alanı: EF değeri parametre olarak gönderir (sorgu planı paylaşılır).
    private static MemberExpression Closure<TValue>(TValue value)
        => Expression.Field(Expression.Constant(new Box<TValue>(value)), nameof(Box<TValue>.Value));

    private sealed class Box<TValue>(TValue value)
    {
        public readonly TValue Value = value;
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
