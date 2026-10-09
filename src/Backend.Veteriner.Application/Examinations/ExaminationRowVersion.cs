namespace Backend.Veteriner.Application.Examinations;

/// <summary>
/// Muayene kayıt sürümünün API gösterimi: SQL Server <c>rowversion</c> (8 bayt) değerinin Base64 metni.
/// </summary>
public static class ExaminationRowVersion
{
    private const int RowVersionByteLength = 8;

    public static string Encode(byte[]? rowVersion)
        => rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : string.Empty;

    public static bool TryDecode(string? value, out byte[] rowVersion)
    {
        rowVersion = [];
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var buffer = new byte[RowVersionByteLength];
        if (!Convert.TryFromBase64String(value.Trim(), buffer, out var written) || written != RowVersionByteLength)
            return false;

        rowVersion = buffer;
        return true;
    }
}
