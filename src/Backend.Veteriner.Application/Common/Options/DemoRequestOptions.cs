namespace Backend.Veteriner.Application.Common.Options;

/// <summary>Canlı demo talepleri (TRIAL-005).</summary>
public sealed class DemoRequestOptions
{
    public const string SectionName = "DemoRequests";

    /// <summary>Yeni talep bildiriminin gideceği adres; boşsa talep yalnız veritabanına yazılır.</summary>
    public string? NotifyEmail { get; init; }
}
