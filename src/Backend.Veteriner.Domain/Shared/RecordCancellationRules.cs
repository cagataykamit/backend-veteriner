namespace Backend.Veteriner.Domain.Shared;

/// <summary>Doğrulanmış gerekçeli iptal; kaydın kendi iptal alanlarına aynen yazılır.</summary>
public sealed record RecordCancellation(DateTime CancelledAtUtc, Guid CancelledByUserId, string Reason);

/// <summary>
/// Klinik kayıtlarda gerekçeli iptal kuralı (tek yer, ADR-043 K4): iptal eden kullanıcı zorunlu, gerekçe <see cref="CorrectionReason"/>
/// (5–500, kırpılmış). Silme yoktur; iptal son durumdur. "Zaten iptal" denetimi ve iptal edilen kaydın değişmezliği kaydın kendisindedir.
/// </summary>
public static class RecordCancellationRules
{
    public static Result<RecordCancellation> Resolve(Guid cancelledByUserId, string? reason, DateTime nowUtc, string errorCode)
    {
        if (cancelledByUserId == Guid.Empty)
            return Result<RecordCancellation>.Failure(errorCode, "İptal eden kullanıcı geçersiz.");

        var reasonCheck = CorrectionReason.Validate(reason, errorCode);
        if (!reasonCheck.IsSuccess)
            return Result<RecordCancellation>.Failure(reasonCheck.Error);

        return Result<RecordCancellation>.Success(new RecordCancellation(NormalizeUtc(nowUtc), cancelledByUserId, reason!.Trim()));
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
