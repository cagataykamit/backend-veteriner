namespace Backend.Veteriner.Domain.Shared;

/// <summary>
/// Kiracıya ait kayıt (ADR-040). Kiracı filtresi (etkin kiracı ya da açık kapsam) ve yazma koruması bu arayüz
/// üzerinden tek yerde uygulanır; filtre her varlık için ayrı yazılmaz.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
