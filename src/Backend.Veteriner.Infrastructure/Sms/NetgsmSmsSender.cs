using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Sms;

/// <summary>
/// Netgsm OTP SMS bağdaştırıcısı, SMS API v3 (api-docs.netgsm.com.tr, 2026-11-15 okundu). Önce <c>POST /auth/v3/token</c>
/// (<c>username</c>, <c>password</c>, <c>scope: sms</c>) ile erişim jetonu alınır ve önbellekte tutulur; gönderim
/// <c>POST /sms/v3/send/otp</c> (<c>sender</c>, <c>text</c>, <c>recipient</c>, <c>appName</c>), başarıda 202 + <c>jobId</c>.
/// 401'de jeton bir kez yenilenir. 500'de yeniden denenmez (Netgsm: kod gitmiş olabilir). Numara, metin ve kimlik bilgisi
/// günlüğe yazılmaz; yalnız durum ve Netgsm hata kodu.
/// </summary>
public sealed class NetgsmSmsSender : ISmsSender
{
    public const string HttpClientName = "netgsm";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NetgsmAccessTokenCache _tokens;
    private readonly NetgsmOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NetgsmSmsSender> _logger;

    public NetgsmSmsSender(
        IHttpClientFactory httpClientFactory,
        NetgsmAccessTokenCache tokens,
        IOptions<SmsOptions> options,
        TimeProvider timeProvider,
        ILogger<NetgsmSmsSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
        _options = options.Value.Netgsm;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<SmsSendResult> SendOneTimeCodeAsync(string normalizedPhone, string text, CancellationToken ct)
    {
        if (!IsConfigured)
            return SmsSendResult.Failure("not-configured");

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var token = await GetTokenAsync(client, forceRefresh: false, ct);
            if (token is null)
                return SmsSendResult.Failure("auth-failed");

            using var response = await SendOtpAsync(client, token, normalizedPhone, text, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                token = await GetTokenAsync(client, forceRefresh: true, ct);
                if (token is null)
                    return SmsSendResult.Failure("auth-failed");

                using var retried = await SendOtpAsync(client, token, normalizedPhone, text, ct);
                return await ToResultAsync(retried, ct);
            }

            return await ToResultAsync(response, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Netgsm OTP isteği tamamlanamadı (zaman aşımı ya da bağlantı). Error={Error}", ex.GetType().Name);
            return SmsSendResult.Failure("unavailable");
        }
    }

    private Task<HttpResponseMessage> SendOtpAsync(
        HttpClient client, string token, string normalizedPhone, string text, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url("/sms/v3/send/otp"))
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["sender"] = _options.Sender,
                ["text"] = text,
                ["recipient"] = normalizedPhone,
                ["appName"] = _options.AppName,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (_options.Enterprise)
            request.Headers.Add("x-client", "enterprise");

        return client.SendAsync(request, ct);
    }

    private async Task<SmsSendResult> ToResultAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await ReadJsonAsync(response, ct);
        if (response.IsSuccessStatusCode)
            return SmsSendResult.Success(Text(body?["jobId"]));

        var code = Text(body?["code"]) ?? "unknown";
        _logger.LogWarning("Netgsm OTP gönderimi reddedildi. Status={Status} Code={Code}", (int)response.StatusCode, code);
        return SmsSendResult.Failure($"netgsm-{(int)response.StatusCode}-{code}");
    }

    private async Task<string?> GetTokenAsync(HttpClient client, bool forceRefresh, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow();
        return await _tokens.GetAsync(forceRefresh, utcNow, async () =>
        {
            using var response = await client.PostAsync(
                Url("/auth/v3/token"),
                JsonContent.Create(new JsonObject
                {
                    ["username"] = _options.Username,
                    ["password"] = _options.Password,
                    ["scope"] = "sms",
                }),
                ct);

            var body = await ReadJsonAsync(response, ct);
            var accessToken = Text(body?["access_token"]);
            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(accessToken))
            {
                _logger.LogWarning(
                    "Netgsm erişim jetonu alınamadı. Status={Status} Code={Code}",
                    (int)response.StatusCode, Text(body?["code"]) ?? "unknown");
                return null;
            }

            // Netgsm: jeton 24 saat geçerli; expires_in dönerse o kullanılır (alan adı doğrulanmalı).
            var lifetime = body?["expires_in"] is JsonValue v && v.TryGetValue<int>(out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromHours(24);
            return new NetgsmAccessToken(accessToken, utcNow.Add(lifetime));
        });
    }

    private Uri Url(string path) => new(new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

    private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Metin ya da sayı olarak gelebilen alanı okur (Netgsm iş numarası ve hata kodu).</summary>
    private static string? Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node?.ToJsonString();
}

public sealed record NetgsmAccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>Netgsm erişim jetonu süreç belleğinde, süresinden 5 dakika önce yenilenir (tek API süreci varsayımı).</summary>
public sealed class NetgsmAccessTokenCache
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private NetgsmAccessToken? _current;

    public async Task<string?> GetAsync(bool forceRefresh, DateTimeOffset utcNow, Func<Task<NetgsmAccessToken?>> fetch)
    {
        if (!forceRefresh && _current is { } cached && cached.ExpiresAt - RefreshMargin > utcNow)
            return cached.Value;

        await _lock.WaitAsync();
        try
        {
            if (!forceRefresh && _current is { } again && again.ExpiresAt - RefreshMargin > utcNow)
                return again.Value;

            _current = await fetch();
            return _current?.Value;
        }
        finally
        {
            _lock.Release();
        }
    }
}
