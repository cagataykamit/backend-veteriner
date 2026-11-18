namespace Backend.Veteriner.Application.Ai;

/// <summary>
/// AI özelliklerinin tek ayar yeri (<c>Ai</c>; ADR-044 madde 8–9): model, istek başına token, kullanıcı başına dakikalık istek,
/// klinik başına aylık token/maliyet ve sistem geneli aylık harcama sınırı, model fiyatları. Sağlayıcı anahtarı burada değil,
/// sağlayıcının kendi bölümündedir (<c>Ai:Anthropic</c>). Aylar İstanbul takvim ayıdır.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Genel anahtar: <c>false</c> iken anahtar olsa da tüm AI özellikleri kapalıdır.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Kullanılan model (ADR-044 Hızlı AI: Claude Haiku 5.5). Fiyatı <see cref="Prices"/>'ta olmalıdır.</summary>
    public string Model { get; init; } = "claude-haiku-5-5";

    /// <summary>Modele gönderilen girdinin tahmini üst sınırı (token); aşan geçmişte en eski kayıtlar çıkarılır.</summary>
    public int MaxInputTokens { get; init; } = 12_000;

    /// <summary>Yanıt (düşünme dahil) için üst sınır (token); sağlayıcıya <c>max_tokens</c> olarak gider.</summary>
    public int MaxOutputTokens { get; init; } = 2_000;

    /// <summary>Kullanıcı başına son 60 saniyedeki en çok istek.</summary>
    public int UserRequestsPerMinute { get; init; } = 3;

    /// <summary>Klinik başına aylık en çok token (giriş + çıkış).</summary>
    public long ClinicMonthlyTokenLimit { get; init; } = 5_000_000;

    /// <summary>Klinik başına aylık en çok tahmini maliyet (USD).</summary>
    public decimal ClinicMonthlyCostLimitUsd { get; init; } = 2m;

    /// <summary>Tüm kiracılar toplamı aylık en çok tahmini maliyet (USD); aşılınca AI herkes için durur.</summary>
    public decimal SystemMonthlyCostLimitUsd { get; init; } = 10m;

    /// <summary>Model → 1 milyon token fiyatı (USD). Fiyatı olmayan modelle istek yapılmaz (maliyet sınırı hesaplanamaz).</summary>
    public Dictionary<string, AiModelPrice> Prices { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        // platform.claude.com, 2026-11-06 (ADR-044 madde 3); Haiku 5.5 fiyatı 100K token'a kadar olan istem içindir.
        ["claude-haiku-5-5"] = new() { InputPerMillionUsd = 0.10m, OutputPerMillionUsd = 0.50m },
        ["claude-sonnet-5-5"] = new() { InputPerMillionUsd = 2m, OutputPerMillionUsd = 10m },
    };

    public AiModelPrice? PriceOf(string model)
        => Prices.TryGetValue(model, out var price) && price.IsValid ? price : null;
}

public sealed class AiModelPrice
{
    public decimal InputPerMillionUsd { get; init; }
    public decimal OutputPerMillionUsd { get; init; }

    public bool IsValid => InputPerMillionUsd > 0 && OutputPerMillionUsd > 0;

    public decimal CostOf(int inputTokens, int outputTokens)
        => decimal.Round((inputTokens * InputPerMillionUsd + outputTokens * OutputPerMillionUsd) / 1_000_000m, 6);
}
