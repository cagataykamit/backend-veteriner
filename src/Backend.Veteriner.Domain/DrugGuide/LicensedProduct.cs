using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.DrugGuide;

/// <summary>
/// Global, salt okunur ruhsatlı veteriner ürün kaydı (REF-001 Dilim 1, ADR-049 K2/K3). Yalnız olgu alanları tutulur:
/// ürün adı, etken maddeler, hedef türler, ruhsat sahibi, ruhsat numarası ve Ürün Özellikleri Özeti bağlantısı. Belge
/// metni saklanmaz. Kaynaktan düşen ürün silinmez, <see cref="IsListedInSource"/> ile işaretlenir.
/// </summary>
public sealed class LicensedProduct : AggregateRoot
{
    public const int NameMaxLength = 300;
    public const int LicenseHolderMaxLength = 300;
    public const int LicenseNumberMaxLength = 100;
    public const int SpcUrlMaxLength = 1000;
    public const int SourceMaxLength = 50;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = default!;
    public string NormalizedName { get; private set; } = default!;
    public string LicenseHolder { get; private set; } = default!;
    public string NormalizedLicenseHolder { get; private set; } = default!;

    /// <summary>Kaynakta yoksa boş (HBS sorgusu ruhsat numarası vermiyor, ADR-049 D2).</summary>
    public string? LicenseNumber { get; private set; }

    /// <summary>Bakanlık sayfasındaki onaylı Ürün Özellikleri Özeti belgesine bağlantı; metin kopyalanmaz.</summary>
    public string? SpcUrl { get; private set; }

    /// <summary>Verinin kaynağı (ör. "HBS").</summary>
    public string Source { get; private set; } = default!;

    public DateTime SourceSyncedAtUtc { get; private set; }

    /// <summary>Kaynak listede hâlâ var mı; düşen ürün "ruhsat listesinde artık yok" diye gösterilir.</summary>
    public bool IsListedInSource { get; private set; }

    private readonly List<LicensedProductIngredient> _ingredients = new();
    private readonly List<LicensedProductTargetSpecies> _targetSpecies = new();

    public IReadOnlyList<LicensedProductIngredient> Ingredients => _ingredients;
    public IReadOnlyList<LicensedProductTargetSpecies> TargetSpecies => _targetSpecies;

    private LicensedProduct() { }

