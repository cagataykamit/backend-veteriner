using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// Kiracı kapsamı verilmiş DI kapsamı (ADR-040). Arka plan işi kendi kapsamını açtığında HTTP isteği yoktur; kiracı
/// filtresi kapalı varsayılanla boş döner. Kapsamın kiracısı (ya da sistem kapsamı) DI kapsamı boyunca geçerlidir ve
/// kapsamla birlikte düşer. <see cref="CreateSystemScope(IServiceScopeFactory)"/> kullanımları mimari testte izinli
/// listeye bağlıdır.
/// </summary>
public static class TenantServiceScopes
{
    /// <summary>İş öğesinin kiracısıyla çalışan kapsam (içe aktarma partisi, kiracının hatırlatmaları vb.).</summary>
    public static AsyncServiceScope CreateTenantScope(this IServiceScopeFactory scopes, Guid tenantId)
        => Create(scopes, tenantScope => tenantScope.UseTenant(tenantId));

    /// <summary>Kiracı filtresi kapalı kapsam: yalnız kiracılar arası iş (kuyruk taraması, bakım, platform işi).</summary>
    public static AsyncServiceScope CreateSystemScope(this IServiceScopeFactory scopes)
        => Create(scopes, tenantScope => tenantScope.AsSystem());

    /// <inheritdoc cref="CreateTenantScope(IServiceScopeFactory, Guid)"/>
    public static AsyncServiceScope CreateTenantScope(this IServiceProvider services, Guid tenantId)
        => services.GetRequiredService<IServiceScopeFactory>().CreateTenantScope(tenantId);

    /// <inheritdoc cref="CreateSystemScope(IServiceScopeFactory)"/>
    public static AsyncServiceScope CreateSystemScope(this IServiceProvider services)
        => services.GetRequiredService<IServiceScopeFactory>().CreateSystemScope();

    private static AsyncServiceScope Create(IServiceScopeFactory scopes, Func<ITenantScope, IDisposable> enter)
    {
        var scope = scopes.CreateAsyncScope();
        try
        {
            // Kapsam DI kapsamına bağlıdır; dönen geri alma nesnesi kapsamla birlikte ömrünü tamamlar.
            enter(scope.ServiceProvider.GetRequiredService<ITenantScope>());
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
