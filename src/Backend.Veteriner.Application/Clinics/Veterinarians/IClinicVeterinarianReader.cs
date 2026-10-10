namespace Backend.Veteriner.Application.Clinics.Veterinarians;

public sealed record ClinicVeterinarianDto(Guid UserId, string? Name);

/// <summary>
/// Klinik hekimleri: kiracıdaki aktif kliniğe atanmış ve "Veteriner" operasyon claim'ine sahip kullanıcılar.
/// Hekim listesi, geliş doğrulaması ve görünen ad çözümü aynı kuralı paylaşır (kural tek yerde).
/// </summary>
public interface IClinicVeterinarianReader
{
    Task<IReadOnlyList<ClinicVeterinarianDto>> ListAsync(Guid tenantId, Guid clinicId, CancellationToken ct);

    /// <summary>Kullanıcı bu kliniğin aktif hekimi mi?</summary>
    Task<bool> IsClinicVeterinarianAsync(Guid userId, Guid tenantId, Guid clinicId, CancellationToken ct);

    /// <summary>Kullanıcı kimliklerinin görünen adları (hekim olup olmadığına bakmaz); bulunamayanlar sözlükte yoktur.</summary>
    Task<IReadOnlyDictionary<Guid, string?>> GetNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}

public static class ClinicVeterinarianRules
{
    /// <summary>Hekim sayılan operasyon claim adı.</summary>
    public const string ClaimName = "Veteriner";
}
