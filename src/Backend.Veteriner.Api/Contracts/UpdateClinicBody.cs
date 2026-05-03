namespace Backend.Veteriner.Api.Contracts;

/// <summary>
/// PUT /api/v1/clinics/{id} istek gövdesi; route id kaynak doğrudur.
/// </summary>
public sealed class UpdateClinicBody
{
    /// <summary>İsteğe bağlı; doluysa route id ile aynı olmalıdır.</summary>
    public Guid? Id { get; init; }

    public string Name { get; init; } = default!;
    public string City { get; init; } = default!;
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? Description { get; init; }
}
