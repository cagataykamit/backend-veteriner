namespace Backend.Veteriner.Api.Contracts;

public sealed class PublicOwnerSignupBody
{
    public string PlanCode { get; set; } = default!;
    public string TenantName { get; set; } = default!;
    public string ClinicName { get; set; } = default!;
    public string ClinicCity { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Password { get; set; } = default!;
}
