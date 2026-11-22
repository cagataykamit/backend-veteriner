using Backend.Veteriner.Domain.DrugGuide;

namespace Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;

public sealed record ActiveIngredientRefDto(Guid Id, string Name);

/// <summary>Ruhsatlı ürün kaydı (yalnız olgu alanları; LICENSED_DRUG_GUIDE_API_CONTRACT.md §3).</summary>
public sealed record LicensedProductDto(
    Guid Id,
    string Name,
    IReadOnlyList<ActiveIngredientRefDto> ActiveIngredients,
    IReadOnlyList<string> TargetSpecies,
    string LicenseHolder,
    string? LicenseNumber,
    string? SpcUrl,
    bool IsListedInSource,
    string Source,
    DateTime SourceSyncedAtUtc)
{
    /// <summary>Etken madde ve hedef türler yüklenmiş olmalıdır.</summary>
    public static LicensedProductDto From(LicensedProduct p)
        => new(
            p.Id,
            p.Name,
            p.Ingredients.OrderBy(i => i.SortOrder).Select(i => new ActiveIngredientRefDto(i.ActiveIngredientId, i.Ingredient?.Name ?? string.Empty)).ToList(),
            p.TargetSpecies.Select(t => t.Label).ToList(),
            p.LicenseHolder,
            p.LicenseNumber,
            p.SpcUrl,
            p.IsListedInSource,
            p.Source,
            p.SourceSyncedAtUtc);
}

/// <summary>Etken madde arama sonucu; <see cref="ProductCount"/> o maddeyi içeren katalog ürünü sayısı.</summary>
public sealed record ActiveIngredientDto(Guid Id, string Name, int ProductCount);

/// <summary>Katalog durumu: boşsa ekran "henüz yüklenmedi" der; doluysa kaynak ve son eşitleme tarihi gösterilir.</summary>
public sealed record DrugGuideSummaryDto(int ProductCount, int IngredientCount, DateTime? LastSyncedAtUtc);

/// <summary>Etken maddeyi içeren, rehbere bağlı klinik ürünü ve aktif klinikteki stok miktarı.</summary>
public sealed record IngredientStockProductDto(
    Guid ProductId,
    string ProductName,
    string Unit,
    bool IsActive,
    Guid LicensedProductId,
    string LicensedProductName,
    decimal QuantityOnHand);

/// <summary>
/// Reçete ve uygulama satırı için rehber bilgisi (REF-001 Dilim 2). <see cref="NotLicensedForPetSpecies"/> yalnız
/// hastanın türü ruhsattaki hedef türler arasında değilse true; bilinmiyorsa null. Engellemez.
/// </summary>
public sealed record ProductDrugInfoDto(
    Guid ProductId,
    Guid LicensedProductId,
    string LicensedProductName,
    IReadOnlyList<ActiveIngredientRefDto> ActiveIngredients,
    IReadOnlyList<string> TargetSpecies,
    bool? NotLicensedForPetSpecies,
    string? PetSpeciesName);
