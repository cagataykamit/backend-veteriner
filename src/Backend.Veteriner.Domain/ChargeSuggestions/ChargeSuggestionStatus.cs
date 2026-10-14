namespace Backend.Veteriner.Domain.ChargeSuggestions;

/// <summary>"Ücret bekliyor" kaleminin durumu. Yalnız <see cref="Pending"/> listede görünür; diğerleri son durumdur (geri dönmez).</summary>
public enum ChargeSuggestionStatus
{
    Pending = 0,
    Converted = 1,
    Waived = 2,
    Cancelled = 3
}
