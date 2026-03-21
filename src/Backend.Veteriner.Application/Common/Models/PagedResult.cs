namespace Backend.Veteriner.Application.Common.Models;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages
)
{
    public static PagedResult<T> Create(IReadOnlyList<T> items, int totalItems, int page, int pageSize)
    {
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        return new(items, page, pageSize, totalItems, totalPages);
    }
}