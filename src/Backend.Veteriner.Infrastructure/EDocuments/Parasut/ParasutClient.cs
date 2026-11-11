using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Domain.EDocuments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.EDocuments.Parasut;

/// <summary>Paraşüt OAuth belirteçleri; şifreli olarak bağlantıda saklanır (JSON). Günlüğe yazılmaz.</summary>
internal sealed record ParasutCredentials(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc)
{
    public string Serialize() => JsonSerializer.Serialize(this);

    public static ParasutCredentials? Parse(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<ParasutCredentials>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed class ParasutException : Exception
{
    public ParasutException(ProviderError error) : base(error.Message) => Error = error;

    public ProviderError Error { get; }
}

/// <summary>Tek çağrı zincirinin belirteç durumu; yenilenirse çağırana döner.</summary>
internal sealed class ParasutSession
{
    public ParasutSession(ParasutCredentials credentials) => Credentials = credentials;

    public ParasutCredentials Credentials { get; private set; }
    public bool Refreshed { get; private set; }

    public void Replace(ParasutCredentials credentials)
    {
        Credentials = credentials;
        Refreshed = true;
    }
}

/// <param name="RetryAfter">429 yanıtındaki bekleme süresi (<c>Retry-After</c>); yoksa <c>null</c>.</param>
internal sealed record ParasutResponse(HttpStatusCode Status, JsonNode? Body, TimeSpan? RetryAfter = null);

/// <summary>
/// Paraşüt HTTP istemcisi: OAuth belirteç uçları ve JSON:API istekleri. Erişim belirteci süresi dolmak üzereyse veya istek
/// 401 dönerse bir kez yenilenir. Hata gövdesi Türkçe kullanıcı metnine ve hata sınıfına çevrilir. İstek adresi ve gövdesi
/// günlüğe yazılmaz (vergi kimliği, belirteç içerebilir); yalnız yöntem, yol şablonu ve durum kodu.
/// </summary>
public sealed class ParasutClient
{
    public const string HttpClientName = "parasut";
    private const string JsonApiMediaType = "application/vnd.api+json";
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);

    /// <summary>Hız sınırında (429) aynı istek içinde en çok bu kadar beklenip yeniden denenir; sonra geçici hata olarak arka plan işine kalır.</summary>
    private const int RateLimitRetries = 2;
    private static readonly TimeSpan DefaultRateLimitWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxRateLimitWait = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ParasutOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ParasutClient> _logger;

    public ParasutClient(
        IHttpClientFactory httpClientFactory,
        IOptions<ParasutOptions> options,
        TimeProvider timeProvider,
        ILogger<ParasutClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public ParasutOptions Options => _options;

    public string AuthorizationUrl(string state, string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId ?? string.Empty,
            ["redirect_uri"] = _options.RedirectUri ?? string.Empty,
            ["response_type"] = "code",
            ["scope"] = "openid",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["kc_idp_hint"] = "parasut",
        };
        return $"{_options.IdentityBaseUrl.TrimEnd('/')}/auth?{string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"))}";
    }

    internal Task<ParasutCredentials> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
        => TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri ?? string.Empty,
            ["code_verifier"] = codeVerifier,
        }, ct);

    /// <summary>JSON:API isteği. <paramref name="path"/> API tabanına göredir (ör. <c>/me</c>, <c>/{şirket}/contacts</c>).</summary>
    internal async Task<ParasutResponse> SendAsync(
        ParasutSession session, HttpMethod method, string path, JsonNode? body, string pathTemplate, CancellationToken ct)
    {
        if (session.Credentials.ExpiresAtUtc - RefreshSkew <= Now())
            await RefreshAsync(session, ct);

        var response = await SendOnceAsync(session, method, path, body, pathTemplate, ct);
        if (response.Status == HttpStatusCode.Unauthorized)
        {
            await RefreshAsync(session, ct);
            response = await SendOnceAsync(session, method, path, body, pathTemplate, ct);
        }

        // Paraşüt paketinin 10 saniyelik istek sınırı düşük olabilir (staging'de belge başına birkaç istek bunu aşıyor).
        for (var attempt = 0; attempt < RateLimitRetries && response.Status == HttpStatusCode.TooManyRequests; attempt++)
        {
            var wait = response.RetryAfter is { } after && after > TimeSpan.Zero ? after : DefaultRateLimitWait;
            await Task.Delay(wait > MaxRateLimitWait ? MaxRateLimitWait : wait, _timeProvider, ct);
            response = await SendOnceAsync(session, method, path, body, pathTemplate, ct);
        }

        return response;
    }

    /// <summary>Süreli PDF adresini indirir (yetki başlığı gönderilmez; adres imzalıdır).</summary>
    internal async Task<byte[]> DownloadAsync(string url, CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                throw new ParasutException(new ProviderError(EDocumentErrorKind.Transient, "PDF indirilemedi; tekrar deneyin.", ((int)response.StatusCode).ToString()));
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (HttpRequestException)
        {
            throw new ParasutException(new ProviderError(EDocumentErrorKind.Transient, "PDF indirilemedi; tekrar deneyin."));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ParasutException(new ProviderError(EDocumentErrorKind.Transient, "PDF indirilemedi (zaman aşımı)."));
        }
    }

    /// <summary>Başarısız yanıtı hata sınıfına çevirir. 422 ayrıntısı kullanıcıya gösterilir (Paraşüt metni, çoğu İngilizce).</summary>
    internal static ProviderError ToError(ParasutResponse response, string action)
    {
        var code = ((int)response.Status).ToString();
        var detail = ErrorDetail(response.Body);
        return response.Status switch
        {
            HttpStatusCode.Unauthorized => new ProviderError(EDocumentErrorKind.Authorization, "Paraşüt yetkisi geçersiz; hesabı yeniden bağlayın.", code),
            HttpStatusCode.PaymentRequired => new ProviderError(EDocumentErrorKind.AccountSuspended, "Paraşüt aboneliği sona ermiş görünüyor; Paraşüt hesabınızı kontrol edin.", code),
            HttpStatusCode.Forbidden => new ProviderError(EDocumentErrorKind.ProviderRejected, $"Paraşüt kullanıcısının {action} yetkisi yok.{Suffix(detail)}", code),
            HttpStatusCode.NotFound => new ProviderError(EDocumentErrorKind.ProviderRejected, $"Paraşüt kaydı bulunamadı ({action}).", code),
            HttpStatusCode.TooManyRequests => new ProviderError(EDocumentErrorKind.Transient, "Paraşüt istek sınırına ulaşıldı; otomatik yeniden denenecek.", code),
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest or HttpStatusCode.Conflict
                => new ProviderError(EDocumentErrorKind.InvalidData, $"Paraşüt {action} isteğini kabul etmedi.{Suffix(detail)}", code),
            >= HttpStatusCode.InternalServerError => new ProviderError(EDocumentErrorKind.Transient, "Paraşüt geçici olarak yanıt vermiyor; otomatik yeniden denenecek.", code),
            _ => new ProviderError(EDocumentErrorKind.Unknown, $"Paraşüt beklenmeyen yanıt verdi ({action}).{Suffix(detail)}", code),
        };
    }

    private static string Suffix(string? detail) => string.IsNullOrWhiteSpace(detail) ? string.Empty : $" Ayrıntı: {detail}";

    /// <summary>JSON:API <c>errors[]</c> (nesne veya metin dizisi) → tek satır ayrıntı.</summary>
    public static string? ErrorDetail(JsonNode? body)
    {
        if (body?["errors"] is not JsonArray errors || errors.Count == 0)
            return null;
        var parts = errors
            .Select(e => e is JsonValue v ? v.ToString() : e?["detail"]?.ToString() ?? e?["title"]?.ToString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .Take(3)
            .ToList();
        if (parts.Count == 0)
            return null;
        var text = string.Join("; ", parts!);
        return text.Length > 400 ? text[..400] : text;
    }

    private async Task<ParasutResponse> SendOnceAsync(
        ParasutSession session, HttpMethod method, string path, JsonNode? body, string pathTemplate, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{_options.ApiBaseUrl.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Credentials.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, JsonApiMediaType);

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("Paraşüt {Method} {Path} → {Status}", method.Method, pathTemplate, (int)response.StatusCode);
            var retryAfter = response.Headers.RetryAfter is { } header
                ? header.Delta ?? (header.Date is { } date ? date - _timeProvider.GetUtcNow() : null)
                : null;
            return new ParasutResponse(response.StatusCode, Parse(text), retryAfter);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Paraşüt {Method} {Path} bağlantı hatası: {Error}", method.Method, pathTemplate, ex.GetType().Name);
            throw new ParasutException(new ProviderError(EDocumentErrorKind.Transient, "Paraşüt'e bağlanılamadı; otomatik yeniden denenecek."));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Paraşüt {Method} {Path} zaman aşımı", method.Method, pathTemplate);
            throw new ParasutException(new ProviderError(EDocumentErrorKind.Transient, "Paraşüt yanıtı zaman aşımına uğradı; otomatik yeniden denenecek."));
        }
    }

    private async Task RefreshAsync(ParasutSession session, CancellationToken ct)
    {
        var refreshed = await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = session.Credentials.RefreshToken,
        }, ct, fallbackRefreshToken: session.Credentials.RefreshToken);
        session.Replace(refreshed);
    }

    private async Task<ParasutCredentials> TokenAsync(
        Dictionary<string, string> form, CancellationToken ct, string? fallbackRefreshToken = null)
    {
        form["client_id"] = _options.ClientId ?? string.Empty;
        form["client_secret"] = _options.ClientSecret ?? string.Empty;
        var grant = form["grant_type"];

        HttpResponseMessage response;
        string text;
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var content = new FormUrlEncodedContent(form);
            response = await client.PostAsync($"{_options.IdentityBaseUrl.TrimEnd('/')}/token", content, ct);
            text = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException)
        {
            throw new ParasutException(new ProviderError(EDocumentErrorKind.Transient, "Paraşüt kimlik sunucusuna bağlanılamadı."));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ParasutException(new ProviderError(EDocumentErrorKind.Transient, "Paraşüt kimlik sunucusu zaman aşımı."));
        }

        using (response)
        {
            _logger.LogInformation("Paraşüt token ({Grant}) → {Status}", grant, (int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
            {
                var error = Parse(text)?["error"]?.ToString();
                var kind = (int)response.StatusCode >= 500 ? EDocumentErrorKind.Transient : EDocumentErrorKind.Authorization;
                throw new ParasutException(new ProviderError(
                    kind,
                    grant == "refresh_token"
                        ? "Paraşüt oturumu yenilenemedi; hesabı yeniden bağlayın."
                        : "Paraşüt yetkilendirmesi tamamlanamadı; tekrar deneyin.",
                    error ?? ((int)response.StatusCode).ToString()));
            }

            var json = Parse(text);
            var access = json?["access_token"]?.ToString();
            if (string.IsNullOrEmpty(access))
                throw new ParasutException(new ProviderError(EDocumentErrorKind.Unknown, "Paraşüt belirteç yanıtı okunamadı."));

            var refresh = json?["refresh_token"]?.ToString() ?? fallbackRefreshToken ?? string.Empty;
            var expiresIn = json?["expires_in"]?.GetValue<int>() ?? 3600;
            return new ParasutCredentials(access, refresh, Now().AddSeconds(expiresIn));
        }
    }

    private static JsonNode? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;
}
