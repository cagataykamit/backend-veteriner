namespace Backend.Veteriner.Api.Contracts;

/// <summary>Tanıtım sitesi canlı demo talebi (TRIAL-005); kurallar docs/TRIAL_DEMO_REQUEST_API_CONTRACT.md §2.</summary>
public sealed class PublicDemoRequestBody
{
    public string? ContactName { get; set; }
    public string? ClinicName { get; set; }
    public string? City { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary><c>SingleVet</c> · <c>SmallTeam</c> · <c>MultiBranch</c>; tanınmayan değer doğrulama hatası verir.</summary>
    public string? ClinicSize { get; set; }

    public string? CurrentSoftware { get; set; }
    public string? Message { get; set; }
    public bool PrivacyNoticeAccepted { get; set; }

    /// <summary>Tuzak alan (honeypot); site gizli tutar.</summary>
    public string? Website { get; set; }
}
