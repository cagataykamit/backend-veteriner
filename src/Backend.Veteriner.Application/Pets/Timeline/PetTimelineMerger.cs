namespace Backend.Veteriner.Application.Pets.Timeline;

/// <summary>Kaynaklardan gelen sıralı listeleri tek <c>(eventAtUtc, typeOrder, sourceId)</c> sırasında birleştirir.</summary>
public static class PetTimelineMerger
{
    /// <param name="sources">Her biri kendi içinde <c>(eventAtUtc DESC, sourceId DESC)</c> sıralı (SQL sırası) listeler.</param>
    public static (IReadOnlyList<PetTimelineRow> Page, bool HasMore) Page(
        IEnumerable<IReadOnlyList<PetTimelineRow>> sources,
        int limit)
    {
        // OrderBy kararlıdır: aynı (tarih, tür) yalnız tek kaynakta bulunur ve o kaynağın SQL sırası (id dahil) korunur.
        // Guid sırası SQL Server ve .NET'te farklı olduğundan id burada yeniden karşılaştırılmaz.
        var merged = sources
            .SelectMany(s => s)
            .OrderByDescending(r => r.EventAtUtc)
            .ThenByDescending(r => (int)r.Kind)
            .ToList();

        return (merged.Take(limit).ToList(), merged.Count > limit);
    }

    public static PetTimelineCursor CursorOf(PetTimelineRow last)
        => new(last.EventAtUtc, (int)last.Kind, last.SourceId);
}
