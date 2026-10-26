using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Common.Behaviors;

/// <summary>
/// <see cref="IAuditableRequest"/> işaretli istekler için denetim kaydı yazar. Denetim yazımı iş sonucunu etkilemez:
/// yazıcı (ayrı DbContext kapsamı) hata verirse hata loglanır, handler'ın yanıtı veya özgün istisnası aynen iletilir.
/// </summary>
public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IMessage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Property names (case-insensitive) whose values are masked in audit RequestPayload.
    /// </summary>
    private static readonly HashSet<string> SensitivePayloadKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "CurrentPassword", "NewPassword", "ConfirmPassword", "Token", "RefreshToken", "AccessToken",
        "Secret", "ApiKey", "ClientSecret", "Hash", "PasswordHash", "OneTimeCode"
    };

    /// <summary>
    /// TCKN/VKN taşıyan özellik adları (case-insensitive; ADR-036): değer <see cref="TurkishTaxIdentity.Mask"/> ile
    /// yanıtlardaki gibi kısmen maskelenir.
    /// </summary>
    private static readonly HashSet<string> IdentityNumberPayloadKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "NationalId", "TaxNumber", "TaxIdentityNumber"
    };

    private readonly IClientContext _client;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ILogger<AuditBehavior<TRequest, TResponse>> _logger;

    public AuditBehavior(
        IClientContext client,
        IAuditLogWriter auditLogWriter,
        ILogger<AuditBehavior<TRequest, TResponse>> logger)
    {
        _client = client;
        _auditLogWriter = auditLogWriter;
        _logger = logger;
    }

    public async ValueTask<TResponse> Handle(
        TRequest request,
        MessageHandlerDelegate<TRequest, TResponse> next,
        CancellationToken ct)
    {
        if (request is not IAuditableRequest auditable)
            return await next(request, ct);

        try
        {
            var response = await next(request, ct);

            var (success, failureReason) = ResultOutcome.Of(response);

            await TryWriteAsync(BuildEntry(request, auditable, success, failureReason), ct);

            return response;
        }
        catch (Exception ex)
        {
            await TryWriteAsync(BuildEntry(request, auditable, success: false, failureReason: ex.Message), ct);

            throw;
        }
    }

    private AuditLogEntry BuildEntry(TRequest request, IAuditableRequest auditable, bool success, string? failureReason)
        => new(
            ActorUserId: _client.UserId,
            Action: auditable.AuditAction,
            TargetType: typeof(TRequest).Name,
            TargetId: auditable.AuditTarget,
            Success: success,
            FailureReason: failureReason,
            Route: _client.Path,
            HttpMethod: _client.Method,
            IpAddress: _client.IpAddress,
            UserAgent: _client.UserAgent,
            CorrelationId: _client.CorrelationId,
            RequestName: typeof(TRequest).Name,
            RequestPayload: SerializeSafely(request),
            OccurredAtUtc: DateTime.UtcNow);

    /// <summary>
    /// Denetim kaydı iş işleminden sonra ayrı bağlantıda yazılır; buradaki hata istemciye 500 olarak yansımamalı
    /// ve handler'ın özgün istisnasını maskelememeli. İptal (<see cref="OperationCanceledException"/>) istisnası
    /// da yutulur: istek zaten iptal edilmiştir, yanıt yolunu iptal nedeniyle değiştirmek yanıltıcı olur.
    /// </summary>
    private async Task TryWriteAsync(AuditLogEntry entry, CancellationToken ct)
    {
        try
        {
            await _auditLogWriter.WriteAsync(entry, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Denetim kaydı yazılamadı. Action={Action} RequestName={RequestName} CorrelationId={CorrelationId} Success={Success}",
                entry.Action, entry.RequestName, entry.CorrelationId, entry.Success);
        }
    }

    private static string? SerializeSafely(TRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            return MaskSensitivePayload(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Masks sensitive property values in JSON payload so they are not stored in audit logs.
    /// </summary>
    private static string? MaskSensitivePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;

        try
        {
            using var doc = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                WriteElementMaskingSensitive(writer, doc.RootElement);
            }
            stream.Position = 0;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return json;
        }
    }

    private static void WriteElementMaskingSensitive(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    if (SensitivePayloadKeys.Contains(prop.Name))
                        writer.WriteStringValue("***");
                    else if (IdentityNumberPayloadKeys.Contains(prop.Name) && prop.Value.ValueKind == JsonValueKind.String)
                        writer.WriteStringValue(TurkishTaxIdentity.Mask(prop.Value.GetString()));
                    else
                        WriteElementMaskingSensitive(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteElementMaskingSensitive(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var i)) writer.WriteNumberValue(i);
                else if (element.TryGetInt64(out var l)) writer.WriteNumberValue(l);
                else if (element.TryGetDouble(out var d)) writer.WriteNumberValue(d);
                else writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteRawValue(element.GetRawText());
                break;
        }
    }
}