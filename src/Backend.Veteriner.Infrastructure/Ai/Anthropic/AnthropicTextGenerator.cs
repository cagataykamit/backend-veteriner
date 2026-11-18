using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Veteriner.Application.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Ai.Anthropic;

/// <summary>
/// Anthropic Messages API istemcisi (<c>POST /v1/messages</c>, yapılandırılmış çıktı: <c>output_config.format</c> JSON şeması).
/// Anahtar <c>x-api-key</c> başlığında gider, günlüğe yazılmaz. İstem ve yanıt gövdesi günlüğe yazılmaz (klinik veri); yalnız
/// durum kodu, hata türü ve token sayıları. Yeniden deneme yapılmaz: istek kullanıcıdan gelir ve maliyeti vardır.
/// </summary>
public sealed class AnthropicTextGenerator : IAiTextGenerator
{
    public const string HttpClientName = "anthropic";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicTextGenerator> _logger;

    public AnthropicTextGenerator(
        IHttpClientFactory httpClientFactory,
        IOptions<AnthropicOptions> options,
        ILogger<AnthropicTextGenerator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<AiGenerationResult> GenerateJsonAsync(AiGenerationRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
            return AiGenerationResult.Failed(AiFailureKind.Rejected);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiBaseUrl.TrimEnd('/')}/v1/messages");
        message.Headers.Add("x-api-key", _options.ApiKey!);
        message.Headers.Add("anthropic-version", _options.ApiVersion);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new StringContent(Body(request).ToJsonString(), Encoding.UTF8, "application/json");

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(message, ct);
            var body = await ParseAsync(response, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorType = Str(body?["error"]?["type"]);
                _logger.LogWarning("Anthropic messages → {Status} {ErrorType}", (int)response.StatusCode, errorType);
                return AiGenerationResult.Failed(IsTransient(response.StatusCode) ? AiFailureKind.Unavailable : AiFailureKind.Rejected);
            }

            return ToResult(body);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Anthropic messages bağlantı hatası: {Error}", ex.GetType().Name);
            return AiGenerationResult.Failed(AiFailureKind.Unavailable);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Anthropic messages zaman aşımı");
            return AiGenerationResult.Failed(AiFailureKind.Unavailable);
        }
    }

    /// <summary>İstek gövdesi. <c>thinking</c> gönderilmez (modelin varsayılanı); derinlik <c>effort</c> ile ayarlanır.</summary>
    private JsonObject Body(AiGenerationRequest request) => new()
    {
        ["model"] = request.Model,
        ["max_tokens"] = request.MaxOutputTokens,
        ["system"] = request.SystemPrompt,
        ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = request.UserContent }),
        ["output_config"] = new JsonObject
        {
            ["effort"] = _options.Effort,
            ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = request.Schema.DeepClone() },
        },
    };

    /// <summary>Başarılı yanıt: metin blokları birleştirilir (düşünme blokları atlanır); token önbellek okuma/yazma dahil sayılır.</summary>
    private static AiGenerationResult ToResult(JsonNode? body)
    {
        var usage = body?["usage"];
        var input = Int(usage?["input_tokens"]) + Int(usage?["cache_creation_input_tokens"]) + Int(usage?["cache_read_input_tokens"]);
        var output = Int(usage?["output_tokens"]);
        var text = string.Concat((body?["content"] as JsonArray ?? [])
            .Where(b => Str(b?["type"]) == "text")
            .Select(b => Str(b?["text"])));

        var status = Str(body?["stop_reason"]) switch
        {
            "refusal" => AiGenerationStatus.Refused,
            "max_tokens" => AiGenerationStatus.Truncated,
            _ when body is null => AiGenerationStatus.Failed,
            _ => AiGenerationStatus.Completed,
        };
        return new AiGenerationResult(
            status,
            status == AiGenerationStatus.Completed ? text : null,
            input,
            output,
            status == AiGenerationStatus.Failed ? AiFailureKind.Unavailable : AiFailureKind.None);
    }

    /// <summary>429 hız sınırı, 408, 5xx ve 529 aşırı yük geçicidir; 4xx (anahtar, yetki, ödeme, istek) kalıcıdır.</summary>
    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)status >= 500;

    private static int Int(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    private static string? Str(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static async Task<JsonNode?> ParseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
