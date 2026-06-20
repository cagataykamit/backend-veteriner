namespace Backend.Veteriner.Infrastructure.Projections.Pets;

public sealed class PetProjectionOptions
{
    public const string SectionName = "PetProjection";

    /// <summary>False ise hosted service hiç poll etmez (processor manuel çağrılabilir kalır).</summary>
    public bool Enabled { get; set; } = false;

    public int BatchSize { get; set; } = 50;

    public int LoopIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// <c>ProcessedProjectionEvents</c> idempotency consumer adı. Appointment/client consumer'larından
    /// ayrıdır; PK (EventId, ConsumerName) olduğundan bağımsız dedup sağlar.
    /// </summary>
    public string ConsumerName { get; set; } = "pet-read-model-v1";
}
