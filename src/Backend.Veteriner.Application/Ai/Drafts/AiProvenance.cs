using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Ai;

namespace Backend.Veteriner.Application.Ai.Drafts;

/// <summary>
/// Kayıttaki "AI taslağından" izi (AI-010; arayüzde "✦ AI taslağından · düzenlendi" çipi). Kaynak, kalıcı taslak karar
/// alanlarıdır (<see cref="AiDraft.AppliedRecordId"/>); hedef kayda ayrı sütun yazılmaz. Klinik metin içermez; kaydı okuyabilen
/// herkes görür (<c>Ai.Use</c> gerekmez). Kabulden sonra kayıt elle değiştirilse de iz kalır.
/// </summary>
/// <param name="Edited">Hekim taslağı kabul etmeden önce düzenledi mi (<c>AcceptedWithEdits</c>).</param>
public sealed record AiProvenanceDto(
    Guid DraftId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AiFeature Feature,
    bool Edited,
    Guid AcceptedByUserId,
    string? AcceptedByName,
    DateTime AcceptedAtUtc);

public interface IAiProvenanceReader
{
    /// <summary>Kayıt kimliği → o kayda yazılmış taslaklar (eskiden yeniye); izi olmayan kayıt sözlükte yoktur.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<AiProvenanceDto>>> ForRecordsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recordIds, CancellationToken ct);
}

public sealed class AiProvenanceReader : IAiProvenanceReader
{
    private readonly IReadRepository<AiDraft> _drafts;
    private readonly IClinicVeterinarianReader _userNames;

    public AiProvenanceReader(IReadRepository<AiDraft> drafts, IClinicVeterinarianReader userNames)
    {
        _drafts = drafts;
        _userNames = userNames;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<AiProvenanceDto>>> ForRecordsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recordIds, CancellationToken ct)
    {
        if (recordIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<AiProvenanceDto>>();

        var drafts = await _drafts.ListAsync(new AcceptedAiDraftsForRecordsSpec(tenantId, recordIds.Distinct().ToList()), ct);
        if (drafts.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<AiProvenanceDto>>();

        var names = await _userNames.GetNamesAsync(drafts.Select(d => d.DecidedByUserId!.Value).Distinct().ToList(), ct);
        return drafts
            .GroupBy(d => d.AppliedRecordId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AiProvenanceDto>)g.Select(d => new AiProvenanceDto(
                        d.Id,
                        d.Feature,
                        d.Status == AiDraftStatus.AcceptedWithEdits,
                        d.DecidedByUserId!.Value,
                        names.GetValueOrDefault(d.DecidedByUserId.Value),
                        d.DecidedAtUtc!.Value))
                    .ToList());
    }
}

public static class AiProvenanceReaderExtensions
{
    /// <summary>Tek kaydın izi; yoksa boş liste.</summary>
    public static async Task<IReadOnlyList<AiProvenanceDto>> ForRecordAsync(
        this IAiProvenanceReader reader, Guid tenantId, Guid recordId, CancellationToken ct)
        => (await reader.ForRecordsAsync(tenantId, [recordId], ct)).GetValueOrDefault(recordId) ?? [];
}
