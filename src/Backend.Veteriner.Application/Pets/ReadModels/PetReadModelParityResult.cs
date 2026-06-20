namespace Backend.Veteriner.Application.Pets.ReadModels;

/// <summary>
/// Command DB <c>Pets</c> ile Query DB <c>PetReadModels</c> satır sayısı karşılaştırması.
/// Pet domain'inde silme/soft-delete yoktur; bu yüzden kuyruk boşken beklenen durum
/// <see cref="InSync"/> == <c>true</c>'dur. <see cref="ScopeTenantId"/> null ise global parity.
/// </summary>
public sealed record PetReadModelParityResult(
    long CommandCount,
    long QueryCount,
    Guid? ScopeTenantId = null)
{
    /// <summary>Command - Query. Pozitif değer read-model'in geride kaldığını gösterir.</summary>
    public long Difference => CommandCount - QueryCount;

    public long AbsoluteDifference => Math.Abs(Difference);

    public bool InSync => CommandCount == QueryCount;
}
