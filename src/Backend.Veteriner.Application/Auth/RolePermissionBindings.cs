using System.Collections.Generic;

namespace Backend.Veteriner.Application.Auth;

/// <summary>
/// Ürün varsayılan rol -> permission bağlama tablosu. Yeni rol izin atamalarını kodda
/// merkezi olarak tutar ve seed/backfill üzerinden idempotent uygular.
/// <para>
/// Kapsam (Faz 5A): Yeni <c>Clinics.Update</c> yetkisi için minimum eşleştirme.
/// <list type="bullet">
///   <item><c>Admin</c> — <c>AdminClaimSeeder</c> zaten tüm permission'ları bağlar; burada ek olarak
///   explicit listelenir ki niyet net olsun ve seeder sırası değişse bile bağlanma garantilensin.</item>
///   <item><c>ClinicAdmin</c> — bu map olmadan hiçbir permission bağlanmaz; tenant admin davet yüzeyi
///   (<see cref="Tenants.InviteAssignableOperationClaimsCatalog"/>) rolü sadece isim olarak açar.</item>
/// </list>
/// </para>
/// <para>
/// <b>Kural:</b> Buraya sadece ürün varsayılan minimum seti girer. Operasyonel olarak kullanıcı bazlı
/// özel atamalar mevcut admin permission yönetim API'si (<c>/api/v1/operation-claim-permissions</c>)
/// üzerinden yapılmaya devam eder. Tablo ROL eşleşmesiyle sınırlıdır; global admin mantığına kaymaz.
/// </para>
/// </summary>
public static class RolePermissionBindings
{
    /// <summary>
    /// OperationClaim adı -> bağlanacak permission kodu listesi. Anahtar karşılaştırma
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>; permission kodları
    /// <see cref="PermissionCatalog"/> ile birebir eşleşmelidir.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Map { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin"] = new[]
            {
                PermissionCatalog.Clinics.Update,
            },
            ["ClinicAdmin"] = new[]
            {
                PermissionCatalog.Clinics.Update,
            },
        };
}
