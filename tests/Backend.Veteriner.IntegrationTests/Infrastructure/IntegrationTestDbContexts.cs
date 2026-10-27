using System.Runtime.CompilerServices;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// DI dışında kurulan <see cref="AppDbContext"/> (seed ve okuyucu testleri). Test yardımcısı kiracılar arası veri
/// okur ve yazar; kiracı filtresi kapalı sistem kapsamıyla çalışır (ADR-040). DI'dan alınan bağlamlar için
/// <c>CreateSystemScope</c> kullanılır.
/// </summary>
internal static class IntegrationTestDbContexts
{
    private static readonly ConditionalWeakTable<AppDbContext, TenantContext> TenantContexts = new();

    public static AppDbContext CreateSystem(DbContextOptions<AppDbContext> options)
    {
        var tenantContext = new TenantContext(new HttpContextAccessor());
        tenantContext.AsSystem();
        var db = new AppDbContext(options, tenantContext);
        TenantContexts.Add(db, tenantContext);
        return db;
    }

    /// <summary>Bağlamın kiracı kapsamı; doğrudan kurulan servis aynı kapsamı paylaşır (DI kapsamındaki gibi).</summary>
    public static ITenantScope TenantScopeOf(AppDbContext db) => TenantContexts.TryGetValue(db, out var tenantContext)
        ? tenantContext
        : throw new InvalidOperationException("Bağlam IntegrationTestDbContexts.CreateSystem ile kurulmadı.");
}
