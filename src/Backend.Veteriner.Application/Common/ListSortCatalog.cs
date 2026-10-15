using Backend.Veteriner.Application.Common.Models;

namespace Backend.Veteriner.Application.Common;

/// <summary>Listede istenen sıralama: beyaz listeden alan ve yön.</summary>
public readonly record struct ListSortChoice<TField>(TField Field, bool Descending)
    where TField : struct, Enum;

/// <summary>
/// Liste <c>sort</c> beyaz listesi (büyük/küçük harf duyarsız). Validator bilinmeyen alanı 400 ile reddeder;
/// <c>sort</c> boşsa <see cref="Resolve"/> <c>null</c> döner ve liste kendi varsayılan sırasını kullanır (<c>order</c> yok sayılır).
/// </summary>
public sealed class ListSortCatalog<TField>
    where TField : struct, Enum
{
    private readonly Dictionary<string, TField> _fields;

    /// <param name="fields">İzinli sort anahtarları ve eşlendikleri alan; eski adlar (alias) aynı alana eşlenebilir.</param>
    public ListSortCatalog(params (string Key, TField Field)[] fields)
    {
        _fields = fields.ToDictionary(x => x.Key, x => x.Field, StringComparer.OrdinalIgnoreCase);
        InvalidSortMessage = $"sort yalnızca şu değerlerden biri olabilir (veya boş): {string.Join(", ", fields.Select(x => x.Key))}.";
    }

    public string InvalidSortMessage { get; }

    public bool IsAllowedSort(string? sort)
        => string.IsNullOrWhiteSpace(sort) || _fields.ContainsKey(sort.Trim());

    /// <summary>Doğrulanmış istekten sıralama seçimi; <c>sort</c> boşsa <c>null</c> (liste varsayılanı).</summary>
    public ListSortChoice<TField>? Resolve(PageRequest page)
    {
        var sort = page.Sort?.Trim();
        if (string.IsNullOrEmpty(sort))
            return null;

        if (!_fields.TryGetValue(sort, out var field))
            throw new InvalidOperationException("Sort alanı doğrulanmış olmalıdır.");

        return new ListSortChoice<TField>(field, ListSortOrder.IsDescending(page.Order));
    }
}

/// <summary>Liste <c>order</c> değeri: <c>asc</c> | <c>desc</c> (büyük/küçük harf duyarsız); boşsa azalan.</summary>
public static class ListSortOrder
{
    public const string InvalidMessage = "order asc veya desc olmalıdır.";

    public static bool IsAllowed(string? order)
        => string.IsNullOrWhiteSpace(order)
            || string.Equals(order.Trim(), "asc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(order.Trim(), "desc", StringComparison.OrdinalIgnoreCase);

    public static bool IsDescending(string? order)
        => string.IsNullOrWhiteSpace(order)
            || string.Equals(order.Trim(), "desc", StringComparison.OrdinalIgnoreCase);
}
