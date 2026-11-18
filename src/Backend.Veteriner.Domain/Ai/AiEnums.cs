namespace Backend.Veteriner.Domain.Ai;

/// <summary>AI özelliği (kullanım kaydında; veritabanında int, mevcut değerler değişmez).</summary>
public enum AiFeature
{
    /// <summary>Hasta geçmişi özeti (AI-002). Taslak kaydı oluşturmaz.</summary>
    PetHistorySummary = 1,

    /// <summary>Serbest muayene notunu yapılandırma (AI-001; taslak, AI-009).</summary>
    ExamNoteStructure = 2,

    /// <summary>Hasta sahibine sade açıklama (AI-003; taslak, AI-009).</summary>
    OwnerExplanation = 3,

    /// <summary>Taburcu / evde bakım talimatı (AI-004; taslak, AI-009).</summary>
    DischargeInstructions = 4,
}

/// <summary>AI taslağının durumu (AI-009; veritabanında int, mevcut değerler değişmez).</summary>
public enum AiDraftStatus
{
    /// <summary>İnceleme bekliyor; hiçbir kayda yazılmadı.</summary>
    Draft = 1,

    /// <summary>Kullanıcı olduğu gibi kabul etti; hedef kayda yazıldı.</summary>
    Accepted = 2,

    /// <summary>Kullanıcı düzenleyip kabul etti; hedef kayda düzenlenmiş hali yazıldı.</summary>
    AcceptedWithEdits = 3,

    /// <summary>Kullanıcı attı; hiçbir kayda yazılmadı.</summary>
    Discarded = 4,

    /// <summary>Saklama süresi içinde karar verilmedi; hiçbir kayda yazılmadı.</summary>
    Expired = 5,
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

/// <summary>Fon hareketinin kaynağı (veritabanında int, mevcut değerler değişmez).</summary>
public enum AiCreditGrantKind
{
    /// <summary>Platform yöneticisinin elle eklediği fon (ücretli satın alma açılana kadar tek yol).</summary>
    Manual = 1,
}
