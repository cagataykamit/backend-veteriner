namespace Backend.Veteriner.Application.Pets.ReadModels;

/// <summary>
/// Saf, deterministik pet read-model parity değerlendirmesi (count karşılaştırması).
/// Canlı sayım okuması <see cref="IPetReadModelParityReader"/>'da; bu sınıf yalnızca karar mantığıdır.
/// </summary>
public static class PetReadModelParityEvaluator
{
    public static PetReadModelParityResult Evaluate(
        long commandCount,
        long queryCount,
        Guid? scopeTenantId = null)
        => new(commandCount, queryCount, scopeTenantId);
}
