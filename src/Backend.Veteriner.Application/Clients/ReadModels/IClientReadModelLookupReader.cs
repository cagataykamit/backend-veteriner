namespace Backend.Veteriner.Application.Clients.ReadModels;

/// <summary>
/// Client read-model üzerinden paylaşılan arama/lookup (12D-2). Liste reader'dan ayrı tutulur;
/// handler routing 12D-3'te bu interface üzerinden yapılacaktır.
/// </summary>
public interface IClientReadModelLookupReader
{
    /// <summary>
    /// Kiracı içinde metin aramasına uyan client id kümesi. <paramref name="request.SearchContainsLikePattern"/>
    /// null/boş ise boş küme döner (command path arama yokken lookup çağrılmaz).
    /// </summary>
    Task<ClientTextSearchLookupResult> ResolveClientIdsByTextSearchAsync(
        ClientTextSearchLookupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Toplu client id → FullName; yalnız istenen tenant satırları.</summary>
    Task<ClientNamesLookupResult> GetNamesByIdsAsync(
        ClientNamesLookupRequest request,
        CancellationToken cancellationToken = default);
}
