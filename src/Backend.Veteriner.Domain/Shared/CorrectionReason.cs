namespace Backend.Veteriner.Domain.Shared;

/// <summary>Ücret ve ödeme düzeltme/iptal gerekçesi için tek doğrulama kuralı (5–500 karakter, kırpılmış).</summary>
public static class CorrectionReason
{
    public const int MinLength = 5;
    public const int MaxLength = 500;

    public static Result Validate(string? reason, string errorCode)
    {
        var length = reason?.Trim().Length ?? 0;
        return length is >= MinLength and <= MaxLength
            ? Result.Success()
            : Result.Failure(errorCode, $"Gerekçe {MinLength}-{MaxLength} karakter olmalıdır.");
    }
}
