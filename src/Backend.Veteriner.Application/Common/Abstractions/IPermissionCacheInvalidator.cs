namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Permission cache invalidation sözleşmesi.
/// PermissionReader cache tuttuğu için rol/permission değişikliklerinde
/// ilgili kullanıcı(lar)ın cache'inin düşürülmesini sağlar.
/// SEC-002: erişimi değiştiren her komutun (rol, rolün izinleri, kiracı üyeliği, klinik ataması) tek bildirim noktasıdır;
/// commit sonrası çağrılır, kullanıcının eldeki erişim token'ı da geçersiz sayılır (yenileme gerekir).
/// </summary>
public interface IPermissionCacheInvalidator
{
    /// <summary>
    /// Tek kullanıcı için permission cache'ini düşürür.
    /// </summary>
    void InvalidateUser(Guid userId);

    /// <summary>
    /// Birden fazla kullanıcı için permission cache'ini düşürür.
    /// </summary>
    void InvalidateUsers(IEnumerable<Guid> userIds);
}
