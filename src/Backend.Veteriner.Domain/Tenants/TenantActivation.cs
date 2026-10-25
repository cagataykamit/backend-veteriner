namespace Backend.Veteriner.Domain.Tenants;

/// <summary>
/// Kiracı başına tek aktivasyon kaydı (TRIAL-008): örnek klinik ne zaman yüklendi, kullanıcının ilk anlamlı aksiyonu
/// ne zaman ve ne oldu. İki işaret de yalnız ilk kez yazılır; sonraki çağrılar no-op döner (kural burada, çağıranlar
/// tekrar kontrolü yapmaz). Ölçüm verisidir; iş kuralı taşımaz.
/// </summary>
public sealed class TenantActivation
{
    public Guid TenantId { get; private set; }
    public DateTime? ExampleClinicLoadedAtUtc { get; private set; }
    public DateTime? FirstActionAtUtc { get; private set; }
    public ActivationSignalKind? FirstActionKind { get; private set; }
    public Guid? FirstActionUserId { get; private set; }

    private TenantActivation() { }

    public TenantActivation(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));

        TenantId = tenantId;
    }

    /// <returns>İşaret bu çağrıda yazıldıysa <c>true</c>; zaten doluysa <c>false</c>.</returns>
    public bool MarkExampleClinicLoaded(DateTime utcNow)
    {
        if (ExampleClinicLoadedAtUtc.HasValue)
            return false;

        ExampleClinicLoadedAtUtc = Utc(utcNow);
        return true;
    }

    /// <returns>İlk aksiyon bu çağrıda yazıldıysa <c>true</c>; zaten doluysa <c>false</c>.</returns>
    public bool MarkFirstAction(ActivationSignalKind kind, Guid? userId, DateTime utcNow)
    {
        if (FirstActionAtUtc.HasValue)
            return false;

        FirstActionAtUtc = Utc(utcNow);
        FirstActionKind = kind;
        FirstActionUserId = userId;
        return true;
    }

    private static DateTime Utc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
