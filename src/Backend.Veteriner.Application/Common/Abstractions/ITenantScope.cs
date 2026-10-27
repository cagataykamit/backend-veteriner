namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Kiracı filtresinin açık kapsamı (ADR-040). Varsayılan, istek kapsamındaki kiracıdır (<see cref="ITenantContext"/>);
/// kiracı yoksa kiracılı sorgu boş döner ve yazma reddedilir. Dönen kapsam atılınca önceki duruma dönülür.
/// Kapsam DI kapsamına bağlıdır: yalnız aynı kapsamdan çözülen DbContext, repository ve işleyicileri etkiler.
/// </summary>
public interface ITenantScope
{
    /// <summary>
    /// Kiracıyı açıkça verir; istek kiracısını geçici olarak geçersiz kılar. Portal (bağlantıdan çözülen kiracı) ve
    /// kiracı bazında dönen arka plan işleri içindir.
    /// </summary>
    IDisposable UseTenant(Guid tenantId);

    /// <summary>
    /// Kiracı filtresini kapatır. Yalnız kiracılar arası çalışması gereken platform işleri içindir (outbox, projeksiyon,
    /// bakım, platform yönetimi, seed); kullanımlar mimari testte izinli listeye bağlıdır.
    /// </summary>
    IDisposable AsSystem();
}
