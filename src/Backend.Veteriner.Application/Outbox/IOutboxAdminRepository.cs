using Backend.Veteriner.Application.Common.Contracts.Outbox;
using Backend.Veteriner.Application.Common.Models;

namespace Backend.Veteriner.Application.Outbox;

/// <summary>
/// Outbox yönetim uçlarının okuma/yazma sözleşmesi. Outbox satırı altyapı varlığıdır; durum geçişleri burada
/// izlenen (tracked) satıra uygulanır, commit handler'da <see cref="Common.Abstractions.IUnitOfWork"/> ile yapılır.
/// </summary>
public interface IOutboxAdminRepository
{
    /// <summary>İşlenmemiş ve dead-letter olmayan en eski mesajlar (<c>CreatedAtUtc</c> artan).</summary>
    Task<IReadOnlyList<OutboxPendingMessageDto>> ListOldestPendingAsync(int take, CancellationToken ct);

    /// <summary>İşlenmemiş dead-letter mesajlar, en son düşen önce.</summary>
    Task<IReadOnlyList<OutboxDeadLetterMessageDto>> ListLatestDeadLettersAsync(int take, CancellationToken ct);

    /// <summary>Bekleyen mesajlar; <c>Type</c> araması, sıralama ve sayfa normalizasyonu (en çok 200) dahil.</summary>
    Task<PagedResult<OutboxItemDto>> GetPendingPagedAsync(PageRequest req, CancellationToken ct);

    /// <summary>Dead-letter mesajlar; <c>Type</c> araması, sıralama ve sayfa normalizasyonu (en çok 200) dahil.</summary>
    Task<PagedResult<OutboxItemDto>> GetDeadLettersPagedAsync(PageRequest req, CancellationToken ct);

    /// <summary>Mesajı durumundan bağımsız sıfırdan kuyruğa alır (dead-letter, hata ve deneme sayacı temizlenir). Yoksa <c>false</c>.</summary>
    Task<bool> RequeueAsync(Guid id, CancellationToken ct);

    /// <summary>Yalnız dead-letter mesajı yeniden denemeye açar; deneme sayacı ve ayrıntılı hata korunur.</summary>
    Task<OutboxRetryOutcome> RetryDeadLetterAsync(Guid id, CancellationToken ct);

    /// <summary>İşlenmemiş tüm dead-letter mesajları yeniden denemeye açar; etkilenen satır sayısını döner.</summary>
    Task<int> RetryAllDeadLettersAsync(CancellationToken ct);

    /// <summary>Mesajı siler. Yoksa <c>false</c>.</summary>
    Task<bool> RemoveAsync(Guid id, CancellationToken ct);
}
