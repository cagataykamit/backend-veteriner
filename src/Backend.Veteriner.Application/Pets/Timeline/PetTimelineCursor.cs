using System.Globalization;
using System.Text;

namespace Backend.Veteriner.Application.Pets.Timeline;

/// <summary>
/// Opak sayfa imleci: son dönen satırın <c>(eventAtUtc, typeOrder, sourceId)</c> değeri. Değer tabanlıdır;
/// ekleme/silme imleci bozmaz.
/// </summary>
public sealed record PetTimelineCursor(DateTime EventAtUtc, int TypeOrder, Guid SourceId)
{
    public string Encode()
    {
        var raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{EventAtUtc.Ticks}.{TypeOrder}.{SourceId:N}");
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string? value, out PetTimelineCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            return false;

        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
            var raw = Encoding.ASCII.GetString(Convert.FromBase64String(padded));
            var parts = raw.Split('.');
            if (parts.Length != 3
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var typeOrder)
                || !Guid.TryParseExact(parts[2], "N", out var id))
            {
                return false;
            }

            cursor = new PetTimelineCursor(new DateTime(ticks, DateTimeKind.Utc), typeOrder, id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
