namespace Backend.Veteriner.Infrastructure.Sms;

/// <summary>
/// SMS ayarları (<c>Sms</c>, INT-003). Gizli değerler (Netgsm kullanıcı adı ve parolası) depoya yazılmaz: geliştirmede
/// user-secrets, sunucuda ortam değişkeni (<c>Sms__Netgsm__Password</c> gibi).
/// </summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    public const string NetgsmProvider = "Netgsm";

    /// <summary>Gönderimi yalnız günlüğe yazan sahte sağlayıcı; üretimde kabul edilmez.</summary>
    public const string LogProvider = "Log";

    /// <summary>
    /// <see cref="NetgsmProvider"/> ya da <see cref="LogProvider"/>. Boşsa Development ve IntegrationTests ortamında
    /// <see cref="LogProvider"/>, diğer ortamlarda yapılandırılmamış sayılır (kod gönderilmez, deneme kaydı açık hatayla durur).
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>Kod kullanıcıyı bekletir; sağlayıcı çağrısının üst sınırı (3–30 sn).</summary>
    public int TimeoutSeconds { get; init; } = 10;

    public NetgsmOptions Netgsm { get; init; } = new();
}

/// <summary>
/// Netgsm SMS API v3 (OTP). Kimlik bilgisi Netgsm panelinde "API Kullanıcısı" türünde açılan alt kullanıcıdır; gönderici adı
/// (<see cref="Sender"/>) Netgsm'de onaylı başlık olmalıdır.
/// </summary>
public sealed class NetgsmOptions
{
    /// <summary>Canlı: <c>https://ext-api.netgsm.com.tr</c>; deneme ortamı: <c>https://ext-sandbox.netgsm.com.tr</c>.</summary>
    public string ApiBaseUrl { get; init; } = "https://ext-api.netgsm.com.tr";

    public string? Username { get; init; }
    public string? Password { get; init; }

    /// <summary>Onaylı SMS başlığı (gönderici adı), 3–11 karakter.</summary>
    public string? Sender { get; init; }

    /// <summary>Netgsm tarafında yalnız kayıt amaçlı uygulama adı.</summary>
    public string AppName { get; init; } = "Vetinity";

    /// <summary>Kurumsal OTP hesabıysa <c>x-client: enterprise</c> başlığı gönderilir.</summary>
    public bool Enterprise { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(Sender)
        && Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out _);
}
