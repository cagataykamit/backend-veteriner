using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Contracts.Dtos;

/// <summary>
/// Tablo başına kayıt sayısı (geri alma). <see cref="OpeningBalances"/>: devir ücretleri (müşterinin eki, müşteriyle aynı
/// kararı alır; ADR-048); geri alınmış sayılarda tutulmaz (<c>null</c>). <see cref="Total"/> devri ayrıca saymaz.
/// </summary>
public sealed record ImportRollbackCountsDto(
    int Clients,
    int Pets,
    int Vaccinations,
    int ClinicalRecords = 0,
    int AccountEntries = 0,
    int Appointments = 0,
    int Products = 0,
    int? OpeningBalances = null)
{
    [JsonIgnore]
    public int Total => Clients + Pets + Vaccinations + ClinicalRecords + AccountEntries + Appointments + Products;
}

/// <summary>Engelli kayıt: kişisel veri yok; satır ayrıntısı <c>rows</c> ucundan.</summary>
public sealed record ImportRollbackBlockedItemDto(
    string Entity,
    Guid Id,
    string Table,
    int RowNumber,
    IReadOnlyList<string> Reasons);

/// <summary>Mevcut kayıtla eşleşen (asla silinmeyen) satırlar.</summary>
public sealed record ImportRollbackKeptDto(int MatchedClients, int MatchedPets, int MatchedProducts = 0);

/// <summary>GET .../rollback-preview (IMPORT_API_CONTRACT.md §7.11).</summary>
public sealed record ImportRollbackPreviewDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ImportBatchStatus Status,
    bool CanRollback,
    bool CanDeleteUnblockedOnly,
    ImportRollbackCountsDto Deletable,
    ImportRollbackCountsDto Blocked,
    IReadOnlyList<ImportRollbackBlockedItemDto> BlockedItems,
    bool BlockedItemsTruncated,
    ImportRollbackKeptDto Kept,
    ImportRollbackCountsDto RolledBack,
    bool RemindersAlreadyReleased,
    IReadOnlyList<string> Warnings);

/// <summary>POST .../rollback sonucu; <see cref="Started"/> false ise aynı <c>requestId</c> ile tekrar istektir (200).</summary>
public sealed record ImportRollbackResultDto(bool Started, ImportBatchDto Batch);
