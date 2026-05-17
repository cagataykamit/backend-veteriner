using Backend.Veteriner.Application.Common.Models;
using Microsoft.AspNetCore.Http;

namespace Backend.Veteriner.Api.Common;

/// <summary>
/// Listelerde <see cref="PageRequest"/> parametre adı <c>page</c> olduğundan model binding varsayılanı
/// <c>page.page</c> / <c>page.pageSize</c> önekidir; SPA/client çoğunlukla düz <c>page</c> / <c>pageSize</c> gönderir.
/// <see cref="BindFromQuery"/> her iki biçimi de okur (düz query önceliklidir).
/// Üst düzey <c>search</c> query parametresi boş değilse trimlenmiş değeri <see cref="PageRequest.Search"/> olarak kullanır
/// (<c>page.search</c> ile birlikte gönderilirse üst düzey <c>search</c> önceliklidir).
/// </summary>
internal static class PageRequestQuery
{
    /// <summary>
    /// Düz (<c>?page=2&amp;pageSize=20</c>) ve nested (<c>?page.page=2</c>) sayfalama query değerlerini birleştirir.
    /// </summary>
    public static PageRequest BindFromQuery(IQueryCollection query, PageRequest? nested = null)
    {
        nested ??= new PageRequest();

        return new PageRequest
        {
            Page = ReadInt(query, nested.Page, "page", "Page", "page.page", "page.Page"),
            PageSize = ReadInt(query, nested.PageSize, "pageSize", "PageSize", "page.pageSize", "page.PageSize"),
            Sort = ReadString(query, nested.Sort, "sort", "Sort", "page.sort", "page.Sort"),
            Order = ReadString(query, nested.Order, "order", "Order", "page.order", "page.Order"),
            Search = ReadString(query, nested.Search, "search", "Search", "page.search", "page.Search"),
        };
    }

    public static PageRequest WithMergedSearch(PageRequest page, string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
            return page;

        return new PageRequest
        {
            Page = page.Page,
            PageSize = page.PageSize,
            Sort = page.Sort,
            Order = page.Order,
            Search = searchQuery.Trim()
        };
    }

    private static int ReadInt(IQueryCollection query, int fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!query.TryGetValue(key, out var values))
                continue;

            var raw = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var parsed) && parsed > 0)
                return parsed;
        }

        return fallback;
    }

    private static string? ReadString(IQueryCollection query, string? fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!query.TryGetValue(key, out var values))
                continue;

            var raw = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw))
                return raw.Trim();
        }

        return fallback;
    }
}
