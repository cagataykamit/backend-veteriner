using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// <see cref="IChangeTracked"/> varlıkların son değişiklik zamanı (TIER-009 dış API <c>updatedSince</c> ve imleç) — tek yer. Eklenen ya
/// da değişen kayıt kaydetme anında damgalanır; sütun ve indeks modeli de buradadır (<see cref="Configure"/>). Toplu
/// <c>ExecuteUpdate</c> bu yoldan geçmez; bu varlıklarda kullanılmamalıdır.
/// </summary>
public sealed class ChangeTrackedSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider;

    public ChangeTrackedSaveChangesInterceptor(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Model kuralı: zorunlu sütun, eski satırlar ve interceptor'sız eklemeler için veritabanı varsayılanı; liste sırası
    /// (<c>ChangedAtUtc</c>, <c>Id</c>) kiracı içinde indeksli.
    /// </summary>
    public static void Configure(ModelBuilder b)
    {
        var tracked = b.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && !t.IsOwned() && typeof(IChangeTracked).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType)
            .ToList();
        foreach (var clrType in tracked)
        {
            var entity = b.Entity(clrType);
            entity.Property(nameof(IChangeTracked.ChangedAtUtc)).IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(nameof(ITenantOwned.TenantId), nameof(IChangeTracked.ChangedAtUtc), "Id");
        }
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
            return;

        context.ChangeTracker.DetectChanges();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var entry in context.ChangeTracker.Entries<IChangeTracked>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Property(nameof(IChangeTracked.ChangedAtUtc)).CurrentValue = nowUtc;
        }
    }
}
