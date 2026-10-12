namespace Backend.Veteriner.Application.Common.Diagnostics;

/// <summary>Günlüklere yazılan kişisel verinin maskelenmesi (tek kural).</summary>
public static class LogMasking
{
    public static string MaskEmail(string? email)
    {
        var t = (email ?? string.Empty).Trim();
        var at = t.IndexOf('@');
        if (at <= 0 || at >= t.Length - 1)
            return "(invalid)";
        return t[0] + "***@" + t[(at + 1)..];
    }
}
