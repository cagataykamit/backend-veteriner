using System.Text.Json.Nodes;
using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Security.Turnstile;

/// <summary>
/// Cloudflare Turnstile sunucu doğrulaması: <c>POST siteverify</c> (form: <c>secret</c>, <c>response</c>, <c>remoteip</c>,
/// <c>idempotency_key</c>); yanıtta <c>success</c>, <c>action</c> ve <c>hostname</c> denetlenir. Jeton tek kullanımlık ve 300 sn
/// geçerlidir. Gizli anahtar, jeton ve IP günlüğe yazılmaz; yalnız sonuç ve <c>error-codes</c>.
/// </summary>
public sealed class TurnstileHumanVerifier : IHumanVerifier
{
    public const string HttpClientName = "turnstile";

    /// <summary>Aşırı uzun girdi sağlayıcıya gönderilmez (istek doğrulayıcılarıyla aynı üst sınır).</summary>
    private const int MaxTokenLength = 4096;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TurnstileOptions _options;
    private readonly ILogger<TurnstileHumanVerifier> _logger;

    public TurnstileHumanVerifier(
        IHttpClientFactory httpClientFactory,
        IOptions<TurnstileOptions> options,
        ILogger<TurnstileHumanVerifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public string? SiteKey => _options.SiteKey;

    public async Task<HumanVerificationResult> VerifyAsync(string? token, string? remoteIp, string action, CancellationToken ct)
    {
        if (!IsConfigured)
            return HumanVerificationResult.Unavailable;

        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
            return HumanVerificationResult.Failed;

        var form = new Dictionary<string, string>
        {
            ["secret"] = _options.SecretKey!,
            ["response"] = token.Trim(),
            ["idempotency_key"] = Guid.NewGuid().ToString(),
        };
        if (!string.IsNullOrWhiteSpace(remoteIp))
            form["remoteip"] = remoteIp.Trim();

        JsonNode? body;
        try
        {
            using var response = await _httpClientFactory.CreateClient(HttpClientName)
                .PostAsync(_options.VerifyUrl, new FormUrlEncodedContent(form), ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            body = string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
            if (!response.IsSuccessStatusCode || body is null)
            {
                _logger.LogWarning("Turnstile doğrulaması yanıt vermedi. Status={Status}", (int)response.StatusCode);
                return HumanVerificationResult.Unavailable;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                   && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Turnstile doğrulamasına ulaşılamadı. Error={Error}", ex.GetType().Name);
            return HumanVerificationResult.Unavailable;
        }

        var errors = (body["error-codes"] as JsonArray)?.Select(e => e?.ToString()).Where(e => e is not null).ToList() ?? [];
        if (body["success"]?.GetValue<bool>() != true)
        {
            // Yanlış gizli anahtar ya da sağlayıcı hatası kullanıcının suçu değildir; yapılandırma sorunu olarak raporlanır.
            if (errors.Any(e => e is "missing-input-secret" or "invalid-input-secret" or "internal-error"))
            {
                _logger.LogError("Turnstile doğrulaması yapılandırma/sağlayıcı hatası verdi. ErrorCodes={ErrorCodes}", string.Join(',', errors));
                return HumanVerificationResult.Unavailable;
            }

            _logger.LogInformation("Turnstile doğrulaması geçmedi. ErrorCodes={ErrorCodes}", string.Join(',', errors));
            return HumanVerificationResult.Failed;
        }

        var returnedAction = body["action"]?.ToString();
        if (!string.IsNullOrEmpty(returnedAction) && !string.Equals(returnedAction, action, StringComparison.Ordinal))
        {
            _logger.LogInformation("Turnstile jetonu başka bir eylem için. Expected={Expected} Actual={Actual}", action, returnedAction);
            return HumanVerificationResult.Failed;
        }

        if (!HostnameMatches(body["hostname"]?.ToString()))
        {
            _logger.LogInformation("Turnstile jetonu beklenmeyen alan adından.");
            return HumanVerificationResult.Failed;
        }

        return HumanVerificationResult.Passed;
    }

    private bool HostnameMatches(string? hostname)
    {
        var expected = _options.ExpectedHostname?.Trim().TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(expected))
            return true;
        if (string.IsNullOrWhiteSpace(hostname))
            return false;

        var actual = hostname.Trim().ToLowerInvariant();
        return actual == expected || actual.EndsWith("." + expected, StringComparison.Ordinal);
    }
}

/// <summary>
/// Anahtarlar girilmemişse (her ortam) kullanılan doğrulayıcı: insan doğrulamasını atlar, site anahtarı vermez (istemci widget
/// çizmez). Hız sınırları ve e-posta kodu kuralları ayrıca çalışır.
/// </summary>
public sealed class PassThroughHumanVerifier : IHumanVerifier
{
    public bool IsConfigured => true;

    public string? SiteKey => null;

    public Task<HumanVerificationResult> VerifyAsync(string? token, string? remoteIp, string action, CancellationToken ct)
        => Task.FromResult(HumanVerificationResult.Passed);
}
