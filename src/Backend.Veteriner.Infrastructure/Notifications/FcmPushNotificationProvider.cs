using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Notifications;

/// <summary>
/// FCM HTTP v1 sağlayıcısı (MOBILE-003): yalnız <c>Push:Fcm</c> anahtarı varken seçilir. Hizmet hesabıyla imzalanan JWT'yi OAuth
/// erişim jetonuna çevirir (bellekte, süresinden 5 dk önce yenilenir) ve <c>messages:send</c> ile tek cihaza gönderir. Android,
/// iOS (APNs anahtarı Firebase konsoluna yüklenir) ve web aynı uçtan geçer. Cihaz jetonu ve erişim jetonu loglanmaz.
/// </summary>
public sealed class FcmPushNotificationProvider : IPushNotificationProvider, IDisposable
{
    public const string HttpClientName = "fcm";

    private const string MessagingScope = "https://www.googleapis.com/auth/firebase.messaging";
    private const int AssertionLifetimeSeconds = 3600;
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FcmServiceAccount _account;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FcmPushNotificationProvider> _logger;
    private readonly RSA _signingKey;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public FcmPushNotificationProvider(
        IHttpClientFactory httpClientFactory,
        FcmServiceAccount account,
        TimeProvider timeProvider,
        ILogger<FcmPushNotificationProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _account = account;
        _timeProvider = timeProvider;
        _logger = logger;
        _signingKey = RSA.Create();
        _signingKey.ImportFromPem(account.PrivateKeyPem);
    }

    public bool IsConfigured => true;

    public async Task<PushDeliveryOutcome> SendAsync(OwnerPushPlatform platform, string deviceToken, PushMessage message, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        try
        {
            var accessToken = await GetAccessTokenAsync(http, ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{_account.ProjectId}/messages:send")
            {
                Content = JsonContent.Create(new
                {
                    message = new
                    {
                        token = deviceToken,
                        notification = new { title = message.Title, body = message.Body },
                        data = new Dictionary<string, string> { ["type"] = message.Type.ToString() },
                    },
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return PushDeliveryOutcome.Delivered;

            var errorCode = await ReadErrorCodeAsync(response, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                _accessToken = null;

            var outcome = Classify(response.StatusCode, errorCode);
            _logger.LogWarning("FCM gönderimi reddedildi. Status={Status} ErrorCode={ErrorCode} Platform={Platform} Outcome={Outcome}",
                (int)response.StatusCode, errorCode, platform, outcome);
            return outcome;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "FCM'e ulaşılamadı. Platform={Platform}", platform);
            return PushDeliveryOutcome.Failed;
        }
    }

    /// <summary>
    /// FCM hata kodu → sonuç. Jeton kaldırılmış (<c>UNREGISTERED</c>, gövdesiz 404), başka projenin (<c>SENDER_ID_MISMATCH</c>) ya da
    /// biçimi bozuk (<c>INVALID_ARGUMENT</c>; ileti biçimi sabit olduğundan jetona bağlanır) ise cihaz silinir; kalanlar geçicidir.
    /// </summary>
    public static PushDeliveryOutcome Classify(HttpStatusCode status, string? errorCode) => errorCode switch
    {
        "UNREGISTERED" or "SENDER_ID_MISMATCH" or "INVALID_ARGUMENT" or "NOT_FOUND" => PushDeliveryOutcome.InvalidToken,
        null when status == HttpStatusCode.NotFound => PushDeliveryOutcome.InvalidToken,
        _ => PushDeliveryOutcome.Failed,
    };

    public void Dispose()
    {
        _signingKey.Dispose();
        _tokenLock.Dispose();
    }

    private async Task<string> GetAccessTokenAsync(HttpClient http, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (_accessToken is not null && now < _accessTokenExpiresAt - RefreshMargin)
                return _accessToken;

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = CreateAssertion(now),
            });
            using var response = await http.PostAsync(_account.TokenUri, content, ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"FCM erişim jetonu alınamadı (HTTP {(int)response.StatusCode}).");

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var accessToken = json.TryGetProperty("access_token", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(accessToken))
                throw new InvalidOperationException("FCM erişim jetonu yanıtı boş.");

            var expiresIn = json.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : AssertionLifetimeSeconds;
            _accessToken = accessToken;
            _accessTokenExpiresAt = now.AddSeconds(expiresIn);
            return accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>Google OAuth hizmet hesabı onayı: RS256 imzalı JWT (iss, scope, aud, iat, exp).</summary>
    private string CreateAssertion(DateTimeOffset now)
    {
        var issuedAt = now.ToUnixTimeSeconds();
        var header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = _account.ClientEmail,
            scope = MessagingScope,
            aud = _account.TokenUri,
            iat = issuedAt,
            exp = issuedAt + AssertionLifetimeSeconds,
        }));
        var signingInput = $"{header}.{claims}";
        var signature = _signingKey.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    /// <summary><c>error.details[].errorCode</c> (FcmError), yoksa <c>error.status</c>; gövde okunamazsa <c>null</c>.</summary>
    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (!json.TryGetProperty("error", out var error))
                return null;

            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("errorCode", out var code) && code.GetString() is { Length: > 0 } value)
                        return value;
                }
            }

            return error.TryGetProperty("status", out var status) ? status.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
