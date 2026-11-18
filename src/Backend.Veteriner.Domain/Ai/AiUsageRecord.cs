using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Ai;

/// <summary>
/// AI sağlayıcısına giden her isteğin kullanım kaydı (ADR-044 madde 8, AI-008): gerçek token, tahmini dolar maliyeti, süre
/// ve sonuç. Klinik içerik (istem, yanıt metni) <b>tutulmaz</b>; yalnız ölçü ve kimlikler. Kota ve harcama sınırları bu
/// kayıtların toplamından hesaplanır. Yalnız eklenir, değişmez.
/// </summary>
public sealed class AiUsageRecord : ITenantOwned
{
    public const int ModelMaxLength = 100;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid UserId { get; private set; }
    public AiFeature Feature { get; private set; }

    /// <summary>Özelliğin konusu olan kayıt (geçmiş özetinde hayvan); denetim içindir.</summary>
    public Guid? SubjectId { get; private set; }

    public string Model { get; private set; } = default!;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }

    /// <summary>Ayardaki model fiyatıyla hesaplanan tahmini maliyet (USD); fatura değildir.</summary>
    public decimal CostUsd { get; private set; }

    public int DurationMs { get; private set; }
    public AiUsageOutcome Outcome { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public int TotalTokens => InputTokens + OutputTokens;

    private AiUsageRecord() { }

    public static AiUsageRecord Create(
        Guid tenantId,
        Guid clinicId,
        Guid userId,
        AiFeature feature,
        Guid? subjectId,
        string model,
        int inputTokens,
        int outputTokens,
        decimal costUsd,
        int durationMs,
        AiUsageOutcome outcome,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId geçersiz.", nameof(userId));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model zorunlu.", nameof(model));

        return new AiUsageRecord
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            UserId = userId,
            Feature = feature,
            SubjectId = subjectId,
            Model = model.Length > ModelMaxLength ? model[..ModelMaxLength] : model,
            InputTokens = Math.Max(0, inputTokens),
            OutputTokens = Math.Max(0, outputTokens),
            CostUsd = Math.Max(0m, costUsd),
            DurationMs = Math.Max(0, durationMs),
            Outcome = outcome,
            CreatedAtUtc = utcNow,
        };
    }
}