    public LicensedProduct(
        string name,
        string licenseHolder,
        string? licenseNumber,
        string? spcUrl,
        string source,
        IReadOnlyList<Guid> activeIngredientIds,
        IReadOnlyList<LicensedProductTargetSpeciesDraft> targetSpecies,
        DateTime syncedAtUtc)
    {
        Require(name, NameMaxLength, "Ürün adı", nameof(name));
        Require(licenseHolder, LicenseHolderMaxLength, "Ruhsat sahibi", nameof(licenseHolder));
        Require(source, SourceMaxLength, "Kaynak", nameof(source));
        if (licenseNumber is not null && licenseNumber.Trim().Length > LicenseNumberMaxLength)
            throw new ArgumentException($"Ruhsat numarası en fazla {LicenseNumberMaxLength} karakter olabilir.", nameof(licenseNumber));
        if (spcUrl is not null && !IsHttpsUrl(spcUrl))
            throw new ArgumentException("Ürün Özellikleri Özeti bağlantısı https adresi olmalıdır.", nameof(spcUrl));
        if (activeIngredientIds.Count == 0 || activeIngredientIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("En az bir geçerli etken madde gerekir.", nameof(activeIngredientIds));
        if (activeIngredientIds.Distinct().Count() != activeIngredientIds.Count)
            throw new ArgumentException("Aynı etken madde iki kez verilemez.", nameof(activeIngredientIds));

        Name = name.Trim();
        NormalizedName = TurkishTextKey.Normalize(name);
        LicenseHolder = licenseHolder.Trim();
        NormalizedLicenseHolder = TurkishTextKey.Normalize(licenseHolder);
        LicenseNumber = string.IsNullOrWhiteSpace(licenseNumber) ? null : licenseNumber.Trim();
        SpcUrl = string.IsNullOrWhiteSpace(spcUrl) ? null : spcUrl.Trim();
        Source = source.Trim();
        SourceSyncedAtUtc = syncedAtUtc;
        IsListedInSource = true;

        for (var i = 0; i < activeIngredientIds.Count; i++)
            _ingredients.Add(new LicensedProductIngredient(Id, activeIngredientIds[i], i + 1));
        foreach (var species in targetSpecies)
            _targetSpecies.Add(new LicensedProductTargetSpecies(Id, species));
    }

    /// <summary>
    /// Hastanın türü ruhsattaki hedef türler arasında mı. Hedef türlerden hiçbiri bizim tür listemize eşlenmemişse
    /// bilinmez (<c>null</c>); not gösterilmez. Sonuç engellemez: ruhsat dışı kullanım hekimin kararıdır (ADR-049 K4).
    /// </summary>
    public bool? IsLicensedFor(Guid speciesId)
    {
        var mapped = _targetSpecies.Where(t => t.SpeciesId.HasValue).ToList();
        if (mapped.Count == 0)
            return null;
        return mapped.Any(t => t.SpeciesId == speciesId);
    }

    /// <summary>Kaynak eşitlemesinde listede bulunmayan ürün silinmez, işaretlenir (ADR-049 K3).</summary>
    public void MarkDelistedFromSource(DateTime syncedAtUtc)
    {
        IsListedInSource = false;
        SourceSyncedAtUtc = syncedAtUtc;
    }

    private static void Require(string value, int maxLength, string label, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{label} boş olamaz.", paramName);
        if (value.Trim().Length > maxLength)
            throw new ArgumentException($"{label} en fazla {maxLength} karakter olabilir.", paramName);
    }

    private static bool IsHttpsUrl(string value)
        => value.Trim().Length <= SpcUrlMaxLength
           && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps;
}

/// <summary>Ürün ile etken madde bağı; sıra kaynaktaki yazım sırasıdır.</summary>
public sealed class LicensedProductIngredient
{
    public Guid LicensedProductId { get; private set; }
    public Guid ActiveIngredientId { get; private set; }
    public int SortOrder { get; private set; }
    public ActiveIngredient? Ingredient { get; private set; }

    private LicensedProductIngredient() { }

    internal LicensedProductIngredient(Guid licensedProductId, Guid activeIngredientId, int sortOrder)
    {
        LicensedProductId = licensedProductId;
        ActiveIngredientId = activeIngredientId;
        SortOrder = sortOrder;
    }
}

/// <summary>
/// Ruhsattaki hedef tür: kaynaktaki yazım (<see cref="Label"/>) ve eşlenebildiyse bizim tür kaydımız
/// (<see cref="SpeciesId"/>). Eşlenemeyen tür (ör. arı) yalnız etiketle gösterilir.
/// </summary>
public sealed class LicensedProductTargetSpecies
{
    public const int LabelMaxLength = 100;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid LicensedProductId { get; private set; }
    public string Label { get; private set; } = default!;
    public Guid? SpeciesId { get; private set; }

    private LicensedProductTargetSpecies() { }

    internal LicensedProductTargetSpecies(Guid licensedProductId, LicensedProductTargetSpeciesDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Label) || draft.Label.Trim().Length > LabelMaxLength)
            throw new ArgumentException($"Hedef tür adı 1–{LabelMaxLength} karakter olmalıdır.", nameof(draft));

        LicensedProductId = licensedProductId;
        Label = draft.Label.Trim();
        SpeciesId = draft.SpeciesId;
    }
}

public sealed record LicensedProductTargetSpeciesDraft(string Label, Guid? SpeciesId);
