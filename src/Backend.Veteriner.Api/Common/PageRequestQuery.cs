using Backend.Veteriner.Application.Common.Models;

namespace Backend.Veteriner.Api.Common;

/// <summary>
/// Listelerde <see cref="PageRequest"/> parametre adı <c>page</c> olduğundan model binding varsayılanı
/// <c>page.search</c> önekidir; SPA/client çoğunlukla düz <c>search=</c> gönderir (Payments ile aynı).
/// Üst düzey <c>search</c> query parametresi boş değilse trimlenmiş değeri <see cref="PageRequest.Search"/> olarak kullanır
/// (<c>page.search</c> ile birlikte gönderilirse üst düzey <c>search</c> önceliklidir).
/// </summary>
internal static class PageRequestQuery
{
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
}
