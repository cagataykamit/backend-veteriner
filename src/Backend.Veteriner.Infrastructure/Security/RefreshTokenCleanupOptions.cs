namespace Backend.Veteriner.Infrastructure.Security;

public sealed class RefreshTokenCleanupOptions
{
    public bool Enabled { get; init; } = true;

    // kaç dakikada bir çalışsın
    public int IntervalMinutes { get; init; } = 360; // 6 saat

    // revoked/expired tokenları kaç gün tutalım
    public int RetentionDays { get; init; } = 30;

    // her çalışmada en fazla kaç kayıt silinsin (yük kontrolü)
    public int BatchSize { get; init; } = 2000;
}
