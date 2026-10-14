using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Contracts.Dtos;

/// <summary>Tablo başına kayıt sayısı (geri alma).</summary>
public sealed record ImportRollbackCountsDto(int Clients, int Pets, int Vaccinations)
{
    [JsonIgnore]
    public int Total => Clients + Pets + Vaccinations;
}

/// <summary>Engelli kayıt: kişisel veri yok; satır ayrıntısı <c>rows</c> ucundan.</summary>
public sealed record ImportRollbackBlockedItemDto(
    string Entity,
    Guid Id,
    string Table,
    int RowNumber,
    IReadOnlyList<string> Reasons);

/// <summary>Mevcut kayıtla eşleşen (asla silinmeyen) satırlar.</summary>
public sealed record ImportRollbackKeptDto(int MatchedClients, int MatchedPets);

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
