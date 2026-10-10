using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Common.Behaviors;

/// <summary>
/// Pipeline davranışlarının <see cref="Result"/> / <see cref="Result{T}"/> yanıtlarından başarı durumunu okuduğu tek yer.
/// Result olmayan yanıtlar başarılı sayılır.
/// </summary>
public static class ResultOutcome
{
    /// <summary>Yanıt Result veya Result{T} ise (IsSuccess, FailureReason); aksi halde (true, null).</summary>
    public static (bool Success, string? FailureReason) Of(object? response)
    {
        if (response is null)
            return (true, null);

        var type = response.GetType();

        if (type == typeof(Result))
        {
            var r = (Result)response;
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
}
