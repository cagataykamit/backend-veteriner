namespace Backend.Veteriner.Application.Clients.ReadModels;

/// <summary>
/// Saf, deterministik client read-model parity değerlendirmesi (count karşılaştırması).
/// Canlı sayım okuması <see cref="IClientReadModelParityReader"/>'da; bu sınıf yalnızca karar mantığıdır.
/// </summary>
public static class ClientReadModelParityEvaluator
{
    public static ClientReadModelParityResult Evaluate(
        long commandCount,
        long queryCount,
        Guid? scopeTenantId = null)
        => new(commandCount, queryCount, scopeTenantId);
}
