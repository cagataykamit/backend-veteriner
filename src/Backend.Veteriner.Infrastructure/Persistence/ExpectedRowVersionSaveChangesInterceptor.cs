using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// İstemcinin beklediği sürüm (tek yer): aggregate'in <c>SetExpectedRowVersion</c> ile yazdığı <c>RowVersion</c> değeri, EF'nin
/// eşzamanlılık koşulunda (<c>WHERE RowVersion = …</c>) kullandığı özgün değere taşınır. Böylece kayıt izlenerek yüklenebilir:
/// diğer alanların özgün değerleri veritabanındaki anlık görüntü olarak kalır ve değişiklik geçmişi (RECORD-002) alan farkını
/// yazar. İzlenmeden yüklenip <c>Update</c> ile eklenen kayıtta özgün ve güncel değer zaten aynıdır; davranış değişmez.
/// </summary>
public sealed class ExpectedRowVersionSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Apply(DbContext? context)
    {
        if (context is null)
            return;

        context.ChangeTracker.DetectChanges();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted))
                continue;

            foreach (var property in entry.Properties)
            {
                var metadata = property.Metadata;
                if (!metadata.IsConcurrencyToken || metadata.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate)
                    continue;

                if (property.CurrentValue is byte[] expected && property.OriginalValue is byte[] original
                    && !expected.AsSpan().SequenceEqual(original))
                {
                    property.OriginalValue = expected;
                }
            }
        }
    }
}
