using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Sms;

/// <summary>
/// Yerel geliştirme ve test için sahte sağlayıcı: SMS gönderilmez, gönderim günlüğe maskeli numarayla yazılır. Metin (kod
/// dahil) yalnız Development ortamında görünür; diğer ortamlarda günlüğe yazılmaz. Üretimde yapılandırılmamış sayılır.
/// </summary>
public sealed class LogOnlySmsSender : ISmsSender
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<LogOnlySmsSender> _logger;

    public LogOnlySmsSender(IHostEnvironment environment, ILogger<LogOnlySmsSender> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public bool IsConfigured => !_environment.IsProduction();

    public Task<SmsSendResult> SendOneTimeCodeAsync(string normalizedPhone, string text, CancellationToken ct)
    {
        if (!IsConfigured)
            return Task.FromResult(SmsSendResult.Failure("log-provider-not-allowed"));

        if (_environment.IsDevelopment())
        {
            _logger.LogInformation(
                "SMS (sahte sağlayıcı, gönderilmedi). Phone={Phone} Text={Text}", TurkishMobilePhone.Mask(normalizedPhone), text);
        }
        else
        {
            _logger.LogInformation("SMS (sahte sağlayıcı, gönderilmedi). Phone={Phone}", TurkishMobilePhone.Mask(normalizedPhone));
        }

        return Task.FromResult(SmsSendResult.Success());
    }
}

/// <summary>Sağlayıcı seçilmemiş ya da eksik ayarlı: gönderim yapılmaz, çağıran açık hatayla durur.</summary>
public sealed class UnconfiguredSmsSender : ISmsSender
{
    public bool IsConfigured => false;

    public Task<SmsSendResult> SendOneTimeCodeAsync(string normalizedPhone, string text, CancellationToken ct)
        => Task.FromResult(SmsSendResult.Failure("not-configured"));
}
