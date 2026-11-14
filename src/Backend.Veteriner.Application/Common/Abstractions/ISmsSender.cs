namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// SMS gönderimi, sağlayıcıdan bağımsız (INT-003). Tek kullanımlık kod outbox'a girmez: kullanıcı kodu beklediği için doğrudan
/// ve zaman aşımlı gönderilir; sonucu çağıran hemen görür. Sağlayıcı seçimi ve kimlik bilgileri yapılandırmadadır (<c>Sms:*</c>).
/// </summary>
public interface ISmsSender
{
    /// <summary>Gönderim yapılabilir mi; yapılandırılmamışsa (ör. üretimde sağlayıcı yok) çağıran açık bir hatayla durur.</summary>
    bool IsConfigured { get; }

    /// <param name="normalizedPhone">Türkiye cep telefonu, 905XXXXXXXXX.</param>
    /// <param name="text">Tek SMS boyunda, Türkçe karaktersiz metin (sağlayıcı OTP kuralları).</param>
    Task<SmsSendResult> SendOneTimeCodeAsync(string normalizedPhone, string text, CancellationToken ct);
}

/// <param name="ProviderMessageId">Sağlayıcının iş numarası (varsa); günlük ve destek için.</param>
/// <param name="FailureReason">Başarısızlıkta sağlayıcı kodu ya da kısa neden; kişisel veri içermez.</param>
public sealed record SmsSendResult(bool Succeeded, string? ProviderMessageId = null, string? FailureReason = null)
{
    public static SmsSendResult Success(string? providerMessageId = null) => new(true, providerMessageId);

    public static SmsSendResult Failure(string reason) => new(false, FailureReason: reason);
}
