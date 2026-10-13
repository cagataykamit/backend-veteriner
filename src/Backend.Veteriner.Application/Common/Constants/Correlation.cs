namespace Backend.Veteriner.Application.Common.Constants;

public static class Correlation
{
    public const string HeaderName = "X-Correlation-ID";

    public const int MaxLength = 64;

    /// <summary>
    /// Gelen kimlik yalnız güvenli karakterlerden oluşuyor ve <see cref="MaxLength"/> sınırını aşmıyorsa korunur;
    /// aksi halde (log enjeksiyonu / şişirme önlemi) yeni kimlik üretilir.
    /// </summary>
    public static string Normalize(string? incoming)
    {
        if (!string.IsNullOrWhiteSpace(incoming) && incoming.Length <= MaxLength && incoming.All(IsAllowed))
            return incoming;

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsAllowed(char c)
        => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.' or ':';
}
