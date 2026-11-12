using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Domain.EDocuments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.EDocuments.Nilvera;

internal sealed class NilveraException : Exception
{
    public NilveraException(ProviderError error) : base(error.Message) => Error = error;

    public ProviderError Error { get; }
}

/// <param name="Content">Gövdenin ham baytları (PDF için); JSON yanıtta <see cref="Body"/> da doludur.</param>
internal sealed record NilveraResponse(HttpStatusCode Status, JsonNode? Body, byte[] Content, string? MediaType, TimeSpan? RetryAfter = null)
{
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    /// <summary>Hata gövdesindeki kodlar (<c>Errors[].Code</c>).</summary>
    public IReadOnlyList<int> ErrorCodes
        => (Body?["Errors"] as JsonArray)?
            .Select(e => e?["Code"] is JsonValue v && v.TryGetValue<int>(out var code) ? code : (int?)null)
            .OfType<int>()
            .ToList() ?? [];
}

/// <summary>
/// Nilvera HTTP istemcisi (developer.nilvera.com, V1). Kimlik doğrulama <c>Authorization: Bearer {API anahtarı}</c>; anahtar
/// her çağrıda bağlantıdan gelir, saklanmaz ve günlüğe yazılmaz. İstek adresi ve gövdesi günlüğe yazılmaz (vergi kimliği
/// içerir); yalnız yöntem, yol şablonu ve durum kodu. Hız sınırında (429) aynı istek içinde sınırlı yeniden denenir.
/// </summary>
public sealed class NilveraClient
{
    public const string HttpClientName = "nilvera";

    private const int RateLimitRetries = 2;
    private static readonly TimeSpan DefaultRateLimitWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxRateLimitWait = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NilveraOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NilveraClient> _logger;

    public NilveraClient(
        IHttpClientFactory httpClientFactory,
        IOptions<NilveraOptions> options,
        TimeProvider timeProvider,
        ILogger<NilveraClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public NilveraOptions Options => _options;

    /// <param name="path">Servis adıyla başlayan yol (ör. <c>/earchive/Send/Model</c>).</param>
    internal async Task<NilveraResponse> SendAsync(
        string apiKey, HttpMethod method, string path, JsonNode? body, string pathTemplate, CancellationToken ct)
    {
        var response = await SendOnceAsync(apiKey, method, path, body, pathTemplate, ct);
        for (var attempt = 0; attempt < RateLimitRetries && response.Status == HttpStatusCode.TooManyRequests; attempt++)
        {
            var wait = response.RetryAfter is { } after && after > TimeSpan.Zero ? after : DefaultRateLimitWait;
            await Task.Delay(wait > MaxRateLimitWait ? MaxRateLimitWait : wait, _timeProvider, ct);
            response = await SendOnceAsync(apiKey, method, path, body, pathTemplate, ct);
        }

        return response;
    }

    /// <summary>Başarısız yanıtı hata sınıfına çevirir. 400/409/422 ayrıntısı (Nilvera'nın Türkçe metni) kullanıcıya gösterilir.</summary>
    internal static ProviderError ToError(NilveraResponse response, string action)
    {
        var detail = ErrorDetail(response.Body);
        var code = response.ErrorCodes.Count > 0 ? response.ErrorCodes[0].ToString() : ((int)response.Status).ToString();
        return response.Status switch
        {
            HttpStatusCode.Unauthorized => new ProviderError(
                EDocumentErrorKind.Authorization, "Nilvera API anahtarı geçersiz veya süresi dolmuş; yeni anahtar girin.", code),
            HttpStatusCode.Forbidden => new ProviderError(
                EDocumentErrorKind.ProviderRejected, $"Nilvera API anahtarının {action} yetkisi yok; anahtarı gerekli yetkilerle yeniden üretin.{Suffix(detail)}", code),
            HttpStatusCode.NotFound => new ProviderError(EDocumentErrorKind.ProviderRejected, $"Nilvera kaydı bulunamadı ({action}).{Suffix(detail)}", code),
            HttpStatusCode.TooManyRequests => new ProviderError(EDocumentErrorKind.Transient, "Nilvera istek sınırına ulaşıldı; otomatik yeniden denenecek.", code),
            HttpStatusCode.UnprocessableEntity when response.ErrorCodes.Contains(InsufficientCredits)
                => new ProviderError(EDocumentErrorKind.AccountSuspended, $"Nilvera kontörü yetersiz; Nilvera hesabınıza kontör yükleyin.{Suffix(detail)}", code),
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest or HttpStatusCode.Conflict
                => new ProviderError(EDocumentErrorKind.InvalidData, $"Nilvera {action} isteğini kabul etmedi.{Suffix(detail)}", code),
            >= HttpStatusCode.InternalServerError => new ProviderError(EDocumentErrorKind.Transient, "Nilvera geçici olarak yanıt vermiyor; otomatik yeniden denenecek.", code),
            _ => new ProviderError(EDocumentErrorKind.Unknown, $"Nilvera beklenmeyen yanıt verdi ({action}).{Suffix(detail)}", code),
        };
    }

    /// <summary>422 / 2000: kontör yetersiz (developer.nilvera.com hata kodları).</summary>
    internal const int InsufficientCredits = 2000;

    /// <summary><c>{"Message":…, "Errors":[{"Code","Description","Detail"}]}</c> → tek satır ayrıntı.</summary>
    internal static string? ErrorDetail(JsonNode? body)
    {
        var parts = (body?["Errors"] as JsonArray)?
            .Select(e => e?["Description"]?.ToString() ?? e?["Detail"]?.ToString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .Take(3)
            .ToList() ?? [];
        if (parts.Count == 0 && body?["Message"]?.ToString() is { Length: > 0 } message)
            parts.Add(message);
        if (parts.Count == 0)
            return null;
        var text = string.Join("; ", parts!);
        return text.Length > 400 ? text[..400] : text;
    }

    private static string Suffix(string? detail) => string.IsNullOrWhiteSpace(detail) ? string.Empty : $" Ayrıntı: {detail}";

    private async Task<NilveraResponse> SendOnceAsync(
        string apiKey, HttpMethod method, string path, JsonNode? body, string pathTemplate, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{_options.ApiBaseUrl.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsByteArrayAsync(ct);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            _logger.LogInformation("Nilvera {Method} {Path} → {Status}", method.Method, pathTemplate, (int)response.StatusCode);
            var retryAfter = response.Headers.RetryAfter is { } header
                ? header.Delta ?? (header.Date is { } date ? date - _timeProvider.GetUtcNow() : null)
                : null;
            return new NilveraResponse(response.StatusCode, Parse(content, mediaType), content, mediaType, retryAfter);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Nilvera {Method} {Path} bağlantı hatası: {Error}", method.Method, pathTemplate, ex.GetType().Name);
            throw new NilveraException(new ProviderError(EDocumentErrorKind.Transient, "Nilvera'ya bağlanılamadı; otomatik yeniden denenecek."));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Nilvera {Method} {Path} zaman aşımı", method.Method, pathTemplate);
            throw new NilveraException(new ProviderError(EDocumentErrorKind.Transient, "Nilvera yanıtı zaman aşımına uğradı; otomatik yeniden denenecek."));
        }
    }

    private static JsonNode? Parse(byte[] content, string? mediaType)
    {
        if (content.Length == 0 || mediaType is "application/pdf" or "application/octet-stream")
            return null;
        try
        {
            return JsonNode.Parse(content);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
