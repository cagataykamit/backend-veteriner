namespace Backend.Veteriner.Domain.Imports;

/// <summary>
/// Bir tablonun parti sayaçları. Doğrulama <c>Total..WillSkip</c>, uygulama <c>Created..Failed</c> alanlarını doldurur;
/// önizleme tahmini ile nihai sonuç ayrı tutulur (uygulama anında eşleşmeler yeniden çözülür, sayılar sapabilir).
/// </summary>
public sealed record ImportTableCounts
{
    public static readonly ImportTableCounts Empty = new();

    public int Total { get; init; }
    public int Valid { get; init; }
    public int Warning { get; init; }
    public int Error { get; init; }
    public int WillCreate { get; init; }
    public int WillMatch { get; init; }
    public int WillSkip { get; init; }
    public int Created { get; init; }
    public int Matched { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
}
