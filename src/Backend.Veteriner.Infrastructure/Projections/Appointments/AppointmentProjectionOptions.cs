namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed class AppointmentProjectionOptions
{
    public const string SectionName = "AppointmentProjection";

    public bool Enabled { get; set; } = true;

    public int BatchSize { get; set; } = 50;

    public int LoopIntervalSeconds { get; set; } = 2;

    public string ConsumerName { get; set; } = "appointment-read-model-v1";
}
