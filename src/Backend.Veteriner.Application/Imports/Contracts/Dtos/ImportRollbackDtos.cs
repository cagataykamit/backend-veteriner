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

/// <summary>
/// Engelli kayıt. <see cref="GroupId"/>: geri alma birimi (müşteri ya da ürün); aynı müşterinin hasta, randevu ve aşıları
/// aynı gruptadır, liste grup grup sıralıdır. <see cref="Record"/>: kaydı tanıtan bilgiler (kişisel veri); yalnız önizlemede ve
/// müşteri ile hasta okuma izni olan kullanıcıya döner, kayıt artık yoksa null.
/// </summary>
public sealed record ImportRollbackBlockedItemDto(
    string Entity,
    Guid Id,
    string Table,
    int RowNumber,
    IReadOnlyList<string> Reasons,
    Guid? GroupId = null,
    ImportRollbackRecordDto? Record = null);

/// <summary>
/// Engelli kaydı kullanıcıya tanıtan bilgiler; alan kayıt türüne göre dolar. <see cref="Name"/>: müşteri adı, hasta adı, aşı
/// adı, ürün adı, klinik kayıt türü ya da hesap hareketi açıklaması. <see cref="AtUtc"/>: randevu ya da aşı anı;
/// <see cref="On"/>: klinik kayıt ya da hesap hareketi günü. <see cref="Kind"/>: randevu türü ya da hesap hareketi türü (kod).
/// <see cref="PetId"/> ve <see cref="ClientId"/> kayda bağlantı içindir.
/// </summary>
public sealed record ImportRollbackRecordDto(
    string? Name = null,
    string? Species = null,
    Guid? PetId = null,
    string? PetName = null,
    Guid? ClientId = null,
    string? OwnerName = null,
    DateTime? AtUtc = null,
    DateOnly? On = null,
    string? Kind = null);

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
    IReadOnlyList<string> Warnings)
{
    /// <summary>Partinin eşleşen müşterilere açtığı bakiye farkı hareketleri (ADR-051, §14.5): silinecek ve kalacak (değişmiş) sayısı.</summary>
    public ImportBalanceAdjustmentCountsDto BalanceAdjustments { get; init; } = new(0, 0);
}

/// <summary>Bakiye farkı hareketi sayıları: <see cref="Delete"/> değişmemiş (geri almada silinir), <see cref="Keep"/> tahsis/mahsup görmüş.</summary>
public sealed record ImportBalanceAdjustmentCountsDto(int Delete, int Keep);

/// <summary>POST .../rollback sonucu; <see cref="Started"/> false ise aynı <c>requestId</c> ile tekrar istektir (200).</summary>
public sealed record ImportRollbackResultDto(bool Started, ImportBatchDto Batch);
