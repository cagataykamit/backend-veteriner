using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Platform yöneticisi (<c>PlatformAdmin</c>) <see cref="OperationClaim"/>'ini idempotent oluşturur,
/// <c>admin@example.com</c> kullanıcısını bu claim'e bağlar ve tüm <see cref="PermissionCatalog"/>
/// permission'larını <c>PlatformAdmin</c> claim'ine bağlar.
/// <para>
/// <b>Tarihçe (Faz 4B-6):</b> Bu seeder daha önce <c>"Admin"</c> claim'ine tüm permission'ları bağlıyordu;
/// bu durum tenant Admin rolü ile platform yöneticisini aynı yetki kümesinde toplayıp güvenlik
/// boşluğu üretiyordu. Yeni davranış:
/// <list type="bullet">
///   <item>Burada yalnızca <c>PlatformAdmin</c> claim'i oluşturulur ve tüm permission'lara bağlanır.</item>
///   <item>Tenant rolü <c>"Admin"</c> claim'i artık <see cref="InviteAssignableOperationClaimsSeeder"/>
///   tarafından oluşturulur ve <see cref="RolePermissionBindingSeeder"/> üzerinden yalnız
///   <c>RolePermissionBindings.Map["Admin"]</c> içindeki tenant/operasyon permission'larını alır.</item>
///   <item><c>PlatformAdmin</c> rolü <see cref="Backend.Veteriner.Application.Tenants.InviteAssignableOperationClaimsCatalog"/>
///   içinde <b>yer almaz</b>; tenant davet/atama akışlarında seçilemez.</item>
/// </list>
/// </para>
/// <para>
/// İdempotent garantileri:
/// <list type="bullet">
///   <item><c>PlatformAdmin</c> claim'i yoksa oluşturulur, varsa tekrar oluşturulmaz.</item>
///   <item><c>admin@example.com</c> kullanıcısına <c>PlatformAdmin</c> bağı yoksa eklenir.</item>
///   <item><c>PlatformAdmin</c> claim'inde eksik permission bağları eklenir; varolanlar değiştirilmez.</item>
///   <item>Eski davranıştan miras kalan <c>"Admin"</c> claim'inin geniş permission seti
///   <see cref="RolePermissionBindingSeeder"/> içindeki Admin whitelist temizliği ile düzeltilir.</item>
/// </list>
/// </para>
/// </summary>
public static class AdminClaimSeeder
{
    /// <summary>Platform yöneticisi OperationClaim adı; tenant davet whitelist'inde görünmez.</summary>
    public const string PlatformAdminClaimName = "PlatformAdmin";

    /// <summary>Varsayılan platform admin kullanıcısının e-posta adresi.</summary>
    public const string PlatformAdminUserEmail = "admin@example.com";

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        // 1) PlatformAdmin claim'i yoksa oluştur (Admin claim'ine dokunulmaz).
        var platformAdminClaim = await db.Set<OperationClaim>()
            .FirstOrDefaultAsync(x => x.Name == PlatformAdminClaimName, ct);

        if (platformAdminClaim is null)
        {
            platformAdminClaim = new OperationClaim(PlatformAdminClaimName);
            await db.AddAsync(platformAdminClaim, ct);
            await db.SaveChangesAsync(ct);
        }

        // 2) Platform admin kullanıcısını bul. DataSeeder/TestDataSeeder henüz çalışmadıysa idempotent skip.
        var adminUser = await db.Users
            .FirstOrDefaultAsync(x => x.Email == PlatformAdminUserEmail, ct);

        if (adminUser is null)
            return;

        // 3) UserOperationClaim (admin user -> PlatformAdmin) yoksa ekle.
        var existingLink = await db.UserOperationClaims
            .AnyAsync(x => x.UserId == adminUser.Id && x.OperationClaimId == platformAdminClaim.Id, ct);

        if (!existingLink)
        {
            var uoc = new UserOperationClaim(adminUser.Id, platformAdminClaim.Id);
            await db.UserOperationClaims.AddAsync(uoc, ct);
            await db.SaveChangesAsync(ct);
        }

        // 4) Tüm PermissionCatalog permission'larını PlatformAdmin claim'ine bağla (idempotent).
        var permissions = await db.Permissions.ToListAsync(ct);

        var existingPermIds = await db.OperationClaimPermissions
            .Where(x => x.OperationClaimId == platformAdminClaim.Id)
            .Select(x => x.PermissionId)
            .ToListAsync(ct);

        var missingPerms = permissions
            .Where(p => !existingPermIds.Contains(p.Id))
            .Select(p => new OperationClaimPermission(platformAdminClaim.Id, p.Id))
            .ToList();

        if (missingPerms.Count > 0)
        {
            await db.OperationClaimPermissions.AddRangeAsync(missingPerms, ct);
            await db.SaveChangesAsync(ct);
        }
    }
}
