namespace Backend.Veteriner.Infrastructure.Projections.Clients;

public sealed class ClientProjectionOptions
{
    public const string SectionName = "ClientProjection";

    /// <summary>False ise hosted service hiç poll etmez (processor manuel çağrılabilir kalır).</summary>
    public bool Enabled { get; set; } = true;

    public int BatchSize { get; set; } = 50;

    public int LoopIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// <c>ProcessedProjectionEvents</c> idempotency consumer adı. Appointment'tan (<c>appointment-read-model-v1</c>)
    /// ayrıdır; PK (EventId, ConsumerName) olduğundan bağımsız dedup sağlar.
    /// </summary>
    public string ConsumerName { get; set; } = "client-read-model-v1";
}
