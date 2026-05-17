namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Species/breed katalog liste cache invalidation.
/// Species değişikliği breed listelerini de etkileyebileceği için <see cref="InvalidateSpecies"/> breeds cache'ini de temizler.
/// </summary>
public interface ICatalogCacheInvalidator
{
    void InvalidateSpecies();

    void InvalidateBreeds();
}
