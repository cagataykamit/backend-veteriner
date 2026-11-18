namespace Backend.Veteriner.Domain.Ai;

/// <summary>
/// AI isteğine kaynak olan kayıt; denetim kaydında ve taslakta <b>yalnız tür ve kimlik</b> tutulur (AI-010), içerik
/// tutulmaz. <see cref="Type"/> zaman çizelgesi türüdür (<c>Pet</c>, <c>Examination</c>, <c>Treatment</c> ...).
/// </summary>
public sealed record AiSourceRef(string Type, Guid Id)
{
    public const int TypeMaxLength = 40;

    /// <summary>Bir istekte tutulan en çok kaynak; fazlası denetime yazılmaz (sıra korunur, ilkler kalır).</summary>
    public const int MaxCount = 300;

    /// <summary>Boş ve yinelenen kaynakları atar, sınırı uygular; sıra korunur.</summary>
    public static IReadOnlyList<AiSourceRef> Normalize(IEnumerable<AiSourceRef>? refs)
        => (refs ?? [])
            .Where(r => r is not null && r.Id != Guid.Empty && !string.IsNullOrWhiteSpace(r.Type))
            .Select(r => r with { Type = Truncate(r.Type.Trim()) })
            .Distinct()
            .Take(MaxCount)
            .ToList();

    private static string Truncate(string type) => type.Length > TypeMaxLength ? type[..TypeMaxLength] : type;
}
