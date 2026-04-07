namespace Backend.Veteriner.Api.Contracts;

public sealed class CreateTenantInviteBody
{
    public string Email { get; set; } = default!;
    public Guid ClinicId { get; set; }
    public Guid OperationClaimId { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}
