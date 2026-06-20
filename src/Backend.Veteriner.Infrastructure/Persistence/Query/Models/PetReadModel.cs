namespace Backend.Veteriner.Infrastructure.Persistence.Query.Models;

/// <summary>
/// Pet list/search için CQRS query read-model (tenant kapsamlı).
/// CQRS-12C-1: yalnızca şema temeli. Projection processor / event emission bu fazın dışındadır.
/// Client/Species/Color adları okuma performansı için denormalize tutulur; rename propagation bu fazda yok.
/// </summary>
public sealed class PetReadModel
{
    public Guid PetId { get; set; }
    public Guid TenantId { get; set; }

    public Guid ClientId { get; set; }

    /// <summary>Denormalize sahibi adı (görüntüleme). Rename propagation bu fazda yapılmaz.</summary>
    public string ClientFullName { get; set; } = default!;

    /// <summary>Sahip adına göre arama/sıralama için normalize değer.</summary>
    public string ClientFullNameNormalized { get; set; } = default!;

    public string Name { get; set; } = default!;

    /// <summary>Hayvan adına göre arama/sıralama için normalize değer.</summary>
    public string NameNormalized { get; set; } = default!;

    public Guid SpeciesId { get; set; }

    /// <summary>Denormalize tür adı (görüntüleme).</summary>
    public string SpeciesName { get; set; } = default!;

    /// <summary>Tür adına göre arama için normalize değer.</summary>
    public string SpeciesNameNormalized { get; set; } = default!;

    /// <summary>Global ırk kaydı (FK); opsiyonel. Domain'de <c>Pet.BreedId</c> nullable.</summary>
    public Guid? BreedId { get; set; }

    /// <summary>Serbest metin ırk (<c>Pet.Breed</c>); opsiyonel.</summary>
    public string? Breed { get; set; }

    /// <summary>Global ırk kaydının çözümlenmiş adı (<c>Pet.BreedRef.Name</c>); opsiyonel.</summary>
    public string? BreedRefName { get; set; }

    public Guid? ColorId { get; set; }

    /// <summary>Denormalize renk adı (görüntüleme); opsiyonel.</summary>
    public string? ColorName { get; set; }

    /// <summary>Renk adına göre arama için normalize değer; opsiyonel.</summary>
    public string? ColorNameNormalized { get; set; }

    /// <summary><c>PetGender</c> int karşılığı (1=Male, 2=Female); opsiyonel.</summary>
    public int? Gender { get; set; }

    public DateOnly? BirthDate { get; set; }

    public decimal? Weight { get; set; }

    public Guid LastEventId { get; set; }

    /// <summary>
    /// Bu satırı en son güncelleyen integration event'in <c>OccurredAtUtc</c> değeri.
    /// Client read-model'deki stale/out-of-order korumasının ordering anahtarıyla aynı desen:
    /// daha eski OccurredAtUtc taşıyan event mevcut veriyi ezmez.
    /// <see cref="LastProjectedAtUtc"/> (projection wall-clock) ile karıştırılmamalıdır.
    /// </summary>
    public DateTime LastEventOccurredAtUtc { get; set; }

    public DateTime LastProjectedAtUtc { get; set; }
}
