namespace Backend.Veteriner.Application.Imports.Mapping;

/// <summary>Değer eşlemenin hedef kataloğu (<c>valueMappings[].kind</c>).</summary>
public enum ImportValueKind
{
    Species = 1,
    Breed = 2,
    Vaccine = 3,
    Color = 4,
}

public static class ImportValueKinds
{
    public static string Key(ImportValueKind kind) => kind switch
    {
        ImportValueKind.Species => "species",
        ImportValueKind.Breed => "breed",
        ImportValueKind.Vaccine => "vaccine",
        ImportValueKind.Color => "color",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParse(string? key, out ImportValueKind kind)
    {
        foreach (var candidate in Enum.GetValues<ImportValueKind>())
        {
            if (string.Equals(Key(candidate), key, StringComparison.Ordinal))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }
}
