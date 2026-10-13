namespace Backend.Veteriner.Api.Controllers;

/// <summary>PUT /api/v1/care-instructions/{id} body; route id is source of truth.</summary>
public sealed class UpdateCareInstructionBody
{
    /// <summary>Optional; when set must match route id.</summary>
    public Guid? Id { get; init; }

    public string Text { get; init; } = default!;

    /// <summary>Base64 version from GET; required.</summary>
    public string? RowVersion { get; init; }
}
