using System.Linq.Expressions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// Kiracı izolasyonu (ADR-040) — tek yer. <see cref="ITenantOwned"/> uygulayan her kök varlığa adlı kiracı filtresi
/// (<see cref="QueryFilterNames.Tenant"/>) ve kayıtta yazma koruması. Etkin kiracı ve sistem kapsamı
/// <see cref="TenantContext"/>'ten okunur; kiracı yoksa kiracılı sorgu satır döndürmez (kapalı varsayılan). Filtre yalnız
/// <c>ITenantScope.AsSystem</c> ile kalkar; <c>IgnoreQueryFilters</c> onu kaldırmaz.
/// </summary>
public abstract class TenantIsolatedDbContext : DbContext
{
    private readonly TenantContext _tenantContext;

    protected TenantIsolatedDbContext(DbContextOptions options, TenantContext tenantContext) : base(options)
        => _tenantContext = tenantContext;

    // EF filtre ifadesindeki bu iki değeri her sorguda DbContext örneğinden parametre olarak okur.
    protected Guid? CurrentTenantId => _tenantContext.TenantId;
    protected bool TenantFilterDisabled => _tenantContext.IsSystem;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Model kurulumunun sonunda çağrılır: <c>e =&gt; TenantFilterDisabled || e.TenantId == CurrentTenantId</c>.</summary>
    protected void ApplyTenantFilters(ModelBuilder b)
    {
        // Bağlam sabiti somut türle kurulur (derleyicinin "this" için ürettiği gibi); EF onu sorgudaki bağlamla değiştirir.
        var context = Expression.Constant(this, GetType());
        var disabled = Expression.Property(context, nameof(TenantFilterDisabled));
        var currentTenant = Expression.Property(context, nameof(CurrentTenantId));

        var tenantOwned = b.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && !t.IsOwned() && typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType)
            .ToList();
        foreach (var clrType in tenantOwned)
        {
            var entity = Expression.Parameter(clrType, "e");
            var sameTenant = Expression.Equal(
                Expression.Convert(Expression.Property(entity, nameof(ITenantOwned.TenantId)), typeof(Guid?)),
                currentTenant);
            b.Entity(clrType).HasQueryFilter(
                QueryFilterNames.Tenant,
                Expression.Lambda(Expression.OrElse(disabled, sameTenant), entity));
        }
    }

    /// <summary>
    /// Yazma koruması: eklenen ya da değiştirilen kiracılı varlık etkin kiracıya ait olmalıdır (değişiklikte eski değer
    /// de). Sistem kapsamı hariç; kiracı yoksa kiracılı varlık yazılamaz.
    /// </summary>
    private void EnsureTenantWrites()
    {
        if (TenantFilterDisabled)
            return;

        var tenantId = CurrentTenantId;
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            var foreign = entry.State switch
            {
                EntityState.Added => entry.Entity.TenantId != tenantId,
                EntityState.Modified => entry.Entity.TenantId != tenantId
                    || (Guid)entry.Property(nameof(ITenantOwned.TenantId)).OriginalValue! != tenantId,
                _ => false,
            };
            if (foreign)
            {
                throw new InvalidOperationException(
                    $"{entry.Metadata.ClrType.Name} etkin kiracıya ait değil; başka kiracının kaydı yazılamaz (ADR-040).");
            }
        }
    }
}
