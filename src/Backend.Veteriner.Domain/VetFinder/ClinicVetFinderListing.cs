using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.VetFinder;

/// <summary>
/// Kliniğin Veteriner bul kaydı (ADR-047 K4): listelenme kliniğin seçimidir, varsayılan kapalı; kapatınca hemen çıkar. Hizmetler
/// <see cref="VetFinderServices"/> listesinden seçilir. Konum isteğe bağlıdır; verilirse ziyaretçi konumuyla mesafe sıralaması yapılır.
/// <see cref="PublicKey"/> herkese açık yüzeyin klinik anahtarıdır (kiracı ve klinik kimliği dışarı çıkmaz); tüm kiracılarda
/// benzersizdir, değişmez. Kiracı + klinik benzersizdir.
/// </summary>
public sealed class ClinicVetFinderListing : ITenantOwned
{
    public const int PublicKeyLength = 10;

    /// <summary>Konum yalnız Türkiye sınırları içinde kabul edilir (kabaca; ürün Türkiye'de çalışır).</summary>
    public const decimal MinLatitude = 35.5m, MaxLatitude = 42.5m, MinLongitude = 25.5m, MaxLongitude = 45.0m;

    private const char ServiceSeparator = ',';

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public bool IsListed { get; private set; }
    public string PublicKey { get; private set; } = default!;

    /// <summary>Seçili hizmet anahtarları, liste sırasıyla virgülle; boş olabilir.</summary>
    public string ServiceKeys { get; private set; } = string.Empty;

    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public IReadOnlyList<string> Services
        => ServiceKeys.Length == 0 ? [] : ServiceKeys.Split(ServiceSeparator);

    private ClinicVetFinderListing() { }

    /// <summary>Yeni kayıt; listelenme kapalı başlar.</summary>
    public static ClinicVetFinderListing Create(Guid tenantId, Guid clinicId, string publicKey, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (string.IsNullOrWhiteSpace(publicKey) || publicKey.Length != PublicKeyLength)
            throw new ArgumentException($"Anahtar {PublicKeyLength} karakter olmalıdır.", nameof(publicKey));

        return new ClinicVetFinderListing
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            IsListed = false,
            PublicKey = publicKey,
            CreatedAtUtc = utcNow,
        };
    }

    /// <summary>Listelenme, hizmetler ve konum birlikte yazılır. Bilinmeyen hizmet ya da yarım/sınır dışı konum reddedilir.</summary>
    public Result Update(bool isListed, IReadOnlyCollection<string> services, decimal? latitude, decimal? longitude, DateTime utcNow)
    {
        var unknown = services.FirstOrDefault(s => VetFinderServices.Find(s) is null);
        if (unknown is not null)
            return Result.Failure("VetFinder.Validation", $"Bilinmeyen hizmet: {unknown}.");

        if (latitude.HasValue != longitude.HasValue)
            return Result.Failure("VetFinder.Validation", "Konum için enlem ve boylam birlikte verilmelidir.");

        if (latitude is { } lat && longitude is { } lng
            && (lat < MinLatitude || lat > MaxLatitude || lng < MinLongitude || lng > MaxLongitude))
            return Result.Failure("VetFinder.Validation", "Konum Türkiye sınırları içinde olmalıdır.");

        IsListed = isListed;
        // Liste sırası korunur, tekrar düşer: kartta hizmetler her klinikte aynı sırada görünür.
        ServiceKeys = string.Join(ServiceSeparator, VetFinderServices.All.Select(s => s.Key).Where(services.Contains));
        Latitude = latitude is { } la ? Math.Round(la, 6) : null;
        Longitude = longitude is { } lo ? Math.Round(lo, 6) : null;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }
}
