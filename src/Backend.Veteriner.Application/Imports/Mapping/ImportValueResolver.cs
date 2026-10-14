namespace Backend.Veteriner.Application.Imports.Mapping;

/// <summary>
/// Katalog kaydı. <see cref="ParentId"/>: ırk için tür, aşı tanımı için tür (null: her tür), diğerlerinde boş.
/// </summary>
public sealed record ImportCatalogEntry(Guid Id, string Name, string? Code, Guid? ParentId);

/// <summary>Eşleme anında kiracıya görünür, <b>aktif</b> katalog kayıtları.</summary>
public sealed record ImportCatalogs(
    IReadOnlyList<ImportCatalogEntry> Species,
    IReadOnlyList<ImportCatalogEntry> Breeds,
    IReadOnlyList<ImportCatalogEntry> Colors,
    IReadOnlyList<ImportCatalogEntry> Vaccines)
{
    public IReadOnlyList<ImportCatalogEntry> Of(ImportValueKind kind) => kind switch
    {
        ImportValueKind.Species => Species,
        ImportValueKind.Breed => Breeds,
        ImportValueKind.Color => Colors,
        ImportValueKind.Vaccine => Vaccines,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

public enum ImportValueResolutionKind
{
    /// <summary>Katalog kaydına eşlendi.</summary>
    Resolved = 1,

    /// <summary>Kullanıcı bilinçli atladı (<c>targetId = null</c>).</summary>
    Skipped = 2,

    /// <summary>Eşleşme yok veya belirsiz; kullanıcı eşlemeli (yeni tanım oluşturulmaz, ADR-019 K6).</summary>
    Unmapped = 3,

    /// <summary>Kaynak değer boş.</summary>
    Empty = 4,
}

public readonly record struct ImportValueResolution(ImportValueResolutionKind Kind, Guid? TargetId)
{
    public static readonly ImportValueResolution Empty = new(ImportValueResolutionKind.Empty, null);
    public static readonly ImportValueResolution Unmapped = new(ImportValueResolutionKind.Unmapped, null);
    public static readonly ImportValueResolution Skipped = new(ImportValueResolutionKind.Skipped, null);
    public static ImportValueResolution Resolved(Guid id) => new(ImportValueResolutionKind.Resolved, id);
}

/// <summary>
/// Tür/ırk/aşı/renk adlarını katalog kaydına çözer (IMPORT_API_CONTRACT.md §5). Sıra: kullanıcı eşlemesi →
/// katalog ad/kod (katlanmış) → yerleşik takma ad → eşlenmemiş. Hiçbir zaman katalog kaydı oluşturmaz.
/// </summary>
public sealed class ImportValueResolver
{
    /// <summary>Katalog adlarının dışında kalan yaygın yazımlar (katlanmış anahtar → katlanmış katalog adı).</summary>
    private static readonly Dictionary<ImportValueKind, Dictionary<string, string>> BuiltInAliases = new()
    {
        [ImportValueKind.Species] = new()
        {
            ["dog"] = "kopek",
            ["cat"] = "kedi",
            ["bird"] = "kus",
            ["rabbit"] = "tavsan",
        },
        [ImportValueKind.Vaccine] = new()
        {
            ["rabies"] = "kuduz",
            ["kuduzasisi"] = "kuduz",
            ["mixed"] = "karma",
            ["karmaasisi"] = "karma",
            ["bronchine"] = "bronsin",
            ["leukemia"] = "losemi",
        },
    };

    private readonly Dictionary<ImportValueKind, ILookup<string, ImportCatalogEntry>> _byKey = new();
    private readonly Dictionary<(ImportValueKind Kind, string Key), Guid?> _userMappings = new();

    public ImportValueResolver(ImportCatalogs catalogs, IEnumerable<ImportValueMapping> userMappings)
    {
        foreach (var kind in Enum.GetValues<ImportValueKind>())
        {
            _byKey[kind] = catalogs.Of(kind)
                .SelectMany(e => new[] { ImportTextFolding.Fold(e.Name), ImportTextFolding.Fold(e.Code) }
                    .Where(k => k.Length > 0)
                    .Distinct()
                    .Select(k => (Key: k, Entry: e)))
                .ToLookup(x => x.Key, x => x.Entry);
        }

        foreach (var mapping in userMappings)
        {
            if (ImportValueKinds.TryParse(mapping.Kind, out var kind))
                _userMappings[(kind, ImportTextFolding.Fold(mapping.Source))] = mapping.TargetId;
        }
    }

    /// <param name="parentId">
    /// Irk için hayvanın türü (zorunlu: türsüz ırk eşlenmez); aşı için hastanın türü (tanımın türü doluysa aynı olmalı).
    /// </param>
    public ImportValueResolution Resolve(ImportValueKind kind, string? source, Guid? parentId = null)
    {
        var key = ImportTextFolding.Fold(source);
        if (key.Length == 0)
            return ImportValueResolution.Empty;

        if (_userMappings.TryGetValue((kind, key), out var target))
            return target is { } id ? ImportValueResolution.Resolved(id) : ImportValueResolution.Skipped;

        if (TryUnique(kind, key, parentId, out var resolved))
            return resolved;

        if (BuiltInAliases.TryGetValue(kind, out var aliases)
            && aliases.TryGetValue(key, out var aliasTarget)
            && TryUnique(kind, aliasTarget, parentId, out resolved))
        {
            return resolved;
        }

        return ImportValueResolution.Unmapped;
    }

    private bool TryUnique(ImportValueKind kind, string key, Guid? parentId, out ImportValueResolution resolution)
    {
        var candidates = _byKey[kind][key]
            .Where(e => InScope(kind, e, parentId))
            .DistinctBy(e => e.Id)
            .Take(2)
            .ToList();

        // Belirsiz (birden fazla kayıt) otomatik eşlenmez; kullanıcı seçer.
        if (candidates.Count == 1)
        {
            resolution = ImportValueResolution.Resolved(candidates[0].Id);
            return true;
        }

        resolution = ImportValueResolution.Unmapped;
        return false;
    }

    private static bool InScope(ImportValueKind kind, ImportCatalogEntry entry, Guid? parentId) => kind switch
    {
        ImportValueKind.Breed => parentId is not null && entry.ParentId == parentId,
        ImportValueKind.Vaccine => entry.ParentId is null || parentId is null || entry.ParentId == parentId,
        _ => true,
    };
}
