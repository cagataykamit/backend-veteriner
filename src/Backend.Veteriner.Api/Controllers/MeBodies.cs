namespace Backend.Veteriner.Api.Controllers;

/// <summary>PUT /me/display-name gövdesi.</summary>
public sealed class SetMyDisplayNameBody
{
    public string? DisplayName { get; init; }
}
