namespace Backend.Veteriner.Application.Clients.ReadModels;

/// <summary>
/// Command DB <c>Clients</c> ile Query DB <c>ClientReadModels</c> satır sayısı karşılaştırması.
/// Client domain'inde silme/soft-delete yoktur; bu yüzden kuyruk boşken beklenen durum
/// <see cref="InSync"/> == <c>true</c>'dur. <see cref="ScopeTenantId"/> null ise global parity.
/// </summary>
public sealed record ClientReadModelParityResult(
    long CommandCount,
    long QueryCount,
    Guid? ScopeTenantId = null)
{
    /// <summary>Command - Query. Pozitif değer read-model'in geride kaldığını gösterir.</summary>
    public long Difference => CommandCount - QueryCount;

    public long AbsoluteDifference => Math.Abs(Difference);

    public bool InSync => CommandCount == QueryCount;
}
