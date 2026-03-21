using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Common.Behaviors;

public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Property names (case-insensitive) whose values are masked in audit RequestPayload.
    /// </summary>
    private static readonly HashSet<string> SensitivePayloadKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "NewPassword", "ConfirmPassword", "Token", "RefreshToken", "AccessToken",
        "Secret", "ApiKey", "ClientSecret", "Hash", "PasswordHash"
    };

    private readonly IClientContext _client;
    private readonly IAuditLogWriter _auditLogWriter;

    public AuditBehavior(
        IClientContext client,
        IAuditLogWriter auditLogWriter)
    {
        _client = client;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (request is not IAuditableRequest auditable)
            return await next();

        try
        {
            var response = await next();

            var (success, failureReason) = TryGetResultOutcome(response);

            await _auditLogWriter.WriteAsync(
                new AuditLogEntry(
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
                    OccurredAtUtc: DateTime.UtcNow),
                ct);

            return response;
        }
        catch (Exception ex)
        {
            await _auditLogWriter.WriteAsync(
                new AuditLogEntry(
                    ActorUserId: _client.UserId,
                    Action: auditable.AuditAction,
                    TargetType: typeof(TRequest).Name,
                    TargetId: auditable.AuditTarget,
                    Success: false,
                    FailureReason: ex.Message,
                    Route: _client.Path,
                    HttpMethod: _client.Method,
                    IpAddress: _client.IpAddress,
                    UserAgent: _client.UserAgent,
                    CorrelationId: _client.CorrelationId,
                    RequestName: typeof(TRequest).Name,
                    RequestPayload: SerializeSafely(request),
                    OccurredAtUtc: DateTime.UtcNow),
                ct);

            throw;
        }
    }

    /// <summary>
    /// When TResponse is Result or Result{T}, returns (IsSuccess, FailureReason).
    /// Otherwise returns (true, null) so non-Result responses are audited as success.
    /// </summary>
    private static (bool success, string? failureReason) TryGetResultOutcome(TResponse? response)
    {
        if (response is null)
            return (true, null);

        var type = response.GetType();

        if (type == typeof(Result))
        {
            var r = (Result)(object)response;
            if (r.IsSuccess) return (true, null);
            return (false, FormatFailureReason(r.Error));
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var isSuccessProp = type.GetProperty("IsSuccess");
            var errorProp = type.GetProperty("Error");
            if (isSuccessProp is null || errorProp is null) return (true, null);

            var isSuccess = (bool)isSuccessProp.GetValue(response)!;
            if (isSuccess) return (true, null);

            var error = errorProp.GetValue(response);
            if (error is null) return (false, "Business rule violation");
            return (false, FormatFailureReason((Error)error));
        }

        return (true, null);
    }

    private static string FormatFailureReason(Error error)
    {
        if (string.IsNullOrWhiteSpace(error.Code))
            return string.IsNullOrWhiteSpace(error.Message) ? "Business rule violation" : error.Message;
        return string.IsNullOrWhiteSpace(error.Message)
            ? error.Code
            : $"{error.Code}: {error.Message}";
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