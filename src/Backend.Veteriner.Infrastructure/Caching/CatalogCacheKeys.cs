using System.Security.Cryptography;
using System.Text;

namespace Backend.Veteriner.Infrastructure.Caching;

internal static class CatalogCacheKeys
{
    internal static readonly TimeSpan ListTtl = TimeSpan.FromMinutes(30);

    public static string SpeciesList(bool? isActive, int page, int pageSize)
        => $"catalog:species:list:isActive:{FormatIsActive(isActive)}:p:{page}:ps:{pageSize}";

    public static string BreedsList(
        bool? isActive,
        Guid? speciesId,
        string? searchTermLower,
        int page,
        int pageSize)
        => $"catalog:breeds:list:isActive:{FormatIsActive(isActive)}:species:{FormatSpeciesId(speciesId)}:search:{FormatSearch(searchTermLower)}:p:{page}:ps:{pageSize}";

    internal static string FormatIsActive(bool? isActive)
        => isActive switch
        {
            null => "all",
            true => "true",
            false => "false"
        };

    internal static string FormatSpeciesId(Guid? speciesId)
        => speciesId.HasValue ? speciesId.Value.ToString("N") : "all";

    internal static string FormatSearch(string? searchTermLower)
    {
        if (string.IsNullOrEmpty(searchTermLower))
            return "none";

        if (searchTermLower.Length <= 32)
            return searchTermLower;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(searchTermLower));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }
}
