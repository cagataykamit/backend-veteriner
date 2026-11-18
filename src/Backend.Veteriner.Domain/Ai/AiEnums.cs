namespace Backend.Veteriner.Domain.Ai;

/// <summary>AI özelliği (kullanım kaydında; veritabanında int, mevcut değerler değişmez).</summary>
public enum AiFeature
{
    /// <summary>Hasta geçmişi özeti (AI-002).</summary>
    PetHistorySummary = 1,
}

/// <summary>Sağlayıcı çağrısının sonucu (kullanım kaydında; veritabanında int).</summary>
public enum AiUsageOutcome
{
    /// <summary>Yanıt alındı ve doğrulandı.</summary>
    Succeeded = 1,

    /// <summary>Yanıt alındı ama beklenen biçimde değildi (geçersiz JSON / şema).</summary>
    InvalidOutput = 2,

    /// <summary>Çıktı sınırına takıldı, yanıt yarım kaldı.</summary>
    Truncated = 3,

    /// <summary>Model güvenlik nedeniyle yanıtlamadı.</summary>
    Refused = 4,

    /// <summary>Sağlayıcı hata döndü ya da ulaşılamadı.</summary>
    ProviderError = 5,
}
