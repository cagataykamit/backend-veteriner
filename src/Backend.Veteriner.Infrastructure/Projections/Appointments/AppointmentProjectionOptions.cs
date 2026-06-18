namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed class AppointmentProjectionOptions
{
    public const string SectionName = "AppointmentProjection";

    public bool Enabled { get; set; } = true;

    public int BatchSize { get; set; } = 50;

    public int LoopIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// Son batch işleminden sonra bu süre boyunca kısa poll aralığı kullanılır (ardışık lifecycle eventleri için).
    /// </summary>
    public int ActiveFollowUpWindowSeconds { get; set; } = 5;

    /// <summary>
    /// Aktif takip penceresindeki boş batch sonrası bekleme (ms).
    /// </summary>
    public int ActiveFollowUpPollMilliseconds { get; set; } = 100;

    public string ConsumerName { get; set; } = "appointment-read-model-v1";
}
